using System.Windows;
using System.Windows.Input;
using MiniPrint.Desktop.ViewModels;

namespace MiniPrint.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private const double CompactWidth = 1180;

    /// <summary>Abaixo de ~1180 px a barra lateral recolhe para ícones e libera espaço ao conteúdo.</summary>
    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < CompactWidth;
        if (Themes.Chrome.GetIsCompact(NavTabs) == compact)
        {
            return;
        }

        Themes.Chrome.SetIsCompact(NavTabs, compact);
        BrandText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        StatusCard.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        StatusDot.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDroppedFile(e) is not null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        var path = TryGetDroppedFile(e);
        if (path is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.SelectedTab = 0;
        try
        {
            await viewModel.Print.LoadDroppedFileAsync(path);
        }
        catch (Exception ex)
        {
            MiniPrint.Desktop.Windows.MessageDialog.Show(
                $"Não foi possível abrir o arquivo:\n{ex.Message}",
                "MiniPrint Desktop",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static string? TryGetDroppedFile(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return null;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
        {
            return null;
        }

        return files[0];
    }
}
