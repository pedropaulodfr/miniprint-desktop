using MiniPrint.Core.Configuration;
using MiniPrint.Core.Templates;
using Xunit;

namespace MiniPrint.Tests;

public class TemplateFormatterTests
{
    [Theory]
    [InlineData("12345678909", "123.456.789-09")]
    [InlineData("123.456.789-09", "123.456.789-09")]
    [InlineData("123", "123")]
    public void FormatCpf_AppliesMask(string input, string expected)
    {
        Assert.Equal(expected, TemplateFieldFormatter.FormatCpf(input));
    }

    [Theory]
    [InlineData("12345678909", true)]
    [InlineData("123.456.789-09", true)]
    [InlineData("11111111111", false)]
    [InlineData("12345678900", false)]
    [InlineData("123", false)]
    public void IsValidCpf_ChecksVerifierDigits(string cpf, bool expected)
    {
        Assert.Equal(expected, TemplateFieldFormatter.IsValidCpf(cpf));
    }

    [Theory]
    [InlineData("11222333000181", true)]
    [InlineData("11.222.333/0001-81", true)]
    [InlineData("11111111111111", false)]
    [InlineData("11222333000180", false)]
    public void IsValidCnpj_ChecksVerifierDigits(string cnpj, bool expected)
    {
        Assert.Equal(expected, TemplateFieldFormatter.IsValidCnpj(cnpj));
    }

    [Theory]
    [InlineData("11987654321", "(11) 98765-4321")]
    [InlineData("1133334444", "(11) 3333-4444")]
    public void FormatPhone_AppliesMask(string input, string expected)
    {
        Assert.Equal(expected, TemplateFieldFormatter.FormatPhone(input));
    }

