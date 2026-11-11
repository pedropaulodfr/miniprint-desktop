using System.Runtime.InteropServices;
using MiniPrint.Core.Abstractions;
using MiniPrint.Core.Logging;
using MiniPrint.Core.Models;
using MiniPrint.Protocol;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace MiniPrint.Bluetooth;

/// <summary>
/// Transporte BLE real sobre <c>Windows.Devices.Bluetooth</c> (GATT).
///
/// Serviço e características do protocolo CAT Printer:
/// <list type="bullet">
/// <item>serviço 0xAE30;</item>
/// <item>escrita 0xAE01 (write without response);</item>
/// <item>notificações 0xAE02 (XOFF/XON e respostas de estado).</item>
/// </list>
/// </summary>
public sealed class BlePrinterTransport : IPrinterTransport
{
    private readonly DiagnosticLog _log;
    private readonly bool _writeWithResponse;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private BluetoothLEDevice? _device;
    private GattDeviceService? _service;
    private GattCharacteristic? _writeCharacteristic;
    private GattCharacteristic? _notifyCharacteristic;
    private GattSession? _session;
    private bool _notificationsEnabled;
    private bool _disposed;
    private string _deviceName = string.Empty;

    public BlePrinterTransport(DiagnosticLog? log = null, bool writeWithResponse = false)
    {
        _log = log ?? new DiagnosticLog(persistToDisk: false);
        _writeWithResponse = writeWithResponse;
    }

    public string DeviceName => _deviceName;

    public bool IsConnected { get; private set; }

    public bool IsSimulated => false;

    public int Mtu { get; private set; } = 23;

    /// <summary>Maior bloco aceito por escrita (MTU menos o cabeçalho ATT de 3 bytes).</summary>
    public int MaxWritePayload => Math.Max(20, Mtu - 3);

    public event EventHandler<NotificationEventArgs>? NotificationReceived;

    public event EventHandler<string>? Disconnected;

