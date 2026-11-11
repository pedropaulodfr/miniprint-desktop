using MiniPrint.Core.Templates;

namespace MiniPrint.Imaging;

/// <summary>
/// Modelos de exemplo já no formato do editor visual.
///
/// São gerados a partir dos modelos de fluxo e convertidos para posicionamento
/// absoluto, garantindo que os dois editores produzam o mesmo conteúdo.
/// </summary>
public static class VisualTemplateSamples
{
    public static IReadOnlyList<DocumentTemplate> All() => new[]
    {
        Convert(TemplateSamples.CreatePaymentReceipt()),
        Convert(TemplateSamples.CreateServiceReceipt()),
        Convert(TemplateSamples.CreateProductLabel()),
        Convert(TemplateSamples.CreateServiceOrder()),
    };

    public static DocumentTemplate Convert(DocumentTemplate flowTemplate)
    {
        var visual = TemplateLayoutConverter.ToVisual(flowTemplate);
        visual.Description = string.IsNullOrWhiteSpace(flowTemplate.Description)
            ? "Modelo de exemplo do editor visual."
            : flowTemplate.Description;
        return visual;
    }
}
