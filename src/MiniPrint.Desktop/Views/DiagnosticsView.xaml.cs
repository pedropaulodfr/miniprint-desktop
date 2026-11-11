using System.Windows.Controls;
using MiniPrint.Desktop.ViewModels;

namespace MiniPrint.Desktop.Views;

public partial class DiagnosticsView : UserControl
{
    public DiagnosticsView()
    {
        InitializeComponent();
        LogList.ItemContainerGenerator.StatusChanged += OnGeneratorStatusChanged;
    }

    private void OnGeneratorStatusChanged(object? sender, EventArgs e)
    {
        if (DataContext is not DiagnosticsViewModel viewModel || !viewModel.AutoScroll)
        {
            return;
        }

        if (LogList.Items.Count == 0)
        {
            return;
        }

        LogList.ScrollIntoView(LogList.Items[^1]);
    }
}
