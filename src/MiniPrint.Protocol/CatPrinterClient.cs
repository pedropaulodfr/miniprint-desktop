using System.Diagnostics;
using MiniPrint.Core.Abstractions;
using MiniPrint.Core.Configuration;
using MiniPrint.Core.Logging;
using MiniPrint.Core.Models;

namespace MiniPrint.Protocol;

/// <summary>
/// Executa um trabalho de impressão sobre um <see cref="IPrinterTransport"/>.
///
/// Responsabilidades:
/// <list type="bullet">
/// <item>serializar trabalhos (um por impressora);</item>
/// <item>fragmentar o fluxo de bytes no tamanho de escrita BLE;</item>
/// <item>respeitar o controle de fluxo (XOFF/XON) enviado pela impressora;</item>
/// <item>aplicar o intervalo entre escritas e tratar timeouts/cancelamento.</item>
/// </list>
/// </summary>
public sealed class CatPrinterClient : IAsyncDisposable
{
    private readonly IPrinterTransport _transport;
    private readonly DiagnosticLog _log;
    private readonly SemaphoreSlim _jobLock = new(1, 1);

    /// <summary>
    /// Garante acesso exclusivo ao dispositivo para comandos curtos (sondas de
    /// estado). Fica tomado durante um trabalho de impressão, para que uma
    /// sonda nunca caia no meio do fluxo de dados.
    /// </summary>
    private readonly SemaphoreSlim _probeLock = new(1, 1);

    private volatile bool _paused;
    private bool _subscribed;
    private long _bytesSent;
    private int _packetsSent;

    public CatPrinterClient(IPrinterTransport transport, DiagnosticLog? log = null)
    {
        _transport = transport;
        _log = log ?? new DiagnosticLog(persistToDisk: false);
    }

    public IPrinterTransport Transport => _transport;

    public bool IsBusy => _jobLock.CurrentCount == 0;

    private void EnsureSubscribed()
    {
        if (_subscribed)
        {
            return;
        }

        _transport.NotificationReceived += OnNotification;
        _subscribed = true;
    }

    private void OnNotification(object? sender, NotificationEventArgs e)
    {
        var data = e.Data;
        if (data.AsSpan().SequenceEqual(CatPrinterProtocol.FlowPause))
        {
            _paused = true;
            _log.Info("BLE<", "XOFF recebido: impressora pediu pausa no envio.");
            return;
        }

        if (data.AsSpan().SequenceEqual(CatPrinterProtocol.FlowResume))
        {
            _paused = false;
            _log.Info("BLE<", "XON recebido: impressora liberou o envio.");
            return;
        }

        _log.Traffic("BLE<", $"notificação ({data.Length} bytes): {CatPrinterFrame.ToHex(data)}");
    }

    /// <summary>Envia um bloco cru para a característica de escrita (diagnóstico).</summary>
    public async Task SendRawAsync(byte[] data, PrinterSettings settings, CancellationToken cancellationToken = default)
    {
        EnsureSubscribed();
        _log.Traffic("BLE>", $"escrita manual ({data.Length} bytes): {CatPrinterFrame.ToHex(data)}");
        await _transport.WriteAsync(data, cancellationToken);
        await Task.Delay(Math.Max(0, settings.InterChunkDelayMs), cancellationToken);
    }

    /// <summary>
    /// Envia um comando e devolve as notificações recebidas dentro do timeout.
    /// Usado no diagnóstico (o formato das respostas varia entre modelos).
    /// </summary>
    public async Task<IReadOnlyList<byte[]>> ProbeAsync(
        byte[] frame,
        PrinterSettings settings,
        CancellationToken cancellationToken = default)
    {
        EnsureSubscribed();

        // Não injeta comandos no meio de um trabalho de impressão.
        if (!await _probeLock.WaitAsync(0, cancellationToken))
        {
            return Array.Empty<byte[]>();
        }

        var received = new List<byte[]>();
        void Handler(object? _, NotificationEventArgs e)
        {
            lock (received)
            {
                received.Add(e.Data);
            }
        }

        _transport.NotificationReceived += Handler;
        try
        {
            _log.Traffic("BLE>", $"sonda ({frame.Length} bytes): {CatPrinterFrame.ToHex(frame)}");
            await _transport.WriteAsync(frame, cancellationToken);
            await Task.Delay(Math.Max(400, settings.InterChunkDelayMs), cancellationToken);

            var deadline = Environment.TickCount64 + Math.Min(settings.CommandTimeoutMs, 3000);
            while (Environment.TickCount64 < deadline)
            {
                lock (received)
                {
                    if (received.Count > 0)
                    {
                        break;
                    }
                }

                await Task.Delay(50, cancellationToken);
            }
        }
        finally
        {
            _transport.NotificationReceived -= Handler;
            _probeLock.Release();
        }

        return received.ToArray();
    }

