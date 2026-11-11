using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using MiniPrint.Core.Configuration;
using MiniPrint.Core.Models;
using MiniPrint.Protocol;

namespace MiniPrint.Imaging;

/// <summary>
/// Converte texto em imagem raster de 1 bit. O texto é renderizado com
/// antialiasing em tons de cinza e depois binarizado por limiar — o resultado
/// impresso fica mais legível que a binarização direta da fonte.
/// </summary>
public static class TextRenderer
{
    /// <summary>Altura máxima aceita (o papel é contínuo, mas evitamos exageros).</summary>
    public const int MaxHeight = 20000;

    public static MonochromeBitmap Render(
        string text,
        TextSettings settings,
        int paperWidth,
        int threshold = 180)
    {
        text ??= string.Empty;
        var marginLeft = Math.Clamp(settings.MarginLeft, 0, paperWidth / 2);
        var marginRight = Math.Clamp(settings.MarginRight, 0, paperWidth / 2);
        var contentWidth = paperWidth - marginLeft - marginRight;
        if (contentWidth < 16)
        {
            contentWidth = 16;
            marginLeft = 0;
        }

        using var font = CreateFont(settings);
        var lines = LayoutLines(text, font, settings, contentWidth);

        using var measureSurface = new Bitmap(1, 1);
        using var measureGraphics = Graphics.FromImage(measureSurface);
        var lineHeight = font.GetHeight(measureGraphics);

        var spacing = Math.Max(1.0, settings.LineSpacing);
        var step = Math.Max(1, (int)Math.Round(lineHeight * spacing));
        var top = Math.Max(0, settings.MarginTop);
        var bottom = Math.Max(0, settings.MarginBottom);
        var height = top + (lines.Count * step) + bottom;
        height = Math.Clamp(height, 1, MaxHeight);

        using var bitmap = new Bitmap(paperWidth, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;

            using var format = new StringFormat(StringFormat.GenericTypographic)
            {
                LineAlignment = StringAlignment.Near,
                FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
            };

            var y = (float)top;
            foreach (var line in lines)
            {
                // Desenho por ponto, com o alinhamento calculado à mão.
                // O overload com RectangleF corta glifos que descem abaixo da
                // linha de base (sublinhados), mesmo com NoClip.
                var lineWidth = (float)Measure(graphics, line, font);
                var x = settings.Alignment switch
                {
                    TextAlignment.Centro => marginLeft + ((contentWidth - lineWidth) / 2f),
                    TextAlignment.Direita => marginLeft + contentWidth - lineWidth,
                    _ => (float)marginLeft,
                };

                graphics.DrawString(line, font, Brushes.Black, x, y, format);
                y += step;
            }
        }

        var gray = Grayscale.FromBitmap(bitmap);
        var binary = Dithering.Threshold(gray, paperWidth, height, Math.Clamp(threshold, 1, 254));
        return CatPrinterRaster.PackPixels(paperWidth, height, binary);
    }

    /// <summary>Quebra o texto em linhas considerando largura, alinhamento e \n.</summary>
    public static List<string> LayoutLines(string text, Font font, TextSettings settings, int contentWidth)
    {
        var result = new List<string>();
        var paragraphs = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        using var surface = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(surface);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        foreach (var paragraph in paragraphs)
        {
            if (!settings.WordWrap || paragraph.Length == 0)
            {
                result.Add(paragraph);
                continue;
            }

            var words = paragraph.Split(' ', StringSplitOptions.None);
            var current = string.Empty;
            foreach (var word in words)
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                var width = Measure(graphics, candidate, font);
                if (width <= contentWidth || current.Length == 0)
                {
                    current = candidate;
                }
                else
                {
                    result.Add(current);
                    current = word;
                }
            }

            result.Add(current);
        }

        if (result.Count == 0)
        {
            result.Add(string.Empty);
        }

        return result;
    }

    private static double Measure(Graphics graphics, string text, Font font)    {
        if (text.Length == 0)
        {
            return 0;
        }

        return graphics.MeasureString(
            text,
            font,
            new PointF(0, 0),
            StringFormat.GenericTypographic).Width;
    }

    /// <summary>
    /// Cria a fonte configurada, com queda para "Segoe UI" quando a família
    /// pedida não existe no sistema.
    /// </summary>
    public static Font CreateFont(TextSettings settings)
    {
        var style = FontStyle.Regular;
        if (settings.Bold)
        {
            style |= FontStyle.Bold;
        }

        if (settings.Italic)
        {
            style |= FontStyle.Italic;
        }

        var size = (float)Math.Clamp(settings.FontSize, 4, 72);
        var family = string.IsNullOrWhiteSpace(settings.FontFamily) ? "Segoe UI" : settings.FontFamily;

        try
        {
            var font = new Font(family, size, style, GraphicsUnit.Point);
            if (font.Name.Equals(family, StringComparison.OrdinalIgnoreCase))
            {
                return font;
            }

            // A família pedida foi substituída silenciosamente: usa a padrão.
            font.Dispose();
        }
        catch (Exception)
        {
            // Fonte ausente no sistema.
        }

        return new Font("Segoe UI", size, style, GraphicsUnit.Point);
    }
}
