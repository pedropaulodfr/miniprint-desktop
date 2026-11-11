namespace MiniPrint.Core.Models;

/// <summary>
/// Um dispositivo Bluetooth Low Energy encontrado durante a descoberta.
/// </summary>
public sealed record PrinterDevice
{
    /// <summary>Nome anunciado pelo dispositivo (pode vir vazio).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Endereço MAC BLE como inteiro com sinal (formato do Windows).</summary>
    public ulong Address { get; init; }

    /// <summary>Indicação de força do sinal em dBm (0 quando desconhecido).</summary>
    public short Rssi { get; init; }

    /// <summary>Um endereço válido foi lido do anúncio.</summary>
    public bool HasAddress => Address != 0;

    /// <summary>
    /// Sinal em dBm como texto. Zero significa "sem medição" (por exemplo, a
    /// impressora já conectada, que não anuncia e por isso não tem RSSI).
    /// </summary>
    public string RssiText => Rssi == 0 ? "—" : $"{Rssi} dBm";

    /// <summary>Endereço MAC formatado como AA:BB:CC:DD:EE:FF.</summary>
    public string AddressText => FormatAddress(Address);

    /// <summary>Perfil sugerido pelo nome anunciado (por prefixo de modelo).</summary>
    public string? ModelHint { get; init; }

    /// <summary>Último instante em que o dispositivo foi visto.</summary>
    public DateTimeOffset LastSeen { get; init; } = DateTimeOffset.Now;

    /// <summary>Nome amigável para exibição.</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name) ? $"(sem nome) {AddressText}" : Name;

    /// <summary>Formata um endereço BLE de 48 bits no padrão AA:BB:CC:DD:EE:FF.</summary>
    public static string FormatAddress(ulong address)
    {
        Span<byte> bytes = stackalloc byte[6];
        for (var i = 5; i >= 0; i--)
        {
            bytes[i] = (byte)(address & 0xFF);
            address >>= 8;
        }

        return string.Join(':', bytes.ToArray().Select(b => b.ToString("X2")));
    }

    /// <summary>
    /// Converte "AA:BB:CC:DD:EE:FF", "AABBCCDDEEFF" ou "AA-BB-..." em
    /// <see cref="ulong"/>. Aceita também valores já numéricos em hexadecimal.
    /// </summary>
    public static bool TryParseAddress(string? text, out ulong address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var cleaned = new string(text.Where(Uri.IsHexDigit).ToArray());
        if (cleaned.Length != 12)
        {
            return false;
        }

        return ulong.TryParse(cleaned, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out address);
    }
}
