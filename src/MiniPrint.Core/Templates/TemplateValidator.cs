using System.Text.RegularExpressions;

namespace MiniPrint.Core.Templates;

public sealed record TemplateValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;

    public string Summary =>
        IsValid
            ? (Warnings.Count == 0 ? "Modelo válido." : $"{Warnings.Count} aviso(s).")
            : $"{Errors.Count} erro(s) impedem salvar.";
}

/// <summary>Validação estrutural do modelo e dos valores preenchidos.</summary>
public static class TemplateValidator
{
    private static readonly Regex KeyPattern =
        new("^[A-Za-z0-9_][A-Za-z0-9_\\-\\.]*$", RegexOptions.Compiled);

    public static TemplateValidationResult ValidateStructure(DocumentTemplate template)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(template.Name))
        {
            errors.Add("O modelo precisa de um nome.");
        }

        if (template.PaperWidth is < 64 or > 576)
        {
            errors.Add("A largura do documento deve ficar entre 64 e 576 pontos.");
        }

        if (template.IsVisual)
        {
            ValidateLayout(template, errors, warnings);
        }
        else if (template.Blocks.Count == 0)
        {
            errors.Add("O documento está vazio: adicione ao menos um bloco.");
        }

        if (template.FontSize is < 6 or > 48)
        {
            errors.Add("O tamanho de fonte do modelo deve ficar entre 6 e 48.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in template.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Key))
            {
                errors.Add($"Há um campo sem nome interno (rótulo \"{field.Label}\").");
                continue;
            }

            if (!KeyPattern.IsMatch(field.Key))
            {
                errors.Add(
                    $"A chave \"{field.Key}\" é inválida. Use letras, números, _ , - ou .");
            }

            if (!seen.Add(field.Key))
            {
                errors.Add($"A chave \"{field.Key}\" está repetida.");
            }

            if (string.IsNullOrWhiteSpace(field.Label))
            {
                warnings.Add($"O campo \"{field.Key}\" não tem rótulo de exibição.");
            }

            if (field.Type == TemplateFieldType.Lista && field.Options.Count == 0)
            {
                warnings.Add($"O campo \"{field.Label}\" é uma lista sem opções.");
            }
        }

        if (template.Fields.Count == 0)
        {
            warnings.Add("O modelo não tem campos dinâmicos; o documento será sempre igual.");
        }

        var used = TemplateComposer.UsedKeys(template);
        foreach (var key in used)
        {
            if (template.FindField(key) is null)
            {
                warnings.Add($"O texto usa o campo \"{key}\", que não existe.");
            }
        }

        var usedInLayout = template.IsVisual
            ? template.Layout!.Elements
                .Where(e => e.Kind == LayoutElementKind.Campo && !string.IsNullOrWhiteSpace(e.FieldKey))
                .Select(e => e.FieldKey!)
                .ToList()
            : new List<string>();

        foreach (var field in template.Fields)
        {
            var inBlocks = used.Contains(field.Key, StringComparer.OrdinalIgnoreCase);
            var inLayout = usedInLayout.Contains(field.Key, StringComparer.OrdinalIgnoreCase);

            if (!inBlocks && !inLayout)
            {
                warnings.Add($"O campo \"{field.Label}\" não é usado no documento.");
            }
        }

        return new TemplateValidationResult(errors, warnings);
    }

    private static void ValidateLayout(
        DocumentTemplate template,
        List<string> errors,
        List<string> warnings)
    {
        var layout = template.Layout!;

        if (layout.Width is < 64 or > 576)
        {
            errors.Add("A largura do layout deve ficar entre 64 e 576 pontos.");
        }

        if (layout.Elements.Count == 0)
        {
            errors.Add("O documento está vazio: adicione ao menos um elemento.");
        }

        var ids = new HashSet<Guid>();
        foreach (var element in layout.Elements)
        {
            if (!ids.Add(element.Id))
            {
                errors.Add($"O elemento \"{element.Name}\" tem identificador repetido.");
            }

            if (element.Right > layout.Width)
            {
                warnings.Add(
                    $"O elemento \"{element.Name}\" passa da largura do papel " +
                    $"({element.Right} > {layout.Width} pontos) e será cortado.");
            }

            if (element.Kind == LayoutElementKind.Campo)
            {
                if (string.IsNullOrWhiteSpace(element.FieldKey))
                {
                    errors.Add($"O campo \"{element.Name}\" não tem campo dinâmico associado.");
                }
                else if (template.FindField(element.FieldKey) is null)
                {
                    errors.Add(
                        $"O elemento \"{element.Name}\" aponta para o campo " +
                        $"\"{element.FieldKey}\", que não existe.");
                }
            }

            if (element.Kind == LayoutElementKind.Imagem && string.IsNullOrEmpty(element.ImageData))
            {
                warnings.Add($"A imagem \"{element.Name}\" está sem arquivo.");
            }
        }
    }

    /// <summary>Valida os valores preenchidos; devolve chave → mensagem de erro.</summary>
    public static Dictionary<string, string> ValidateValues(
        DocumentTemplate template,
        IReadOnlyDictionary<string, string> values)
    {
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in template.Fields)
        {
            values.TryGetValue(field.Key, out var raw);
            var error = TemplateFieldFormatter.Validate(field, raw);
            if (error is not null)
            {
                errors[field.Key] = error;
            }
        }

        return errors;
    }
}
