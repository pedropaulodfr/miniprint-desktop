using System.Globalization;
using System.Text;

namespace MiniPrint.Core.Templates;

/// <summary>
/// Máscaras, formatação e validação dos campos dinâmicos.
/// Os valores são armazenados como o usuário digitou e formatados apenas na
/// hora de compor o documento.
/// </summary>
public static class TemplateFieldFormatter
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Remove tudo que não for dígito.</summary>
    public static string DigitsOnly(string? text) =>
        new((text ?? string.Empty).Where(char.IsDigit).ToArray());

    /// <summary>Formata o valor conforme o tipo do campo, para exibição no documento.</summary>
    public static string Format(TemplateField field, string? rawValue)
    {
        var raw = (rawValue ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        return field.Type switch
        {
            TemplateFieldType.Cpf => FormatCpf(raw),
            TemplateFieldType.Cnpj => FormatCnpj(raw),
            TemplateFieldType.Telefone => FormatPhone(raw),
            TemplateFieldType.Monetario => TryParseMoney(raw, out var money)
                ? money.ToString("N2", PtBr)
                : raw,
            TemplateFieldType.Data => TryParseDate(raw, out var date)
                ? date.ToString("dd/MM/yyyy", PtBr)
                : raw,
            TemplateFieldType.Numero => TryParseNumber(raw, out var number)
                ? number.ToString("0.##", PtBr)
                : raw,
            _ => raw,
        };
    }

    /// <summary>Aplica a máscara de digitação (usada ao sair do campo).</summary>
    public static string ApplyMask(TemplateFieldType type, string? text)
    {
        var raw = (text ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        return type switch
        {
            TemplateFieldType.Cpf => FormatCpf(raw),
            TemplateFieldType.Cnpj => FormatCnpj(raw),
            TemplateFieldType.Telefone => FormatPhone(raw),
            TemplateFieldType.Monetario => TryParseMoney(raw, out var money)
                ? money.ToString("N2", PtBr)
                : raw,
            _ => raw,
        };
    }

    public static string FormatCpf(string value)
    {
        var digits = DigitsOnly(value);
        if (digits.Length != 11)
        {
            return value.Trim();
        }

        return $"{digits[..3]}.{digits[3..6]}.{digits[6..9]}-{digits[9..]}";
    }

    public static string FormatCnpj(string value)
    {
        var digits = DigitsOnly(value);
        if (digits.Length != 14)
        {
            return value.Trim();
        }

        return $"{digits[..2]}.{digits[2..5]}.{digits[5..8]}/{digits[8..12]}-{digits[12..]}";
    }

    public static string FormatPhone(string value)
    {
        var digits = DigitsOnly(value);
        return digits.Length switch
        {
            11 => $"({digits[..2]}) {digits[2..7]}-{digits[7..]}",
            10 => $"({digits[..2]}) {digits[2..6]}-{digits[6..]}",
            _ => value.Trim(),
        };
    }

    /// <summary>Aceita "150", "150,00", "1.234,56", "R$ 150,00" e "150.00".</summary>
    public static bool TryParseMoney(string? text, out decimal value)
    {
        value = 0;
        var raw = (text ?? string.Empty)
            .Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u00A0", string.Empty)
            .Replace(" ", string.Empty)
            .Trim();

        if (raw.Length == 0)
        {
            return false;
        }

        var negative = raw.StartsWith('-') || (raw.StartsWith('(') && raw.EndsWith(')'));
        raw = raw.Trim('(', ')', '-', '+');

        string normalized;
        var hasComma = raw.Contains(',');
        var hasDot = raw.Contains('.');

        if (hasComma)
        {
            // Formato brasileiro: ponto é separador de milhar.
            normalized = raw.Replace(".", string.Empty).Replace(',', '.');
        }
        else if (hasDot)
        {
            var parts = raw.Split('.');
            var lastGroup = parts[^1];
            // "1.500" e "1.234.567" são milhares; "150.00" é decimal.
            normalized = lastGroup.Length == 3 && parts.Length >= 2
                ? raw.Replace(".", string.Empty)
                : raw;
        }
        else
        {
            normalized = raw;
        }

        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        if (negative)
        {
            value = -value;
        }

        return true;
    }

    public static bool TryParseDate(string? text, out DateTime value)
    {
        value = default;
        var raw = (text ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return false;
        }

        string[] formats =
        {
            "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "yyyy-MM-dd",
            "dd.MM.yyyy", "dd/MM/yy", "d/M/yy",
        };

        return DateTime.TryParseExact(raw, formats, PtBr, DateTimeStyles.None, out value)
               || DateTime.TryParse(raw, PtBr, DateTimeStyles.None, out value);
    }

    public static bool TryParseNumber(string? text, out decimal value)
    {
        value = 0;
        var raw = (text ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return false;
        }

        return decimal.TryParse(raw, NumberStyles.Number, PtBr, out value)
               || decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Valida o valor de um campo. Devolve a mensagem de erro, ou nulo.</summary>
    public static string? Validate(TemplateField field, string? rawValue)
    {
        var raw = (rawValue ?? string.Empty).Trim();

        if (raw.Length == 0)
        {
            return field.Required ? "Campo obrigatório." : null;
        }

        switch (field.Type)
        {
            case TemplateFieldType.Cpf:
            {
                var digits = DigitsOnly(raw);
                if (digits.Length != 11)
                {
                    return "CPF deve ter 11 dígitos.";
                }

                return IsValidCpf(digits) ? null : "CPF inválido (dígitos verificadores não conferem).";
            }

            case TemplateFieldType.Cnpj:
            {
                var digits = DigitsOnly(raw);
                if (digits.Length != 14)
                {
                    return "CNPJ deve ter 14 dígitos.";
                }

                return IsValidCnpj(digits) ? null : "CNPJ inválido (dígitos verificadores não conferem).";
            }

            case TemplateFieldType.Telefone:
            {
                var digits = DigitsOnly(raw);
                return digits.Length is 10 or 11 ? null : "Telefone deve ter 10 ou 11 dígitos.";
            }

            case TemplateFieldType.Monetario:
                return TryParseMoney(raw, out _) ? null : "Valor inválido. Use, por exemplo, 150,00.";

            case TemplateFieldType.Data:
                return TryParseDate(raw, out _) ? null : "Data inválida. Use dd/mm/aaaa.";

            case TemplateFieldType.Numero:
                return TryParseNumber(raw, out _) ? null : "Número inválido.";

            case TemplateFieldType.Lista:
                if (field.Options.Count > 0 && !field.Options.Contains(raw, StringComparer.OrdinalIgnoreCase))
                {
                    return "Selecione uma das opções.";
                }

                return null;

            default:
                return null;
        }
    }

    /// <summary>CPF com os dois dígitos verificadores.</summary>
    public static bool IsValidCpf(string cpf)
    {
        var digits = DigitsOnly(cpf);
        if (digits.Length != 11)
        {
            return false;
        }

        // Sequências repetidas passam no cálculo, mas não são CPFs válidos.
        if (digits.Distinct().Count() == 1)
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (digits[i] - '0') * (10 - i);
        }

        var first = (sum * 10) % 11;
        if (first == 10)
        {
            first = 0;
        }

        if (first != digits[9] - '0')
        {
            return false;
        }

        sum = 0;
        for (var i = 0; i < 10; i++)
        {
            sum += (digits[i] - '0') * (11 - i);
        }

        var second = (sum * 10) % 11;
        if (second == 10)
        {
            second = 0;
        }

        return second == digits[10] - '0';
    }

    /// <summary>CNPJ com os dois dígitos verificadores.</summary>
    public static bool IsValidCnpj(string cnpj)
    {
        var digits = DigitsOnly(cnpj);
        if (digits.Length != 14)
        {
            return false;
        }

        if (digits.Distinct().Count() == 1)
        {
            return false;
        }

        int[] weights1 = { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        int[] weights2 = { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };

        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            sum += (digits[i] - '0') * weights1[i];
        }

        var rest = sum % 11;
        var first = rest < 2 ? 0 : 11 - rest;
        if (first != digits[12] - '0')
        {
            return false;
        }

        sum = 0;
        for (var i = 0; i < 13; i++)
        {
            sum += (digits[i] - '0') * weights2[i];
        }

        rest = sum % 11;
        var second = rest < 2 ? 0 : 11 - rest;
        return second == digits[13] - '0';
    }

    /// <summary>Gera uma chave interna a partir de um rótulo: "Nome do pagador" → "nome_do_pagador".</summary>
    public static string SuggestKey(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return "campo";
        }

        var normalized = label.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }
        }

        var key = builder.ToString().Trim('_');
        return key.Length == 0 ? "campo" : key;
    }
}
