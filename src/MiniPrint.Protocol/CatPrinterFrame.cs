namespace MiniPrint.Protocol;

/// <summary>
/// Constrói e interpreta frames do protocolo CAT Printer.
///
/// Estrutura (confirmada no código da referência Cat-Printer e do
/// bitbank2/Thermal_Printer):
///
/// <code>
/// 0x51 0x78 | cmd | 0x00 | len_lo | len_hi | dados... | crc8(dados) | 0xFF
/// prefixo     cmd   direção  tamanho (LE)      payload      checksum     fim
/// </code>
///
/// O tamanho do payload é limitado a 255 bytes (o campo alto é sempre 0).
/// </summary>
public static class CatPrinterFrame
{
    /// <summary>Tamanho do cabeçalho antes do payload (6 bytes).</summary>
    public const int HeaderSize = 6;

    /// <summary>Tamanho do rodapé depois do payload (CRC + sufixo).</summary>
    public const int FooterSize = 2;

    /// <summary>Menor frame possível (payload vazio).</summary>
    public const int MinFrameSize = HeaderSize + FooterSize;

    /// <summary>Cria o frame completo de um comando.</summary>
    public static byte[] Build(byte command, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > CatPrinterProtocol.MaxPayloadSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                $"Payload de {payload.Length} bytes excede o máximo de " +
                $"{CatPrinterProtocol.MaxPayloadSize} do protocolo.");
        }

        var frame = new byte[HeaderSize + payload.Length + FooterSize];
        frame[0] = CatPrinterProtocol.Prefix0;
        frame[1] = CatPrinterProtocol.Prefix1;
        frame[2] = command;
        frame[3] = CatPrinterProtocol.DirectionToPrinter;
        frame[4] = (byte)payload.Length;
        frame[5] = 0x00;
        payload.CopyTo(frame.AsSpan(HeaderSize));
        frame[HeaderSize + payload.Length] = Crc8.Compute(payload);
        frame[HeaderSize + payload.Length + 1] = CatPrinterProtocol.Suffix;
        return frame;
    }

    public static byte[] Build(CatPrinterCommand command, params byte[] payload) =>
        Build((byte)command, payload);

    /// <summary>Constrói o frame e acrescenta um prefixo opcional (usado pelo GB03).</summary>
    public static byte[] BuildWithPrefix(byte prefix, CatPrinterCommand command, params byte[] payload)
    {
        var frame = Build((byte)command, payload);
        var result = new byte[frame.Length + 1];
        result[0] = prefix;
        frame.CopyTo(result, 1);
        return result;
    }

    /// <summary>
    /// Lê um frame de um buffer. Retorna false quando ainda faltam bytes.
    /// Lança <see cref="InvalidDataException"/> quando o CRC ou o sufixo não
    /// conferem (frame corrompido em trânsito).
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> buffer, out ParsedFrame frame, out int consumed)
    {
        frame = default;
        consumed = 0;

        if (buffer.Length < MinFrameSize)
        {
            return false;
        }

        if (buffer[0] != CatPrinterProtocol.Prefix0 || buffer[1] != CatPrinterProtocol.Prefix1)
        {
            throw new InvalidDataException(
                $"Prefixo inesperado: {buffer[0]:X2} {buffer[1]:X2} (esperado 51 78).");
        }

        var payloadLength = buffer[4] | (buffer[5] << 8);
        var total = HeaderSize + payloadLength + FooterSize;
        if (buffer.Length < total)
        {
            return false;
        }

        if (buffer[HeaderSize + payloadLength + 1] != CatPrinterProtocol.Suffix)
        {
            throw new InvalidDataException("Sufixo 0xFF ausente: frame desalinhado ou corrompido.");
        }

        var payload = buffer.Slice(HeaderSize, payloadLength).ToArray();
        var expectedCrc = buffer[HeaderSize + payloadLength];
        var actualCrc = Crc8.Compute(payload);
        if (expectedCrc != actualCrc)
        {
            throw new InvalidDataException(
                $"CRC inválido: recebido 0x{expectedCrc:X2}, calculado 0x{actualCrc:X2}.");
        }

        frame = new ParsedFrame(buffer[2], buffer[3], payload);
        consumed = total;
        return true;
    }

    public static string ToHex(ReadOnlySpan<byte> data, int maxBytes = 64)
    {
        var slice = data.Length > maxBytes ? data[..maxBytes] : data;
        var text = string.Join(' ', slice.ToArray().Select(b => b.ToString("X2")));
        return data.Length > maxBytes ? $"{text} ... ({data.Length} bytes)" : text;
    }
}

/// <summary>Frame já validado.</summary>
public readonly record struct ParsedFrame(byte Command, byte Direction, byte[] Payload)
{
    public CatPrinterCommand? KnownCommand =>
        Enum.IsDefined(typeof(CatPrinterCommand), Command) ? (CatPrinterCommand)Command : null;

    public override string ToString() =>
        $"cmd=0x{Command:X2} dir=0x{Direction:X2} len={Payload.Length} " +
        $"dados=[{CatPrinterFrame.ToHex(Payload, 16)}]";
}
