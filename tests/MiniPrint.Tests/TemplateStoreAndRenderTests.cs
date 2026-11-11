using MiniPrint.Core.Models;
using MiniPrint.Core.Templates;
using MiniPrint.Imaging;
using Xunit;

namespace MiniPrint.Tests;

public class TemplateStoreTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"miniprint-templates-{Guid.NewGuid():N}.json");

    [Fact]
    public void Store_RoundTripsTemplate()
    {
        var path = TempPath();
        try
        {
            var store = new JsonTemplateStore(path);
            var template = TemplateSamples.CreatePaymentReceipt();

            store.Save(new[] { template });
            var loaded = store.Load();

            var single = Assert.Single(loaded);
            Assert.Equal(template.Id, single.Id);
            Assert.Equal(template.Name, single.Name);
            Assert.Equal(template.Fields.Count, single.Fields.Count);
            Assert.Equal(template.Blocks.Count, single.Blocks.Count);
            Assert.Equal("nome_pagador", single.Fields[0].Key);
            Assert.Equal(TemplateFieldType.Monetario, single.Fields[2].Type);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Store_PersistsBetweenInstances()
    {
        var path = TempPath();
        try
        {
            new JsonTemplateStore(path).Save(TemplateSamples.All());

            var reopened = new JsonTemplateStore(path).Load();

            Assert.Equal(TemplateSamples.All().Count, reopened.Count);
            Assert.Contains(reopened, t => t.Name == "Comprovante de Pagamento");
            Assert.Contains(reopened, t => t.Name == "Ordem de Serviço");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Store_MissingFileReturnsEmpty()
    {
        var store = new JsonTemplateStore(TempPath());
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Store_CorruptedFileReturnsEmpty()
    {
        var path = TempPath();
        File.WriteAllText(path, "{ isto não é json");
        try
        {
            Assert.Empty(new JsonTemplateStore(path).Load());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Store_OverwritesPreviousContent()
    {
        var path = TempPath();
        try
        {
            var store = new JsonTemplateStore(path);
            store.Save(TemplateSamples.All());
            store.Save(new[] { TemplateSamples.CreatePaymentReceipt() });

            Assert.Single(store.Load());
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class TemplateSerializerTests
{
    [Fact]
    public void Serialize_ProducesValidEnvelope()
    {
        var json = TemplateSerializer.Serialize(TemplateSamples.CreatePaymentReceipt());

        Assert.Contains("miniprint.template", json);
        Assert.Contains("Comprovante de Pagamento", json);
    }

    [Fact]
    public void Deserialize_RoundTrips()
    {
        var original = TemplateSamples.CreatePaymentReceipt();
        var json = TemplateSerializer.Serialize(original);

        var result = TemplateSerializer.Deserialize(json);

        Assert.True(result.Success, result.ErrorText);
        Assert.NotNull(result.Template);
        Assert.Equal(original.Name, result.Template!.Name);
        Assert.Equal(original.Fields.Count, result.Template.Fields.Count);
        Assert.Equal(original.Blocks.Count, result.Template.Blocks.Count);
    }

    [Fact]
    public void Deserialize_AssignsNewIdentity()
    {
        var original = TemplateSamples.CreatePaymentReceipt();
        var result = TemplateSerializer.Deserialize(TemplateSerializer.Serialize(original));

        Assert.True(result.Success);
        Assert.NotEqual(original.Id, result.Template!.Id);
    }

    [Fact]
    public void Deserialize_RejectsInvalidJson()
    {
        var result = TemplateSerializer.Deserialize("{ nada }");
        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Deserialize_RejectsForeignFormat()
    {
        var result = TemplateSerializer.Deserialize("{\"Format\":\"outro.app\",\"Version\":1}");
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("não é um modelo"));
    }

    [Fact]
    public void Deserialize_RejectsStructurallyInvalidTemplate()
    {
        var template = TemplateSamples.CreatePaymentReceipt();
        template.Name = string.Empty;
        template.Fields.Add(new TemplateField { Key = "valor", Label = "Duplicado" });

        var result = TemplateSerializer.Deserialize(TemplateSerializer.Serialize(template));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("nome"));
    }

    [Fact]
    public void Deserialize_RejectsEmptyFile()
    {
        Assert.False(TemplateSerializer.Deserialize(string.Empty).Success);
    }
}

public class TemplateRendererTests
{
    private static Dictionary<string, string> FullValues() => new()
    {
        ["nome_pagador"] = "Ana Maria Silva",
        ["documento"] = "123.456.789-09",
        ["valor"] = "150",
        ["descricao"] = "Serviço de manutenção",
        ["forma_pagamento"] = "PIX",
        ["data_pagamento"] = "05/11/2026",
    };

    [Fact]
    public void Render_ProducesFullWidthBitmap()
    {
        var bitmap = TemplateRenderer.Render(TemplateSamples.CreatePaymentReceipt(), FullValues());

        Assert.Equal(384, bitmap.Width);
        Assert.Equal(48, bitmap.Stride);
        Assert.True(bitmap.Height > 50, $"Altura inesperada: {bitmap.Height}");
    }

    [Fact]
    public void Render_ProducesInk()
    {
        var bitmap = TemplateRenderer.Render(TemplateSamples.CreatePaymentReceipt(), FullValues());

        var ink = bitmap.Bits.Sum(b => System.Numerics.BitOperations.PopCount(b));
        Assert.True(ink > 500, $"Pouca tinta: {ink} pontos.");
    }

    [Fact]
    public void Render_RespectsMargins()
    {
        var template = TemplateSamples.CreatePaymentReceipt();
        template.MarginLeft = 24;
        template.MarginRight = 24;

        var bitmap = TemplateRenderer.Render(template, FullValues());

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < 20; x++)
            {
                Assert.Equal(0, bitmap[x, y]);
                Assert.Equal(0, bitmap[bitmap.Width - 1 - x, y]);
            }
        }
    }

    [Fact]
    public void Render_LongTextIncreasesHeight()
    {
        var shortTemplate = TemplateSamples.CreatePaymentReceipt();
        var longTemplate = TemplateSamples.CreatePaymentReceipt();

        var shortBitmap = TemplateRenderer.Render(shortTemplate, FullValues());

        var longValues = FullValues();
        longValues["descricao"] = string.Join(' ', Enumerable.Repeat("Descrição bem longa do serviço", 12));
        var longBitmap = TemplateRenderer.Render(longTemplate, longValues);

        Assert.True(longBitmap.Height > shortBitmap.Height);
    }

    [Fact]
    public void Render_DifferentValuesProduceDifferentBitmaps()
    {
        var template = TemplateSamples.CreatePaymentReceipt();
        var a = TemplateRenderer.Render(template, FullValues());

        var other = FullValues();
        other["nome_pagador"] = "Carlos Pereira";
        var b = TemplateRenderer.Render(template, other);

        Assert.NotEqual(a.Bits, b.Bits);
    }

    [Fact]
    public void Render_EmptyOptionalFieldDoesNotBreak()
    {
        var values = FullValues();
        values["descricao"] = string.Empty;

        var bitmap = TemplateRenderer.Render(TemplateSamples.CreatePaymentReceipt(), values);

        Assert.Equal(384, bitmap.Width);
        Assert.True(bitmap.Height > 50);
    }

    [Fact]
    public void Render_ManyFieldsAndBlocksStillFitsWidth()
    {
        var template = TemplateSamples.CreatePaymentReceipt();
        for (var i = 0; i < 12; i++)
        {
            template.Fields.Add(new TemplateField
            {
                Key = $"extra_{i}",
                Label = $"Extra {i}",
                Type = TemplateFieldType.TextoCurto,
            });
            template.Blocks.Add(new TemplateBlock
            {
                Kind = TemplateBlockKind.Texto,
                Text = $"Extra {i}: {{{{extra_{i}}}}}",
            });
        }

        var values = FullValues();
        for (var i = 0; i < 12; i++)
        {
            values[$"extra_{i}"] = $"valor {i}";
        }

        var bitmap = TemplateRenderer.Render(template, values);

        Assert.Equal(384, bitmap.Width);
        Assert.True(bitmap.Height > 100);
    }

    [Fact]
    public void Render_SolidDividerProducesLine()
    {
        var template = new DocumentTemplate
        {
            Name = "Linha",
            MarginLeft = 0,
            MarginRight = 0,
            MarginTop = 0,
            MarginBottom = 0,
            Blocks =
            {
                new TemplateBlock { Kind = TemplateBlockKind.Divisor, DividerStyle = DividerStyle.LinhaSolida },
            },
        };

        var bitmap = TemplateRenderer.Render(template, new Dictionary<string, string>());

        Assert.True(bitmap.Height >= 2);
        var inkFirstRow = Enumerable.Range(0, bitmap.Width).Sum(x => bitmap[x, 0]);
        Assert.True(inkFirstRow > bitmap.Width / 2);
    }

    [Fact]
    public void Render_EmptyDocumentStillProducesBitmap()
    {
        var template = new DocumentTemplate { Name = "Vazio" };
        var bitmap = TemplateRenderer.Render(template, new Dictionary<string, string>());

        Assert.Equal(384, bitmap.Width);
        Assert.True(bitmap.Height >= 1);
    }

    [Fact]
    public void Render_SampleServiceReceiptIsValid()
    {
        var template = TemplateSamples.CreateServiceReceipt();
        var values = new Dictionary<string, string>
        {
            ["cliente"] = "João",
            ["cpf"] = "12345678909",
            ["telefone"] = "11987654321",
            ["servico"] = "Instalação",
            ["valor"] = "250,00",
            ["data"] = "05/11/2026",
        };

        var bitmap = TemplateRenderer.Render(template, values);

        Assert.Equal(384, bitmap.Width);
        var ink = bitmap.Bits.Sum(b => System.Numerics.BitOperations.PopCount(b));
        Assert.True(ink > 300);
    }
}
