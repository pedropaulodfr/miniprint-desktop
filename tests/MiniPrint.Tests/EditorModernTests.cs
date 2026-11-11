using MiniPrint.Core.Templates;
using MiniPrint.Desktop;
using MiniPrint.Desktop.Editing;
using MiniPrint.Desktop.ViewModels;
using Xunit;

namespace MiniPrint.Tests;

/// <summary>
/// Regressões do editor visual modernizado: encaixe inteligente, seleção
/// múltipla, histórico cobrindo campos, copiar estilo e busca/substituição.
/// </summary>
public class EditorSnapEngineTests
{
    private static readonly SnapOptions Default = new(
        Grid: false, GridSize: 8, Objects: true, Margins: true, PageCenter: true, Tolerance: 4);

    [Fact]
    public void SnapMove_AlignsCenterToPageCenter()
    {
        var moving = new SnapRect(170, 20, 40, 10);

        var outcome = EditorSnapEngine.SnapMove(
            moving, Array.Empty<SnapRect>(), pageWidth: 384, pageHeight: 200,
            contentLeft: 8, contentRight: 376, contentTop: 8, Default);

        Assert.Equal(172, outcome.X);
        Assert.Contains(outcome.Guides, g => g.Source == SnapSource.PageCenter);
    }

    [Fact]
    public void SnapMove_AlignsLeftEdgeWithAnotherElement()
    {
        var others = new[] { new SnapRect(100, 0, 60, 20) };
        var moving = new SnapRect(98, 40, 50, 12);

        var outcome = EditorSnapEngine.SnapMove(
            moving, others, 384, 200, 8, 376, 8, Default);

        Assert.Equal(100, outcome.X);
        Assert.Equal(40, outcome.Y);
    }

    [Fact]
    public void SnapMove_DoesNotSnapOutsideTolerance()
    {
        var others = new[] { new SnapRect(100, 0, 60, 20) };
        var moving = new SnapRect(60, 40, 30, 12);

        var outcome = EditorSnapEngine.SnapMove(
            moving, others, 384, 200, 8, 376, 8, Default);

        Assert.Equal(60, outcome.X);
        Assert.Empty(outcome.Guides);
    }

    [Fact]
    public void SnapMove_UsesGridWhenEnabled()
    {
        var options = Default with { Grid = true, GridSize = 8, Objects = false, PageCenter = false, Margins = false };
        var moving = new SnapRect(11, 19, 20, 10);

        var outcome = EditorSnapEngine.SnapMove(
            moving, Array.Empty<SnapRect>(), 384, 200, 8, 376, 8, options);

        // O encaixe pode alinhar qualquer borda (esquerda, centro ou direita)
        // à grade; o contrato é que alguma delas caia em múltiplo de 8.
        var verticals = new[] { outcome.X, outcome.X + moving.Width / 2, outcome.X + moving.Width };
        var horizontals = new[] { outcome.Y, outcome.Y + moving.Height / 2, outcome.Y + moving.Height };

        Assert.Contains(verticals, v => v % 8 == 0);
        Assert.Contains(horizontals, v => v % 8 == 0);
        Assert.NotEmpty(outcome.Guides);
        Assert.All(outcome.Guides, g => Assert.Equal(SnapSource.Grid, g.Source));
    }

    [Fact]
    public void SnapMove_AlignsToLeftMargin()
    {
        var options = Default with { Objects = false, PageCenter = false };
        var moving = new SnapRect(10, 40, 30, 10);

        var outcome = EditorSnapEngine.SnapMove(
            moving, Array.Empty<SnapRect>(), 384, 200, 8, 376, 8, options);

        Assert.Equal(8, outcome.X);
        Assert.Contains(outcome.Guides, g => g.Source == SnapSource.Margin);
    }

    [Fact]
    public void SnapResize_SnapsTheMovedEdge()
    {
        var others = new[] { new SnapRect(100, 0, 60, 20) };
        var resizing = new SnapRect(20, 40, 78, 12);

        var outcome = EditorSnapEngine.SnapResize(
            resizing, "E", others, 384, 200, 8, 376, 8, Default);

        Assert.Equal(80, outcome.Width);
        Assert.Equal(20, outcome.X);
    }

    [Fact]
    public void SnapResize_KeepsMinimumSize()
    {
        var others = new[] { new SnapRect(10, 0, 60, 20) };
        var resizing = new SnapRect(20, 40, 40, 12);

        var outcome = EditorSnapEngine.SnapResize(
            resizing, "W", others, 384, 200, 8, 376, 8, Default);

        Assert.True(outcome.Width >= 4);
        Assert.True(outcome.X <= resizing.Right - 4);
    }
}

