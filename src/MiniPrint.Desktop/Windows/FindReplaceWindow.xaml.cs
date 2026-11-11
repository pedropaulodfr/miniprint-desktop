using System.Windows;
using MiniPrint.Desktop.ViewModels;

namespace MiniPrint.Desktop.Windows;

/// <summary>
/// Busca e substituição nos textos fixos do documento. Não toca nas chaves dos
/// campos dinâmicos nem nos valores preenchidos.
/// </summary>
public partial class FindReplaceWindow : Window
{
    private readonly VisualEditorViewModel _viewModel;

    public FindReplaceWindow(VisualEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        Loaded += (_, _) => FindBox.Focus();
    }

    private void OnFindChanged(object sender, RoutedEventArgs e) => UpdateCount();

    private void UpdateCount()
    {
        if (ResultText is null)
        {
            return;
        }

        var term = FindBox.Text ?? string.Empty;
        if (term.Length == 0)
        {
            ResultText.Text = "Digite o texto para localizar.";
            ReplaceButton.IsEnabled = false;
            ReplaceAllButton.IsEnabled = false;
            return;
        }

        var count = _viewModel.CountMatches(term, MatchCaseBox.IsChecked == true);
        ResultText.Text = count == 0
            ? "Nenhuma ocorrência encontrada."
            : count == 1
                ? "1 ocorrência encontrada."
                : $"{count} ocorrências encontradas.";

        ReplaceButton.IsEnabled = count > 0;
        ReplaceAllButton.IsEnabled = count > 0;
    }

    private void OnReplace(object sender, RoutedEventArgs e)
    {
        var term = FindBox.Text ?? string.Empty;
        var replacement = ReplaceBox.Text ?? string.Empty;

        var matches = _viewModel.ReplaceInDocument(
            term, replacement, MatchCaseBox.IsChecked == true, replaceAll: false, out var affected);

        UpdateCount();
        ResultText.Text = matches == 0
            ? "Nenhuma ocorrência encontrada."
            : $"Substituída em {affected} texto(s).";
    }

    private void OnReplaceAll(object sender, RoutedEventArgs e)
    {
        var term = FindBox.Text ?? string.Empty;
        var replacement = ReplaceBox.Text ?? string.Empty;

        var matches = _viewModel.ReplaceInDocument(
            term, replacement, MatchCaseBox.IsChecked == true, replaceAll: true, out var affected);

        UpdateCount();
        ResultText.Text = matches == 0
            ? "Nenhuma ocorrência encontrada."
            : $"{matches} ocorrência(s) substituída(s) em {affected} texto(s).";
    }
}
