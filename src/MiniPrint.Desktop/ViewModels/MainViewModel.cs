using System.Reflection;
using System.Windows;
using MiniPrint.Desktop.ViewModels;

namespace MiniPrint.Desktop;

public sealed class MainViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private int _selectedTab;

    public MainViewModel(AppServices services)
    {
        _services = services;

        Diagnostics = new DiagnosticsViewModel(services);
        History = new HistoryViewModel(services);
        Settings = new SettingsViewModel(services);
        Printer = new PrinterViewModel(services);
        Templates = new TemplatesViewModel(services);

        // Qualquer tela pode registrar um trabalho no histórico.
        services.JobRecorded = entry =>
        {
            History.Record(entry);
            Diagnostics.RefreshConnection();
            OnPropertyChanged(nameof(FooterStatus));
        };

        Print = new PrintViewModel(
            services,
            () => services.Printer.IsConnected,
            entry =>
            {
                History.Record(entry);
                Diagnostics.RefreshConnection();
                OnPropertyChanged(nameof(FooterStatus));
            });

        services.Printer.StateChanged += (_, _) =>
        {
            Diagnostics.RefreshConnection();
            OnPropertyChanged(nameof(FooterStatus));
            OnPropertyChanged(nameof(IsSimulated));
        };
    }

    public PrintViewModel Print { get; }

    public PrinterViewModel Printer { get; }

    public TemplatesViewModel Templates { get; }

    public HistoryViewModel History { get; }

    public SettingsViewModel Settings { get; }

    public DiagnosticsViewModel Diagnostics { get; }

    public int SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    public string VersionText =>
        $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"}";

    public bool IsSimulated => _services.Printer.IsConnected && _services.Printer.IsSimulated;

    public string FooterStatus =>
        _services.Printer.IsConnected
            ? (Print.StatusMessage ?? string.Empty)
            : "Conecte a impressora para começar.";

    public void PersistState()
    {
        try
        {
            Print.PersistToSettings();
            Settings.Flush();
            _services.Log.Info("Aplicativo", "Encerrando MiniPrint Desktop.");
        }
        catch (Exception ex)
        {
            _services.Log.Error("Aplicativo", $"Falha ao encerrar: {ex.Message}");
        }
    }
}