    [Theory]
    [InlineData("150", 150)]
    [InlineData("150,00", 150)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("R$ 1.234,56", 1234.56)]
    [InlineData("150.00", 150)]
    [InlineData("1.500", 1500)]
    [InlineData("0,99", 0.99)]
    public void TryParseMoney_AcceptsBrazilianFormats(string input, double expected)
    {
        Assert.True(TemplateFieldFormatter.TryParseMoney(input, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("R$")]
    public void TryParseMoney_RejectsInvalidInput(string input)
    {
        Assert.False(TemplateFieldFormatter.TryParseMoney(input, out _));
    }

    [Fact]
    public void FormatMoney_UsesBrazilianNotation()
    {
        var field = new TemplateField { Key = "v", Type = TemplateFieldType.Monetario };
        Assert.Equal("1.234,56", TemplateFieldFormatter.Format(field, "1234,56"));
        Assert.Equal("150,00", TemplateFieldFormatter.Format(field, "150"));
    }

    [Fact]
    public void FormatDate_UsesDayMonthYear()
    {
        var field = new TemplateField { Key = "d", Type = TemplateFieldType.Data };
        Assert.Equal("05/11/2026", TemplateFieldFormatter.Format(field, "2026-11-05"));
        Assert.Equal("05/11/2026", TemplateFieldFormatter.Format(field, "05/11/2026"));
    }

    [Fact]
    public void Format_EmptyValueStaysEmpty()
    {
        var field = new TemplateField { Key = "x", Type = TemplateFieldType.Cpf };
        Assert.Equal(string.Empty, TemplateFieldFormatter.Format(field, null));
        Assert.Equal(string.Empty, TemplateFieldFormatter.Format(field, "   "));
    }

    [Fact]
    public void SuggestKey_NormalizesLabel()
    {
        Assert.Equal("nome_do_pagador", TemplateFieldFormatter.SuggestKey("Nome do pagador"));
        Assert.Equal("valor", TemplateFieldFormatter.SuggestKey("Valor"));
        Assert.Equal("cpf_cnpj", TemplateFieldFormatter.SuggestKey("CPF/CNPJ"));
        Assert.Equal("referente_a", TemplateFieldFormatter.SuggestKey("Referente à"));
    }

    [Fact]
    public void Validate_RequiredFieldReportsError()
    {
        var field = new TemplateField { Key = "nome", Label = "Nome", Required = true };
        Assert.Equal("Campo obrigatório.", TemplateFieldFormatter.Validate(field, ""));
    }

    [Fact]
    public void Validate_OptionalEmptyFieldIsAccepted()
    {
        var field = new TemplateField { Key = "obs", Label = "Obs", Required = false };
        Assert.Null(TemplateFieldFormatter.Validate(field, ""));
    }

    [Fact]
    public void Validate_CpfRejectsInvalidCheckDigits()
    {
        var field = new TemplateField { Key = "cpf", Type = TemplateFieldType.Cpf };
        Assert.NotNull(TemplateFieldFormatter.Validate(field, "123.456.789-00"));
        Assert.Null(TemplateFieldFormatter.Validate(field, "123.456.789-09"));
    }

    [Fact]
    public void Validate_ListAcceptsOnlyConfiguredOptions()
    {
        var field = new TemplateField
        {
            Key = "forma",
            Type = TemplateFieldType.Lista,
            Options = new List<string> { "PIX", "Dinheiro" },
        };

        Assert.Null(TemplateFieldFormatter.Validate(field, "PIX"));
        Assert.NotNull(TemplateFieldFormatter.Validate(field, "Cartão"));
    }
}

public class TemplateComposerTests
{
    private static DocumentTemplate TemplateWithFields() => new()
    {
        Name = "Teste",
        Fields =
        {
            new TemplateField { Key = "nome", Label = "Nome", Type = TemplateFieldType.TextoCurto },
            new TemplateField { Key = "valor", Label = "Valor", Type = TemplateFieldType.Monetario },
        },
    };

    [Fact]
    public void Resolve_ReplacesTokensWithFormattedValues()
    {
        var template = TemplateWithFields();
        var values = new Dictionary<string, string> { ["nome"] = "Ana", ["valor"] = "150" };

        var text = TemplateComposer.Resolve(
            "Recebemos de {{nome}} o valor de R$ {{valor}}.", values, template.Fields, out var unknown);

        Assert.Equal("Recebemos de Ana o valor de R$ 150,00.", text);
        Assert.Empty(unknown);
    }

    [Fact]
    public void Resolve_AcceptsSpacesInsideBraces()
    {
        var template = TemplateWithFields();
        var values = new Dictionary<string, string> { ["nome"] = "Ana" };

        var text = TemplateComposer.Resolve("Olá {{ nome }}!", values, template.Fields, out _);
        Assert.Equal("Olá Ana!", text);
    }

    [Fact]
    public void Resolve_EmptyOptionalValueBecomesEmptyText()
    {
        var template = TemplateWithFields();
        var values = new Dictionary<string, string> { ["nome"] = "Ana", ["valor"] = "" };

        var text = TemplateComposer.Resolve("[{{valor}}]", values, template.Fields, out _);
        Assert.Equal("[]", text);
    }

    [Fact]
    public void Resolve_ReportsUnknownKeys()
    {
        var template = TemplateWithFields();
        var text = TemplateComposer.Resolve("{{inexistente}}", new Dictionary<string, string>(),
            template.Fields, out var unknown);

        Assert.Equal(string.Empty, text);
        Assert.Contains("inexistente", unknown);
    }

    [Fact]
    public void ExtractTokens_FindsAllUniqueKeys()
    {
        var tokens = TemplateComposer.ExtractTokens("{{a}} {{b}} {{a}} {{ c }}");
        Assert.Equal(new[] { "a", "b", "c" }, tokens);
    }

    [Fact]
    public void Describe_ReplacesTokensWithLabels()
    {
        var template = TemplateWithFields();
        Assert.Equal("Nome: «Nome»", TemplateComposer.Describe("Nome: {{nome}}", template.Fields));
    }

    [Fact]
    public void InsertField_AddsTokenAtCaret()
    {
        Assert.Equal("Recebemos de {{nome}}", TemplateComposer.InsertField("Recebemos de ", "nome", 13));
    }

    [Fact]
    public void UsedKeys_ListsOnlyTokensInTextBlocks()
    {
        var template = TemplateWithFields();
        template.Blocks.Add(new TemplateBlock { Kind = TemplateBlockKind.Texto, Text = "{{nome}}" });
        template.Blocks.Add(new TemplateBlock { Kind = TemplateBlockKind.Divisor });

        Assert.Equal(new[] { "nome" }, TemplateComposer.UsedKeys(template));
    }
}

public class TemplateValidatorTests
{
    [Fact]
    public void ValidateStructure_AcceptsSampleTemplate()
    {
        var result = TemplateValidator.ValidateStructure(TemplateSamples.CreatePaymentReceipt());
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void ValidateStructure_RequiresName()
    {
        var template = TemplateSamples.CreatePaymentReceipt();
        template.Name = "  ";

        var result = TemplateValidator.ValidateStructure(template);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("nome"));
    }

    [Fact]
    public void ValidateStructure_RequiresAtLeastOneBlock()
    {
        var template = TemplateSamples.CreatePaymentReceipt();
        template.Blocks.Clear();

        Assert.False(TemplateValidator.ValidateStructure(template).IsValid);
    }

    [Fact]
    public void ValidateStructure_RejectsDuplicateKeys()
    {
        var template = TemplateSamples.CreatePaymentReceipt();
        template.Fields.Add(new TemplateField { Key = "valor", Label = "Outro valor" });

        var result = TemplateValidator.ValidateStructure(template);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("repetida"));
    }

    [Fact]
    public void ValidateStructure_WarnsAboutUnusedField()
    {
        var template = TemplateSamples.CreatePaymentReceipt();
        template.Fields.Add(new TemplateField { Key = "extra", Label = "Extra" });

        var result = TemplateValidator.ValidateStructure(template);
        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("não é usado"));
    }

    [Fact]
    public void ValidateStructure_WarnsAboutTokenWithoutField()
    {
        var template = TemplateSamples.CreatePaymentReceipt();
        template.Blocks.Add(new TemplateBlock { Kind = TemplateBlockKind.Texto, Text = "{{fantasma}}" });

        var result = TemplateValidator.ValidateStructure(template);
        Assert.Contains(result.Warnings, w => w.Contains("fantasma"));
    }

    [Fact]
    public void ValidateValues_ReportsMissingRequiredFields()
    {
        var template = TemplateSamples.CreatePaymentReceipt();
        var errors = TemplateValidator.ValidateValues(template, new Dictionary<string, string>());

        Assert.Contains("nome_pagador", errors.Keys);
        Assert.Contains("valor", errors.Keys);
    }

    [Fact]
    public void ValidateValues_AcceptsCompleteForm()
    {
        var template = TemplateSamples.CreatePaymentReceipt();
        var values = new Dictionary<string, string>
        {
            ["nome_pagador"] = "Ana Silva",
            ["documento"] = "123.456.789-09",
            ["valor"] = "150,00",
            ["descricao"] = "Serviço prestado",
            ["forma_pagamento"] = "PIX",
            ["data_pagamento"] = "05/11/2026",
        };

        Assert.Empty(TemplateValidator.ValidateValues(template, values));
    }
}
