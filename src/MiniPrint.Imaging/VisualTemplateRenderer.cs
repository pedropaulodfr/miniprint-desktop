using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using MiniPrint.Core.Configuration;
using MiniPrint.Core.Models;
using MiniPrint.Core.Templates;
using MiniPrint.Protocol;

namespace MiniPrint.Imaging;

/// <summary>
/// Renderiza um documento com posicionamento absoluto (editor visual).
///
/// Usa o mesmo caminho dos outros renderizadores: GDI+ com antialiasing,
/// luminância, binarização por limiar e empacotamento em 384 pontos. A
/// pré-visualização e o papel saem do mesmo código.
/// </summary>
public static class VisualTemplateRenderer
{
    public const int Threshold = 180;

    public const int MaxHeight = 24000;

    /// <summary>Renderiza o layout com os valores dos campos já preenchidos.</summary>
    public static MonochromeBitmap Render(
        DocumentTemplate template,
        IReadOnlyDictionary<string, string> values)
    {
        var layout = template.Layout
            ?? throw new InvalidOperationException("O modelo não tem layout visual.");

        var width = Math.Clamp(layout.Width, 64, 576);
        var height = Math.Clamp(layout.EffectiveHeight, 1, MaxHeight);

        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;

            foreach (var element in layout.Elements)
            {
                if (!element.Visible)
                {
                    continue;
                }

                switch (element.Kind)
                {
                    case LayoutElementKind.Texto:
                    case LayoutElementKind.Campo:
                        DrawTextElement(graphics, element, template, values);
                        break;

                    case LayoutElementKind.Imagem:
                        DrawImageElement(graphics, element);
                        break;

                    case LayoutElementKind.Linha:
                        DrawLineElement(graphics, element, width);
                        break;

                    case LayoutElementKind.Retangulo:
                        DrawRectangleElement(graphics, element);
                        break;
                }
            }
        }

