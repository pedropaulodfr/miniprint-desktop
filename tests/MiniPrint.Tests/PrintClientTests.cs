using System.Diagnostics;
using MiniPrint.Core.Abstractions;
using MiniPrint.Core.Configuration;
using MiniPrint.Core.Models;
using MiniPrint.Protocol;
using MiniPrint.Protocol.Simulation;
using Xunit;

namespace MiniPrint.Tests;

public class PrintClientTests
{
    private static readonly PrinterSettings FastSettings = new()
    {
        ChunkSize = 200,
        InterChunkDelayMs = 0,
        CommandTimeoutMs = 2000,
        UseSimulator = true,
    };

    private static PrintDocument Document(int lines = 20) => new()
    {
        Bitmap = new MonochromeBitmap(384, lines, new byte[48 * lines]),
        Kind = PrintJobKind.Image,
        Title = "teste",
    };

    private static async Task<SimulatedPrinterTransport> ConnectedSimulatorAsync(SimulationOptions? options = null)
    {
        var simulator = new SimulatedPrinterTransport(options);
        await simulator.ConnectAsync(new PrinterDevice { Name = "simulador" });
        return simulator;
    }

    [Fact]
    public async Task Print_SendsCompleteJobToSimulator()
    {
        var simulator = await ConnectedSimulatorAsync(new SimulationOptions { SimulateFlowControl = false });
        await using var client = new CatPrinterClient(simulator);

        var result = await client.PrintAsync(Document(12), new CatPrinterJobOptions(), FastSettings);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(result.WasSimulated);
        Assert.Equal(12, simulator.Report.ScanlinesReceived);
        Assert.True(simulator.Report.LatticeStarted);
        Assert.True(simulator.Report.LatticeEnded);
        Assert.Equal(132, simulator.Report.FeedPoints); // 4 linhas + 128 de margem
        Assert.Empty(simulator.Report.Errors);
    }

    [Fact]
    public async Task Print_ReportsProgressUntilCompletion()
    {
        var simulator = await ConnectedSimulatorAsync(new SimulationOptions { SimulateFlowControl = false });
        await using var client = new CatPrinterClient(simulator);

        var reports = new List<PrintProgress>();
        var progress = new Progress<PrintProgress>(p => reports.Add(p));

        var result = await client.PrintAsync(Document(40), new CatPrinterJobOptions(), FastSettings, progress);
        Assert.True(result.Success);

        // Progress<T> entrega de forma assíncrona; aguarda o último relatório.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline && (reports.Count == 0 || reports[^1].Fraction < 0.99))
        {
            await Task.Delay(20);
        }

        Assert.NotEmpty(reports);
        Assert.True(reports[^1].Fraction >= 0.99);
        Assert.Contains(reports, r => r.LinesSent > 0);
    }

