using MiniPrint.Core.Abstractions;
using MiniPrint.Core.Models;

namespace MiniPrint.Diagnostics;

/// <summary>
/// Transporte que apenas repassa as escritas ao transporte real, gravando tudo
/// em um arquivo. Serve para capturar o tráfego bruto e comparar com o que o
/// aplicativo do celular envia.
/// </summary>
public sealed class TeeTransport : IPrinterTransport
{
    private readonly IPrinterTransport _inner;
    private readonly FileStream _capture;

    public TeeTransport(IPrinterTransport inner, string capturePath)
    {
        _inner = inner;
        _capture = new FileStream(capturePath, FileMode.Create, FileAccess.Write, FileShare.Read);
    }

    public string DeviceName => _inner.DeviceName;

    public bool IsConnected => _inner.IsConnected;

    public int Mtu => _inner.Mtu;

    public bool IsSimulated => _inner.IsSimulated;

    public event EventHandler<NotificationEventArgs>? NotificationReceived
    {
        add => _inner.NotificationReceived += value;
        remove => _inner.NotificationReceived -= value;
    }

    public event EventHandler<string>? Disconnected
    {
        add => _inner.Disconnected += value;
        remove => _inner.Disconnected -= value;
    }

    public Task ConnectAsync(PrinterDevice device, CancellationToken cancellationToken = default) =>
        _inner.ConnectAsync(device, cancellationToken);

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        await _capture.WriteAsync(data, cancellationToken);
        await _capture.FlushAsync(cancellationToken);
        await _inner.WriteAsync(data, cancellationToken);
    }

    public Task DisconnectAsync() => _inner.DisconnectAsync();

    public async ValueTask DisposeAsync()
    {
        await _capture.DisposeAsync();
        await _inner.DisposeAsync();
    }
}