        var gray = Grayscale.FromBitmap(bitmap);
        var binary = Dithering.Threshold(gray, width, height, Threshold);
        return CatPrinterRaster.PackPixels(width, height, binary);
    }

    /// <summary>Texto que o elemento representa, já com o campo resolvido.</summary>
    public static string ResolveElementText(
        LayoutElement element,
        DocumentTemplate template,
        IReadOnlyDictionary<string, string> values)
    {
        if (element.Kind == LayoutElementKind.Campo)
        {
            var field = template.Fields.FirstOrDefault(f =>
                string.Equals(f.Key, element.FieldKey, StringComparison.OrdinalIgnoreCase));

            if (field is null)
            {
                return string.Empty;
            }

            values.TryGetValue(field.Key, out var raw);
            return TemplateFieldFormatter.Format(field, raw);
        }

        // Elementos de texto também aceitam {{chave}} no conteúdo.
        return TemplateComposer.Resolve(element.Text, values, template.Fields, out _);
    }

    /// <summary>
    /// Altura necessária para o texto do elemento, respeitando largura,
    /// entrelinhas e quebra de linha. Usada pelo editor para altura automática.
    /// </summary>
    public static int MeasureTextHeight(
        LayoutElement element,
        DocumentTemplate template,
        IReadOnlyDictionary<string, string>? values = null)
    {
        var text = values is null
            ? (element.Kind == LayoutElementKind.Campo
                ? $"«{element.Text}»"
                : element.Text)
            : ResolveElementText(element, template, values);

        if (string.IsNullOrEmpty(text))
        {
            return Math.Max(4, element.Height);
        }

        using var measureBitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(measureBitmap);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using var font = CreateFont(element);
        var lines = TextRenderer.LayoutLines(
            text,
            font,
            new TextSettings { WordWrap = true, LineSpacing = element.LineSpacing },
            Math.Max(8, element.Width));

        var lineHeight = font.GetHeight(graphics) * Math.Max(1.0, element.LineSpacing);
        return Math.Max(1, (int)Math.Ceiling(lines.Count * lineHeight));
    }

    private static void DrawTextElement(
        Graphics graphics,
        LayoutElement element,
        DocumentTemplate template,
        IReadOnlyDictionary<string, string> values)
    {
        var text = ResolveElementText(element, template, values);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var size = element.FontSize;
        Font? font = null;
        List<string> lines = new();
        double lineHeight = 0;

        // Procura o maior tamanho que cabe, quando o modo é "reduzir fonte".
        while (true)
        {
            font?.Dispose();
            font = CreateFont(element, size);

            lines = TextRenderer.LayoutLines(
                text,
                font,
                new TextSettings { WordWrap = true, LineSpacing = element.LineSpacing },
                Math.Max(8, element.Width)).ToList();

            lineHeight = font.GetHeight(graphics) * Math.Max(1.0, element.LineSpacing);
            var totalHeight = lines.Count * lineHeight;

            var canShrink = element.Overflow == OverflowMode.ReduzirFonte
                            && totalHeight > element.Height
                            && element.Height > 4
                            && size > 5;

            if (!canShrink)
            {
                break;
            }

            size -= 0.5;
        }

        try
        {
            DrawLines(graphics, element, font!, lines, lineHeight);
        }
        finally
        {
            font?.Dispose();
        }
    }

    private static void DrawLines(
        Graphics graphics,
        LayoutElement element,
        Font font,
        IReadOnlyList<string> lines,
        double lineHeight)
    {
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            LineAlignment = StringAlignment.Near,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
        };

        var clip = element.Overflow == OverflowMode.Expandir
            ? RectangleF.Empty
            : new RectangleF(element.X, element.Y, element.Width, element.Height);

        if (clip != RectangleF.Empty)
        {
            graphics.SetClip(clip);
        }

        try
        {
            var y = (float)element.Y;
            foreach (var line in lines)
            {
                var lineWidth = graphics.MeasureString(
                    line, font, new PointF(0, 0), StringFormat.GenericTypographic).Width;

                var x = element.Alignment switch
                {
                    TextAlignment.Centro => element.X + ((element.Width - lineWidth) / 2f),
                    TextAlignment.Direita => element.X + element.Width - lineWidth,
                    _ => (float)element.X,
                };

                graphics.DrawString(line, font, Brushes.Black, x, y, format);
                y += (float)lineHeight;
            }
        }
        finally
        {
            graphics.ResetClip();
        }
    }

    private static void DrawImageElement(Graphics graphics, LayoutElement element)
    {
        using var source = LoadElementImage(element);
        if (source is null)
        {
            return;
        }

        var (targetWidth, targetHeight) = FitImage(source, element);

        var options = new ImagePrintOptions
        {
            Brightness = element.Brightness,
            Contrast = element.Contrast,
            Dithering = element.Dithering,
            Rotation = element.Rotation,
            Threshold = 128,
            Invert = false,
            CropLeft = element.CropLeft,
            CropRight = element.CropRight,
            CropTop = element.CropTop,
            CropBottom = element.CropBottom,
        };

        var mono = ImageProcessor.PrepareSized(source, targetWidth, targetHeight, options);
        using var rendered = ImageProcessor.ToPreview(mono);

        var x = element.X + ((element.Width - targetWidth) / 2);
        graphics.DrawImage(rendered, x, element.Y, targetWidth, targetHeight);
    }

    private static (int Width, int Height) FitImage(Bitmap source, LayoutElement element)
    {
        if (!element.KeepAspect)
        {
            return (Math.Max(1, element.Width), Math.Max(1, element.Height));
        }

        var scale = Math.Min(
            (double)element.Width / source.Width,
            (double)element.Height / source.Height);

        return (
            Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale)));
    }

    /// <summary>Decodifica a imagem embutida (ou o arquivo, se só houver caminho).</summary>
    public static Bitmap? LoadElementImage(LayoutElement element)
    {
        try
        {
            if (!string.IsNullOrEmpty(element.ImageData))
            {
                var bytes = Convert.FromBase64String(element.ImageData);
                using var stream = new MemoryStream(bytes);
                using var loaded = Image.FromStream(stream, false, true);
                return new Bitmap(loaded);
            }

            if (!string.IsNullOrEmpty(element.ImagePath) && File.Exists(element.ImagePath))
            {
                return ImageProcessor.Load(element.ImagePath);
            }
        }
        catch (Exception)
        {
            // Imagem inválida não deve impedir a impressão do restante.
        }

        return null;
    }

    private static void DrawLineElement(Graphics graphics, LayoutElement element, int documentWidth)
    {
        var width = Math.Min(element.Width, Math.Max(1, documentWidth - element.X));
        var thickness = Math.Max(1, element.Thickness);
        var y = element.Y + Math.Max(0, (element.Height - thickness) / 2);

        graphics.FillRectangle(Brushes.Black, element.X, y, width, thickness);
    }

    private static void DrawRectangleElement(Graphics graphics, LayoutElement element)
    {
        var thickness = Math.Max(1, element.Thickness);

        if (element.Filled)
        {
            graphics.FillRectangle(Brushes.Black, element.X, element.Y, element.Width, element.Height);
            return;
        }

        graphics.FillRectangle(Brushes.Black, element.X, element.Y, element.Width, thickness);
        graphics.FillRectangle(Brushes.Black, element.X, element.Bottom - thickness, element.Width, thickness);
        graphics.FillRectangle(Brushes.Black, element.X, element.Y, thickness, element.Height);
        graphics.FillRectangle(Brushes.Black, element.Right - thickness, element.Y, thickness, element.Height);
    }

    private static Font CreateFont(LayoutElement element, double? sizeOverride = null)
    {
        var settings = new TextSettings
        {
            FontFamily = element.FontFamily,
            FontSize = sizeOverride ?? element.FontSize,
            Bold = element.Bold,
            Italic = element.Italic,
        };

        return TextRenderer.CreateFont(settings);
    }
}
