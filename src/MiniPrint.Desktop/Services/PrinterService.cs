using MiniPrint.Core.Abstractions;
using MiniPrint.Core.Configuration;
using MiniPrint.Core.Logging;
using MiniPrint.Core.Models;
using MiniPrint.Protocol;
using MiniPrint.Protocol.Simulation;

namespace MiniPrint.Desktop.Services;

/// <summary>
/// Ponto único de acesso ao hardware: mantém o transporte atual, o cliente de
/// protocolo, o estado da conexão e executa os trabalhos.
///
/// A interface gráfica nunca fala com o Windows.Devices.Bluetooth diretamente;
/// quando o modo simulado está ativo, o transporte é substituído pelo simulador.
/// </summary>
public sealed class PrinterService : IAsyncDisposable
{
    private readonly DiagnosticLog _log;
    private readonly IPrinterDiscovery _discovery;
    private readonly Func<PrinterSettings>? _settingsProvider;

    private IPrinterTransport? _transport;
    private CatPrinterClient? _client;
    private PrinterDevice? _device;

    private PrinterDevice? _lastDevice;
    private PrinterSettings? _lastSettings;
    private bool _intentionalDisconnect;
    private bool _reconnecting;
    private readonly object _reconnectGate = new();

    public PrinterService(
        DiagnosticLog log,
        IPrinterDiscovery? discovery = null,
        Func<PrinterSettings>? settingsProvider = null)
    {
        _log = log;
        _discovery = discovery ?? new MiniPrint.Bluetooth.BlePrinterDiscovery(log);
        _settingsProvider = settingsProvider;
    }

    /// <summary>Configuração atual (pode mudar enquanto o app está aberto).</summary>
    private PrinterSettings? CurrentSettings => _settingsProvider?.Invoke() ?? _lastSettings;

    /// <summary>Avisa a interface que a configuração mudou (nome, MAC, modo...).</summary>
    public void NotifyConfigurationChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public event EventHandler? StateChanged;

    public bool IsConnected => _transport?.IsConnected == true;

    public bool IsSimulated { get; private set; }

    public string DeviceName
    {
        get
        {
            var advertised = _transport?.DeviceName ?? string.Empty;

            // Se o usuário deu um nome à impressora nas configurações, ele vale
            // mais do que o nome anunciado (que pode vir vazio).
            if (!IsSimulated
                && _device is not null
                && CurrentSettings is { } settings
                && !string.IsNullOrWhiteSpace(settings.DeviceName)
                && PrinterDevice.TryParseAddress(settings.DeviceMac, out var configured)
                && configured == _device.Address)
            {
                return settings.DeviceName;
            }

            return advertised;
        }
    }

    public int Mtu => _transport?.Mtu ?? 0;

    public PrinterDevice? Device => _device;

    /// <summary>Relatório da última execução no simulador (nulo no modo real).</summary>
    public SimulationReport? LastSimulation { get; private set; }

    public Task<IReadOnlyList<PrinterDevice>> ScanAsync(
        TimeSpan duration,
        IProgress<PrinterDevice>? onFound = null,
        CancellationToken cancellationToken = default) =>
        _discovery.ScanAsync(duration, onFound, cancellationToken);

    public async Task ConnectAsync(
        PrinterDevice device,
        bool simulate,
        PrinterSettings settings,
        CancellationToken cancellationToken = default)
    {
        _intentionalDisconnect = true;
        await DisconnectAsync();
        _intentionalDisconnect = false;

        IsSimulated = simulate;
        _device = device;
        _lastDevice = device;
        _lastSettings = settings.Clone();
        LastSimulation = null;

        if (simulate)
        {
            var simulator = new SimulatedPrinterTransport();
            await simulator.ConnectAsync(device, cancellationToken);
            _transport = simulator;
            LastSimulation = simulator.Report;
            _log.Info("Conexão", "Modo simulado ativado: nenhum dado será enviado ao hardware.");
        }
        else
        {
            var transport = new MiniPrint.Bluetooth.BlePrinterTransport(_log, settings.WriteWithResponse);
            transport.Disconnected += OnTransportDisconnected;
            await transport.ConnectAsync(device, cancellationToken);
            _transport = transport;
        }

        _client = new CatPrinterClient(_transport, _log);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTransportDisconnected(object? sender, string reason)
    {
        _log.Warn("Conexão", reason);
        StateChanged?.Invoke(this, EventArgs.Empty);

        if (_intentionalDisconnect || IsSimulated || _lastDevice is null || _lastSettings is null)
        {
            return;
        }

        if (!_lastSettings.AutoReconnect)
        {
            _log.Info("Conexão", "Reconexão automática desativada nas configurações.");
            return;
        }

        lock (_reconnectGate)
        {
            if (_reconnecting)
            {
                return;
            }

            _reconnecting = true;
        }

        _ = Task.Run(ReconnectAsync);
    }

    /// <summary>
    /// Tenta reconectar após uma queda inesperada. Nunca lança: o resultado vai
    /// para o log e para o estado exibido na interface.
    /// </summary>
    private async Task ReconnectAsync()
    {
        try
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1.5 * attempt));

                if (IsConnected || _intentionalDisconnect)
                {
                    return;
                }