    public async Task ConnectAsync(PrinterDevice device, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (device.Address == 0)
        {
            throw new ArgumentException("Endereço BLE inválido.", nameof(device));
        }

        await DisconnectAsync();
        _deviceName = device.DisplayName;

        _log.Info("BLE", $"Conectando a {device.DisplayName} ({device.AddressText})...");

        var status = await BluetoothSupport.CheckAsync();
        if (!status.CanScan)
        {
            throw new InvalidOperationException(status.Message);
        }

        var bluetoothDevice = await RunWithTimeout(
            BluetoothLEDevice.FromBluetoothAddressAsync(device.Address).AsTask(),
            TimeSpan.FromSeconds(15),
            cancellationToken,
            "O Windows não encontrou o dispositivo Bluetooth.");

        if (bluetoothDevice is null)
        {
            throw new InvalidOperationException(
                $"Não foi possível abrir o dispositivo {device.AddressText}. " +
                "Verifique se ele está ligado e ao alcance.");
        }

        _device = bluetoothDevice;
        _device.ConnectionStatusChanged += OnConnectionStatusChanged;

        try
        {
            // MaintainConnection mantém o enlace ativo enquanto o app estiver aberto.
            try
            {
                _session = await RunWithTimeout(
                    GattSession.FromDeviceIdAsync(_device.BluetoothDeviceId).AsTask(),
                    TimeSpan.FromSeconds(10),
                    cancellationToken,
                    "Não foi possível criar a sessão GATT.");
                _session.MaintainConnection = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Warn("BLE", $"Sessão GATT indisponível: {ex.Message}. Seguindo sem ela.");
                _session = null;
            }

            var servicesResult = await RunWithTimeout(
                _device.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(),
                TimeSpan.FromSeconds(25),
                cancellationToken,
                "Tempo esgotado ao descobrir os serviços GATT.");

            if (servicesResult.Status != GattCommunicationStatus.Success)
            {
                throw new InvalidOperationException(
                    $"Falha ao descobrir serviços GATT: {servicesResult.Status}. " +
                    ServicesHint(BluetoothCacheMode.Uncached));
            }

            _log.Info("BLE", $"Serviços descobertos: {servicesResult.Services.Count}.");

            _service = servicesResult.Services.FirstOrDefault(s => s.Uuid == CatPrinterProtocol.ServiceUuid);
            if (_service is null)
            {
                _log.Warn("BLE",
                    $"Serviço {CatPrinterProtocol.ServiceUuid} não encontrado; " +
                    "procurando 0xAE01 em todos os serviços.");
            }

            var characteristics = new List<GattCharacteristic>();
            if (_service is not null)
            {
                var charResult = await RunWithTimeout(
                    _service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(),
                    TimeSpan.FromSeconds(15),
                    cancellationToken,
                    "Tempo esgotado ao listar as características.");

                _log.Info("BLE",
                    $"Características do serviço 0xAE30: status {charResult.Status}, " +
                    $"{charResult.Characteristics?.Count ?? 0} encontrada(s).");

                if (charResult.Status == GattCommunicationStatus.Success && charResult.Characteristics is not null)
                {
                    characteristics.AddRange(charResult.Characteristics);
                }
            }

            // Fallback: varre todos os serviços. Em alguns enlaces o Windows
            // devolve o serviço 0xAE30 sem as características na primeira leitura.
            if (characteristics.All(c => c.Uuid != CatPrinterProtocol.WriteCharacteristicUuid))
            {
                foreach (var service in servicesResult.Services)
                {
                    if (ReferenceEquals(service, _service))
                    {
                        continue;
                    }

                    try
                    {
                        var charResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                        if (charResult.Status == GattCommunicationStatus.Success)
                        {
                            characteristics.AddRange(charResult.Characteristics);
                        }
                    }
                    catch (Exception)
                    {
                        // Serviço sem permissão de leitura: ignora e segue.
                    }
                }
            }

            foreach (var characteristic in characteristics)
            {
                _log.Info("BLE",
                    $"  caract. {characteristic.Uuid} ({Describe(characteristic.CharacteristicProperties)})");
            }

            _writeCharacteristic = characteristics.FirstOrDefault(
                c => c.Uuid == CatPrinterProtocol.WriteCharacteristicUuid);

            _notifyCharacteristic = characteristics.FirstOrDefault(
                c => c.Uuid == CatPrinterProtocol.NotifyCharacteristicUuid);

            // Fallback: qualquer característica com escrita / notificação.
            _writeCharacteristic ??= characteristics.FirstOrDefault(
                c => (c.CharacteristicProperties & (GattCharacteristicProperties.Write |
                                                    GattCharacteristicProperties.WriteWithoutResponse)) != 0);

            _notifyCharacteristic ??= characteristics.FirstOrDefault(
                c => (c.CharacteristicProperties & GattCharacteristicProperties.Notify) != 0);

            if (_writeCharacteristic is null)
            {
                throw new InvalidOperationException(
                    "A impressora não expõe a característica de escrita 0xAE01. " +
                    "Este modelo pode não ser compatível com o protocolo CAT Printer.");
            }

            if (_notifyCharacteristic is not null)
            {
                await EnableNotificationsAsync(cancellationToken);
            }
            else
            {
                _log.Warn("BLE", "Característica de notificação 0xAE02 ausente: sem XOFF/XON.");
            }

            Mtu = _session?.MaxPduSize ?? 23;
            if (Mtu < 23)
            {
                Mtu = 23;
            }

            IsConnected = true;
            _log.Info("BLE",
                $"Conectado. MTU={Mtu}, bloco máximo de escrita={MaxWritePayload} bytes, " +
                $"notificações={( _notificationsEnabled ? "ativas" : "inativas")}.");

            if (_device.ConnectionStatus != BluetoothConnectionStatus.Connected)
            {
                _log.Warn("BLE", $"Estado do enlace: {_device.ConnectionStatus}.");
            }
        }
        catch
        {
            await DisconnectAsync();
            throw;
        }
    }

    private async Task EnableNotificationsAsync(CancellationToken cancellationToken)
    {
        if (_notifyCharacteristic is null)
        {
            return;
        }

        var properties = _notifyCharacteristic.CharacteristicProperties;
        var value = (properties & GattCharacteristicProperties.Notify) != 0
            ? GattClientCharacteristicConfigurationDescriptorValue.Notify
            : GattClientCharacteristicConfigurationDescriptorValue.Indicate;

        var result = await RunWithTimeout(
            _notifyCharacteristic
                .WriteClientCharacteristicConfigurationDescriptorAsync(value)
                .AsTask(),
            TimeSpan.FromSeconds(10),
            cancellationToken,
            "Tempo esgotado ao ativar as notificações.");

        if (result == GattCommunicationStatus.Success)
        {
            _notifyCharacteristic.ValueChanged += OnValueChanged;
            _notificationsEnabled = true;
        }
        else
        {
            _log.Warn("BLE", $"Não foi possível ativar notificações: {result}.");
        }
    }

