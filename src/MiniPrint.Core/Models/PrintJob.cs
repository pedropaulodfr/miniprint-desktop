namespace MiniPrint.Core.Models;

public enum PrintJobKind
{
    Text,
    Image,
    TestPattern,

    /// <summary>Documento gerado a partir de um modelo preenchido.</summary>
    Modelo,
}

public enum PrintJobStatus
{
    Pendente,
    Preparando,
    Enviando,
    Concluido,
    Falhou,
    Cancelado,
}

/// <summary>Imagem monocromática já empacotada no formato de 1 bit por pixel.</summary>
public sealed class MonochromeBitmap
{
    public MonochromeBitmap(int width, int height, byte[] bits)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        var stride = (width + 7) / 8;
        if (bits.Length != stride * height)
        {
            throw new ArgumentException(
                $"Buffer com {bits.Length} bytes não corresponde a {width}x{height} " +
                $"(esperado {stride * height}).", nameof(bits));
        }

        Width = width;
        Height = height;
        Bits = bits;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Bytes por linha (arredondado para cima em múltiplos de 8).</summary>
    public int Stride => (Width + 7) / 8;

    /// <summary>
    /// Bits em ordem MSB-first: o bit 7 do primeiro byte é o pixel mais à
    /// esquerda. 1 = ponto impresso (preto).
    /// </summary>
    public byte[] Bits { get; }

    /// <summary>Lê o pixel como 0 (branco) ou 1 (preto).</summary>
    public byte this[int x, int y]
    {
        get
        {
            var b = Bits[(y * Stride) + (x >> 3)];
            return (byte)((b >> (7 - (x & 7))) & 1);
        }
    }
}

/// <summary>Conteúdo a ser impresso, já convertido para o formato do equipamento.</summary>
public sealed class PrintDocument
{
    public required MonochromeBitmap Bitmap { get; init; }

    public required PrintJobKind Kind { get; init; }

    /// <summary>Identificação do trabalho no histórico (nome do arquivo, resumo do texto...).</summary>
    public required string Title { get; init; }
}

public sealed class PrintJobResult
{
    public required bool Success { get; init; }
    public required PrintJobStatus Status { get; init; }
    public string? ErrorMessage { get; init; }
    public int BytesSent { get; init; }
    public int PacketsSent { get; init; }
    public int LinesPrinted { get; init; }
    public TimeSpan Duration { get; init; }
    public bool WasSimulated { get; init; }
}

public sealed class PrintProgress
{
    public required double Fraction { get; init; }
    public required string Message { get; init; }
    public int LinesSent { get; init; }
    public int TotalLines { get; init; }

    public static PrintProgress Of(double fraction, string message) =>
        new() { Fraction = Math.Clamp(fraction, 0, 1), Message = message };
}
