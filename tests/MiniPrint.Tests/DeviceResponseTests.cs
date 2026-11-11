using MiniPrint.Protocol;
using MiniPrint.Protocol.Simulation;
using Xunit;

namespace MiniPrint.Tests;

/// <summary>
/// Fixtures capturadas da impressora SC03h-5718 real (MAC 18:57:2A:44:E0:BC)
/// durante a validação do protocolo. Servem como regressão.
/// </summary>
public class DeviceResponseTests
{
    public const string DeviceStateHex = "51 78 A3 01 03 00 00 11 29 9D FF";

    public const string DeviceInfoHex = "51 78 A8 01 0A 00 81 00 03 33 2E 30 2E 34 38 00 00 FF";

    private static byte[] FromHex(string hex) =>
        Convert.FromHexString(hex.Replace(" ", string.Empty));

    [Fact]
    public void DeviceStateResponse_ParsesAndValidatesCrc()
    {
        var frames = CatPrinterResponses.ParseAll(FromHex(DeviceStateHex));

        var frame = Assert.Single(frames);
        Assert.Equal(0xA3, frame.Command);
        Assert.Equal(CatPrinterResponses.DirectionFromPrinter, frame.Direction);
        Assert.Equal(3, frame.Payload.Length);

        var status = CatPrinterResponses.TryParseDeviceStatus(frame);
        Assert.NotNull(status);
        Assert.Equal((byte)0x00, status!.StatusByte);
        Assert.Equal("00 11 29", status.Hex);
    }

    [Fact]
    public void DeviceInfoResponse_ContainsFirmwareVersion()
    {
        var frames = CatPrinterResponses.ParseAll(FromHex(DeviceInfoHex));
        var frame = Assert.Single(frames);

        var info = CatPrinterResponses.TryParseDeviceInfo(frame);
        Assert.NotNull(info);
        Assert.Equal("3.0.48", info!.Version);
    }

    [Fact]
    public void ParseAll_HandlesSeveralFramesInOneNotification()
    {
        var combined = FromHex(DeviceStateHex).Concat(FromHex(DeviceInfoHex)).ToArray();
        var frames = CatPrinterResponses.ParseAll(combined);

        Assert.Equal(2, frames.Count);
        Assert.Equal(0xA3, frames[0].Command);
        Assert.Equal(0xA8, frames[1].Command);
    }

    [Fact]
    public void ParseAll_SkipsLeadingGarbage()
    {
        var noisy = new byte[] { 0x00, 0xFF, 0x12 }.Concat(FromHex(DeviceStateHex)).ToArray();
        var frames = CatPrinterResponses.ParseAll(noisy);

        Assert.Single(frames);
    }

    [Fact]
    public void DeviceInfoResponse_CorruptedCrcIsDropped()
    {
        var corrupted = FromHex(DeviceInfoHex);
        corrupted[^2] ^= 0xFF; // estraga o CRC
        var frames = CatPrinterResponses.ParseAll(corrupted);

        Assert.Empty(frames);
    }

    [Fact]
    public void DeviceStatus_DecodesDocumentedFlags()
    {
        // Payload observado na SC03h real: 00 11 29 (ociosa).
        var status = CatPrinterResponses.TryParseStatus(FromHex("00 11 29"));

        Assert.NotNull(status);
        Assert.True(status!.IsOk);
        Assert.False(status.LowBattery);
        Assert.False(status.PaperOut);
        Assert.Equal("pronta", status.Summary);
        Assert.Equal((byte)0x11, status.Byte2);
        Assert.Equal((byte)0x29, status.Byte3);
    }

    [Theory]
    [InlineData(0x01, "PaperOut")]
    [InlineData(0x02, "CoverOpen")]
    [InlineData(0x04, "HeadHot")]
    [InlineData(0x08, "LowBattery")]
    [InlineData(0x80, "Busy")]
    public void DeviceStatus_EachBitIsDecoded(byte flags, string expectedFlag)
    {
        var status = CatPrinterResponses.TryParseStatus(new[] { flags, (byte)0x11, (byte)0x29 });

        Assert.NotNull(status);
        var property = typeof(DeviceStatus).GetProperty(expectedFlag);
        Assert.NotNull(property);
        Assert.True((bool)property!.GetValue(status)!, $"Bit {flags:X2} não acionou {expectedFlag}.");
    }

    [Fact]
    public void DeviceStatus_CombinedFlagsAreSummarized()
    {
        var status = CatPrinterResponses.TryParseStatus(new byte[] { 0x0F, 0x10, 0x25 });

        Assert.NotNull(status);
        Assert.True(status!.PaperOut);
        Assert.True(status.CoverOpen);
        Assert.True(status.HeadHot);
        Assert.True(status.LowBattery);
        Assert.Contains("sem papel", status.Summary);
        Assert.Contains("bateria fraca", status.Summary);
    }

