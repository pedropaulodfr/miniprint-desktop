using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MiniPrint.Bluetooth;
using MiniPrint.Core.Models;
using MiniPrint.Desktop.Services;
using MiniPrint.Protocol;

namespace MiniPrint.Desktop.ViewModels;

public sealed class PrinterViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly List<PrinterDevice> _found = new();

    private PrinterDevice? _selectedDevice;
    private bool _isScanning;
    private bool _isConnecting;
    private bool _useSimulator;
    private int _feedAmount = 128;
    private string _statusText = PrinterStatusText.Disconnected().Summary;
    private string _statusDetail = PrinterStatusText.Disconnected().Detail;
    private string _discoveryText = "Nenhuma pesquisa realizada.";
    private string _discoveryDetail = "Clique em Pesquisar impressoras para listar os dispositivos BLE próximos.";
    private string _manualMac = string.Empty;
    private string _bluetoothMessage = "Verificando Bluetooth...";
    private bool _bluetoothReady;
    private int _scanSeconds = 8;
    private string _gattSummary = string.Empty;
    private bool _isInspecting;
    private bool _discoveryShowsConnection;

    public PrinterViewModel(AppServices services)
    {
        _services = services;
        _useSimulator = services.Settings.Printer.UseSimulator;

        ScanCommand = new AsyncRelayCommand(_ => ScanAsync(), _ => !_isScanning, HandleError);
        ConnectCommand = new AsyncRelayCommand(_ => ConnectOrDisconnectAsync(), CanConnect, HandleError);
        InspectCommand = new AsyncRelayCommand(_ => InspectAsync(), _ => CanConnect(null), HandleError);
        ClearListCommand = new RelayCommand(ClearList);
        UseSavedPrinterCommand = new AsyncRelayCommand(_ => UseSavedPrinterAsync(), null, HandleError);
        FeedPaperCommand = new AsyncRelayCommand(_ => FeedPaperAsync(), _ => IsConnected, HandleError);
        RefreshStatusCommand = new AsyncRelayCommand(_ => RefreshStatusAsync(), _ => IsConnected, HandleError);

        services.Printer.StateChanged += (_, _) => OnUiThread(RefreshConnectionState);

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();

        _ = RefreshBluetoothAsync();
    }

    private readonly DispatcherTimer _statusTimer;
    private DeviceStatus? _deviceStatus;

    public ObservableCollection<PrinterDevice> Devices { get; } = new();

    public IReadOnlyList<int> ScanDurations { get; } = new[] { 4, 8, 12, 20 };

    public AsyncRelayCommand ScanCommand { get; }

    public AsyncRelayCommand ConnectCommand { get; }

    public AsyncRelayCommand InspectCommand { get; }

    public AsyncRelayCommand UseSavedPrinterCommand { get; }

    public RelayCommand ClearListCommand { get; }

    public AsyncRelayCommand FeedPaperCommand { get; }

    public AsyncRelayCommand RefreshStatusCommand { get; }

    public PrinterDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                ConnectCommand.RaiseCanExecuteChanged();
                InspectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(ScanButtonText));
                ScanCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ScanButtonText => IsScanning ? "Procurando..." : "Pesquisar impressoras";

    public bool IsConnecting
    {
        get => _isConnecting;
        private set
        {
            if (SetProperty(ref _isConnecting, value))
            {
                ConnectCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(ConnectButtonText));
            }
        }
    }

    public bool IsConnected => _services.Printer.IsConnected;

    public bool IsSimulated => _services.Printer.IsSimulated;

    public bool UseSimulator
    {
        get => _useSimulator;
        set
        {
            if (SetProperty(ref _useSimulator, value))
            {
                _services.Settings.Printer.UseSimulator = value;
                _services.SaveSettings();
                OnPropertyChanged(nameof(SimulationNotice));
            }
        }
    }

    public string SimulationNotice =>
        UseSimulator
            ? "Modo simulado: os dados são validados e descartados. Nenhum papel é impresso."
            : "Modo real: os dados serão enviados à impressora física.";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string StatusDetail
    {
        get => _statusDetail;
        private set => SetProperty(ref _statusDetail, value);
    }

    /// <summary>
    /// Resultado da última busca BLE. Fica separado de <see cref="StatusText"/>
    /// de propósito: buscar não pode mudar o estado da conexão exibido.
    /// </summary>
    public string DiscoveryText
    {
        get => _discoveryText;
        private set => SetProperty(ref _discoveryText, value);
    }

    public string DiscoveryDetail
    {
        get => _discoveryDetail;
        private set => SetProperty(ref _discoveryDetail, value);
    }

    public Brush StatusBrush => IsConnected
        ? (IsSimulated ? new SolidColorBrush(Color.FromRgb(0xB4, 0x53, 0x09)) : new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x3D)))
        : new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));

    public string ConnectButtonText
    {
        get
        {
            if (IsConnecting)
            {
                return "Conectando...";
            }

            return IsConnected ? "Desconectar" : "Conectar";
        }
    }

    public string ManualMac
    {
        get => _manualMac;
        set
        {
            if (SetProperty(ref _manualMac, value))
            {
                ConnectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string BluetoothMessage
    {
        get => _bluetoothMessage;
        private set => SetProperty(ref _bluetoothMessage, value);
    }

    public bool BluetoothReady
    {
        get => _bluetoothReady;
        private set => SetProperty(ref _bluetoothReady, value);
    }

    public int ScanSeconds
    {
        get => _scanSeconds;
        set => SetProperty(ref _scanSeconds, Math.Clamp(value, 2, 30));
    }

    public string GattSummary
    {
        get => _gattSummary;
        private set => SetProperty(ref _gattSummary, value);
    }

    public bool IsInspecting
    {
        get => _isInspecting;
        private set => SetProperty(ref _isInspecting, value);
    }

    public string ConnectedDeviceText =>
        _services.Printer.IsConnected
            ? $"{_services.Printer.DeviceName}"
            : "Nenhuma";

    public string MtuText =>
        _services.Printer.Mtu > 0 ? $"{_services.Printer.Mtu} bytes" : "-";

    // ---------- estado do equipamento (0xA3) ----------

    public bool HasDeviceStatus => _deviceStatus is not null;

    public string DeviceStatusText => _deviceStatus?.Summary ?? "—";

    public string DeviceStatusRaw => _deviceStatus?.RawText ?? "leia o estado para ver os bytes";

    public string BatteryText => _deviceStatus is null
        ? "—"
        : (_deviceStatus.LowBattery ? "Fraca" : "OK");

    public bool LowBattery => _deviceStatus?.LowBattery == true;

    public bool PaperOut => _deviceStatus?.PaperOut == true;

    public bool CoverOpen => _deviceStatus?.CoverOpen == true;

    public bool HeadHot => _deviceStatus?.HeadHot == true;

    public bool Busy => _deviceStatus?.Busy == true;

    public bool HasWarning => LowBattery || PaperOut || CoverOpen || HeadHot;

    /// <summary>Aviso curto para o cabeçalho, quando há algo errado.</summary>
    public string HeaderWarning
    {
        get
        {
            if (_deviceStatus is null)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            if (_deviceStatus.PaperOut)
            {
                parts.Add("sem papel");
            }

            if (_deviceStatus.CoverOpen)
            {
                parts.Add("tampa aberta");
            }

            if (_deviceStatus.LowBattery)
            {
                parts.Add("bateria fraca");
            }

            if (_deviceStatus.HeadHot)
            {
                parts.Add("cabeçote quente");
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>Lê o estado no equipamento (comando 0xA3).</summary>
    public async Task RefreshStatusAsync()
    {
        if (!IsConnected)
        {
            ApplyStatus(null);
            return;
        }

        try
        {
            var status = await _services.Printer.ReadStatusAsync(_services.Settings.Printer);
            OnUiThread(() => ApplyStatus(status));
        }
        catch (Exception ex)
        {
            _services.Log.Warn("Estado", $"Falha ao consultar o estado: {ex.Message}");
        }
    }

    private void ApplyStatus(DeviceStatus? status)
    {
        _deviceStatus = status;

        OnPropertyChanged(nameof(HasDeviceStatus));
        OnPropertyChanged(nameof(DeviceStatusText));
        OnPropertyChanged(nameof(DeviceStatusRaw));
        OnPropertyChanged(nameof(BatteryText));
        OnPropertyChanged(nameof(LowBattery));
        OnPropertyChanged(nameof(PaperOut));
        OnPropertyChanged(nameof(CoverOpen));
        OnPropertyChanged(nameof(HeadHot));
        OnPropertyChanged(nameof(Busy));
        OnPropertyChanged(nameof(HasWarning));
        OnPropertyChanged(nameof(HeaderWarning));
    }

    /// <summary>Quantidade de pontos para o avanço manual de papel.</summary>
    public int FeedAmount
    {
        get => _feedAmount;
        set => SetProperty(ref _feedAmount, Math.Clamp(value, 1, 2000));
    }

    public string FeedAmountHint => $"≈ {FeedAmount * 0.125:0.0} mm";

    private async Task FeedPaperAsync()
    {
        if (!IsConnected)
        {
            StatusDetail = "Conecte a impressora para avançar o papel.";
            return;
        }

        var amount = Math.Clamp(FeedAmount, 1, 2000);
        StatusDetail = $"Avançando {amount} pontos (≈ {amount * 0.125:0.0} mm)...";

        try
        {
            var ok = await _services.Printer.FeedPaperAsync(amount, _services.Settings.Printer);
            StatusDetail = ok
                ? $"Papel avançado em {amount} pontos (≈ {amount * 0.125:0.0} mm)."
                : "Não foi possível avançar o papel: impressora desconectada.";
        }
        catch (Exception ex)
        {
            StatusDetail = $"Falha ao avançar o papel: {ex.Message}";
            _services.Log.Error("Impressora", StatusDetail);
        }
    }

    public async Task RefreshBluetoothAsync()
    {
        var status = await BluetoothSupport.CheckAsync();
        OnUiThread(() =>
        {
            BluetoothMessage = status.Message;
            BluetoothReady = status.CanScan;
        });
    }

    private void ClearList()
    {
        _found.Clear();
        Devices.Clear();
        SelectedDevice = null;
        _discoveryShowsConnection = false;
        DiscoveryText = "Lista limpa.";
        DiscoveryDetail = "Clique em Pesquisar impressoras para procurar de novo.";
    }

    /// <summary>
    /// Insere ou atualiza um dispositivo na lista exibida. A impressora
    /// conectada fica sempre no topo e a seleção do usuário é preservada.
    /// </summary>
    private void PublishDevice(PrinterDevice device)
    {
        if (string.IsNullOrWhiteSpace(device.ModelHint))
        {
            device = device with { ModelHint = PrinterProfile.Resolve(device.Name).Key };
        }

        var existing = _found.FirstOrDefault(d => d.Address == device.Address);
        if (existing is not null)
        {
            _found.Remove(existing);
        }

        _found.Add(device);

        var connectedAddress = _services.Printer.IsConnected ? _services.Printer.Device?.Address : null;
        var selectedAddress = SelectedDevice?.Address;

        var ordered = _found
            .OrderByDescending(d => connectedAddress is { } connected && d.Address == connected)
            .ThenByDescending(d => BlePrinterDiscovery.IsCompatible(d))
            .ThenByDescending(d => d.Rssi)
            .ToList();

        Devices.Clear();
        foreach (var item in ordered)
        {
            Devices.Add(item);
        }

        if (selectedAddress is { } keep)
        {
            SelectedDevice = Devices.FirstOrDefault(d => d.Address == keep);
        }
    }

    /// <summary>
    /// Garante que a impressora em uso apareça na lista, inclusive depois de uma
    /// reconexão automática ou de uma conexão manual pelo MAC.
    /// </summary>
    private void EnsureConnectedDeviceListed()
    {
        var device = _services.Printer.Device;
        if (device is null || _found.Any(d => d.Address == device.Address))
        {
            return;
        }

        PublishDevice(device);
    }

    private async Task ScanAsync()
    {
        IsScanning = true;
        _discoveryShowsConnection = false;
        _found.Clear();
        Devices.Clear();
        SelectedDevice = null;

        // Uma impressora conectada não anuncia por Bluetooth. Sem devolvê-la à
        // lista, a busca pareceria tê-la "perdido" e o resumo diria que nenhuma
        // impressora compatível existe.
        var connected = _services.Printer.IsConnected ? _services.Printer.Device : null;
        if (connected is not null)
        {
            PublishDevice(connected with { LastSeen = DateTimeOffset.Now });
            SelectedDevice = Devices.FirstOrDefault(d => d.Address == connected.Address);
        }

        var scanning = PrinterStatusText.Scanning(ScanSeconds);
        DiscoveryText = scanning.Summary;
        DiscoveryDetail = scanning.Detail;

        try
        {
            var progress = new Progress<PrinterDevice>(device =>
            {
                // Impressoras que anunciam sem nome recebem o nome salvo,
                // quando o MAC confere com o configurado.
                if (string.IsNullOrWhiteSpace(device.Name)
                    && PrinterDevice.TryParseAddress(_services.Settings.Printer.DeviceMac, out var savedMac)
                    && savedMac == device.Address
                    && !string.IsNullOrWhiteSpace(_services.Settings.Printer.DeviceName))
                {
                    device = device with
                    {
                        Name = _services.Settings.Printer.DeviceName,
                        ModelHint = PrinterProfile.Resolve(_services.Settings.Printer.DeviceName).Key,
                    };
                }

                PublishDevice(device);

                if (device.Name.StartsWith("SC03h", StringComparison.OrdinalIgnoreCase) &&
                    SelectedDevice is null)
                {
                    SelectedDevice = Devices.FirstOrDefault(d => d.Address == device.Address);
                }
            });

            await _services.Printer.ScanAsync(TimeSpan.FromSeconds(ScanSeconds), progress);

            var connectedAddress = connected?.Address;
            var compatible = _found.Count(d =>
                d.Address != connectedAddress && BlePrinterDiscovery.IsCompatible(d));

            var message = PrinterStatusText.ScanCompleted(
                compatible,
                _found.Count,
                _services.Printer.IsConnected,
                _services.Printer.DeviceName);

            DiscoveryText = message.Summary;
            DiscoveryDetail = message.Detail;

            // O resumo de uma busca feita conectado cita a conexão; se ela cair,
            // o texto precisa ser substituído em vez de mentir.
            _discoveryShowsConnection = _services.Printer.IsConnected;

            if (_services.Printer.IsConnected)
            {
                SelectedDevice = Devices.FirstOrDefault(d => d.Address == connectedAddress) ?? SelectedDevice;
            }
            else
            {
                SelectedDevice ??= Devices.FirstOrDefault(d => BlePrinterDiscovery.IsCompatible(d));
            }
        }
        catch (Exception ex)
        {
            var failure = PrinterStatusText.ScanFailed(ex.Message);
            DiscoveryText = failure.Summary;
            DiscoveryDetail = failure.Detail;
            _services.Log.Error("BLE", $"Varredura falhou: {ex.Message}");
        }
        finally
        {
            IsScanning = false;
        }
    }

    private bool CanConnect(object? parameter) =>
        (IsConnected || SelectedDevice is not null || IsValidManualMac()) && !IsConnecting;

    private bool IsValidManualMac() =>
        PrinterDevice.TryParseAddress(ManualMac, out _);

    private async Task UseSavedPrinterAsync()
    {
        if (PrinterDevice.TryParseAddress(_services.Settings.Printer.DeviceMac, out var address))
        {
            ManualMac = PrinterDevice.FormatAddress(address);
            var device = new PrinterDevice
            {
                Name = _services.Settings.Printer.DeviceName,
                Address = address,
            };

            PublishDevice(device);
            SelectedDevice = Devices.FirstOrDefault(d => d.Address == address) ?? device;
            await ConnectOrDisconnectAsync();
        }
    }

    private async Task ConnectOrDisconnectAsync()
    {
        if (IsConnected)
        {
            StatusText = "Desconectando...";
            await _services.Printer.DisconnectAsync();
            RefreshConnectionState();
            return;
        }

        PrinterDevice? device = SelectedDevice;
        if (device is null && IsValidManualMac())
        {
            PrinterDevice.TryParseAddress(ManualMac, out var address);
            device = new PrinterDevice { Address = address, Name = ManualMac };
        }

        if (device is null)
        {
            StatusDetail = "Selecione um dispositivo ou informe o endereço MAC.";
            return;
        }

        IsConnecting = true;
        StatusText = "Conectando...";
        StatusDetail = $"Estabelecendo conexão com {device.DisplayName}...";

        try
        {
            await _services.Printer.ConnectAsync(device, UseSimulator, _services.Settings.Printer);
            RefreshConnectionState();

            if (IsConnected)
            {
                _services.Settings.Printer.DeviceName = device.Name;
                _services.Settings.Printer.DeviceMac = device.AddressText;
                _services.SaveSettings();

                // Deixa a impressora em uso visível na lista e explica por que
                // ela não aparece nas buscas seguintes.
                PublishDevice(device);
                var name = string.IsNullOrWhiteSpace(_services.Printer.DeviceName)
                    ? "A impressora"
                    : _services.Printer.DeviceName;

                DiscoveryText = "Impressora em uso";
                DiscoveryDetail =
                    $"{name} está conectada e não anuncia por Bluetooth; " +
                    "Pesquisar lista apenas os outros dispositivos.";
                _discoveryShowsConnection = true;
            }
        }
        catch (Exception ex)
        {
            StatusText = "Falha na conexão";
            StatusDetail = ex.Message;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private async Task InspectAsync()
    {
        PrinterDevice? device = SelectedDevice;
        if (device is null && IsValidManualMac())
        {
            PrinterDevice.TryParseAddress(ManualMac, out var address);
            device = new PrinterDevice { Address = address, Name = ManualMac };
        }

        if (device is null)
        {
            return;
        }

        IsInspecting = true;
        GattSummary = "Consultando serviços GATT...";
        try
        {
            var inspection = await Task.Run(() => GattInspector.InspectAsync(device.Address));

            var lines = new List<string>
            {
                $"Dispositivo: {inspection.DeviceName} ({inspection.Address})",
                $"MTU (PDU máx.): {inspection.MaxPduSize} bytes",
                $"Serviço 0xAE30: {(inspection.HasPrinterService ? "presente" : "ausente")} | " +
                $"0xAE01 escrita: {(inspection.HasWriteCharacteristic ? "presente" : "ausente")} | " +
                $"0xAE02 notificação: {(inspection.HasNotifyCharacteristic ? "presente" : "ausente")}",
                inspection.Message,
                string.Empty,
            };

            foreach (var service in inspection.Services)
            {
                lines.Add($"Serviço {service.Uuid}");
                foreach (var characteristic in service.Characteristics)
                {
                    lines.Add($"    {characteristic.Uuid}  ({characteristic.Properties})");
                }
            }

            GattSummary = string.Join(Environment.NewLine, lines);
            _services.Log.Info("GATT", inspection.Message);
        }
        catch (Exception ex)
        {
            GattSummary = $"Falha ao inspecionar: {ex.Message}";
        }
        finally
        {
            IsInspecting = false;
        }
    }

    private void RefreshConnectionState()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsSimulated));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(ConnectedDeviceText));
        OnPropertyChanged(nameof(MtuText));

        if (IsConnected)
        {
            var connected = PrinterStatusText.Connected(_services.Printer.DeviceName, IsSimulated);
            StatusText = connected.Summary;
            StatusDetail = connected.Detail;

            EnsureConnectedDeviceListed();
            _statusTimer.Start();
            _ = RefreshStatusAsync();
        }
        else
        {
            var disconnected = PrinterStatusText.Disconnected();
            StatusText = disconnected.Summary;
            StatusDetail = disconnected.Detail;

            // A dica de descoberta pode estar falando da impressora que acabou
            // de ser desconectada; nesse caso ela deixaria de ser verdadeira.
            if (_discoveryShowsConnection)
            {
                _discoveryShowsConnection = false;
                DiscoveryText = "Desconectado.";
                DiscoveryDetail = "Pesquise para listar os dispositivos BLE disponíveis.";
            }

            _statusTimer.Stop();
            ApplyStatus(null);
        }

        ConnectCommand.RaiseCanExecuteChanged();
        InspectCommand.RaiseCanExecuteChanged();
        FeedPaperCommand.RaiseCanExecuteChanged();
        RefreshStatusCommand.RaiseCanExecuteChanged();
    }

    private void HandleError(Exception ex)
    {
        _services.Log.Error("UI", $"Erro em comando: {ex.GetType().Name}: {ex.Message}");
        MiniPrint.Desktop.Windows.MessageDialog.Show(ex.Message, "MiniPrint Desktop", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static void OnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
