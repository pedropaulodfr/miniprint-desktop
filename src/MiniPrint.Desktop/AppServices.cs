using MiniPrint.Core.Abstractions;
using MiniPrint.Core.Configuration;
using MiniPrint.Core.Logging;
using MiniPrint.Desktop.Services;

namespace MiniPrint.Desktop;

/// <summary>
/// Contêiner de serviços criado uma vez na inicialização e compartilhado pelas
/// telas. Mantém configurações, histórico, log e o acesso à impressora.
/// </summary>
public sealed class AppServices
{
    public AppServices(
        AppSettings settings,
        ISettingsStore settingsStore,
        IPrintHistoryStore historyStore,
        DiagnosticLog log,
        PrinterService printer)
    {
        Settings = settings;
        SettingsStore = settingsStore;
        HistoryStore = historyStore;
        Log = log;
        Printer = printer;
    }

    public AppSettings Settings { get; private set; }

    public ISettingsStore SettingsStore { get; }

    public IPrintHistoryStore HistoryStore { get; }

    public DiagnosticLog Log { get; }

    public PrinterService Printer { get; }

    /// <summary>
    /// Registro de trabalhos concluídos. A janela principal liga este callback ao
    /// histórico, para que qualquer tela possa registrar uma impressão.
    /// </summary>
    public Action<MiniPrint.Core.Models.PrintHistoryEntry>? JobRecorded { get; set; }

    public int PaperWidth => Math.Clamp(Settings.Printer.PaperWidth, 8, 1024);

    public void SaveSettings()
    {
        try
        {
            SettingsStore.Save(Settings);
        }
        catch (Exception ex)
        {
            Log.Error("Configurações", $"Não foi possível salvar: {ex.Message}");
        }
    }

    public static AppServices CreateDefault()
    {
        var log = new DiagnosticLog(persistToDisk: true);
        var settingsStore = new JsonSettingsStore();
        var settings = settingsStore.Load();
        var historyStore = new JsonPrintHistoryStore();
        var printer = new PrinterService(log, null, () => settings.Printer);

        log.Info("Aplicativo",
            $"MiniPrint Desktop iniciado. Dados em {AppPaths.DataDirectory}");

        return new AppServices(settings, settingsStore, historyStore, log, printer);
    }
}
