using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using MiniPrint.Core.Configuration;
using MiniPrint.Core.Models;
using MiniPrint.Protocol;

namespace MiniPrint.Desktop.ViewModels;

public sealed class DiagnosticsViewModel : ViewModelBase
{
    private const int MaxUiEntries = 2500;

    private readonly AppServices _services;
    private readonly List<DiagnosticEntry> _all = new();

    private bool _showTraffic = true;
    private string _filterText = string.Empty;
    private bool _autoScroll = true;
    private string _deviceState = "Nenhuma leitura realizada.";
    private string _deviceInfo = "Nenhuma leitura realizada.";
    private string _statusMessage = string.Empty;
    private bool _isProbing;

    public DiagnosticsViewModel(AppServices services)
    {
        _services = services;

        ExportCommand = new RelayCommand(Export);
        ClearCommand = new RelayCommand(Clear);
        ProbeStateCommand = new AsyncRelayCommand(_ => ProbeAsync(CatPrinterCommand.DeviceState), _ => !IsProbing, HandleError);
        ProbeInfoCommand = new AsyncRelayCommand(_ => ProbeAsync(CatPrinterCommand.DeviceInfo), _ => !IsProbing, HandleError);
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder);

        foreach (var entry in _services.Log.Snapshot())
        {
            _all.Add(entry);
        }

        Rebuild();
        _services.Log.EntryAdded += OnEntryAdded;
    }

    public ObservableCollection<DiagnosticEntry> Entries { get; } = new();

    public RelayCommand ExportCommand { get; }

    public RelayCommand ClearCommand { get; }

    public AsyncRelayCommand ProbeStateCommand { get; }

    public AsyncRelayCommand ProbeInfoCommand { get; }

    public RelayCommand OpenLogFolderCommand { get; }

    public bool ShowTraffic
    {
        get => _showTraffic;
        set
        {
            if (SetProperty(ref _showTraffic, value))
            {
                Rebuild();
            }
        }
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                Rebuild();
            }
        }
    }

    public bool AutoScroll
    {
        get => _autoScroll;
        set => SetProperty(ref _autoScroll, value);
    }

    public string DeviceState
    {
        get => _deviceState;
        private set => SetProperty(ref _deviceState, value);
    }

    public string DeviceInfoText
    {
        get => _deviceInfo;
        private set => SetProperty(ref _deviceInfo, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsProbing
    {
        get => _isProbing;
        private set
        {
            if (SetProperty(ref _isProbing, value))
            {
                ProbeStateCommand.RaiseCanExecuteChanged();
                ProbeInfoCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ConnectionSummary =>
        _services.Printer.IsConnected
            ? $"{_services.Printer.DeviceName} — MTU {_services.Printer.Mtu} bytes" +
              (_services.Printer.IsSimulated ? " (simulado)" : string.Empty)
            : "Sem conexão";

    public string SimulationSummary =>
        _services.Printer.LastSimulation?.Summary ?? "Nenhuma simulação executada.";

    public string LogFilePath => Path.Combine(AppPaths.DataDirectory, "diagnostico.log");

    /// <summary>Atualiza os textos que dependem do estado da conexão.</summary>
    public void RefreshConnection()
    {
        OnPropertyChanged(nameof(ConnectionSummary));
        OnPropertyChanged(nameof(SimulationSummary));
    }

    private void OnEntryAdded(object? sender, DiagnosticEntry entry)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => OnEntryAdded(sender, entry));
            return;
        }

        _all.Add(entry);
        if (_all.Count > MaxUiEntries)
        {
            _all.RemoveRange(0, _all.Count - MaxUiEntries);
        }

        if (Matches(entry))
        {
            Entries.Add(entry);
            while (Entries.Count > MaxUiEntries)
            {
                Entries.RemoveAt(0);
            }
        }
    }

    private bool Matches(DiagnosticEntry entry)
    {
        if (!ShowTraffic && entry.Level == DiagnosticLevel.Trafego)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(FilterText))
        {
            return true;
        }

        return entry.Message.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
               || entry.Category.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    }

    private void Rebuild()
    {
        Entries.Clear();
        foreach (var entry in _all.Where(Matches))
        {
            Entries.Add(entry);
        }
    }

    private void Clear()
    {
        _all.Clear();
        Entries.Clear();
        StatusMessage = "Visão do log limpa (o arquivo em disco é mantido).";
    }

    private void Export()
    {
        try
        {
            var path = _services.Log.Export();
            StatusMessage = $"Log exportado em {path}";
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Falha ao exportar: {ex.Message}";
        }
    }

    private void OpenLogFolder()
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
            StatusMessage = $"Falha ao abrir a pasta: {ex.Message}";
        }
    }

    private async Task ProbeAsync(CatPrinterCommand command)
    {
        if (!_services.Printer.IsConnected)
        {
            StatusMessage = "Conecte a impressora para consultar o dispositivo.";
            return;
        }

        IsProbing = true;
        StatusMessage = $"Enviando 0x{(byte)command:X2}...";

        try
        {
            var replies = await _services.Printer.ProbeAsync(command, _services.Settings.Printer);

            if (replies.Count == 0)
            {
                StatusMessage = $"Comando 0x{(byte)command:X2}: sem resposta em 3 s " +
                                "(normal para comandos de configuração).";
                return;
            }

            var descriptions = new List<string>();
            foreach (var reply in replies)
            {
                descriptions.Add(CatPrinterFrame.ToHex(reply));

                foreach (var frame in CatPrinterResponses.ParseAll(reply))
                {
                    if (CatPrinterResponses.TryParseDeviceStatus(frame) is { } status)
                    {
                        DeviceState = $"Resposta bruta: {status.Hex}\n" +
                                      $"Bytes: {status.Length}\n" +
                                      $"Primeiro byte: 0x{status.StatusByte:X2}\n\n" +
                                      "O fabricante não documenta estes campos. O valor é " +
                                      "registrado para comparação entre leituras.";
                    }

                    if (CatPrinterResponses.TryParseDeviceInfo(frame) is { } info)
                    {
                        DeviceInfoText = $"Versão de firmware: {info.Version ?? "não identificada"}\n" +
                                         $"Resposta bruta: {info.Hex}";
                    }
                }
            }

            StatusMessage = $"Resposta(s) de 0x{(byte)command:X2}: {string.Join(" | ", descriptions)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Falha na consulta: {ex.Message}";
        }
        finally
        {
            IsProbing = false;
        }
    }

    private void HandleError(Exception ex)
    {
        StatusMessage = ex.Message;
    }
}