                _log.Info("Conexão", $"Tentativa de reconexão {attempt}/3 com {_lastDevice!.DisplayName}...");

                try
                {
                    await ConnectAsync(_lastDevice, false, _lastSettings!);
                    if (IsConnected)
                    {
                        _log.Info("Conexão", "Reconectado com sucesso.");
                        StateChanged?.Invoke(this, EventArgs.Empty);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn("Conexão", $"Tentativa {attempt} falhou: {ex.Message}");
                }
            }

            _log.Error("Conexão",
                "Não foi possível reconectar automaticamente. Conecte manualmente.");
        }
        finally
        {
            lock (_reconnectGate)
            {
                _reconnecting = false;
            }
        }
    }

    public async Task DisconnectAsync()
    {
        var wasIntentional = _intentionalDisconnect;
        _intentionalDisconnect = true;

        if (_client is not null)
        {
            try
            {
                await _client.DisposeAsync();
            }
            catch (Exception ex)
            {
                _log.Warn("Conexão", $"Falha ao encerrar o cliente: {ex.Message}");
            }

            _client = null;
        }

        if (_transport is not null)
        {
            try
            {
                await _transport.DisposeAsync();
            }
            catch (Exception ex)
            {
                _log.Warn("Conexão", $"Falha ao desconectar: {ex.Message}");
            }
        }

        _transport = null;
        _device = null;
        _intentionalDisconnect = wasIntentional;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Executa o trabalho. O <paramref name="kind"/> escolhe a energia térmica.</summary>
    public async Task<PrintJobResult> PrintAsync(
        PrintDocument document,
        PrinterSettings settings,
        IProgress<PrintProgress>? progress = null,
        CancellationToken cancellationToken = default,
        ushort? energyOverride = null)
    {
        if (_client is null || _transport is null || !_transport.IsConnected)
        {
            return new PrintJobResult
            {
                Success = false,
                Status = PrintJobStatus.Falhou,
                ErrorMessage = "Conecte a impressora antes de imprimir.",
                WasSimulated = IsSimulated,
            };
        }

        // Respeita o MTU real: um bloco maior que o negociado seria dividido
        // pelo Windows, mas preferimos controlar o tamanho.
        var effective = settings.Clone();
        if (!_transport.IsSimulated && _transport.Mtu >= 23)
        {
            var maxPayload = Math.Max(20, _transport.Mtu - 3);
            if (effective.ChunkSize > maxPayload)
            {
                effective.ChunkSize = maxPayload;
            }
        }

        var energy = energyOverride
                     ?? (document.Kind is PrintJobKind.Text or PrintJobKind.Modelo
                         ? settings.TextEnergy
                         : settings.ImageEnergy);
        var options = CatPrinterJobOptions.FromSettings(effective, null, energy);

        var result = await _client.PrintAsync(document, options, effective, progress, cancellationToken);

        if (IsSimulated && _transport is SimulatedPrinterTransport simulator)
        {
            LastSimulation = simulator.Report;
        }

        return result;
    }

    /// <summary>
    /// Avança o papel sem imprimir. Útil para posicionar o papel antes do
    /// lacre ou para conferir a unidade do comando 0xA1.
    /// </summary>
    public async Task<bool> FeedPaperAsync(
        int points,
        PrinterSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (_client is null || _transport is null || !_transport.IsConnected)
        {
            return false;
        }

        var feed = Math.Clamp(points, 0, 0xFFFF);
        if (feed == 0)
        {
            return true;
        }

        var speed = CatPrinterFrame.Build(CatPrinterCommand.SetSpeed, 8);
        var frame = CatPrinterFrame.Build(
            CatPrinterCommand.FeedPaper,
            (byte)(feed & 0xFF),
            (byte)(feed >> 8));

        var payload = new byte[speed.Length + frame.Length];
        speed.CopyTo(payload, 0);
        frame.CopyTo(payload, speed.Length);

        await _client.SendRawAsync(payload, settings, cancellationToken);
        return true;
    }

    /// <summary>
    /// Lê o estado do equipamento (comando 0xA3): papel, tampa, temperatura do
    /// cabeçote, bateria fraca e ocupado. Devolve nulo quando não há conexão,
    /// quando a impressora não responde ou quando há impressão em andamento.
    /// </summary>
    public async Task<DeviceStatus?> ReadStatusAsync(
        PrinterSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (_client is null || _transport is null || !_transport.IsConnected)
        {
            return null;
        }

        try
        {
            var frame = CatPrinterFrame.Build(CatPrinterCommand.DeviceState, 0x00);
            var replies = await _client.ProbeAsync(frame, settings, cancellationToken);

            foreach (var reply in replies)
            {
                var status = CatPrinterResponses.TryParseStatusFromNotification(reply);
                if (status is not null)
                {
                    return status;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warn("Estado", $"Falha ao ler o estado: {ex.Message}");
        }

        return null;
    }

    /// <summary>Envia uma sonda e devolve as notificações recebidas (diagnóstico).</summary>
    public async Task<IReadOnlyList<byte[]>> ProbeAsync(
        CatPrinterCommand command,
        PrinterSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (_client is null)
        {
            return Array.Empty<byte[]>();
        }

        var frame = CatPrinterFrame.Build(command, 0x00);
        return await _client.ProbeAsync(frame, settings, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}
