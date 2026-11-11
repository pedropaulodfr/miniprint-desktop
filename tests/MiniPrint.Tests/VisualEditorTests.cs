using MiniPrint.Core.Configuration;
using MiniPrint.Core.Models;
using MiniPrint.Core.Templates;
using MiniPrint.Imaging;
using Xunit;

namespace MiniPrint.Tests;

public class DocumentLayoutTests
{
    private static LayoutElement Text(int x, int y, int w, int h) => new()
    {
        Kind = LayoutElementKind.Texto,
        Name = "Texto",
        X = x,
        Y = y,
        Width = w,
        Height = h,
        Text = "Teste",
        FontSize = 11,
    };

    [Fact]
    public void EffectiveHeight_GrowsWithElements()
    {
        var layout = new DocumentLayout { AutoHeight = true, MarginTop = 8, MarginBottom = 12 };
        Assert.Equal(16, layout.EffectiveHeight);

        layout.Elements.Add(Text(0, 100, 200, 20));
        Assert.Equal(132, layout.EffectiveHeight);
    }

    [Fact]
    public void EffectiveHeight_UsesFixedHeightWhenAutomaticIsOff()
    {
        var layout = new DocumentLayout { AutoHeight = false, Height = 500 };
        layout.Elements.Add(Text(0, 10, 100, 10));

        Assert.Equal(500, layout.EffectiveHeight);
    }

    [Fact]
    public void HiddenElements_DoNotAffectHeight()
    {
        var layout = new DocumentLayout { AutoHeight = true };
        layout.Elements.Add(new LayoutElement { Y = 400, Height = 20, Visible = false });

        Assert.True(layout.EffectiveHeight < 100);
    }

    [Fact]
    public void Clone_PreservesElementsAndCoordinates()
    {
        var layout = new DocumentLayout { Width = 384 };
        layout.Elements.Add(Text(10, 20, 100, 30));

        var copy = layout.Clone();

        Assert.Single(copy.Elements);
        Assert.Equal(10, copy.Elements[0].X);
        Assert.Equal(20, copy.Elements[0].Y);
        Assert.Equal(layout.Elements[0].Id, copy.Elements[0].Id);
    }

    [Fact]
    public void CloneWithSameId_KeepsIdentity()
    {
        var element = Text(1, 2, 3, 4);
        Assert.Equal(element.Id, element.CloneWithSameId().Id);
        Assert.NotEqual(element.Id, element.Clone().Id);
    }

    [Fact]
    public void Element_ClampsNegativeCoordinates()
    {
        var element = Text(0, 0, 100, 20);
        element.X = -10;
        element.Y = -5;

        Assert.Equal(0, element.X);
        Assert.Equal(0, element.Y);
    }

    [Fact]
    public void Element_ClampsSize()
    {
        var element = Text(0, 0, 100, 20);
        element.Width = 1;
        element.Height = 0;

        Assert.True(element.Width >= 4);
        Assert.True(element.Height >= 2);
    }

    [Fact]
    public void Element_DisplayTextShowsFieldLabel()
    {
        var element = new LayoutElement
        {
            Kind = LayoutElementKind.Campo,
            Text = "Nome do pagador",
            FieldKey = "nome",
        };

        Assert.Equal("«Nome do pagador»", element.DisplayText);
    }
}

public class VisualRendererTests
{
    private static DocumentTemplate BuildTemplate()
    {
        var template = new DocumentTemplate
        {
            Name = "Visual",
            PaperWidth = 384,
            Fields =
            {
                new TemplateField { Key = "nome", Label = "Nome", Type = TemplateFieldType.TextoCurto },
                new TemplateField { Key = "valor", Label = "Valor", Type = TemplateFieldType.Monetario },
            },
            Layout = new DocumentLayout
            {
                Width = 384,
                MarginLeft = 10,
                MarginRight = 10,
                MarginTop = 10,
                MarginBottom = 10,
                AutoHeight = true,
            },
        };

        template.Layout.Elements.Add(new LayoutElement
        {
            Kind = LayoutElementKind.Texto,
            Name = "Título",
            X = 10,
            Y = 10,
            Width = 364,
            Height = 22,
            Text = "COMPROVANTE",
            FontSize = 14,
            Bold = true,
            Alignment = TextAlignment.Centro,
        });

        template.Layout.Elements.Add(new LayoutElement
        {
            Kind = LayoutElementKind.Texto,
            Name = "Rótulo",
            X = 10,
            Y = 40,
            Width = 100,
            Height = 16,
            Text = "Recebemos de:",
            FontSize = 10,
        });

        template.Layout.Elements.Add(new LayoutElement
        {
            Kind = LayoutElementKind.Campo,
            Name = "Nome",
            X = 115,
            Y = 40,
            Width = 250,
            Height = 16,
            Text = "Nome",
            FieldKey = "nome",
            FontSize = 10,
        });

        template.Layout.Elements.Add(new LayoutElement
        {
            Kind = LayoutElementKind.Linha,
            Name = "Linha",
            X = 10,
            Y = 64,
            Width = 364,
            Height = 3,
            Thickness = 2,
        });

        return template;
    }