public class VisualEditorModernTests
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

    private static DocumentTemplate BuildTemplate()
    {
        var template = new DocumentTemplate
        {
            Name = "Documento",
            PaperWidth = 384,
            Layout = new DocumentLayout
            {
                Width = 384,
                MarginLeft = 8,
                MarginRight = 8,
                AutoHeight = true,
            },
        };

        template.Layout.Elements.Add(new LayoutElement
        {
            Kind = LayoutElementKind.Texto,
            Name = "Título",
            X = 8,
            Y = 8,
            Width = 200,
            Height = 20,
            Text = "RECIBO",
            FontSize = 14,
        });

        template.Layout.Elements.Add(new LayoutElement
        {
            Kind = LayoutElementKind.Texto,
            Name = "Corpo",
            X = 8,
            Y = 40,
            Width = 200,
            Height = 20,
            Text = "Recebemos de",
            FontSize = 11,
        });

        return template;
    }

    private static VisualEditorViewModel CreateEditor(DocumentTemplate template) =>
        new(new FakeHost(), template, isNew: true);

    [Fact]
    public void CreatingAFieldIsASingleUndoStep()
    {
        var template = BuildTemplate();
        var viewModel = CreateEditor(template);

        viewModel.CreateField("Nome do cliente", TemplateFieldType.TextoCurto, false, "", "");
        viewModel.AddFieldElementAt(template.Fields[0]);

        Assert.Single(template.Fields);
        Assert.Contains(template.Layout!.Elements, e => e.FieldKey == template.Fields[0].Key);

        viewModel.UndoCommand.Execute(null);

        Assert.Empty(template.Fields);
        Assert.DoesNotContain(template.Layout!.Elements, e => e.IsField);
    }

    [Fact]
    public void ShiftClickAddsToTheSelectionAndCtrlClickToggles()
    {
        var template = BuildTemplate();
        var viewModel = CreateEditor(template);
        var first = viewModel.Elements[0];
        var second = viewModel.Elements[1];

        viewModel.SelectElement(first);
        viewModel.SelectElement(second, additive: true);

        Assert.Equal(2, viewModel.SelectedElements.Count);
        Assert.True(viewModel.HasMultipleSelection);

        viewModel.SelectElement(second, toggle: true);

        Assert.Single(viewModel.SelectedElements);
        Assert.Same(first, viewModel.SelectedElement);
    }

    [Fact]
    public void MarqueeSelectsOnlyElementsInsideTheArea()
    {
        var template = BuildTemplate();
        var viewModel = CreateEditor(template);

        viewModel.SelectInRect(0, 0, 250, 35);

        Assert.Single(viewModel.SelectedElements);
        Assert.Equal("Título", viewModel.SelectedElement!.Name);
    }

    [Fact]
    public void DuplicatingOffsetsTheCopyAndSelectsIt()
    {
        var template = BuildTemplate();
        var viewModel = CreateEditor(template);
        var original = viewModel.Elements[0];

        viewModel.SelectElement(original);
        viewModel.DuplicateSelection();

        var copy = viewModel.SelectedElement!;
        Assert.NotSame(original, copy);
        Assert.Equal(original.X + 8, copy.X);
        Assert.Equal(original.Y + 8, copy.Y);
    }

    [Fact]
    public void CopyStyleAppliesFormattingToAnotherElement()
    {
        var template = BuildTemplate();
        var viewModel = CreateEditor(template);
        var title = viewModel.Elements[0];
        var body = viewModel.Elements[1];

        title.FontSize = 20;
        title.Bold = true;

        viewModel.SelectElement(title);
        viewModel.CopyStyle();

        Assert.True(viewModel.HasCopiedStyle);

        viewModel.SelectElement(body);
        viewModel.ApplyStyle();

        Assert.Equal(20, body.FontSize);
        Assert.True(body.Bold);
    }

    [Fact]
    public void ReplaceInDocumentChangesFixedTextsOnly()
    {
        var template = BuildTemplate();
        var viewModel = CreateEditor(template);

        var matches = viewModel.ReplaceInDocument("Recebemos", "Paguei", matchCase: false, replaceAll: true, out var affected);

        Assert.Equal(1, matches);
        Assert.Equal(1, affected);
        Assert.Equal("Paguei de", template.Layout!.Elements[1].Text);
        Assert.Equal("RECIBO", template.Layout.Elements[0].Text);
    }

    [Fact]
    public void MovingALayerChangesTheDrawingOrder()
    {
        var template = BuildTemplate();
        var viewModel = CreateEditor(template);

        var topBefore = viewModel.Layers[0];

        viewModel.MoveLayer(1, 0);

        Assert.Same(topBefore, viewModel.Layers[1]);
    }

    [Fact]
    public void SettingATypeOnAFieldMarksTheDocumentDirty()
    {
        var template = BuildTemplate();
        var viewModel = CreateEditor(template);
        var field = viewModel.CreateField("Valor", TemplateFieldType.TextoCurto, false, "", "");
        viewModel.AddFieldElementAt(field, pushUndo: false);

        viewModel.SelectElement(viewModel.Elements.Last());
        viewModel.SelectedFieldType = TemplateFieldType.Monetario;

        Assert.True(viewModel.IsDirty);
        Assert.Equal(TemplateFieldType.Monetario, field.Type);
    }

    [Fact]
    public void InsertingTheSameFieldTwiceCreatesTwoIndependentFields()
    {
        var template = BuildTemplate();
        var viewModel = CreateEditor(template);

        viewModel.InsertTool("field-doc");
        viewModel.InsertTool("field-doc");

        Assert.Equal(2, template.Fields.Count);
        Assert.NotEqual(template.Fields[0].Key, template.Fields[1].Key);
        Assert.Equal("CPF/CNPJ", template.Fields[0].Label);
        Assert.Equal("CPF/CNPJ 2", template.Fields[1].Label);

        // Renomear o segundo campo não pode afetar o primeiro.
        var segundo = template.Fields[1];
        viewModel.SelectedElement = viewModel.Elements.First(e => e.FieldKey == segundo.Key);
        viewModel.SelectedFieldLabel = "CPF do receptor";

        Assert.Equal("CPF do receptor", template.Fields[1].Label);
        Assert.Equal("CPF/CNPJ", template.Fields[0].Label);
    }

    [Fact]
    public void RenamingAFieldElementKeepsOtherFieldsUntouched()
    {
        var template = BuildTemplate();
        var viewModel = CreateEditor(template);

        viewModel.InsertTool("field-doc");
        viewModel.InsertTool("field-doc");

        var primeiro = template.Fields[0];
        var elemento = viewModel.Elements.First(e => e.FieldKey == primeiro.Key);
        viewModel.SelectedElement = elemento;
        elemento.Name = "CPF do pagador";

        Assert.Equal("CPF do pagador", template.Fields[0].Label);
        Assert.Equal("CPF/CNPJ 2", template.Fields[1].Label);
    }
    [Fact]
    public void DeletingTheLastFieldElementRemovesTheField()
    {
        var template = BuildTemplate();
        var viewModel = CreateEditor(template);

        viewModel.InsertTool("field-doc");
        var key = template.Fields[0].Key;
        viewModel.SelectedElement = viewModel.Elements.First(e => e.FieldKey == key);

        viewModel.DeleteSelection();

        Assert.Empty(template.Fields);
        Assert.DoesNotContain(template.Layout!.Elements, e => e.FieldKey == key);

        // O mesmo passo de histórico devolve o campo e o elemento.
        viewModel.UndoCommand.Execute(null);

        Assert.Single(template.Fields);
        Assert.Contains(template.Layout!.Elements, e => e.FieldKey == key);
    }

    [Fact]
    public void DeletingOneFieldElementKeepsTheOtherFields()
    {
        var template = BuildTemplate();
        var viewModel = CreateEditor(template);

        viewModel.InsertTool("field-doc");
        viewModel.InsertTool("field-doc");

        var primeiro = template.Fields[0].Key;
        viewModel.SelectedElement = viewModel.Elements.First(e => e.FieldKey == primeiro);
        viewModel.DeleteSelection();

        Assert.Single(template.Fields);
        Assert.Equal("CPF/CNPJ 2", template.Fields[0].Label);
    }

    [Fact]
    public void UnusedFieldIsNotReportedAsUsed()
    {
        var template = BuildTemplate();
        var viewModel = CreateEditor(template);
        viewModel.InsertTool("field-doc");
        template.Fields.Add(new TemplateField { Key = "solto", Label = "Campo solto" });

        Assert.True(template.IsFieldUsed(template.Fields[0].Key));
        Assert.False(template.IsFieldUsed("solto"));
        Assert.Contains(template.UnusedFields, f => f.Key == "solto");
    }

    [Fact]
    public void RemovingAFieldByCommandUpdatesTheList()
    {
        var template = BuildTemplate();
        var viewModel = CreateEditor(template);
        var solto = new TemplateField { Key = "solto", Label = "Campo solto" };
        template.Fields.Add(solto);
        viewModel.RefreshFieldChoices();

        viewModel.RemoveFieldCommand.Execute(solto);

        Assert.DoesNotContain(template.Fields, f => f.Key == "solto");
        Assert.DoesNotContain(viewModel.FieldChoices, f => f.Key == "solto");
    }
    [Fact]
    public void EmptyDocumentHasNoSelectionAndNoLayers()
    {
        var template = new DocumentTemplate
        {
            Name = "Vazio",
            Layout = new DocumentLayout { Width = 384 },
        };

        var viewModel = CreateEditor(template);

        Assert.Empty(viewModel.Elements);
        Assert.Null(viewModel.SelectedElement);
        Assert.Empty(viewModel.Layers);
        Assert.False(viewModel.HasSelection);
    }
}
