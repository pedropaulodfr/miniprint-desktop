using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using MiniPrint.Core.Configuration;
using MiniPrint.Core.Models;
using MiniPrint.Protocol;

namespace MiniPrint.Imaging;

/// <summary>Como a imagem é ajustada à largura do papel.</summary>
public enum FitMode
{
    /// <summary>Escala para preencher exatamente a largura útil (mantém proporção).</summary>
    PreencherLargura,

    /// <summary>Nunca amplia: só reduz quando a imagem é mais larga que o papel.</summary>
    TamanhoOriginal,
}

/// <summary>Parâmetros de conversão de imagem para impressão térmica.</summary>
public sealed class ImagePrintOptions
{
    public int PaperWidth { get; init; } = CatPrinterProtocol.DefaultPaperWidth;

    public DitheringMode Dithering { get; init; } = DitheringMode.FloydSteinberg;

    public int Threshold { get; init; } = 128;

    public bool Invert { get; init; }

    public double Brightness { get; init; } = 1.0;

    public double Contrast { get; init; } = 1.0;

    public FitMode Fit { get; init; } = FitMode.PreencherLargura;

    /// <summary>Rotação em graus: 0, 90, 180 ou 270.</summary>
    public int Rotation { get; init; }

    /// <summary>Frações de recorte (0 a 0.45) aplicadas antes do redimensionamento.</summary>
    public double CropLeft { get; init; }

    public double CropRight { get; init; }

    public double CropTop { get; init; }

    public double CropBottom { get; init; }

    public int MarginLeft { get; init; }

    public int MarginRight { get; init; }

    public bool SkipDitheringForText { get; init; }

    public static ImagePrintOptions FromSettings(ImageSettings settings, int paperWidth) => new()
    {
        PaperWidth = paperWidth,
        Dithering = settings.Dithering,
        Threshold = settings.Threshold,
        Invert = settings.Invert,
        Brightness = settings.Brightness,
        Contrast = settings.Contrast,
        Rotation = settings.Rotation,
        MarginLeft = settings.MarginLeft,
        MarginRight = settings.MarginRight,
    };
}

public static class ImageProcessor
{
    /// <summary>Carrega PNG, JPG/JPEG ou BMP e devolve uma cópia independente do arquivo.</summary>
    public static Bitmap Load(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp"))
        {
            throw new NotSupportedException(
                $"Formato '{extension}' não suportado. Use PNG, JPG/JPEG ou BMP.");
        }

        // Abrir via stream evita manter o arquivo travado.
        using var stream = File.OpenRead(path);
        using var loaded = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        var copy = new Bitmap(loaded.Width, loaded.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(copy))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImage(loaded, 0, 0, loaded.Width, loaded.Height);
        }