    private static Dictionary<string, string> Values() => new()
    {
        ["nome"] = "Ana Maria Silva",
        ["valor"] = "150",
    };

    [Fact]
    public void Render_UsesPaperWidth()
    {
        var bitmap = VisualTemplateRenderer.Render(BuildTemplate(), Values());

        Assert.Equal(384, bitmap.Width);
        Assert.Equal(48, bitmap.Stride);
        Assert.True(bitmap.Height > 40);
    }

    [Fact]
    public void Render_ProducesInk()
    {
        var bitmap = VisualTemplateRenderer.Render(BuildTemplate(), Values());
        var ink = bitmap.Bits.Sum(b => System.Numerics.BitOperations.PopCount(b));

        Assert.True(ink > 200, $"Pouca tinta: {ink}.");
    }

    [Fact]
    public void Render_IsMonochrome()
    {
        var bitmap = VisualTemplateRenderer.Render(BuildTemplate(), Values());
        var distinct = bitmap.Bits.Distinct().Count();

        Assert.True(distinct <= 256);
        Assert.All(bitmap.Bits, b => Assert.InRange(b, 0, 255));
    }

    [Fact]
    public void Render_RespectsMargins()
    {
        var bitmap = VisualTemplateRenderer.Render(BuildTemplate(), Values());

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                Assert.Equal(0, bitmap[x, y]);
            }
        }
    }

    [Fact]
    public void Render_DifferentValuesChangeOutput()
    {
        var template = BuildTemplate();
        var a = VisualTemplateRenderer.Render(template, Values());

        var other = Values();
        other["nome"] = "Carlos Pereira de Albuquerque";
        var b = VisualTemplateRenderer.Render(template, other);

        Assert.NotEqual(a.Bits, b.Bits);
    }

    [Fact]
    public void Render_LongTextGrowsDocumentWhenAutoHeight()
    {
        var template = BuildTemplate();
        var element = template.Layout!.Elements.First(e => e.Name == "Rótulo");
        element.Width = 120;
        element.Text = string.Join(' ', Enumerable.Repeat("texto comprido", 8));
        element.Height = 120;

        var tall = VisualTemplateRenderer.Render(template, Values());

        var template2 = BuildTemplate();
        template2.Layout!.Elements.First(e => e.Name == "Rótulo").Height = 20;
        var shortOne = VisualTemplateRenderer.Render(template2, Values());

        Assert.True(tall.Height >= shortOne.Height);
    }

    [Fact]
    public void Render_HiddenElementIsSkipped()
    {
        var template = BuildTemplate();
        var withLine = VisualTemplateRenderer.Render(template, Values());

        template.Layout!.Elements.First(e => e.Name == "Linha").Visible = false;
        var withoutLine = VisualTemplateRenderer.Render(template, Values());

        Assert.NotEqual(withLine.Bits, withoutLine.Bits);
    }

    [Fact]
    public void ResolveElementText_FormatsFieldValue()
    {
        var template = BuildTemplate();
        var fieldElement = template.Layout!.Elements.First(e => e.Kind == LayoutElementKind.Campo);

        var text = VisualTemplateRenderer.ResolveElementText(fieldElement, template, Values());

        Assert.Equal("Ana Maria Silva", text);
    }

    [Fact]
    public void ResolveElementText_FormatsMoney()
    {
        var template = BuildTemplate();
        var element = new LayoutElement
        {
            Kind = LayoutElementKind.Campo,
            Text = "Valor",
            FieldKey = "valor",
            Width = 200,
        };

        var text = VisualTemplateRenderer.ResolveElementText(element, template, Values());
        Assert.Equal("150,00", text);
    }

    [Fact]
    public void ResolveElementText_MissingFieldReturnsEmpty()
    {
        var template = BuildTemplate();
        var element = new LayoutElement { Kind = LayoutElementKind.Campo, FieldKey = "inexistente" };

        Assert.Equal(string.Empty, VisualTemplateRenderer.ResolveElementText(element, template, Values()));
    }

    [Fact]
    public void Render_ImageElementProducesInk()
    {
        var template = BuildTemplate();
        template.Layout!.Elements.Add(new LayoutElement
        {
            Kind = LayoutElementKind.Imagem,
            Name = "Logo",
            X = 10,
            Y = 80,
            Width = 80,
            Height = 40,
            ImageData = CreateHalfBlackPng(),
            KeepAspect = false,
        });

        var bitmap = VisualTemplateRenderer.Render(template, Values());
        var ink = bitmap.Bits.Sum(b => System.Numerics.BitOperations.PopCount(b));

        Assert.True(ink > 300, $"Imagem não gerou tinta suficiente: {ink}.");
    }

    [Fact]
    public void Render_ImageCropChangesResult()
    {
        var template = BuildTemplate();
        template.Layout!.Elements.Add(new LayoutElement
        {
            Kind = LayoutElementKind.Imagem,
            Name = "Logo",
            X = 10,
            Y = 80,
            Width = 80,
            Height = 40,
            ImageData = CreateHalfBlackPng(),
            KeepAspect = false,
        });

        var full = VisualTemplateRenderer.Render(template, Values());

        template.Layout.Elements[^1].CropLeft = 0.45;
        var cropped = VisualTemplateRenderer.Render(template, Values());

        Assert.NotEqual(full.Bits, cropped.Bits);
    }

    /// <summary>PNG 40x20 com metade esquerda preta.</summary>
    private static string CreateHalfBlackPng()
    {
        using var bitmap = new System.Drawing.Bitmap(40, 20);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(System.Drawing.Color.White);
            graphics.FillRectangle(System.Drawing.Brushes.Black, 0, 0, 20, 20);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return Convert.ToBase64String(stream.ToArray());
    }

    [Fact]
    public void MeasureTextHeight_GrowsWithText()    {
        var template = BuildTemplate();
        var element = new LayoutElement
        {
            Kind = LayoutElementKind.Texto,
            Width = 120,
            Text = "curto",
            FontSize = 11,
        };

        var small = VisualTemplateRenderer.MeasureTextHeight(element, template);
        element.Text = string.Join(' ', Enumerable.Repeat("texto bem maior", 10));
        var large = VisualTemplateRenderer.MeasureTextHeight(element, template);

        Assert.True(large > small);
    }
}

