using MiniPrint.Core.Models;

namespace MiniPrint.Protocol;

/// <summary>
/// Empacotamento da imagem monocromática no formato aceito pelo equipamento.
///
/// Cada linha é enviada em um frame 0xA2 com <c>largura / 8</c> bytes. O byte
/// enviado é o espelho do byte gerado pelo app: a impressora trata o bit 0 como
/// o ponto mais à esquerda, enquanto o formato interno (e o PBM P4) usa o bit 7.
/// Por isso cada byte passa por <see cref="BitReverse"/>.
/// </summary>
public static class CatPrinterRaster
{
    /// <summary>Bytes por linha para a largura informada.</summary>
    public static int Stride(int widthPoints) => (widthPoints + 7) / 8;

    /// <summary>
    /// Gera as linhas prontas para envio, já espelhadas e ajustadas à largura
    /// alvo. Linhas mais estreitas que o alvo são completadas com branco à
    /// direita; mais largas são cortadas à direita.
    /// </summary>
    public static IEnumerable<byte[]> EncodeScanlines(MonochromeBitmap bitmap, int targetWidthPoints)
    {
        if (targetWidthPoints <= 0 || targetWidthPoints % 8 != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetWidthPoints),
                "A largura alvo deve ser múltipla de 8 pontos.");
        }

        var destinationStride = targetWidthPoints / 8;
        var sourceStride = bitmap.Stride;
        var copyBytes = Math.Min(destinationStride, sourceStride);

        for (var y = 0; y < bitmap.Height; y++)
        {
            var line = new byte[destinationStride];
            var sourceOffset = y * sourceStride;
            for (var i = 0; i < copyBytes; i++)
            {
                line[i] = BitReverse.Reverse(bitmap.Bits[sourceOffset + i]);
            }

            yield return line;
        }
    }

    /// <summary>
    /// Converte uma matriz de pixels (0 = branco, &gt;0 = preto) em bitmap de
    /// 1 bit por pixel no formato interno (MSB = ponto mais à esquerda).
    /// </summary>
    public static MonochromeBitmap PackPixels(int width, int height, ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length != width * height)
        {
            throw new ArgumentException("A matriz de pixels não corresponde a width*height.", nameof(pixels));
        }

        var stride = Stride(width);
        var bits = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * width;
            var outOffset = y * stride;
            for (var x = 0; x < width; x++)
            {
                if (pixels[rowOffset + x] != 0)
                {
                    bits[outOffset + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
        }

        return new MonochromeBitmap(width, height, bits);
    }

    /// <summary>Linhas em branco usadas no lugar do comando de avanço em modelos problemáticos.</summary>
    public static IEnumerable<byte[]> BlankLines(int widthPoints, int lineCount)
    {
        var blank = new byte[Stride(widthPoints)];
        for (var i = 0; i < lineCount; i++)
        {
            yield return (byte[])blank.Clone();
        }
    }
}
