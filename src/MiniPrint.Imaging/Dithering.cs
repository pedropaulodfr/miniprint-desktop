using MiniPrint.Core.Configuration;

namespace MiniPrint.Imaging;

/// <summary>
/// Algoritmos de meio-tom para impressão térmica monocromática.
/// Todos devolvem um vetor com 1 (ponto impresso) ou 0 (branco) por pixel.
/// </summary>
public static class Dithering
{
    /// <summary>Floyd-Steinberg clássico com difusão de erro serpentina desativada.</summary>
    public static byte[] FloydSteinberg(ReadOnlySpan<byte> gray, int width, int height, int threshold)
    {
        var working = ToErrorBuffer(gray);
        var output = new byte[width * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;
                var oldValue = Clamp(working[index] / 16.0);
                var newValue = oldValue < threshold ? 0 : 255;
                output[index] = newValue == 0 ? (byte)1 : (byte)0;
                var error = oldValue - newValue;

                if (x + 1 < width)
                {
                    Add(ref working[index + 1], error * 7 / 16);
                }

                if (y + 1 < height)
                {
                    var nextRow = index + width;
                    if (x > 0)
                    {
                        Add(ref working[nextRow - 1], error * 3 / 16);
                    }

                    Add(ref working[nextRow], error * 5 / 16);
                    if (x + 1 < width)
                    {
                        Add(ref working[nextRow + 1], error * 1 / 16);
                    }
                }
            }
        }

        return output;
    }

    /// <summary>Atkinson: difunde 6/8 do erro para 6 vizinhos (contraste mais alto).</summary>
    public static byte[] Atkinson(ReadOnlySpan<byte> gray, int width, int height, int threshold)
    {
        var working = ToErrorBuffer(gray);
        var output = new byte[width * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;
                var oldValue = Clamp(working[index] / 16.0);
                var newValue = oldValue < threshold ? 0 : 255;
                output[index] = newValue == 0 ? (byte)1 : (byte)0;
                var error = (oldValue - newValue) / 8.0;

                if (x + 1 < width)
                {
                    Add(ref working[index + 1], error);
                }

                if (x + 2 < width)
                {
                    Add(ref working[index + 2], error);
                }

                if (y + 1 < height)
                {
                    var row = index + width;
                    if (x > 0)
                    {
                        Add(ref working[row - 1], error);
                    }

                    Add(ref working[row], error);
                    if (x + 1 < width)
                    {
                        Add(ref working[row + 1], error);
                    }
                }

                if (y + 2 < height)
                {
                    Add(ref working[index + (width * 2)], error);
                }
            }
        }

        return output;
    }

    /// <summary>Bayer 4x4 (meio-tom ordenado).</summary>
    public static byte[] Bayer(ReadOnlySpan<byte> gray, int width, int height, int threshold, int matrixSize)
    {
        int[] matrix = matrixSize switch
        {
            4 => new[] { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 },
            8 => Bayer8x8,
            _ => throw new ArgumentOutOfRangeException(nameof(matrixSize), "Use 4 ou 8."),
        };

        var output = new byte[width * height];
        var scale = 255.0 / (matrix.Length + 1);
        // O limiar do usuário ajusta o ponto de corte médio da matriz.
        var bias = (128 - threshold) * 0.5;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;
                var matrixValue = matrix[((y % matrixSize) * matrixSize) + (x % matrixSize)];
                var level = (matrixValue + 0.5) * scale;
                output[index] = gray[index] + bias < level ? (byte)1 : (byte)0;
            }
        }

        return output;
    }

    /// <summary>Limiar global simples.</summary>
    public static byte[] Threshold(ReadOnlySpan<byte> gray, int width, int height, int threshold)
    {
        var output = new byte[width * height];
        for (var i = 0; i < gray.Length; i++)
        {
            output[i] = gray[i] < threshold ? (byte)1 : (byte)0;
        }

        return output;
    }

    /// <summary>Despacha para o algoritmo configurado.</summary>
    public static byte[] Apply(ReadOnlySpan<byte> gray, int width, int height, DitheringMode mode, int threshold)
    {
        threshold = Math.Clamp(threshold, 1, 254);
        return mode switch
        {
            DitheringMode.FloydSteinberg => FloydSteinberg(gray, width, height, threshold),
            DitheringMode.Atkinson => Atkinson(gray, width, height, threshold),
            DitheringMode.Bayer4 => Bayer(gray, width, height, threshold, 4),
            DitheringMode.Bayer8 => Bayer(gray, width, height, threshold, 8),
            _ => Threshold(gray, width, height, threshold),
        };
    }

    private static readonly int[] Bayer8x8 =
    {
        0, 32, 8, 40, 2, 34, 10, 42,
        48, 16, 56, 24, 50, 18, 58, 26,
        12, 44, 4, 36, 14, 46, 6, 38,
        60, 28, 52, 20, 62, 30, 54, 22,
        3, 35, 11, 43, 1, 33, 9, 41,
        51, 19, 59, 27, 49, 17, 57, 25,
        15, 47, 7, 39, 13, 45, 5, 37,
        63, 31, 55, 23, 61, 29, 53, 21,
    };

    /// <summary>Buffer de erro em ponto fixo (valor * 16) para evitar alocação por pixel.</summary>
    private static int[] ToErrorBuffer(ReadOnlySpan<byte> gray)
    {
        var buffer = new int[gray.Length];
        for (var i = 0; i < gray.Length; i++)
        {
            buffer[i] = gray[i] * 16;
        }

        return buffer;
    }

    private static void Add(ref int target, double delta) =>
        target += (int)Math.Round(delta * 16);

    private static double Clamp(double value) => Math.Clamp(value, 0, 255);
}