public class LayoutConverterTests
{
    [Fact]
    public void Convert_CreatesLayoutWithElements()
    {
        var flow = TemplateSamples.CreatePaymentReceipt();
        var visual = TemplateLayoutConverter.ToVisual(flow);

        Assert.True(visual.IsVisual);
        Assert.NotNull(visual.Layout);
        Assert.True(visual.Layout!.Elements.Count > 0);
        Assert.Equal(flow.Blocks.Count, visual.Blocks.Count);
    }

    [Fact]
    public void Convert_PreservesFields()
    {
        var flow = TemplateSamples.CreatePaymentReceipt();
        var visual = TemplateLayoutConverter.ToVisual(flow);

        Assert.Equal(flow.Fields.Count, visual.Fields.Count);
        Assert.Contains(visual.Fields, f => f.Key == "nome_pagador");
    }

    [Fact]
    public void Convert_PreservesFieldTokensInText()
    {
        var flow = TemplateSamples.CreatePaymentReceipt();
        var visual = TemplateLayoutConverter.ToVisual(flow);

        Assert.Contains(visual.Layout!.Elements,
            e => e.Kind == LayoutElementKind.Texto && e.Text.Contains("{{nome_pagador}}"));
    }

    [Fact]
    public void Convert_StacksElementsVertically()
    {
        var visual = TemplateLayoutConverter.ToVisual(TemplateSamples.CreatePaymentReceipt());
        var ordered = visual.Layout!.Elements.OrderBy(e => e.Y).ToList();

        for (var i = 1; i < ordered.Count; i++)
        {
            Assert.True(ordered[i].Y >= ordered[i - 1].Y);
        }
    }

    [Fact]
    public void Convert_ProducesRenderableDocument()
    {
        var visual = VisualTemplateSamples.Convert(TemplateSamples.CreatePaymentReceipt());
        var values = new Dictionary<string, string>
        {
            ["nome_pagador"] = "Ana",
            ["documento"] = "123.456.789-09",
            ["valor"] = "150",
            ["descricao"] = "Serviço",
            ["forma_pagamento"] = "PIX",
            ["data_pagamento"] = "05/11/2026",
        };

        var bitmap = VisualTemplateRenderer.Render(visual, values);

        Assert.Equal(384, bitmap.Width);
        var ink = bitmap.Bits.Sum(b => System.Numerics.BitOperations.PopCount(b));
        Assert.True(ink > 500);
    }