    [Fact]
    public async Task Print_HandlesFlowControlPause()
    {
        var simulator = await ConnectedSimulatorAsync(new SimulationOptions
        {
            SimulateFlowControl = true,
            PauseAfterBytes = 120,
            PauseDuration = TimeSpan.FromMilliseconds(120),
            WriteLatency = TimeSpan.Zero,
        });
        await using var client = new CatPrinterClient(simulator);

        var stopwatch = Stopwatch.StartNew();
        var result = await client.PrintAsync(Document(20), new CatPrinterJobOptions(), FastSettings);
        stopwatch.Stop();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(20, simulator.Report.ScanlinesReceived);
        // O trabalho precisou esperar a liberação do fluxo.
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(80),
            $"Esperava aguardar o XOFF, mas levou {stopwatch.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public async Task Print_WhenPausedForever_ReturnsTimeout()
    {
        var transport = new StallingTransport();
        await using var client = new CatPrinterClient(transport);

        var settings = FastSettings.Clone();
        settings.CommandTimeoutMs = 300;
        settings.ChunkSize = 64;

        var result = await client.PrintAsync(Document(30), new CatPrinterJobOptions(), settings);

        Assert.False(result.Success);
        Assert.Equal(PrintJobStatus.Falhou, result.Status);
        Assert.Contains("pausou o fluxo", result.ErrorMessage);
    }

    [Fact]
    public async Task Print_RejectsWhenAnotherJobIsRunning()
    {
        var simulator = await ConnectedSimulatorAsync(new SimulationOptions { SimulateFlowControl = false });
        await using var client = new CatPrinterClient(simulator);

        var settings = FastSettings.Clone();
        settings.InterChunkDelayMs = 15;

        var first = client.PrintAsync(Document(40), new CatPrinterJobOptions(), settings);
        await Task.Delay(50);
        var second = await client.PrintAsync(Document(5), new CatPrinterJobOptions(), settings);

        Assert.False(second.Success);
        Assert.Contains("andamento", second.ErrorMessage);

        var firstResult = await first;
        Assert.True(firstResult.Success, firstResult.ErrorMessage);
    }

    [Fact]
    public async Task Print_CanBeCancelled()
    {
        var simulator = await ConnectedSimulatorAsync(new SimulationOptions { SimulateFlowControl = false });
        await using var client = new CatPrinterClient(simulator);

        var settings = FastSettings.Clone();
        settings.InterChunkDelayMs = 10;

        using var cts = new CancellationTokenSource();
        var task = client.PrintAsync(Document(200), new CatPrinterJobOptions(), settings, null, cts.Token);
        await Task.Delay(60);
        cts.Cancel();

        var result = await task;
        Assert.False(result.Success);
        Assert.Equal(PrintJobStatus.Cancelado, result.Status);
    }

    [Fact]
    public async Task Print_InjectedCommunicationFailure_IsReported()
    {
        var simulator = await ConnectedSimulatorAsync(new SimulationOptions
        {
            SimulateFlowControl = false,
            FailAfterBytes = 400,
        });
        await using var client = new CatPrinterClient(simulator);

        var result = await client.PrintAsync(Document(30), new CatPrinterJobOptions(), FastSettings);

        Assert.False(result.Success);
        Assert.Equal(PrintJobStatus.Falhou, result.Status);
        Assert.Contains("falha de comunicação", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Print_WhenDisconnected_ReturnsFailureWithoutThrowing()
    {
        var simulator = new SimulatedPrinterTransport();
        await using var client = new CatPrinterClient(simulator);

        var result = await client.PrintAsync(Document(5), new CatPrinterJobOptions(), FastSettings);

        Assert.False(result.Success);
        Assert.Contains("não está conectada", result.ErrorMessage);
    }

    [Fact]
    public async Task Print_UsesConfiguredEnergyAndSpeed()
    {
        var simulator = await ConnectedSimulatorAsync(new SimulationOptions { SimulateFlowControl = false });
        await using var client = new CatPrinterClient(simulator);

        var options = new CatPrinterJobOptions { Energy = 0x6000, Speed = 12 };
        var result = await client.PrintAsync(Document(4), options, FastSettings);

        Assert.True(result.Success);
        Assert.Equal(0x6000, simulator.Report.Energy);

        // A velocidade de impressão é a do primeiro 0xBD; o epílogo muda a
        // velocidade antes do avanço de papel, então o último valor é o de feed.
        var firstSpeed = simulator.ReceivedFrames
            .First(f => f.Command == (byte)CatPrinterCommand.SetSpeed)
            .Payload[0];
        Assert.Equal(12, firstSpeed);
    }

    /// <summary>Transporte que envia XOFF e nunca libera, para testar o timeout.</summary>
    private sealed class StallingTransport : IPrinterTransport
    {
        private bool _stalled;

        public string DeviceName => "travado";

        public bool IsConnected { get; private set; } = true;

        public int Mtu => 247;

        public bool IsSimulated => true;

        public event EventHandler<NotificationEventArgs>? NotificationReceived;

        public event EventHandler<string>? Disconnected;

        public Task ConnectAsync(PrinterDevice device, CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            if (!_stalled)
            {
                _stalled = true;
                NotificationReceived?.Invoke(this, new NotificationEventArgs(
                    CatPrinterProtocol.FlowPause.ToArray()));
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disconnected?.Invoke(this, "fim");
            return ValueTask.CompletedTask;
        }
    }
}
