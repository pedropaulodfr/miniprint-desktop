using System.Text;
using System.Text.RegularExpressions;

namespace MiniPrint.Core.Templates;

/// <summary>
/// Substituição dos campos dinâmicos no texto do modelo.
///
/// Um campo é escrito como <c>{{chave}}</c>; espaços internos são tolerados
/// (<c>{{ chave }}</c>). A substituição usa o valor formatado conforme o tipo.
/// </summary>
public static class TemplateComposer
{
    public static readonly Regex TokenRegex =
        new(@"\{\{\s*(?<key>[A-Za-z0-9_\-\.]+)\s*\}\}", RegexOptions.Compiled);

    /// <summary>Lista as chaves citadas em um texto, sem repetição e em ordem.</summary>
    public static IReadOnlyList<string> ExtractTokens(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<string>();
        }

        var keys = new List<string>();
        foreach (Match match in TokenRegex.Matches(text))
        {
            var key = match.Groups["key"].Value;
            if (!keys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    /// <summary>
    /// Substitui os campos pelos valores. Chaves sem campo correspondente são
    /// removidas do texto e reportadas em <paramref name="unknownKeys"/>.
    /// </summary>
    public static string Resolve(
        string? text,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyList<TemplateField> fields,
        out IReadOnlyList<string> unknownKeys)
    {
        var unknown = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            unknownKeys = unknown;
            return string.Empty;
        }

        var result = TokenRegex.Replace(text, match =>
        {
            var key = match.Groups["key"].Value;
            var field = fields.FirstOrDefault(
                f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

            if (field is null)
            {
                if (!unknown.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    unknown.Add(key);
                }

                return string.Empty;
            }

            values.TryGetValue(field.Key, out var raw);
            return TemplateFieldFormatter.Format(field, raw);
        });

        unknownKeys = unknown;
        return result;
    }

    /// <summary>
    /// Versão para exibição na lista de blocos: troca <c>{{chave}}</c> pelo
    /// rótulo entre marcadores, para o usuário não ver a chave crua.
    /// </summary>
    public static string Describe(string? text, IReadOnlyList<TemplateField> fields)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return TokenRegex.Replace(text, match =>
        {
            var key = match.Groups["key"].Value;
            var field = fields.FirstOrDefault(
                f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

            return field is null ? $"«{key}?»" : $"«{field.Label}»";
        });
    }

    /// <summary>
    /// Prévia do documento com os valores preenchidos, usada em listagens.
    /// </summary>
    public static string Preview(DocumentTemplate template, IReadOnlyDictionary<string, string> values)
    {
        var builder = new StringBuilder();
        foreach (var block in template.Blocks)
        {
            switch (block.Kind)
            {
                case TemplateBlockKind.Texto:
                    builder.AppendLine(Resolve(block.Text, values, template.Fields, out _));
                    break;
                case TemplateBlockKind.Divisor:
                    builder.AppendLine(new string('-', 24));
                    break;
                default:
                    builder.AppendLine();
                    break;
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>Chaves usadas em algum bloco do modelo.</summary>
    public static IReadOnlyList<string> UsedKeys(DocumentTemplate template)
    {
        var keys = new List<string>();
        foreach (var block in template.Blocks.Where(b => b.Kind == TemplateBlockKind.Texto))
        {
            foreach (var key in ExtractTokens(block.Text))
            {
                if (!keys.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }

    /// <summary>Insere um campo no texto, na posição indicada (ou no fim).</summary>
    public static string InsertField(string? text, string key, int caretIndex)
    {
        text ??= string.Empty;
        var token = $"{{{{{key}}}}}";
        var index = Math.Clamp(caretIndex, 0, text.Length);

        // Evita colar o campo no meio de uma palavra.
        if (index > 0 && index < text.Length && !char.IsWhiteSpace(text[index - 1]) && !char.IsWhiteSpace(text[index]))
        {
            token = " " + token;
        }

        return text.Insert(index, token);
    }
}
