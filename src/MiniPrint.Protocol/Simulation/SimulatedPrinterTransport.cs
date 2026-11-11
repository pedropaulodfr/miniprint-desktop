using MiniPrint.Core.Abstractions;
using MiniPrint.Core.Models;

namespace MiniPrint.Protocol.Simulation;

/// <summary>Comportamento do simulador.</summary>
public sealed class SimulationOptions
{
    /// <summary>MTU fictício, equivalente a um enlace BLE 4.2 típico.</summary>
    public int Mtu { get; init; } = 247;

    /// <summary>Atraso aplicado a cada escrita (simula o enlace).</summary>
    public TimeSpan WriteLatency { get; init; } = TimeSpan.FromMilliseconds(1);

    /// <summary>Emite XOFF/XON no meio do trabalho para exercitar o controle de fluxo.</summary>
    public bool SimulateFlowControl { get; init; } = true;

    public int PauseAfterBytes { get; init; } = 1200;

    public TimeSpan PauseDuration { get; init; } = TimeSpan.FromMilliseconds(120);

    /// <summary>Falha injetada após N bytes (-1 desativa).</summary>
    public int FailAfterBytes { get; init; } = -1;

    /// <summary>Desconexão injetada após N bytes (-1 desativa).</summary>
    public int DisconnectAfterBytes { get; init; } = -1;
}

/// <summary>Resumo do que o simulador recebeu e validou.</summary>
public sealed class SimulationReport
{
    public bool Connected { get; set; }
    public int BytesReceived { get; set; }
    public int FramesReceived { get; set; }
    public int ScanlinesReceived { get; set; }
    public int FeedPoints { get; set; }
    public ushort Energy { get; set; }
    public byte Speed { get; set; }
    public byte DrawingMode { get; set; }
    public byte Quality { get; set; }
    public bool LatticeStarted { get; set; }
    public bool LatticeEnded { get; set; }
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();

    public bool HasProblems => Errors.Count > 0;

    public string Summary =>
        $"Simulação: {FramesReceived} frames, {BytesReceived} bytes, {ScanlinesReceived} linhas, " +
        $"avanço {FeedPoints}, energia 0x{Energy:X4}, velocidade {Speed}, modo {DrawingMode}. " +
        $"Lattice: {(LatticeStarted ? "iniciado" : "não")}/{(LatticeEnded ? "finalizado" : "não")}. " +
        $"Erros: {Errors.Count}, avisos: {Warnings.Count}.";
}

/// <summary>
/// Impressora simulada. Valida o fluxo de bytes exatamente como o equipamento
/// faria: confere prefixo, CRC, sufixo e ordem dos comandos, e responde com
/// XOFF/XON pelos mesmos frames reais.
///
/// Nada aqui vira papel: o resultado é apenas um relatório de validação.
/// </summary>
public sealed class SimulatedPrinterTransport : IPrinterTransport
{
    private readonly SimulationOptions _options;
    private readonly List<ParsedFrame> _frames = new();
    private readonly List<byte[]> _scanlines = new();
    private readonly List<byte> _rx = new();
    private readonly object _gate = new();

    private bool _disposed;
    private bool _flowPauseSent;
    private bool _flowResumeSent;

    public SimulatedPrinterTransport(SimulationOptions? options = null)
    {
        _options = options ?? new SimulationOptions();
    }

    public string DeviceName => "Impressora simulada (SC03h)";

    public bool IsConnected { get; private set; }

    public bool IsSimulated => true;

    public int Mtu => _options.Mtu;

    public event EventHandler<NotificationEventArgs>? NotificationReceived;

    public event EventHandler<string>? Disconnected;

    public SimulationReport Report { get; } = new();

    public IReadOnlyList<ParsedFrame> ReceivedFrames
    {
        get
        {
            lock (_gate)
            {
                return _frames.ToArray();
            }
        }
    }

    public Task ConnectAsync(PrinterDevice device, CancellationToken cancellationToken = default)
    {
        IsConnected = true;
        Report.Connected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        Report.Connected = false;
        return Task.CompletedTask;
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsConnected)
        {
            throw new InvalidOperationException("Simulador não está conectado.");
        }

        if (_options.FailAfterBytes >= 0 && Report.BytesReceived + data.Length > _options.FailAfterBytes)
        {
            Report.Errors.Add("Falha de comunicação injetada pelo simulador.");
            throw new IOException("Simulação: falha de comunicação injetada no enlace.");
        }

        if (_options.DisconnectAfterBytes >= 0 &&
            Report.BytesReceived + data.Length > _options.DisconnectAfterBytes)
        {
            IsConnected = false;
            Report.Connected = false;
            Disconnected?.Invoke(this, "Simulação: desconexão inesperada injetada.");
            throw new IOException("Simulação: o dispositivo desconectou durante o envio.");
        }

        if (_options.WriteLatency > TimeSpan.Zero)
        {
            await Task.Delay(_options.WriteLatency, cancellationToken);
        }

        lock (_gate)
        {
            Report.BytesReceived += data.Length;
            _rx.AddRange(data.ToArray());
            DrainFrames();
        }