    private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        try
        {
            var length = (int)args.CharacteristicValue.Length;
            var bytes = new byte[length];
            if (length > 0)
            {
                using var reader = DataReader.FromBuffer(args.CharacteristicValue);
                reader.ReadBytes(bytes);
            }

            NotificationReceived?.Invoke(this, new NotificationEventArgs(bytes));
        }
        catch (Exception ex)
        {
            _log.Error("BLE", $"Falha ao ler notificação: {ex.Message}");
        }
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected && IsConnected)
        {
            IsConnected = false;
            _log.Warn("BLE", "Desconexão inesperada do dispositivo.");
            Disconnected?.Invoke(this, "A impressora desconectou.");
        }
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        var characteristic = _writeCharacteristic
            ?? throw new InvalidOperationException("Transporte não conectado.");

        if (!IsConnected)
        {
            throw new InvalidOperationException("A impressora não está conectada.");
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            using var writer = new DataWriter();
            writer.WriteBytes(data.ToArray());
            var buffer = writer.DetachBuffer();

            var option = _writeWithResponse ||
                         (characteristic.CharacteristicProperties & GattCharacteristicProperties.WriteWithoutResponse) == 0
                ? GattWriteOption.WriteWithResponse
                : GattWriteOption.WriteWithoutResponse;

            var result = await RunWithTimeout(
                characteristic.WriteValueWithResultAsync(buffer, option).AsTask(),
                TimeSpan.FromSeconds(10),
                cancellationToken,
                "Tempo esgotado ao escrever na impressora.");

            if (result.Status != GattCommunicationStatus.Success)
            {
                if (result.Status == GattCommunicationStatus.AccessDenied)
                {
                    throw new IOException(
                        "A impressora recusou a escrita (acesso negado). Isso normalmente acontece " +
                        "quando outro aplicativo está conectado a ela — o próprio MiniPrint em outra " +
                        "janela, o Fun Printer ou o iPrint no celular. Desconecte o outro aplicativo e " +
                        "tente novamente.");
                }

                throw new IOException(
                    $"Escrita GATT falhou: {result.Status} " +
                    $"({(result.ProtocolError is null ? "sem detalhe" : result.ProtocolError.ToString())}).");
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        IsConnected = false;

        if (_notifyCharacteristic is not null && _notificationsEnabled)
        {
            try
            {
                _notifyCharacteristic.ValueChanged -= OnValueChanged;
                await _notifyCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.None);
            }
            catch (Exception)
            {
                // Desconexão forçada: nada a fazer.
            }

            _notificationsEnabled = false;
        }

        if (_device is not null)
        {
            _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
        }

        if (_session is not null)
        {
            try
            {
                _session.MaintainConnection = false;
                _session.Dispose();
            }
            catch (Exception)
            {
                // ignorado
            }

            _session = null;
        }

        _service?.Dispose();
        _service = null;
        _writeCharacteristic = null;
        _notifyCharacteristic = null;

        _device?.Dispose();
        _device = null;
    }

    private static string ServicesHint(BluetoothCacheMode cacheMode) =>
        "Verifique se a impressora está ligada, ao alcance e se já não está conectada a outro " +
        "aplicativo (celular/tablet). Feche o app do celular e tente novamente.";

    private static string Describe(GattCharacteristicProperties properties)
    {
        var parts = new List<string>();
        if (properties.HasFlag(GattCharacteristicProperties.Read))
        {
            parts.Add("read");
        }

        if (properties.HasFlag(GattCharacteristicProperties.Write))
        {
            parts.Add("write");
        }

        if (properties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
        {
            parts.Add("write-no-resp");
        }

        if (properties.HasFlag(GattCharacteristicProperties.Notify))
        {
            parts.Add("notify");
        }

        if (properties.HasFlag(GattCharacteristicProperties.Indicate))
        {
            parts.Add("indicate");
        }

        return parts.Count == 0 ? "nenhuma" : string.Join(", ", parts);
    }

    /// <summary>Espera a tarefa, mas aborta se o Windows travar a chamada GATT.</summary>
    private static async Task<T> RunWithTimeout<T>(
        Task<T> task,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string message)
    {
        var delay = Task.Delay(timeout, cancellationToken);
        var completed = await Task.WhenAny(task, delay);
        if (completed != task)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(message);
        }

        return await task;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await DisconnectAsync();
        _writeLock.Dispose();
    }
}