        return copy;
    }

    /// <summary>Converte um arquivo de imagem em bitmap de 1 bit pronto para envio.</summary>
    public static MonochromeBitmap ProcessFile(string path, ImagePrintOptions options)
    {
        using var bitmap = Load(path);
        return Prepare(bitmap, options);
    }

    /// <summary>Aplica rotação, recorte, escala e meio-tom.</summary>
    public static MonochromeBitmap Prepare(Bitmap source, ImagePrintOptions options)
    {
        var paperWidth = options.PaperWidth;
        var marginLeft = Math.Clamp(options.MarginLeft, 0, paperWidth - 8);
        var marginRight = Math.Clamp(options.MarginRight, 0, paperWidth - 8 - marginLeft);
        var contentWidth = Math.Max(8, paperWidth - marginLeft - marginRight);

        using var rotated = Rotate(source, options.Rotation);
        using var cropped = Crop(rotated, options);

        var scale = 1.0;
        if (cropped.Width > contentWidth || options.Fit == FitMode.PreencherLargura)
        {
            scale = (double)contentWidth / cropped.Width;
        }

        var targetWidth = Math.Max(1, (int)Math.Round(cropped.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(cropped.Height * scale));

        using var scaled = Resize(cropped, targetWidth, targetHeight);
        var gray = Grayscale.FromBitmap(scaled);
        Grayscale.ApplyBrightnessContrast(gray, options.Brightness, options.Contrast);
        if (options.Invert)
        {
            Grayscale.Invert(gray);
        }

        var content = Dithering.Apply(gray, targetWidth, targetHeight, options.Dithering, options.Threshold);
        return ComposeSheet(content, targetWidth, targetHeight, paperWidth, marginLeft);
    }

    /// <summary>
    /// Converte luminância já calculada em bitmap de 1 bit de largura total,
    /// centralizando/deslocando a margem esquerda.
    /// </summary>
    public static MonochromeBitmap ComposeSheet(
        byte[] binaryContent,
        int contentWidth,
        int contentHeight,
        int paperWidth,
        int marginLeft)
    {
        if (contentWidth > paperWidth)
        {
            throw new ArgumentException("O conteúdo é mais largo que o papel.", nameof(contentWidth));
        }

        var sheet = new byte[paperWidth * contentHeight];
        for (var y = 0; y < contentHeight; y++)
        {
            var sourceRow = y * contentWidth;
            var targetRow = y * paperWidth;
            for (var x = 0; x < contentWidth; x++)
            {
                sheet[targetRow + marginLeft + x] = binaryContent[sourceRow + x];
            }
        }

        return CatPrinterRaster.PackPixels(paperWidth, contentHeight, sheet);
    }

    /// <summary>
    /// Converte um bitmap em exatamente <paramref name="width"/> x
    /// <paramref name="height"/> pontos de 1 bit. Usado pelos elementos de
    /// imagem do editor visual, onde o tamanho vem da caixa do elemento.
    /// </summary>
    public static MonochromeBitmap PrepareSized(
        Bitmap source,
        int width,
        int height,
        ImagePrintOptions options)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        using var rotated = Rotate(source, options.Rotation);
        using var cropped = Crop(rotated, options);
        using var scaled = Resize(cropped, width, height);
        return Binarize(scaled, width, height, options);
    }

    /// <summary>Luminância → ajustes → meio-tom → bitmap de 1 bit.</summary>
    private static MonochromeBitmap Binarize(Bitmap scaled, int width, int height, ImagePrintOptions options)
    {
        var gray = Grayscale.FromBitmap(scaled);
        Grayscale.ApplyBrightnessContrast(gray, options.Brightness, options.Contrast);
        if (options.Invert)
        {
            Grayscale.Invert(gray);
        }

        var content = Dithering.Apply(gray, width, height, options.Dithering, options.Threshold);
        return CatPrinterRaster.PackPixels(width, height, content);
    }

    /// <summary>Gera uma imagem de visualização (1 bit por pixel ampliado).</summary>
    public static Bitmap ToPreview(
        MonochromeBitmap bitmap,
        int scale = 1,
        Color? ink = null,
        Color? paper = null)
    {
        scale = Math.Max(1, scale);
        var inkColor = ink ?? Color.Black;
        var paperColor = paper ?? Color.White;
        var result = new Bitmap(bitmap.Width * scale, Math.Max(1, bitmap.Height * scale), PixelFormat.Format24bppRgb);

        var data = result.LockBits(
            new Rectangle(0, 0, result.Width, result.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format24bppRgb);

        try
        {
            var stride = data.Stride;
            var buffer = new byte[stride * result.Height];
            for (var y = 0; y < result.Height; y++)
            {
                var sourceY = y / scale;
                var row = y * stride;
                for (var x = 0; x < result.Width; x++)
                {
                    var on = bitmap[x / scale, sourceY] != 0;
                    var color = on ? inkColor : paperColor;
                    var offset = row + (x * 3);
                    buffer[offset] = color.B;
                    buffer[offset + 1] = color.G;
                    buffer[offset + 2] = color.R;
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
        }
        finally
        {
            result.UnlockBits(data);
        }

        return result;
    }

    private static Bitmap Rotate(Bitmap source, int rotation)
    {
        var normalized = ((rotation % 360) + 360) % 360;
        var copy = new Bitmap(source);
        var flip = normalized switch
        {
            90 => RotateFlipType.Rotate90FlipNone,
            180 => RotateFlipType.Rotate180FlipNone,
            270 => RotateFlipType.Rotate270FlipNone,
            _ => RotateFlipType.RotateNoneFlipNone,
        };

        if (flip != RotateFlipType.RotateNoneFlipNone)
        {
            copy.RotateFlip(flip);
        }

        return copy;
    }

    private static Bitmap Crop(Bitmap source, ImagePrintOptions options)
    {
        var left = Math.Clamp(options.CropLeft, 0, 0.45);
        var right = Math.Clamp(options.CropRight, 0, 0.45);
        var top = Math.Clamp(options.CropTop, 0, 0.45);
        var bottom = Math.Clamp(options.CropBottom, 0, 0.45);

        if (left + right >= 0.9 || top + bottom >= 0.9)
        {
            return new Bitmap(source);
        }

        if (left + right + top + bottom <= 1e-6)
        {
            return new Bitmap(source);
        }

        var x = (int)Math.Round(source.Width * left);
        var y = (int)Math.Round(source.Height * top);
        var width = Math.Max(1, source.Width - x - (int)Math.Round(source.Width * right));
        var height = Math.Max(1, source.Height - y - (int)Math.Round(source.Height * bottom));

        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.DrawImage(
            source,
            new Rectangle(0, 0, width, height),
            new Rectangle(x, y, width, height),
            GraphicsUnit.Pixel);
        return result;
    }

    private static Bitmap Resize(Bitmap source, int width, int height)
    {
        if (source.Width == width && source.Height == height)
        {
            return new Bitmap(source);
        }

        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        graphics.DrawImage(
            source,
            new Rectangle(0, 0, width, height),
            0,
            0,
            source.Width,
            source.Height,
            GraphicsUnit.Pixel,
            attributes);
        return result;
    }
}
