using System.Windows;
using System.Windows.Controls;
using MiniPrint.Core.Templates;
using MiniPrint.Desktop.ViewModels;

namespace MiniPrint.Desktop.Views;

public partial class TemplateEditorView : UserControl
{
    public TemplateEditorView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Insere o campo escolhido na posição do cursor do editor de texto.
    /// Fica no code-behind porque depende do CaretIndex do TextBox.
    /// </summary>
    private void OnInsertField(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TemplateEditorViewModel viewModel)
        {
            return;
        }

        if (FieldPicker.SelectedItem is not TemplateField field)
        {
            return;
        }

        var caret = BlockText.CaretIndex;
        viewModel.InsertFieldToken(field.Key, caret);

        BlockText.Focus();
        BlockText.CaretIndex = Math.Min(BlockText.Text.Length, caret + field.Key.Length + 4);
    }
}
