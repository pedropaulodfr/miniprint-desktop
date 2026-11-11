using MiniPrint.Core.Models;

namespace MiniPrint.Core.Abstractions;

/// <summary>
/// Canal de bytes com o equipamento. A implementação real usa GATT/BLE;
/// a simulada apenas valida os frames e devolve notificações.
/// Isso mantém a camada de protocolo independente do Windows.
/// </summary>
public interface IPrinterTransport : IAsyncDisposable
{
    /// <summary>Nome do dispositivo conectado (ou "Simulador").</summary>
    string DeviceName { get; }

    bool IsConnected { get; }

    /// <summary>Tamanho máximo de PDU negociado, em bytes (inclui o cabeçalho ATT).</summary>
    int Mtu { get; }

    /// <summary>Verdadeiro quando os dados vão para um simulador, não para o hardware.</summary>
    bool IsSimulated { get; }

    /// <summary>Dados recebidos pela característica de notificação.</summary>
    event EventHandler<NotificationEventArgs>? NotificationReceived;

    /// <summary>Desconexão inesperada (queda de enlace, dispositivo desligado...).</summary>
    event EventHandler<string>? Disconnected;

    Task ConnectAsync(PrinterDevice device, CancellationToken cancellationToken = default);

    Task DisconnectAsync();

    /// <summary>Escreve um bloco de bytes na característica de escrita.</summary>
    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
}

public sealed class NotificationEventArgs : EventArgs
{
    public NotificationEventArgs(byte[] data)
    {
        Data = data;
    }

    public byte[] Data { get; }
}

/// <summary>Descoberta de impressoras BLE nas proximidades.</summary>
public interface IPrinterDiscovery
{
    /// <summary>
    /// Procura dispositivos BLE durante o período indicado, reportando cada
    /// achado (inclusive atualizações de RSSI) via <paramref name="onFound"/>.
    /// </summary>
    Task<IReadOnlyList<PrinterDevice>> ScanAsync(
        TimeSpan duration,
        IProgress<PrinterDevice>? onFound = null,
        CancellationToken cancellationToken = default);
}
