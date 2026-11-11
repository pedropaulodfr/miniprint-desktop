using System.Drawing;
using System.Drawing.Imaging;
using MiniPrint.Core.Configuration;
using MiniPrint.Core.Models;
using MiniPrint.Imaging;
using Xunit;

namespace MiniPrint.Tests;

public class ImagingTests
{
    private static string CreateTestImage(int width, int height, Action<Bitmap>? paint = null)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
        }

        paint?.Invoke(bitmap);

        var path = Path.Combine(Path.GetTempPath(), $"miniprint-test-{Guid.NewGuid():N}.png");
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    [Fact]
    public void ProcessFile_ScalesTo384Points()
    {
        var path = CreateTestImage(1000, 400);
        try
        {
            var bitmap = ImageProcessor.ProcessFile(path, new ImagePrintOptions());
            Assert.Equal(384, bitmap.Width);
            Assert.True(bitmap.Height > 0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ProcessFile_PreservesAspectRatio()
    {
        var path = CreateTestImage(768, 384);
        try
        {
            var bitmap = ImageProcessor.ProcessFile(path, new ImagePrintOptions());
            // 768x384 -> 384x192
            Assert.Equal(192, bitmap.Height);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ProcessFile_UsesFullBytesPerLine()
    {
        var path = CreateTestImage(300, 100);
        try
        {
            var bitmap = ImageProcessor.ProcessFile(path, new ImagePrintOptions());
            Assert.Equal(48, bitmap.Stride);
            Assert.Equal(48 * bitmap.Height, bitmap.Bits.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ProcessFile_RotationSwapsDimensions()
    {
        var path = CreateTestImage(400, 100);
        try
        {
            var normal = ImageProcessor.ProcessFile(path, new ImagePrintOptions());
            var rotated = ImageProcessor.ProcessFile(path, new ImagePrintOptions { Rotation = 90 });

            Assert.True(rotated.Height > normal.Height);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ProcessFile_RejectsUnsupportedExtension()
    {
        var path = Path.Combine(Path.GetTempPath(), $"miniprint-{Guid.NewGuid():N}.gif");
        File.WriteAllBytes(path, new byte[] { 0x47, 0x49, 0x46 });
        try
        {
            Assert.Throws<NotSupportedException>(() =>
                ImageProcessor.ProcessFile(path, new ImagePrintOptions()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ProcessFile_BlackImageProducesInk()
    {
        var path = CreateTestImage(384, 48, bmp =>
        {
            using var graphics = Graphics.FromImage(bmp);
            graphics.Clear(Color.Black);
        });

        try
        {
            var bitmap = ImageProcessor.ProcessFile(path, new ImagePrintOptions
            {
                Dithering = DitheringMode.Limiar,
            });

            var ink = 0;
            for (var x = 0; x < bitmap.Width; x++)
            {
                ink += bitmap[x, 0];
            }

            Assert.True(ink > bitmap.Width / 2, $"Esperava maioria preta, obteve {ink} pontos.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ProcessFile_WhiteImageProducesNoInk()
    {
        var path = CreateTestImage(384, 48);
        try
        {
            var bitmap = ImageProcessor.ProcessFile(path, new ImagePrintOptions());
            Assert.All(bitmap.Bits, b => Assert.Equal(0, b));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Invert_SwapsInkAndPaper()
    {
        var path = CreateTestImage(384, 48);
        try
        {
            var options = new ImagePrintOptions { Invert = true };
            var bitmap = ImageProcessor.ProcessFile(path, options);
            Assert.All(bitmap.Bits, b => Assert.Equal(0xFF, b));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Margins_LeaveLeftAndRightBandsBlank()
    {
        var path = CreateTestImage(384, 48, bmp =>
        {
            using var graphics = Graphics.FromImage(bmp);
            graphics.Clear(Color.Black);
        });

        try
        {
            var bitmap = ImageProcessor.ProcessFile(path, new ImagePrintOptions
            {
                MarginLeft = 16,
                MarginRight = 16,
                Dithering = DitheringMode.Limiar,
            });

            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < 16; x++)
                {
                    Assert.Equal(0, bitmap[x, y]);
                    Assert.Equal(0, bitmap[bitmap.Width - 1 - x, y]);
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Dithering_GradientProducesMixedOutput()
    {
        var width = 64;
        var height = 32;
        var gray = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                gray[(y * width) + x] = (byte)(x * 255 / (width - 1));
            }
        }

        foreach (var mode in new[]
                 {
                     DitheringMode.FloydSteinberg, DitheringMode.Atkinson,
                     DitheringMode.Bayer4, DitheringMode.Bayer8, DitheringMode.Limiar,
                 })
        {
            var result = Dithering.Apply(gray, width, height, mode, 128);
            Assert.Equal(width * height, result.Length);

            var ink = result.Count(v => v == 1);
            Assert.InRange(ink, 1, (width * height) - 1);

            // A saída deve ser determinística.
            var again = Dithering.Apply(gray, width, height, mode, 128);
            Assert.Equal(result, again);
        }
    }

    [Fact]
    public void FloydSteinberg_UniformMidGray_ProducesBalancedCoverage()
    {
        const int width = 32;
        const int height = 32;
        var gray = Enumerable.Repeat((byte)128, width * height).ToArray();

        var result = Dithering.FloydSteinberg(gray, width, height, 128);
        var ink = result.Count(v => v == 1);
        var coverage = (double)ink / (width * height);

        Assert.InRange(coverage, 0.3, 0.7);
    }

    [Fact]
    public void TextRenderer_ProducesRequestedWidth()
    {
        var bitmap = TextRenderer.Render("Olá MiniPrint", new TextSettings(), 384);

        Assert.Equal(384, bitmap.Width);
        Assert.True(bitmap.Height > 10);
    }

    [Fact]
    public void TextRenderer_RendersInk()
    {
        var bitmap = TextRenderer.Render("TESTE", new TextSettings { FontSize = 20 }, 384);
        var ink = bitmap.Bits.Sum(b => System.Numerics.BitOperations.PopCount(b));

        Assert.True(ink > 50, $"Texto renderizado sem tinta suficiente ({ink} pontos).");
    }

    [Fact]
    public void TextRenderer_MoreLinesProduceMoreHeight()
    {
        var single = TextRenderer.Render("uma linha", new TextSettings(), 384);
        var many = TextRenderer.Render("uma\n duas\n três\n quatro", new TextSettings(), 384);

        Assert.True(many.Height > single.Height);
    }

    [Fact]
    public void TextRenderer_CenterAlignmentDiffersFromLeft()
    {
        var left = TextRenderer.Render("X", new TextSettings { Alignment = TextAlignment.Esquerda }, 384);
        var center = TextRenderer.Render("X", new TextSettings { Alignment = TextAlignment.Centro }, 384);

        Assert.NotEqual(FirstInkColumn(left), FirstInkColumn(center));
    }

    [Fact]
    public void TextRenderer_HandlesEmptyText()
    {
        var bitmap = TextRenderer.Render(string.Empty, new TextSettings(), 384);
        Assert.True(bitmap.Height >= 1);
    }

    [Fact]
    public void TestPattern_IsWithinWidthAndHasInk()
    {
        var bitmap = TestPatterns.DiagnosticPage(384, DitheringMode.FloydSteinberg);

        Assert.Equal(384, bitmap.Width);
        Assert.Equal(48, bitmap.Stride);
        Assert.Contains(bitmap.Bits, b => b != 0);
    }

    [Fact]
    public void ToPreview_ProducesScaledBitmap()
    {
        var bitmap = new MonochromeBitmap(384, 10, new byte[48 * 10]);
        using var preview = ImageProcessor.ToPreview(bitmap, 2);

        Assert.Equal(768, preview.Width);
        Assert.Equal(20, preview.Height);
    }

    private static int FirstInkColumn(MonochromeBitmap bitmap)
    {
        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                if (bitmap[x, y] != 0)
                {
                    return x;
                }
            }
        }

        return -1;
    }
}
