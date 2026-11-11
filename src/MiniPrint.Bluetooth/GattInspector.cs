using MiniPrint.Core.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace MiniPrint.Bluetooth;

public sealed record GattCharacteristicInfo(Guid Uuid, string Properties, bool CanWrite, bool CanNotify);

public sealed record GattServiceInfo(Guid Uuid, IReadOnlyList<GattCharacteristicInfo> Characteristics);

public sealed record GattInspection(
    bool Success,
    string Message,
    string DeviceName,
    string Address,
    int MaxPduSize,
    IReadOnlyList<GattServiceInfo> Services)
{
    public bool HasPrinterService { get; init; }

    public bool HasWriteCharacteristic { get; init; }

    public bool HasNotifyCharacteristic { get; init; }
}

/// <summary>
/// Enumera serviços e características GATT sem imprimir nada.
/// É a base do "Teste 3 - Serviços" do roteiro de validação física.
/// </summary>
public static class GattInspector
{
    public static async Task<GattInspection> InspectAsync(
        ulong address,
        CancellationToken cancellationToken = default)
    {
        var addressText = PrinterDevice.FormatAddress(address);
        BluetoothLEDevice? device = null;
        GattSession? session = null;

        try
        {
            device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            if (device is null)
            {
                return new GattInspection(
                    false,
                    $"Dispositivo {addressText} não encontrado.",
                    string.Empty,
                    addressText,
                    0,
                    Array.Empty<GattServiceInfo>());
            }

            var name = string.IsNullOrWhiteSpace(device.Name) ? addressText : device.Name;

            try
            {
                session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId);
                session.MaintainConnection = true;
            }
            catch (Exception)
            {
                session = null;
            }

            var result = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
            if (result.Status != GattCommunicationStatus.Success)
            {
                return new GattInspection(
                    false,
                    $"Falha ao listar serviços: {result.Status}.",
                    name,
                    addressText,
                    session?.MaxPduSize ?? 0,
                    Array.Empty<GattServiceInfo>());
            }

            var services = new List<GattServiceInfo>();
            var hasPrintService = false;
            var hasWrite = false;
            var hasNotify = false;

            foreach (var service in result.Services)
            {
                var characteristics = new List<GattCharacteristicInfo>();
                try
                {
                    var chars = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                    if (chars.Status == GattCommunicationStatus.Success)
                    {
                        foreach (var characteristic in chars.Characteristics)
                        {
                            var canWrite =
                                (characteristic.CharacteristicProperties & (GattCharacteristicProperties.Write |
                                                                            GattCharacteristicProperties.WriteWithoutResponse)) != 0;
                            var canNotify =
                                (characteristic.CharacteristicProperties & (GattCharacteristicProperties.Notify |
                                                                            GattCharacteristicProperties.Indicate)) != 0;

                            characteristics.Add(new GattCharacteristicInfo(
                                characteristic.Uuid,
                                Describe(characteristic.CharacteristicProperties),
                                canWrite,
                                canNotify));

                            if (characteristic.Uuid == Protocol.CatPrinterProtocol.WriteCharacteristicUuid)
                            {
                                hasWrite = true;
                            }

                            if (characteristic.Uuid == Protocol.CatPrinterProtocol.NotifyCharacteristicUuid)
                            {
                                hasNotify = true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    characteristics.Add(new GattCharacteristicInfo(
                        Guid.Empty,
                        $"erro ao ler características: {ex.Message}",
                        false,
                        false));
                }

                if (service.Uuid == Protocol.CatPrinterProtocol.ServiceUuid)
                {
                    hasPrintService = true;
                }

                services.Add(new GattServiceInfo(service.Uuid, characteristics));
            }

            var message = hasPrintService && hasWrite
                ? "Serviço 0xAE30 e característica de escrita 0xAE01 confirmados."
                : "Serviço esperado (0xAE30/0xAE01) não localizado; veja a lista abaixo.";

            return new GattInspection(
                true,
                message,
                name,
                addressText,
                session?.MaxPduSize ?? 0,
                services)
            {
                HasPrinterService = hasPrintService,
                HasWriteCharacteristic = hasWrite,
                HasNotifyCharacteristic = hasNotify,
            };
        }
        catch (Exception ex)
        {
            return new GattInspection(
                false,
                $"{ex.GetType().Name}: {ex.Message}",
                string.Empty,
                addressText,
                0,
                Array.Empty<GattServiceInfo>());
        }
        finally
        {
            if (session is not null)
            {
                try
                {
                    session.MaintainConnection = false;
                    session.Dispose();
                }
                catch (Exception)
                {
                    // ignorado
                }
            }

            device?.Dispose();
        }
    }

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
}
