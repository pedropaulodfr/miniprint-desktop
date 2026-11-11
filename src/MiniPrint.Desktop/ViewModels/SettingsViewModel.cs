using System.Diagnostics;
using System.Windows;
using MiniPrint.Core.Configuration;

namespace MiniPrint.Desktop.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private string _statusMessage = string.Empty;

    public SettingsViewModel(AppServices services)
    {
        _services = services;

        SaveCommand = new RelayCommand(Save);
        RestoreDefaultsCommand = new RelayCommand(RestoreDefaults);
        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
    }

    public PrinterSettings Printer => _services.Settings.Printer;

    public ImageSettings Image => _services.Settings.Image;

    public TextSettings Text => _services.Settings.Text;

    public RelayCommand SaveCommand { get; }

    public RelayCommand RestoreDefaultsCommand { get; }

    public RelayCommand OpenDataFolderCommand { get; }

    public string DataFolder => AppPaths.DataDirectory;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public void Save()
    {
        _services.SaveSettings();

        // O nome da impressora pode ter mudado: atualiza o cabeçalho e a lista.
        _services.Printer.NotifyConfigurationChanged();

        StatusMessage = $"Configurações salvas ({DateTime.Now:HH:mm:ss}).";
        OnPropertyChanged(nameof(Printer));
        OnPropertyChanged(nameof(Image));
        OnPropertyChanged(nameof(Text));
    }

    private void RestoreDefaults()
    {
        var confirm = MiniPrint.Desktop.Windows.MessageDialog.Show(
            "Restaurar os valores padrão da SC03h-5718 (384 pontos, 4 linhas de avanço)?",
            "MiniPrint Desktop",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var defaults = new PrinterSettings();
        Printer.PaperWidth = defaults.PaperWidth;
        Printer.FeedLines = defaults.FeedLines;
        Printer.Speed = defaults.Speed;
        Printer.ImageEnergy = defaults.ImageEnergy;
        Printer.TextEnergy = defaults.TextEnergy;
        Printer.DrawingMode = defaults.DrawingMode;
        Printer.ChunkSize = defaults.ChunkSize;
        Printer.InterChunkDelayMs = defaults.InterChunkDelayMs;
        Printer.CommandTimeoutMs = defaults.CommandTimeoutMs;
        Printer.AutoReconnect = defaults.AutoReconnect;
        Printer.SendStartLattice = defaults.SendStartLattice;
        Printer.SendEndLattice = defaults.SendEndLattice;
        Printer.WriteWithResponse = defaults.WriteWithResponse;

        Save();
        StatusMessage = "Valores padrão restaurados.";
    }

    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.DataDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Não foi possível abrir a pasta: {ex.Message}";
        }
    }

    /// <summary>Chamado ao fechar a janela.</summary>
    public void Flush()
    {
        _services.SaveSettings();
    }
}