        MaybeRaiseFlowControl();
    }

    private void DrainFrames()
    {
        var span = _rx.ToArray().AsSpan();
        var consumedTotal = 0;

        while (span.Length - consumedTotal >= CatPrinterFrame.MinFrameSize)
        {
            var slice = span[consumedTotal..];
            if (slice[0] != CatPrinterProtocol.Prefix0 || slice[1] != CatPrinterProtocol.Prefix1)
            {
                var resync = FindPrefix(slice);
                if (resync < 0)
                {
                    consumedTotal = span.Length;
                    Report.Errors.Add("Fluxo sem prefixo 51 78: dados descartados.");
                    break;
                }

                Report.Warnings.Add($"{resync} byte(s) fora de frame descartados.");
                consumedTotal += resync;
                continue;
            }

            try
            {
                if (!CatPrinterFrame.TryParse(slice, out var frame, out var consumed))
                {
                    break;
                }

                consumedTotal += consumed;
                HandleFrame(frame);
            }
            catch (InvalidDataException ex)
            {
                Report.Errors.Add($"Frame inválido: {ex.Message}");
                consumedTotal += 1;
            }
        }

        if (consumedTotal > 0)
        {
            _rx.RemoveRange(0, consumedTotal);
        }
    }

    private static int FindPrefix(ReadOnlySpan<byte> data)
    {
        for (var i = 1; i < data.Length - 1; i++)
        {
            if (data[i] == CatPrinterProtocol.Prefix0 && data[i + 1] == CatPrinterProtocol.Prefix1)
            {
                return i;
            }
        }

        return -1;
    }

    private void HandleFrame(ParsedFrame frame)
    {
        _frames.Add(frame);
        Report.FramesReceived++;

        switch ((CatPrinterCommand)frame.Command)
        {
            case CatPrinterCommand.DrawBitmap:
                if (!Report.LatticeStarted)
                {
                    Report.Warnings.Add("Linha enviada antes do início do lattice.");
                }

                _scanlines.Add(frame.Payload);
                Report.ScanlinesReceived++;
                break;

            case CatPrinterCommand.FeedPaper:
                if (frame.Payload.Length >= 2)
                {
                    Report.FeedPoints = frame.Payload[0] | (frame.Payload[1] << 8);
                }

                break;

            case CatPrinterCommand.SetEnergy:
                if (frame.Payload.Length >= 2)
                {
                    Report.Energy = (ushort)(frame.Payload[0] | (frame.Payload[1] << 8));
                }

                break;

            case CatPrinterCommand.SetSpeed:
                if (frame.Payload.Length >= 1)
                {
                    Report.Speed = frame.Payload[0];
                }

                break;

            case CatPrinterCommand.DrawingMode:
                if (frame.Payload.Length >= 1)
                {
                    Report.DrawingMode = frame.Payload[0];
                }

                break;

            case CatPrinterCommand.SetQuality:
                if (frame.Payload.Length >= 1)
                {
                    Report.Quality = frame.Payload[0];
                }

                break;

            case CatPrinterCommand.ControlLattice:
                if (frame.Payload.AsSpan().SequenceEqual(CatPrinterSequence.LatticeStartPayload))
                {
                    Report.LatticeStarted = true;
                }
                else if (frame.Payload.AsSpan().SequenceEqual(CatPrinterSequence.LatticeEndPayload))
                {
                    Report.LatticeEnded = true;
                }

                break;
        }
    }

    private void MaybeRaiseFlowControl()
    {
        if (!_options.SimulateFlowControl || _flowPauseSent)
        {
            return;
        }

        if (Report.BytesReceived < _options.PauseAfterBytes)
        {
            return;
        }

        _flowPauseSent = true;
        // XOFF real da impressora (frame idêntico ao do equipamento).
        NotificationReceived?.Invoke(this, new NotificationEventArgs(
            CatPrinterProtocol.FlowPause.ToArray()));

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_options.PauseDuration);
                if (!IsConnected || _flowResumeSent)
                {
                    return;
                }

                _flowResumeSent = true;
                NotificationReceived?.Invoke(this, new NotificationEventArgs(
                    CatPrinterProtocol.FlowResume.ToArray()));
            }
            catch (Exception)
            {
                // O simulador nunca deve derrubar o processo.
            }
        });
    }

    /// <summary>Reconstrói o bitmap que teria sido impresso (para pré-visualização).</summary>
    public MonochromeBitmap? DecodePrintedBitmap(int widthPoints)
    {
        lock (_gate)
        {
            if (_scanlines.Count == 0)
            {
                return null;
            }

            var stride = widthPoints / 8;
            var bits = new byte[stride * _scanlines.Count];
            for (var y = 0; y < _scanlines.Count; y++)
            {
                var line = _scanlines[y];
                var copy = Math.Min(stride, line.Length);
                for (var i = 0; i < copy; i++)
                {
                    bits[(y * stride) + i] = BitReverse.Reverse(line[i]);
                }
            }

            return new MonochromeBitmap(widthPoints, _scanlines.Count, bits);
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}
