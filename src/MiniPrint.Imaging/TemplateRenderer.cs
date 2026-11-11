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
/// Renderiza um modelo de documento já preenchido em um bitmap de 1 bit.
///
/// Usa exatamente o mesmo caminho do texto livre: GDI+ com antialiasing,
/// conversão para luminância, binarização por limiar e empacotamento em
/// 384 pontos — para a pré-visualização bater com o papel.
/// </summary>
public static class TemplateRenderer
{
    /// <summary>Altura máxima de segurança (papel é contínuo, mas evitamos exageros).</summary>
    public const int MaxHeight = 24000;

    /// <summary>Limiar de binarização, igual ao usado pelo texto livre.</summary>
    public const int Threshold = 180;

    public static MonochromeBitmap Render(
        DocumentTemplate template,
        IReadOnlyDictionary<string, string> values,
        int? paperWidthOverride = null)
    {
        var paperWidth = Math.Clamp(paperWidthOverride ?? template.PaperWidth, 64, 576);
        var marginLeft = Math.Clamp(template.MarginLeft, 0, paperWidth / 2);
        var marginRight = Math.Clamp(template.MarginRight, 0, paperWidth / 2);
        var contentWidth = Math.Max(16, paperWidth - marginLeft - marginRight);
        var spacing = Math.Max(1.0, template.LineSpacing);

        var fonts = new List<Font>();
        try
        {
            using var measureBitmap = new Bitmap(1, 1);
            using var measureGraphics = Graphics.FromImage(measureBitmap);
            measureGraphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            var ops = BuildOperations(
                template, values, contentWidth, spacing, measureGraphics, fonts);

            var contentHeight = ops.Sum(op => op.SpacingBefore + op.Height);
            var marginTop = Math.Max(0, template.MarginTop);
            var marginBottom = Math.Max(0, template.MarginBottom);
            var totalHeight = Math.Clamp(
                (int)Math.Ceiling(marginTop + contentHeight + marginBottom),
                1,
                MaxHeight);

            using var bitmap = new Bitmap(paperWidth, totalHeight, PixelFormat.Format24bppRgb);
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

                var y = (float)marginTop;
                foreach (var op in ops)
                {
                    y += (float)op.SpacingBefore;

                    switch (op.Kind)
                    {
                        case RenderOpKind.Text when op.Font is not null:
                            // Desenho por ponto: o overload com retângulo corta
                            // sublinhados e outros glifos abaixo da linha de base.
                            var lineWidth = (float)measureGraphics.MeasureString(
                                op.Text,
                                op.Font,
                                new PointF(0, 0),
                                StringFormat.GenericTypographic).Width;

                            var x = op.Alignment switch
                            {
                                TextAlignment.Centro => marginLeft + ((contentWidth - lineWidth) / 2f),
                                TextAlignment.Direita => marginLeft + contentWidth - lineWidth,
                                _ => (float)marginLeft,
                            };

                            graphics.DrawString(op.Text, op.Font, Brushes.Black, x, y, format);
                            break;

                        case RenderOpKind.SolidLine:
                            graphics.FillRectangle(
                                Brushes.Black,
                                marginLeft,
                                y,
                                contentWidth,
                                Math.Max(1, (float)op.Height));
                            break;
                    }

                    y += (float)op.Height;
                }
            }

            var gray = Grayscale.FromBitmap(bitmap);
            var binary = Dithering.Threshold(gray, paperWidth, totalHeight, Threshold);
            return CatPrinterRaster.PackPixels(paperWidth, totalHeight, binary);
        }
        finally
        {
            foreach (var font in fonts)
            {
                font.Dispose();
            }
        }
    }

    private static List<RenderOp> BuildOperations(
        DocumentTemplate template,
        IReadOnlyDictionary<string, string> values,
        int contentWidth,
        double spacing,
        Graphics measureGraphics,
        List<Font> fonts)
    {
        var ops = new List<RenderOp>();

        foreach (var block in template.Blocks)
        {
            switch (block.Kind)
            {
                case TemplateBlockKind.Texto:
                {
                    var text = TemplateComposer.Resolve(block.Text, values, template.Fields, out _);
                    var font = CreateFont(template, block, fonts);
                    var lineHeight = font.GetHeight(measureGraphics) * spacing;

                    var lines = TextRenderer.LayoutLines(
                        text,
                        font,
                        new TextSettings { WordWrap = true, LineSpacing = spacing },
                        contentWidth);

                    var alignment = block.Alignment ?? template.Alignment;
                    for (var i = 0; i < lines.Count; i++)
                    {
                        ops.Add(new RenderOp
                        {
                            Kind = RenderOpKind.Text,
                            Text = lines[i],
                            Font = font,
                            Alignment = alignment,
                            Height = lineHeight,
                            SpacingBefore = i == 0 ? Math.Max(0, block.SpacingBefore) : 0,
                        });
                    }

                    if (lines.Count == 0)
                    {
                        ops.Add(new RenderOp
                        {
                            Kind = RenderOpKind.Gap,
                            Height = lineHeight,
                            SpacingBefore = Math.Max(0, block.SpacingBefore),
                        });
                    }

                    if (block.SpacingAfter > 0)
                    {
                        ops.Add(new RenderOp { Kind = RenderOpKind.Gap, Height = block.SpacingAfter });
                    }

                    break;
                }

                case TemplateBlockKind.Divisor:
                {
                    var before = Math.Max(0, block.SpacingBefore);
                    if (block.DividerStyle == DividerStyle.LinhaSolida)
                    {
                        ops.Add(new RenderOp
                        {
                            Kind = RenderOpKind.SolidLine,
                            Height = 2,
                            SpacingBefore = before,
                        });
                    }
                    else
                    {
                        var font = CreateFont(template, block, fonts);
                        var lineHeight = font.GetHeight(measureGraphics) * spacing;
                        var character = block.DividerStyle switch
                        {
                            DividerStyle.Igual => '=',
                            DividerStyle.Sublinhado => '_',
                            _ => '-',
                        };

                        var charWidth = measureGraphics.MeasureString(
                            character.ToString(), font, new PointF(0, 0), StringFormat.GenericTypographic).Width;

                        var count = charWidth > 0
                            ? Math.Max(1, (int)Math.Floor(contentWidth / charWidth))
                            : contentWidth / 4;

                        ops.Add(new RenderOp
                        {
                            Kind = RenderOpKind.Text,
                            Text = new string(character, Math.Min(count, 400)),
                            Font = font,
                            Alignment = TextAlignment.Esquerda,
                            Height = lineHeight,
                            SpacingBefore = before,
                        });
                    }

                    if (block.SpacingAfter > 0)
                    {
                        ops.Add(new RenderOp { Kind = RenderOpKind.Gap, Height = block.SpacingAfter });
                    }

                    break;
                }

                case TemplateBlockKind.Espaco:
                {
                    ops.Add(new RenderOp
                    {
                        Kind = RenderOpKind.Gap,
                        Height = Math.Clamp(block.Height, 1, 400),
                        SpacingBefore = Math.Max(0, block.SpacingBefore),
                    });
                    break;
                }
            }
        }

        if (ops.Count == 0)
        {
            ops.Add(new RenderOp { Kind = RenderOpKind.Gap, Height = 12 });
        }

        return ops;
    }

    private static Font CreateFont(DocumentTemplate template, TemplateBlock block, List<Font> fonts)
    {
        var size = block.FontSize > 0 ? block.FontSize : template.FontSize;
        var settings = new TextSettings
        {
            FontSize = Math.Clamp(size, 6, 48),
            Bold = block.Bold || template.Bold,
            Italic = block.Italic,
        };

        var font = TextRenderer.CreateFont(settings);
        fonts.Add(font);
        return font;
    }

    private enum RenderOpKind
    {
        Text,
        SolidLine,
        Gap,
    }

    private sealed class RenderOp
    {
        public RenderOpKind Kind { get; init; }

        public string Text { get; init; } = string.Empty;

        public Font? Font { get; init; }

        public TextAlignment Alignment { get; init; } = TextAlignment.Esquerda;

        public double Height { get; init; }

        public double SpacingBefore { get; init; }
    }
}
