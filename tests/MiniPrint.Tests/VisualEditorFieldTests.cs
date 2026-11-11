using MiniPrint.Core.Templates;
using MiniPrint.Desktop;
using MiniPrint.Desktop.ViewModels;
using Xunit;

namespace MiniPrint.Tests;

/// <summary>
/// Regressões do editor visual: o nome da camada e o rótulo do campo precisam
/// ser a mesma coisa (é esse rótulo que o formulário de preenchimento mostra) e
/// o título do documento precisa acompanhar o nome digitado.
/// </summary>
public class VisualEditorFieldTests
{
    private sealed class FakeHost : ITemplateHost
    {
        public AppServices Services => null!;

        public IReadOnlyList<DocumentTemplate> Templates => Array.Empty<DocumentTemplate>();

        public void ShowTemplateList()
        {
        }

        public void EditTemplate(DocumentTemplate template, bool isNew)
        {
        }

        public void NewVisualTemplate()
        {
        }

        public void UseTemplate(DocumentTemplate template)
        {
        }

        public void SaveTemplate(DocumentTemplate template, bool isNew)
        {
        }

        public void DeleteTemplate(DocumentTemplate template)
        {
        }

        public void DuplicateTemplate(DocumentTemplate template)
        {
        }

        public void AddSample(DocumentTemplate sample)
        {
        }

        public DocumentTemplate? ImportFromFile(string path) => null;

        public void ExportToFile(DocumentTemplate template)
        {
        }

        public void RefreshTemplateList()
        {
        }
    }

    private static DocumentTemplate BuildTemplate(string fieldLabel = "Novo campo")
    {
        var template = new DocumentTemplate
        {
            Name = "Novo documento",
            PaperWidth = 384,
            Layout = new DocumentLayout
            {
                Width = 384,
                MarginLeft = 8,
                MarginRight = 8,
                AutoHeight = true,
            },
        };

        template.Fields.Add(new TemplateField
        {
            Key = "nome",
            Label = fieldLabel,
            Type = TemplateFieldType.TextoCurto,
        });

        template.Layout.Elements.Add(new LayoutElement
        {
            Kind = LayoutElementKind.Campo,
            Name = fieldLabel,
            Text = fieldLabel,
            FieldKey = "nome",
            X = 8,
            Y = 8,
            Width = 200,
            Height = 20,
        });

        return template;
    }

    [Fact]
    public void Title_StartsAsNewDocumentAndFollowsTheNameTyped()
    {
        var template = BuildTemplate();
        var viewModel = new VisualEditorViewModel(new FakeHost(), template, isNew: true);

        Assert.Equal("Novo documento", viewModel.Title);

        viewModel.Name = "Recibo da loja";

        Assert.Equal("Recibo da loja", viewModel.Title);
    }

    [Fact]
    public void FieldsFromTheTemplateAreListedAsSoonAsTheEditorOpens()
    {
        var template = BuildTemplate(fieldLabel: "Nome do cliente");
        var viewModel = new VisualEditorViewModel(new FakeHost(), template, isNew: false);

        Assert.Single(viewModel.FieldChoices);
        Assert.Equal("nome", viewModel.FieldChoices[0].Key);
    }

    [Fact]
    public void RenamingTheLayerRenamesTheFieldUsedWhenFilling()
    {
        var template = BuildTemplate();
        var viewModel = new VisualEditorViewModel(new FakeHost(), template, isNew: true);
        viewModel.SelectedElement = viewModel.Elements[0];

        viewModel.SelectedElement!.Name = "Nome do cliente";

        Assert.Equal("Nome do cliente", template.Fields[0].Label);
        Assert.Equal("Nome do cliente", template.Layout!.Elements[0].Text);
    }

    [Fact]
    public void RenamingTheFieldUpdatesTheLayerName()
    {
        var template = BuildTemplate();
        var viewModel = new VisualEditorViewModel(new FakeHost(), template, isNew: true);
        viewModel.SelectedElement = viewModel.Elements[0];

        viewModel.SelectedFieldLabel = "Valor pago";

        Assert.Equal("Valor pago", template.Fields[0].Label);
        Assert.Equal("Valor pago", template.Layout!.Elements[0].Name);
        Assert.Equal("Valor pago", template.Layout.Elements[0].Text);
    }

    [Fact]
    public void ChangingTheFieldTypeKeepsTheModelInSync()
    {
        var template = BuildTemplate();
        var viewModel = new VisualEditorViewModel(new FakeHost(), template, isNew: true);
        viewModel.SelectedElement = viewModel.Elements[0];

        Assert.True(viewModel.HasSelectedField);
        viewModel.SelectedFieldType = TemplateFieldType.Monetario;

        Assert.Equal(TemplateFieldType.Monetario, template.Fields[0].Type);
        Assert.Equal(TemplateFieldType.Monetario, viewModel.SelectedFieldType);
    }

    [Fact]
    public void DateFieldsAreAvailableInTheTypeList()
    {
        var template = BuildTemplate();
        var viewModel = new VisualEditorViewModel(new FakeHost(), template, isNew: true);

        var date = viewModel.FieldTypes.Single(option => option.Type == TemplateFieldType.Data);
        Assert.Contains("Data", date.Label);
    }

    [Fact]
    public void OldTemplatesWithDefaultLabelAdoptTheLayerName()
    {
        // Modelo salvo antes da correção: camada "Nome do cliente", rótulo "Novo campo".
        var template = BuildTemplate();
        template.Layout!.Elements[0].Name = "Nome do cliente";
        template.Layout.Elements[0].Text = "Nome do cliente";

        var viewModel = new VisualEditorViewModel(new FakeHost(), template, isNew: false);

        Assert.Equal("Nome do cliente", template.Fields[0].Label);
        Assert.Single(viewModel.Elements);
    }
}
