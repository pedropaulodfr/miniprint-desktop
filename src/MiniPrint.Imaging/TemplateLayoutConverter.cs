using MiniPrint.Core.Configuration;
using MiniPrint.Core.Templates;

namespace MiniPrint.Imaging;

/// <summary>
/// Converte um modelo de fluxo de blocos (formato antigo) em um modelo com
/// layout absoluto, preservando textos, campos, fontes e alinhamentos.
///
/// A conversão existe para que modelos criados antes do editor visual possam
/// ser abertos nele sem perder conteúdo.
/// </summary>
public static class TemplateLayoutConverter
{
    public static bool IsConvertible(DocumentTemplate template) =>
        !template.IsVisual && template.Blocks.Count > 0;

    /// <summary>Devolve uma cópia do modelo com <see cref="DocumentTemplate.Layout"/> preenchido.</summary>
    public static DocumentTemplate ToVisual(DocumentTemplate source)
    {
        var template = source.Clone();

        var layout = new DocumentLayout
        {
            Width = Math.Clamp(template.PaperWidth, 64, 576),
            MarginLeft = template.MarginLeft,
            MarginRight = template.MarginRight,
            MarginTop = template.MarginTop,
            MarginBottom = template.MarginBottom,
            AutoHeight = true,
        };

        var contentWidth = Math.Max(16, layout.Width - layout.MarginLeft - layout.MarginRight);
        var y = layout.MarginTop;

        using var measureBitmap = new System.Drawing.Bitmap(1, 1);
        using var graphics = System.Drawing.Graphics.FromImage(measureBitmap);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        foreach (var block in template.Blocks)
        {
            y += Math.Max(0, block.SpacingBefore);

            switch (block.Kind)
            {
                case TemplateBlockKind.Texto:
                {
                    var fontSize = block.FontSize > 0 ? block.FontSize : template.FontSize;
                    var element = new LayoutElement
                    {
                        Kind = LayoutElementKind.Texto,
                        Name = ShortName(block.Text, "Texto"),
                        X = layout.MarginLeft,
                        Y = y,
                        Width = contentWidth,
                        Text = block.Text,
                        FontSize = fontSize,
                        Bold = block.Bold || template.Bold,
                        Italic = block.Italic,
                        Alignment = block.Alignment ?? template.Alignment,
                        LineSpacing = template.LineSpacing,
                        AutoHeight = true,
                    };

                    element.Height = Math.Max(8, VisualTemplateRenderer.MeasureTextHeight(element, template));
                    layout.Elements.Add(element);
                    y += element.Height;
                    break;
                }

                case TemplateBlockKind.Divisor:
                {
                    layout.Elements.Add(new LayoutElement
                    {
                        Kind = LayoutElementKind.Linha,
                        Name = "Linha divisória",
                        X = layout.MarginLeft,
                        Y = y + 2,
                        Width = contentWidth,
                        Height = 2,
                        Thickness = block.DividerStyle == DividerStyle.LinhaSolida ? 2 : 1,
                    });

                    y += 6;
                    break;
                }

                case TemplateBlockKind.Espaco:
                    y += Math.Clamp(block.Height, 1, 400);
                    break;
            }

            y += Math.Max(0, block.SpacingAfter);
        }

        layout.Elements = layout.Elements.OrderBy(e => e.Y).ToList();
        template.Layout = layout;
        return template;
    }

    private static string ShortName(string text, string fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        var clean = TemplateComposer.TokenRegex.Replace(text, m => "«" + m.Groups["key"].Value + "»");
        clean = clean.Trim();
        return clean.Length <= 28 ? clean : clean[..28] + "…";
    }
}
