using MiniPrint.Core.Configuration;
using MiniPrint.Core.Logging;
using MiniPrint.Core.Models;
using MiniPrint.Desktop.Services;
using Xunit;

namespace MiniPrint.Tests;

/// <summary>
/// Integração da camada de serviço que a interface usa: conexão, estado e
/// execução de trabalho. Usa o simulador, portanto não toca em hardware.
/// </summary>
public class PrinterServiceTests
{
    private static PrinterService CreateService() =>
        new(new DiagnosticLog(persistToDisk: false));

    private static PrinterSettings FastSettings() => new()
    {
        ChunkSize = 200,
        InterChunkDelayMs = 0,
        CommandTimeoutMs = 2000,
    };

    private static PrintDocument Document(int lines) => new()
    {
        Bitmap = new MonochromeBitmap(384, lines, new byte[48 * lines]),
        Kind = PrintJobKind.Text,
        Title = "documento de teste",
    };

    [Fact]
    public async Task Print_WithoutConnection_FailsGracefully()
    {
        await using var service = CreateService();

        var result = await service.PrintAsync(Document(5), FastSettings());

        Assert.False(result.Success);
        Assert.Contains("Conecte a impressora", result.ErrorMessage);
    }

    [Fact]
    public async Task SimulatedConnection_PrintsAndReportsSimulation()
    {
        await using var service = CreateService();
        var device = new PrinterDevice { Name = "SC03h-5718", Address = 0x18572A44E0BC };

        await service.ConnectAsync(device, simulate: true, FastSettings());

        Assert.True(service.IsConnected);
        Assert.True(service.IsSimulated);
        Assert.Equal("Impressora simulada (SC03h)", service.DeviceName);

        var result = await service.PrintAsync(Document(8), FastSettings());

        Assert.True(result.Success);
        Assert.True(result.WasSimulated);
        Assert.Equal(8, result.LinesPrinted);
        Assert.NotNull(service.LastSimulation);
        Assert.Empty(service.LastSimulation!.Errors);
        Assert.Equal(8, service.LastSimulation.ScanlinesReceived);
    }

    [Fact]
    public async Task Connection_RaisesStateChanged()
    {
        await using var service = CreateService();
        var raised = 0;
        service.StateChanged += (_, _) => Interlocked.Increment(ref raised);

        await service.ConnectAsync(
            new PrinterDevice { Name = "SC03h", Address = 0x18572A44E0BC },
            simulate: true,
            FastSettings());

        await service.DisconnectAsync();

        Assert.True(raised >= 2);
        Assert.False(service.IsConnected);
    }

    [Fact]
    public async Task TextJob_UsesTextEnergy()
    {
        await using var service = CreateService();
        await service.ConnectAsync(
            new PrinterDevice { Name = "SC03h", Address = 0x18572A44E0BC },
            simulate: true,
            FastSettings());

        var settings = FastSettings();
        settings.TextEnergy = 0x6ABC;
        settings.ImageEnergy = 0x1111;

        var text = new PrintDocument
        {
            Bitmap = new MonochromeBitmap(384, 3, new byte[48 * 3]),
            Kind = PrintJobKind.Text,
            Title = "texto",
        };

        await service.PrintAsync(text, settings);

        Assert.Equal(0x6ABC, service.LastSimulation!.Energy);
    }

    [Fact]
    public async Task ImageJob_UsesImageEnergy()
    {
        await using var service = CreateService();
        await service.ConnectAsync(
            new PrinterDevice { Name = "SC03h", Address = 0x18572A44E0BC },
            simulate: true,
            FastSettings());

        var settings = FastSettings();
        settings.TextEnergy = 0x6ABC;
        settings.ImageEnergy = 0x1111;

        var image = new PrintDocument
        {
            Bitmap = new MonochromeBitmap(384, 3, new byte[48 * 3]),
            Kind = PrintJobKind.Image,
            Title = "imagem",
        };

        await service.PrintAsync(image, settings);

        Assert.Equal(0x1111, service.LastSimulation!.Energy);
    }

    [Fact]
    public async Task EnergyOverride_AppliesToTheJob()
    {
        await using var service = CreateService();
        await service.ConnectAsync(
            new PrinterDevice { Name = "SC03h", Address = 0x18572A44E0BC },
            simulate: true,
            FastSettings());

        var settings = FastSettings();
        settings.ImageEnergy = 0x1111;

        var image = new PrintDocument
        {
            Bitmap = new MonochromeBitmap(384, 3, new byte[48 * 3]),
            Kind = PrintJobKind.Image,
            Title = "imagem",
        };

        await service.PrintAsync(image, settings, energyOverride: 0x2A2A);

        Assert.Equal(0x2A2A, service.LastSimulation!.Energy);
    }

    [Fact]
    public async Task SimulatedPrint_RecordsFeedLines()
    {
        await using var service = CreateService();
        await service.ConnectAsync(
            new PrinterDevice { Name = "SC03h", Address = 0x18572A44E0BC },
            simulate: true,
            FastSettings());

        var settings = FastSettings();
        settings.FeedLines = 7;

        await service.PrintAsync(Document(4), settings);

        Assert.Equal(7 + 128, service.LastSimulation!.FeedPoints); // 7 + margem padrão
    }
}
