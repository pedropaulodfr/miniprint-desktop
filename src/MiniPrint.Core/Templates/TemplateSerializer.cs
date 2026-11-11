using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniPrint.Core.Templates;

/// <summary>Envelope do arquivo de exportação de modelo.</summary>
public sealed class TemplateFileEnvelope
{
    public const string FormatId = "miniprint.template";

    public string Format { get; set; } = FormatId;

    /// <summary>Versão 1: fluxo de blocos. Versão 2: inclui layout absoluto.</summary>
    public int Version { get; set; } = CurrentVersion;

    public const int CurrentVersion = 2;

    public DocumentTemplate? Template { get; set; }
}

public sealed record TemplateImportResult(
    bool Success,
    DocumentTemplate? Template,
    IReadOnlyList<string> Errors)
{
    public string ErrorText => string.Join(Environment.NewLine, Errors);
}

/// <summary>Exportação e importação de modelos em JSON.</summary>
public static class TemplateSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(DocumentTemplate template)
    {
        var envelope = new TemplateFileEnvelope { Template = template };
        return JsonSerializer.Serialize(envelope, Options);
    }

    /// <summary>
    /// Lê o JSON e valida a estrutura antes de devolver o modelo. Um arquivo
    /// inválido nunca é aceito parcialmente.
    /// </summary>
    public static TemplateImportResult Deserialize(string json)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(json))
        {
            return new TemplateImportResult(false, null, new[] { "O arquivo está vazio." });
        }

        TemplateFileEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<TemplateFileEnvelope>(json, Options);
        }
        catch (JsonException ex)
        {
            return new TemplateImportResult(
                false,
                null,
                new[] { $"JSON inválido: {ex.Message}" });
        }

        if (envelope is null)
        {
            return new TemplateImportResult(false, null, new[] { "Não foi possível ler o modelo." });
        }

        if (!string.Equals(envelope.Format, TemplateFileEnvelope.FormatId, StringComparison.Ordinal))
        {
            errors.Add(
                "O arquivo não é um modelo do MiniPrint Desktop " +
                $"(formato encontrado: \"{envelope.Format}\").");
        }

        if (envelope.Version is < 1 or > TemplateFileEnvelope.CurrentVersion)
        {
            errors.Add($"Versão de arquivo não suportada: {envelope.Version}.");
        }

        var template = envelope.Template;
        if (template is null)
        {
            errors.Add("O arquivo não contém um modelo.");
            return new TemplateImportResult(false, null, errors);
        }

        template.Blocks ??= new List<TemplateBlock>();
        template.Fields ??= new List<TemplateField>();

        var validation = TemplateValidator.ValidateStructure(template);
        errors.AddRange(validation.Errors);

        if (errors.Count > 0)
        {
            return new TemplateImportResult(false, null, errors);
        }

        // Importar sempre cria um modelo novo, sem sobrescrever o existente.
        var imported = template.Clone(newIdentity: true);
        if (string.IsNullOrWhiteSpace(imported.Description))
        {
            imported.Description = "Importado de arquivo.";
        }

        return new TemplateImportResult(true, imported, Array.Empty<string>());
    }
}
