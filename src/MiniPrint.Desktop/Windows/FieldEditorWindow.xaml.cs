using System.Windows;
using MiniPrint.Core.Templates;
using MiniPrint.Desktop.ViewModels;

namespace MiniPrint.Desktop.Windows;

/// <summary>
/// Criação e edição de um campo dinâmico em uma única janela curta: rótulo,
/// tipo, obrigatoriedade, orientação e valor padrão. A chave interna é gerada
/// sozinha, então o usuário nunca precisa lidar com identificadores.
/// </summary>
public partial class FieldEditorWindow : Window
{
    private readonly VisualEditorViewModel _viewModel;
    private readonly TemplateField? _existing;

    public FieldEditorWindow(VisualEditorViewModel viewModel, TemplateField? existing)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _existing = existing;

        TypeBox.ItemsSource = viewModel.FieldTypes;

        if (existing is null)
        {
            TypeBox.SelectedValue = TemplateFieldType.TextoCurto;
            LabelBox.Focus();
        }
        else
        {
            HeaderText.Text = "Editar campo";
            Title = "Editar campo";
            LabelBox.Text = existing.Label;
            TypeBox.SelectedValue = existing.Type;
            PlaceholderBox.Text = existing.Placeholder;
            DefaultBox.Text = existing.DefaultValue;
            RequiredBox.IsChecked = existing.Required;
            LabelBox.SelectAll();
            LabelBox.Focus();
        }

        UpdateKeyPreview();
    }

    /// <summary>true quando um campo novo foi criado e inserido no documento.</summary>
    public bool CreatedField { get; private set; }

    private void OnInputChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateKeyPreview();

    private void UpdateKeyPreview()
    {
        if (KeyText is null)
        {
            return;
        }

        var label = string.IsNullOrWhiteSpace(LabelBox.Text) ? "Campo" : LabelBox.Text;
        KeyText.Text = _existing?.Key ?? TemplateFieldFormatter.SuggestKey(label);
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        var label = LabelBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(label))
        {
            MiniPrint.Desktop.Windows.MessageDialog.Show(this, "Informe o rótulo do campo.", "MiniPrint Desktop",
                MessageBoxButton.OK, MessageBoxImage.Information);
            LabelBox.Focus();
            return;
        }

        var type = TypeBox.SelectedValue is TemplateFieldType selected
            ? selected
            : TemplateFieldType.TextoCurto;

        if (_existing is null)
        {
            var field = _viewModel.CreateField(
                label, type, RequiredBox.IsChecked == true, PlaceholderBox.Text, DefaultBox.Text);
            _viewModel.AddFieldElementAt(field, pushUndo: false);
            CreatedField = true;
        }
        else
        {
            _viewModel.UpdateField(
                _existing, label, type, RequiredBox.IsChecked == true, PlaceholderBox.Text, DefaultBox.Text);
        }

        DialogResult = true;
    }

    /// <summary>Abre a janela e, ao criar, já insere o campo no documento.</summary>
    public static void Show(Window? owner, VisualEditorViewModel viewModel, TemplateField? existing)
    {
        var window = new FieldEditorWindow(viewModel, existing);
        if (owner is not null && !ReferenceEquals(owner, window))
        {
            window.Owner = owner;
        }

        window.ShowDialog();
    }
}
