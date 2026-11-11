using MiniPrint.Core.Templates;

namespace MiniPrint.Desktop.ViewModels;

/// <summary>
/// Opção do seletor de tipo de campo: o valor gravado no modelo e o texto
/// mostrado na interface.
/// </summary>
public sealed record TemplateFieldTypeChoice(TemplateFieldType Type, string Label)
{
    public static IReadOnlyList<TemplateFieldTypeChoice> All { get; } = new[]
    {
        new TemplateFieldTypeChoice(TemplateFieldType.TextoCurto, "Texto curto"),
        new TemplateFieldTypeChoice(TemplateFieldType.TextoLongo, "Texto longo (várias linhas)"),
        new TemplateFieldTypeChoice(TemplateFieldType.Numero, "Número"),
        new TemplateFieldTypeChoice(TemplateFieldType.Monetario, "Valor (decimal, R$)"),
        new TemplateFieldTypeChoice(TemplateFieldType.Data, "Data (dd/mm/aaaa)"),
        new TemplateFieldTypeChoice(TemplateFieldType.Cpf, "CPF"),
        new TemplateFieldTypeChoice(TemplateFieldType.Cnpj, "CNPJ"),
        new TemplateFieldTypeChoice(TemplateFieldType.Telefone, "Telefone"),
        new TemplateFieldTypeChoice(TemplateFieldType.Lista, "Lista de opções"),
    };
}
