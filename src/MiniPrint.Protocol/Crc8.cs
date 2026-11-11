namespace MiniPrint.Protocol;

/// <summary>
/// CRC-8/ATM (polinômio 0x07, valor inicial 0, sem reflexão), calculado
/// apenas sobre o campo de dados do frame.
///
/// Confirmado em duas implementações independentes: Cat-Printer
/// (<c>printer_lib/commander.py</c>) e bitbank2/Thermal_Printer
/// (<c>cChecksumTable</c>). Âncora de teste: CRC8(0x10) == 0x70, valor que
/// aparece no frame de pausa de fluxo do equipamento.
/// </summary>
public static class Crc8
{
    private static readonly byte[] Table = BuildTable();

    private static byte[] BuildTable()
    {
        var table = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var crc = (byte)i;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x80) != 0
                    ? (byte)((crc << 1) ^ 0x07)
                    : (byte)(crc << 1);
            }

            table[i] = crc;
        }

        return table;
    }

    public static byte Compute(ReadOnlySpan<byte> payload)
    {
        var crc = 0;
        foreach (var b in payload)
        {
            crc = Table[(crc ^ b) & 0xFF];
        }

        return (byte)crc;
    }

    public static byte Compute(byte value) => Table[value];
}

/// <summary>Inverte a ordem dos bits de cada byte (bit 0 &lt;-&gt; bit 7).</summary>
public static class BitReverse
{
    private static readonly byte[] Table = BuildTable();

    private static byte[] BuildTable()
    {
        var table = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var value = (byte)i;
            var result = 0;
            for (var bit = 0; bit < 8; bit++)
            {
                if ((value & (1 << bit)) != 0)
                {
                    result |= 1 << (7 - bit);
                }
            }

            table[i] = (byte)result;
        }

        return table;
    }

    public static byte Reverse(byte value) => Table[value];
}
