using MiniPrint.Core.Models;
using MiniPrint.Protocol;
using Xunit;

namespace MiniPrint.Tests;

public class RasterTests
{
    [Fact]
    public void PackPixels_UsesMsbFirstPerByte()
    {
        // 8x1: apenas o pixel mais à esquerda aceso.
        var pixels = new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 };
        var bitmap = CatPrinterRaster.PackPixels(8, 1, pixels);
        Assert.Equal(0x80, bitmap.Bits[0]);
        Assert.Equal(1, bitmap[0, 0]);
        Assert.Equal(0, bitmap[7, 0]);
    }

    [Fact]
    public void PackPixels_RejectsMismatchedBuffer()
    {
        Assert.Throws<ArgumentException>(() => CatPrinterRaster.PackPixels(8, 2, new byte[8]));
    }

    [Fact]
    public void EncodeScanlines_ReversesBitsForPrinter()
    {
        var pixels = new byte[8];
        pixels[0] = 1; // pixel mais à esquerda
        var bitmap = CatPrinterRaster.PackPixels(8, 1, pixels);

        var line = CatPrinterRaster.EncodeScanlines(bitmap, 8).Single();

        // O equipamento trata o bit 0 como o ponto mais à esquerda.
        Assert.Equal(0x01, line[0]);
    }

    [Fact]
    public void EncodeScanlines_For384Points_Produces48Bytes()
    {
        var bitmap = new MonochromeBitmap(384, 5, new byte[48 * 5]);
        var lines = CatPrinterRaster.EncodeScanlines(bitmap, 384).ToList();

        Assert.Equal(5, lines.Count);
        Assert.All(lines, line => Assert.Equal(48, line.Length));
    }

    [Fact]
    public void EncodeScanlines_EachLineFitsInsideAFrame()
    {
        var bitmap = new MonochromeBitmap(384, 3, new byte[48 * 3]);
        foreach (var line in CatPrinterRaster.EncodeScanlines(bitmap, 384))
        {
            var frame = CatPrinterFrame.Build(CatPrinterCommand.DrawBitmap, line);
            Assert.True(frame.Length <= 56);
        }
    }

    [Fact]
    public void EncodeScanlines_PadsNarrowerBitmapToFullWidth()
    {
        var bitmap = new MonochromeBitmap(8, 1, new byte[] { 0xFF });
        var line = CatPrinterRaster.EncodeScanlines(bitmap, 16).Single();

        Assert.Equal(2, line.Length);
        Assert.Equal(0xFF, line[0]);
        Assert.Equal(0x00, line[1]);
    }
}