    /// <summary>Executa o trabalho. Nunca lança: o resultado descreve o que houve.</summary>
    public async Task<PrintJobResult> PrintAsync(
        PrintDocument document,
        CatPrinterJobOptions options,
        PrinterSettings settings,
        IProgress<PrintProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!_transport.IsConnected)
        {
            return new PrintJobResult
            {
                Success = false,
                Status = PrintJobStatus.Falhou,
                ErrorMessage = "A impressora não está conectada.",
                WasSimulated = _transport.IsSimulated,
            };
        }

        if (!await _jobLock.WaitAsync(0, cancellationToken))
        {
            return new PrintJobResult
            {
                Success = false,
                Status = PrintJobStatus.Falhou,
                ErrorMessage = "Já existe um trabalho de impressão em andamento.",
                WasSimulated = _transport.IsSimulated,
            };
        }

        var stopwatch = Stopwatch.StartNew();
        _bytesSent = 0;
        _packetsSent = 0;
        _paused = false;

        // Espera uma sonda de estado em andamento e bloqueia novas durante o
        // trabalho, para nenhum comando cair no meio do fluxo de dados.
        if (!await _probeLock.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken))
        {
            _jobLock.Release();
            return new PrintJobResult
            {
                Success = false,
                Status = PrintJobStatus.Falhou,
                ErrorMessage = "A impressora está ocupada respondendo uma consulta de estado. Tente novamente.",
                WasSimulated = _transport.IsSimulated,
            };
        }

        try
        {
            EnsureSubscribed();
            _log.Info("Impressão",
                $"Iniciando trabalho '{document.Title}' ({document.Kind}), " +
                $"{document.Bitmap.Width}x{document.Bitmap.Height} pontos, " +
                $"energia 0x{options.Energy:X4}, velocidade {options.Speed}.");

            progress?.Report(PrintProgress.Of(0.01, "Preparando impressão"));
            var writer = new ChunkWriter(
                WriteChunkAsync,
                Math.Max(20, settings.ChunkSize),
                Math.Max(0, settings.InterChunkDelayMs),
                Math.Max(1000, settings.CommandTimeoutMs));

            foreach (var frame in CatPrinterSequence.BuildPreamble(options))
            {
                await writer.AddAsync(frame, cancellationToken);
            }

            await writer.FlushAsync(cancellationToken);

            var totalLines = document.Bitmap.Height;
            var sentLines = 0;
            var lastReport = 0;

            foreach (var line in CatPrinterRaster.EncodeScanlines(document.Bitmap, options.PaperWidth))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.AddAsync(
                    CatPrinterFrame.Build(CatPrinterCommand.DrawBitmap, line),
                    cancellationToken);

                sentLines++;

                if (sentLines - lastReport >= 4 || sentLines == totalLines)
                {
                    lastReport = sentLines;
                    var fraction = totalLines == 0 ? 1 : (double)sentLines / totalLines;
                    progress?.Report(new PrintProgress
                    {
                        Fraction = 0.02 + (fraction * 0.95),
                        Message = $"Enviando dados ({sentLines}/{totalLines} linhas)",
                        LinesSent = sentLines,
                        TotalLines = totalLines,
                    });
                }
            }

            await writer.FlushAsync(cancellationToken);

            progress?.Report(PrintProgress.Of(0.98, "Finalizando (avanço de papel)"));

            foreach (var frame in CatPrinterSequence.BuildEpilogue(options))
            {
                await writer.AddAsync(frame, cancellationToken);
            }

            await writer.FlushAsync(cancellationToken);

            stopwatch.Stop();
            progress?.Report(PrintProgress.Of(1.0, _transport.IsSimulated
                ? "Simulação concluída (nenhum papel foi impresso)"
                : "Impressão concluída"));

            _log.Info("Impressão",
                $"Trabalho concluído: {_bytesSent} bytes em {_packetsSent} escritas BLE, " +
                $"{sentLines} linhas, {stopwatch.ElapsedMilliseconds} ms.");

            return new PrintJobResult
            {
                Success = true,
                Status = PrintJobStatus.Concluido,
                BytesSent = (int)_bytesSent,
                PacketsSent = _packetsSent,
                LinesPrinted = sentLines,
                Duration = stopwatch.Elapsed,
                WasSimulated = _transport.IsSimulated,
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _log.Warn("Impressão", "Trabalho cancelado pelo usuário.");
            return new PrintJobResult
            {
                Success = false,
                Status = PrintJobStatus.Cancelado,
                ErrorMessage = "Trabalho cancelado.",
                BytesSent = (int)_bytesSent,
                PacketsSent = _packetsSent,
                Duration = stopwatch.Elapsed,
                WasSimulated = _transport.IsSimulated,
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _log.Error("Impressão", $"Falha no trabalho: {ex.GetType().Name}: {ex.Message}");
            return new PrintJobResult
            {
                Success = false,
                Status = PrintJobStatus.Falhou,
                ErrorMessage = ex.Message,
                BytesSent = (int)_bytesSent,
                PacketsSent = _packetsSent,
                Duration = stopwatch.Elapsed,
                WasSimulated = _transport.IsSimulated,
            };
        }
        finally
        {
            _probeLock.Release();
            _jobLock.Release();
        }
    }

    private async Task WriteChunkAsync(byte[] buffer, int length, CancellationToken cancellationToken, int delayMs, int timeoutMs)
    {
        await WaitWhilePausedAsync(timeoutMs, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        await _transport.WriteAsync(buffer.AsMemory(0, length), cancellationToken);
        _bytesSent += length;
        _packetsSent++;

        if (delayMs > 0)
        {
            await Task.Delay(delayMs, cancellationToken);
        }
    }

    private async Task WaitWhilePausedAsync(int timeoutMs, CancellationToken cancellationToken)
    {
        if (!_paused)
        {
            return;
        }

        var start = Environment.TickCount64;
        while (_paused)
        {
            if (Environment.TickCount64 - start > timeoutMs)
            {
                throw new TimeoutException(
                    $"A impressora pausou o fluxo (XOFF) e não retomou em {timeoutMs} ms.");
            }

            await Task.Delay(20, cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscribed)
        {
            _transport.NotificationReceived -= OnNotification;
            _subscribed = false;
        }

        _jobLock.Dispose();
        await _transport.DisposeAsync();
    }

    /// <summary>
    /// Acumula bytes de vários frames e fatia em blocos do tamanho de escrita
    /// BLE. A fragmentação é puramente de transporte: o equipamento recompõe o
    /// fluxo, e as duas implementações de referência também cortam frames no meio.
    /// </summary>
    private sealed class ChunkWriter
    {
        private readonly Func<byte[], int, CancellationToken, int, int, Task> _sink;
        private readonly int _chunkSize;
        private byte[] _buffer;
        private int _length;

        public ChunkWriter(
            Func<byte[], int, CancellationToken, int, int, Task> sink,
            int chunkSize,
            int delayMs,
            int timeoutMs)
        {
            _sink = sink;
            _chunkSize = chunkSize;
            _buffer = new byte[chunkSize];
            DelayMs = delayMs;
            TimeoutMs = timeoutMs;
        }

        public int DelayMs { get; }

        public int TimeoutMs { get; }

        public async Task AddAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < data.Length)
            {
                var space = _chunkSize - _length;
                var take = Math.Min(space, data.Length - offset);
                data.Slice(offset, take).CopyTo(_buffer.AsMemory(_length));
                _length += take;
                offset += take;

                if (_length == _chunkSize)
                {
                    await FlushAsync(cancellationToken);
                }
            }
        }

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (_length == 0)
            {
                return;
            }

            await _sink(_buffer, _length, cancellationToken, DelayMs, TimeoutMs);
            _length = 0;
        }
    }
}