    [Fact]
    public void DeviceStatus_EmptyPayloadIsRejected()
    {
        Assert.Null(CatPrinterResponses.TryParseStatus(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void DeviceStatus_ShortPayloadUsesZeroForMissingBytes()
    {
        var status = CatPrinterResponses.TryParseStatus(new byte[] { 0x08 });

        Assert.NotNull(status);
        Assert.True(status!.LowBattery);
        Assert.Equal((byte)0, status.Byte2);
    }

    [Fact]
    public void DeviceStatus_ParsesFromFullNotification()
    {
        var notification = FromHex(DeviceStateHex);
        var status = CatPrinterResponses.TryParseStatusFromNotification(notification);

        Assert.NotNull(status);
        Assert.True(status!.IsOk);
        Assert.Equal("00 11 29", CatPrinterFrame.ToHex(new[] { status.Flags, status.Byte2, status.Byte3 }));
    }

    [Fact]
    public void FlowControlFrames_AreRecognized()    {
        Assert.True(CatPrinterResponses.IsFlowPause(CatPrinterProtocol.FlowPause));
        Assert.True(CatPrinterResponses.IsFlowResume(CatPrinterProtocol.FlowResume));
        Assert.False(CatPrinterResponses.IsFlowPause(CatPrinterProtocol.FlowResume));
    }

    [Fact]
    public void ExtractVersion_IgnoresBinaryPrefix()
    {
        Assert.Equal("3.0.48", CatPrinterResponses.ExtractVersion(FromHex("81 00 03 33 2E 30 2E 34 38 00")[..9]
            .Concat(new byte[] { 0x00 }).ToArray()));
    }

    [Fact]
    public void ExtractVersion_ReturnsNullWhenNoPrintableRun()
    {
        Assert.Null(CatPrinterResponses.ExtractVersion(new byte[] { 0x00, 0x01, 0x02 }));
    }
}

public class SimulatorTests
{
    [Fact]
    public async Task Simulator_ValidatesARealJob()
    {
        var simulator = new SimulatedPrinterTransport(new SimulationOptions { SimulateFlowControl = false });
        await simulator.ConnectAsync(new MiniPrint.Core.Models.PrinterDevice { Name = "sim" });

        var document = new MiniPrint.Core.Models.PrintDocument
        {
            Bitmap = new MiniPrint.Core.Models.MonochromeBitmap(384, 6, new byte[48 * 6]),
            Kind = MiniPrint.Core.Models.PrintJobKind.Image,
            Title = "sim",
        };

        var frames = CatPrinterSequence.EnumerateJobFrames(document, new CatPrinterJobOptions()).ToList();
        foreach (var frame in frames)
        {
            await simulator.WriteAsync(frame);
        }

        Assert.Equal(frames.Count, simulator.Report.FramesReceived);
        Assert.Equal(6, simulator.Report.ScanlinesReceived);
        Assert.Empty(simulator.Report.Errors);
    }

    [Fact]
    public async Task Simulator_DetectsCorruptedChecksum()
    {
        var simulator = new SimulatedPrinterTransport(new SimulationOptions { SimulateFlowControl = false });
        await simulator.ConnectAsync(new MiniPrint.Core.Models.PrinterDevice { Name = "sim" });

        var frame = CatPrinterFrame.Build(CatPrinterCommand.SetSpeed, 0x20);
        frame[6] ^= 0xFF;

        await simulator.WriteAsync(frame);

        Assert.NotEmpty(simulator.Report.Errors);
        Assert.Contains(simulator.Report.Errors, e => e.Contains("CRC"));
    }

    [Fact]
    public async Task Simulator_ReassemblesFramesAcrossWrites()
    {
        var simulator = new SimulatedPrinterTransport(new SimulationOptions { SimulateFlowControl = false });
        await simulator.ConnectAsync(new MiniPrint.Core.Models.PrinterDevice { Name = "sim" });

        var frame = CatPrinterFrame.Build(CatPrinterCommand.SetEnergy, 0x00, 0x40);
        await simulator.WriteAsync(frame.AsMemory(0, 3));
        Assert.Equal(0, simulator.Report.FramesReceived);

        await simulator.WriteAsync(frame.AsMemory(3));
        Assert.Equal(1, simulator.Report.FramesReceived);
        Assert.Equal(0x4000, simulator.Report.Energy);
    }

    [Fact]
    public async Task Simulator_DecodesPrintedBitmap()
    {
        var simulator = new SimulatedPrinterTransport(new SimulationOptions { SimulateFlowControl = false });
        await simulator.ConnectAsync(new MiniPrint.Core.Models.PrinterDevice { Name = "sim" });

        var pixels = new byte[384];
        pixels[0] = 1;
        var bitmap = CatPrinterRaster.PackPixels(384, 1, pixels);
        var document = new MiniPrint.Core.Models.PrintDocument
        {
            Bitmap = bitmap,
            Kind = MiniPrint.Core.Models.PrintJobKind.Image,
            Title = "sim",
        };

        foreach (var frame in CatPrinterSequence.EnumerateJobFrames(document, new CatPrinterJobOptions()))
        {
            await simulator.WriteAsync(frame);
        }

        var decoded = simulator.DecodePrintedBitmap(384);
        Assert.NotNull(decoded);
        Assert.Equal(1, decoded![0, 0]);
        Assert.Equal(0, decoded[1, 0]);
    }
}
