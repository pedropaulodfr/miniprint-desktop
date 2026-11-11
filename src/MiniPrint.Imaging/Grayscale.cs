using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MiniPrint.Imaging;

/// <summary>Extração de luminância e utilitários de bitmap.</summary>
public static class Grayscale
{
    /// <summary>
    /// Converte um bitmap para um vetor de luminância 0-255 (linha a linha),
    /// compondo transparência sobre branco — papel térmico não tem tinta branca.
    /// </summary>
    public static byte[] FromBitmap(Bitmap source)
    {
        var width = source.Width;
        var height = source.Height;
        using var normalized = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(normalized))
        {
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            graphics.DrawImage(source, 0, 0, width, height);
        }

        var result = new byte[width * height];
        var data = normalized.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            var stride = data.Stride;
            var buffer = new byte[stride * height];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

            for (var y = 0; y < height; y++)
            {
                var sourceRow = y * stride;
                var targetRow = y * width;
                for (var x = 0; x < width; x++)
                {
                    var offset = sourceRow + (x * 4);
                    var b = buffer[offset];
                    var g = buffer[offset + 1];
                    var r = buffer[offset + 2];
                    var a = buffer[offset + 3];

                    double luminance = (0.299 * r) + (0.587 * g) + (0.114 * b);
                    if (a < 255)
                    {
                        var alpha = a / 255.0;
                        luminance = (luminance * alpha) + (255 * (1 - alpha));
                    }

                    result[targetRow + x] = (byte)Math.Clamp((int)Math.Round(luminance), 0, 255);
                }
            }
        }
        finally
        {
            normalized.UnlockBits(data);
        }

        return result;
    }

    /// <summary>
    /// Aplica brilho e contraste. <paramref name="brightness"/> 1.0 e
    /// <paramref name="contrast"/> 1.0 mantêm a imagem original.
    /// </summary>
    public static void ApplyBrightnessContrast(byte[] pixels, double brightness, double contrast)
    {
        if (Math.Abs(brightness - 1.0) < 1e-6 && Math.Abs(contrast - 1.0) < 1e-6)
        {
            return;
        }

        var offset = (brightness - 1.0) * 128.0;
        for (var i = 0; i < pixels.Length; i++)
        {
            var value = ((pixels[i] - 128.0) * contrast) + 128.0 + offset;
            pixels[i] = (byte)Math.Clamp((int)Math.Round(value), 0, 255);
        }
    }

    public static void Invert(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(255 - pixels[i]);
        }
    }
}
