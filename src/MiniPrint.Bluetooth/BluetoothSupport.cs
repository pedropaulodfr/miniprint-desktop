using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Radios;

namespace MiniPrint.Bluetooth;

public enum BluetoothAvailability
{
    /// <summary>Adaptador presente e ligado.</summary>
    Disponivel,

    /// <summary>O computador não tem adaptador Bluetooth.</summary>
    SemAdaptador,

    /// <summary>Adaptador existe, mas o rádio está desligado (modo avião, chave física).</summary>
    Desligado,

    /// <summary>O Windows negou o acesso ao rádio.</summary>
    AcessoNegado,

    /// <summary>Falha inesperada ao consultar o estado.</summary>
    Erro,
}

public sealed record BluetoothStatus(BluetoothAvailability Availability, string Message)
{
    public bool CanScan => Availability == BluetoothAvailability.Disponivel;
}

public static class BluetoothSupport
{
    /// <summary>
    /// Verifica o adaptador e o estado do rádio. Não lança: devolve o motivo
    /// quando algo impede a descoberta.
    /// </summary>
    public static async Task<BluetoothStatus> CheckAsync()
    {
        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            if (adapter is null)
            {
                return new BluetoothStatus(
                    BluetoothAvailability.SemAdaptador,
                    "Nenhum adaptador Bluetooth encontrado neste computador.");
            }

            if (!adapter.IsLowEnergySupported)
            {
                return new BluetoothStatus(
                    BluetoothAvailability.SemAdaptador,
                    "O adaptador Bluetooth não suporta Bluetooth Low Energy.");
            }

            if (!adapter.IsCentralRoleSupported)
            {
                return new BluetoothStatus(
                    BluetoothAvailability.SemAdaptador,
                    "O adaptador não suporta o papel central (necessário para conectar à impressora).");
            }

            var radio = await adapter.GetRadioAsync();
            if (radio is null)
            {
                return new BluetoothStatus(
                    BluetoothAvailability.Erro,
                    "Não foi possível obter o rádio Bluetooth do adaptador.");
            }

            if (radio.State != RadioState.On)
            {
                return new BluetoothStatus(
                    BluetoothAvailability.Desligado,
                    "O Bluetooth está desligado. Ative-o nas configurações do Windows.");
            }

            return new BluetoothStatus(
                BluetoothAvailability.Disponivel,
                $"Bluetooth disponível ({adapter.BluetoothAddress:x}).");
        }
        catch (UnauthorizedAccessException ex)
        {
            return new BluetoothStatus(
                BluetoothAvailability.AcessoNegado,
                $"O Windows negou o acesso ao Bluetooth: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new BluetoothStatus(
                BluetoothAvailability.Erro,
                $"Falha ao consultar o Bluetooth: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Lê o nome amigável de um dispositivo já conhecido pelo Windows.
    /// Retorna nulo quando o dispositivo não é conhecido ou está inacessível.
    /// </summary>
    public static async Task<string?> TryGetDeviceNameAsync(ulong address)
    {
        try
        {
            using var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            return string.IsNullOrWhiteSpace(device?.Name) ? null : device!.Name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Inicia a descoberta; centraliza o tratamento de erros do watcher.</summary>
    internal static BluetoothLEAdvertisementWatcher StartWatcher(
        Windows.Foundation.TypedEventHandler<BluetoothLEAdvertisementWatcher, BluetoothLEAdvertisementReceivedEventArgs> handler,
        BluetoothLEScanningMode mode = BluetoothLEScanningMode.Active)
    {
        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = mode,
        };

        watcher.Received += handler;
        watcher.Start();
        return watcher;
    }
}