    [Fact]
    public void IsConvertible_IsFalseForVisualTemplates()
    {
        var visual = TemplateLayoutConverter.ToVisual(TemplateSamples.CreatePaymentReceipt());
        Assert.False(TemplateLayoutConverter.IsConvertible(visual));
    }

    [Fact]
    public void AllVisualSamples_AreValid()
    {
        foreach (var sample in VisualTemplateSamples.All())
        {
            var result = TemplateValidator.ValidateStructure(sample);
            Assert.True(result.IsValid, $"{sample.Name}: {string.Join(" | ", result.Errors)}");
        }
    }
}

public class VisualTemplateValidationTests
{
    private static DocumentTemplate VisualWithFieldElement(string? key)
    {
        var template = new DocumentTemplate
        {
            Name = "Teste",
            Layout = new DocumentLayout { Width = 384 },
            Fields = { new TemplateField { Key = "nome", Label = "Nome" } },
        };

        template.Layout!.Elements.Add(new LayoutElement
        {
            Kind = LayoutElementKind.Campo,
            Name = "Campo",
            X = 10,
            Y = 10,
            Width = 100,
            Height = 20,
            Text = "Nome",
            FieldKey = key,
        });

        return template;
    }

    [Fact]
    public void FieldElementWithoutKey_IsError()
    {
        var result = TemplateValidator.ValidateStructure(VisualWithFieldElement(null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("campo dinâmico"));
    }

    [Fact]
    public void FieldElementWithUnknownKey_IsError()
    {
        var result = TemplateValidator.ValidateStructure(VisualWithFieldElement("outro"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("não existe"));
    }

    [Fact]
    public void FieldElementWithValidKey_IsValid()
    {
        var result = TemplateValidator.ValidateStructure(VisualWithFieldElement("nome"));
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void ElementBeyondPaperWidth_IsWarning()
    {
        var template = VisualWithFieldElement("nome");
        template.Layout!.Elements[0].X = 350;
        template.Layout.Elements[0].Width = 200;

        var result = TemplateValidator.ValidateStructure(template);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("largura do papel"));
    }

    [Fact]
    public void EmptyVisualDocument_IsError()
    {
        var template = new DocumentTemplate
        {
            Name = "Vazio",
            Layout = new DocumentLayout { Width = 384 },
        };

        var result = TemplateValidator.ValidateStructure(template);
        Assert.False(result.IsValid);
    }
}

public class VisualTemplateSerializationTests
{
    private static DocumentTemplate Sample() =>
        VisualTemplateSamples.Convert(TemplateSamples.CreateServiceReceipt());

    [Fact]
    public void Serialize_IncludesLayout()
    {
        var json = TemplateSerializer.Serialize(Sample());

        Assert.Contains("\"Layout\"", json);
        Assert.Contains("\"Elements\"", json);
        Assert.Contains("\"Version\": 2", json);
    }

    [Fact]
    public void RoundTrip_PreservesElementsAndCoordinates()
    {
        var original = Sample();
        var result = TemplateSerializer.Deserialize(TemplateSerializer.Serialize(original));

        Assert.True(result.Success, result.ErrorText);
        var restored = result.Template!;

        Assert.True(restored.IsVisual);
        Assert.Equal(original.Layout!.Elements.Count, restored.Layout!.Elements.Count);
        Assert.Equal(original.Layout.Elements[0].X, restored.Layout.Elements[0].X);
        Assert.Equal(original.Layout.Elements[0].Y, restored.Layout.Elements[0].Y);
        Assert.Equal(original.Layout.Width, restored.Layout.Width);
    }

    [Fact]
    public void RoundTrip_PreservesFieldLinks()
    {
        var result = TemplateSerializer.Deserialize(TemplateSerializer.Serialize(Sample()));
        var restored = result.Template!;

        var fieldElements = restored.Layout!.Elements
            .Where(e => e.Kind == LayoutElementKind.Campo)
            .ToList();

        Assert.All(fieldElements, e => Assert.False(string.IsNullOrWhiteSpace(e.FieldKey)));
    }

    [Fact]
    public void Store_PersistsLayout()
    {
        var path = Path.Combine(Path.GetTempPath(), $"miniprint-visual-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonTemplateStore(path);
            store.Save(new[] { Sample() });

            var loaded = store.Load().Single();

            Assert.True(loaded.IsVisual);
            Assert.True(loaded.Layout!.Elements.Count > 0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OldFormatWithoutLayout_StillLoads()
    {
        var flow = TemplateSamples.CreatePaymentReceipt();
        var result = TemplateSerializer.Deserialize(TemplateSerializer.Serialize(flow));

        Assert.True(result.Success, result.ErrorText);
        Assert.False(result.Template!.IsVisual);
        Assert.True(result.Template.Blocks.Count > 0);
    }
}
