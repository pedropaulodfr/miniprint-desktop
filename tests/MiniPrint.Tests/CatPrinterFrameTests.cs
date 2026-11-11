using MiniPrint.Protocol;
using Xunit;

namespace MiniPrint.Tests;

/// <summary>
/// Testes do formato de frame e do checksum.
/// Os valores esperados vêm de três fontes independentes: a tabela CRC-8 da
/// referência Cat-Printer, os frames constantes do bitbank2/Thermal_Printer e
/// as respostas capturadas da impressora SC03h-5718 real.
/// </summary>
public class CatPrinterFrameTests
{
    [Theory]
    [InlineData(0x00, 0x00)]
    [InlineData(0x01, 0x07)]
    [InlineData(0x10, 0x70)] // frame de XOFF real
    [InlineData(0x32, 0x9E)] // frame de qualidade observado ao vivo
    public void Crc8_MatchesReferenceVectors(byte input, byte expected)
    {
        Assert.Equal(expected, Crc8.Compute(new[] { input }));
    }

    [Fact]
    public void Crc8_LatticeStart_MatchesReferenceTableValue()
    {
        // O frame de início de lattice da referência carrega o CRC 0xA1.
        Assert.Equal(0xA1, Crc8.Compute(CatPrinterSequence.LatticeStartPayload));
    }

    [Fact]
    public void Crc8_LatticeEnd_MatchesReferenceTableValue()
    {
        Assert.Equal(0x11, Crc8.Compute(CatPrinterSequence.LatticeEndPayload));
    }

    [Fact]
    public void Build_DeviceState_MatchesKnownBytes()
    {
        var frame = CatPrinterFrame.Build(CatPrinterCommand.DeviceState, 0x00);
        Assert.Equal("51 78 A3 00 01 00 00 00 FF", CatPrinterFrame.ToHex(frame));
    }

    [Fact]
    public void FlowControlFrames_HavePrinterDirectionByte()
    {
        // Os frames de XOFF/XON usam a direção 0x01 (impressora -> PC) nos
        // bytes de referência. O checksum cobre apenas o payload.
        Assert.True(CatPrinterFrame.TryParse(CatPrinterProtocol.FlowPause, out var pause, out _));
        Assert.Equal(1, pause.Direction);
        Assert.Equal(0xAE, pause.Command);
        Assert.Equal((byte)0x10, pause.Payload[0]);

        Assert.True(CatPrinterFrame.TryParse(CatPrinterProtocol.FlowResume, out var resume, out _));
        Assert.Equal(1, resume.Direction);
        Assert.Equal((byte)0x00, resume.Payload[0]);
    }

    [Fact]
    public void Build_LatticeStart_MatchesReferenceBytes()
    {
        var frame = CatPrinterFrame.Build(CatPrinterCommand.ControlLattice, CatPrinterSequence.LatticeStartPayload);
        Assert.Equal(
            "51 78 A6 00 0B 00 AA 55 17 38 44 5F 5F 5F 44 38 2C A1 FF",
            CatPrinterFrame.ToHex(frame));
    }

    [Fact]
    public void Build_LatticeEnd_MatchesReferenceBytes()
    {
        var frame = CatPrinterFrame.Build(CatPrinterCommand.ControlLattice, CatPrinterSequence.LatticeEndPayload);
        Assert.Equal(
            "51 78 A6 00 0B 00 AA 55 17 00 00 00 00 00 00 00 17 11 FF",
            CatPrinterFrame.ToHex(frame));
    }

    [Fact]
    public void Build_Quality_MatchesFrameObservedOnPrinter()
    {
        // Capturado ao vivo em 18:57:2A:44:E0:BC.
        var frame = CatPrinterFrame.Build(CatPrinterCommand.SetQuality, 0x32);
        Assert.Equal("51 78 A4 00 01 00 32 9E FF", CatPrinterFrame.ToHex(frame));
    }

    [Fact]
    public void Build_Energy_IsLittleEndian()
    {
        var frame = CatPrinterFrame.Build(CatPrinterCommand.SetEnergy, 0x00, 0x40);
        Assert.Equal(0xAF, frame[2]);
        Assert.Equal(0x00, frame[6]);
        Assert.Equal(0x40, frame[7]);
    }

    [Fact]
    public void Build_RejectsPayloadLargerThan255()
    {
        var payload = new byte[256];
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CatPrinterFrame.Build(CatPrinterCommand.DrawBitmap, payload));
    }

    [Fact]
    public void Parse_RoundTrips()
    {
        var original = CatPrinterFrame.Build(CatPrinterCommand.SetSpeed, 0x20);
        Assert.True(CatPrinterFrame.TryParse(original, out var parsed, out var consumed));
        Assert.Equal(original.Length, consumed);
        Assert.Equal((byte)CatPrinterCommand.SetSpeed, parsed.Command);
        Assert.Equal(0x20, parsed.Payload[0]);
    }

    [Fact]
    public void Parse_IncompleteFrame_RequestsMoreData()
    {
        var original = CatPrinterFrame.Build(CatPrinterCommand.SetSpeed, 0x20);
        Assert.False(CatPrinterFrame.TryParse(original.AsSpan(0, original.Length - 1), out _, out _));
    }

    [Fact]
    public void Parse_DetectsCorruptedCrc()
    {
        var frame = CatPrinterFrame.Build(CatPrinterCommand.SetSpeed, 0x20);
        frame[6] ^= 0xFF;
        var ex = Assert.Throws<InvalidDataException>(() =>
            CatPrinterFrame.TryParse(frame, out _, out _));
        Assert.Contains("CRC", ex.Message);
    }

    [Theory]
    [InlineData(0x00, 0x00)]
    [InlineData(0x01, 0x80)]
    [InlineData(0x80, 0x01)]
    [InlineData(0xA5, 0xA5)]
    public void BitReverse_InvertsBits(byte input, byte expected)
    {
        Assert.Equal(expected, BitReverse.Reverse(input));
    }
}
