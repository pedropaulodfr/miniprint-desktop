using System.Collections.Concurrent;
using MiniPrint.Core.Abstractions;
using MiniPrint.Core.Logging;
using MiniPrint.Core.Models;
using Windows.Devices.Bluetooth.Advertisement;

namespace MiniPrint.Bluetooth;

/// <summary>
/// Descoberta de dispositivos BLE via <see cref="BluetoothLEAdvertisementWatcher"/>.
/// O watcher entrega o endereço MAC de 48 bits do anúncio, o nome local e o RSSI,
/// que é exatamente o que a tela de seleção precisa mostrar.
/// </summary>
public sealed class BlePrinterDiscovery : IPrinterDiscovery
{
    private readonly DiagnosticLog _log;

    public BlePrinterDiscovery(DiagnosticLog? log = null)
    {
        _log = log ?? new DiagnosticLog(persistToDisk: false);
    }

    public async Task<IReadOnlyList<PrinterDevice>> ScanAsync(
        TimeSpan duration,
        IProgress<PrinterDevice>? onFound = null,
        CancellationToken cancellationToken = default)
    {
        var status = await BluetoothSupport.CheckAsync();
        if (!status.CanScan)
        {
            _log.Error("BLE", status.Message);
            throw new InvalidOperationException(status.Message);
        }

        var found = new ConcurrentDictionary<ulong, PrinterDevice>();
        var unnamed = new ConcurrentDictionary<ulong, byte>();

        void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            var address = args.BluetoothAddress;
            if (address == 0 || address > 0xFFFFFFFFFFFF)
            {
                return;
            }

            var name = args.Advertisement?.LocalName;
            if (string.IsNullOrWhiteSpace(name) &&
                args.Advertisement?.DataSections is { Count: > 0 } sections)
            {
                foreach (var section in sections)
                {
                    if (section.DataType == (byte)Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementDataTypes.CompleteLocalName)
                    {
                        name = ReadText(section.Data);
                        break;
                    }
                }
            }

            var displayName = name ?? string.Empty;
            var isNew = !found.ContainsKey(address);
            var profile = PrinterProfile.Resolve(displayName);

            var device = new PrinterDevice
            {
                Name = displayName,
                Address = address,
                Rssi = args.RawSignalStrengthInDBm,
                ModelHint = profile.Key,
                LastSeen = DateTimeOffset.Now,
            };

            found[address] = device;
            if (device.Name.Length == 0)
            {
                unnamed.TryAdd(address, 0);
            }

            if (isNew)
            {
                _log.Info("BLE",
                    $"Encontrado: '{device.DisplayName}' {device.AddressText} " +
                    $"RSSI {device.Rssi} dBm (perfil sugerido: {profile.Key})");
            }

            onFound?.Report(device);
        }

        _log.Info("BLE", $"Iniciando varredura por {duration.TotalSeconds:0.#} s...");

        BluetoothLEAdvertisementWatcher? watcher = null;
        try
        {
            watcher = BluetoothSupport.StartWatcher(OnReceived);

            var deadline = DateTimeOffset.Now + duration;
            while (DateTimeOffset.Now < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = deadline - DateTimeOffset.Now;
                await Task.Delay((int)Math.Clamp(remaining.TotalMilliseconds, 1, 200), cancellationToken);
            }
        }
        finally
        {
            if (watcher is not null)
            {
                try
                {
                    watcher.Stop();
                    watcher.Received -= OnReceived;
                }
                catch (Exception)
                {
                    // Encerramento do watcher nunca deve derrubar a UI.
                }
            }
        }

        // Dispositivos que anunciaram sem nome: tenta o nome conhecido do Windows.
        foreach (var address in unnamed.Keys.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = await BluetoothSupport.TryGetDeviceNameAsync(address);
            if (resolved is null)
            {
                continue;
            }

            var previous = found[address];
            found[address] = previous with
            {
                Name = resolved,
                ModelHint = PrinterProfile.Resolve(resolved).Key,
            };
        }

        var list = found.Values
            .OrderByDescending(d => IsCompatible(d))
            .ThenBy(d => d.Name.Length == 0)
            .ThenByDescending(d => d.Rssi)
            .ToList();

        _log.Info("BLE", $"Varredura concluída: {list.Count} dispositivo(s) BLE.");
        return list;
    }

    private static string? ReadText(Windows.Storage.Streams.IBuffer buffer)
    {
        try
        {
            using var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer);
            var bytes = new byte[buffer.Length];
            reader.ReadBytes(bytes);
            var text = System.Text.Encoding.UTF8.GetString(bytes).Trim('\0', ' ');
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Verdadeiro quando o nome anunciado corresponde a um modelo conhecido.</summary>
    public static bool IsCompatible(PrinterDevice device)
    {
        if (string.IsNullOrWhiteSpace(device.Name))
        {
            return false;
        }

        var profile = PrinterProfile.Resolve(device.Name);
        return !string.Equals(profile.Key, PrinterProfile.GenericCat.Key, StringComparison.Ordinal)
               || device.Name.StartsWith("SC03", StringComparison.OrdinalIgnoreCase);
    }
}
