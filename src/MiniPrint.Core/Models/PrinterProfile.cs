namespace MiniPrint.Core.Models;

/// <summary>
/// Preset de uma família de impressoras compatíveis com o protocolo
/// "Cat Printer" (GB0x / GT01 / SC03h / MX0x / YT01 / PD01 / MXTP).
///
/// Os valores abaixo vêm de implementações abertas que já suportam esses
/// equipamentos; ver docs/PROTOCOLO.md para as referências e para o que é
/// hipótese vs. o que foi confirmado no código de referência.
/// </summary>
public sealed record PrinterProfile
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Largura útil em pontos (pixels de 1 bit) por linha.</summary>
    public int PaperWidth { get; init; } = 384;

    /// <summary>
    /// Modelos "novos" aceitam o comando de início com prefixo 0x12 e
    /// (em tese) dados comprimidos. Apenas GB03 é marcado assim na referência
    /// Cat-Printer; a SC03h usa o comando clássico.
    /// </summary>
    public bool IsNewKind { get; init; }

    /// <summary>
    /// Alguns modelos (MX05..MX10) não tratam bem o comando de avanço direto de
    /// papel; nesses casos a referência envia linhas em branco.
    /// </summary>
    public bool ProblemFeeding { get; init; }

    /// <summary>Energia térmica padrão sugerida para imagens (0x0000-0xFFFF).</summary>
    public ushort ImageEnergy { get; init; } = 0x4000;

    /// <summary>Energia térmica padrão sugerida para texto (0x0000-0xFFFF).</summary>
    public ushort TextEnergy { get; init; } = 0x6000;

    /// <summary>Valor de "speed" do comando 0xBD (menor = mais rápido).</summary>
    public byte Speed { get; init; } = 32;

    /// <summary>Prefixo do nome anunciado por Bluetooth usado na identificação.</summary>
    public string NamePrefix { get; init; } = string.Empty;

    public static PrinterProfile Sc03h { get; } = new()
    {
        Key = "SC03h",
        DisplayName = "SC03h (CAT Printer, 384 pontos)",
        PaperWidth = 384,
        NamePrefix = "SC03h",
    };

    public static PrinterProfile GenericCat { get; } = new()
    {
        Key = "_ZZ00",
        DisplayName = "CAT Printer genérica (384 pontos)",
        PaperWidth = 384,
        NamePrefix = string.Empty,
    };

    /// <summary>Perfis conhecidos, do mais específico para o mais genérico.</summary>
    public static IReadOnlyList<PrinterProfile> All { get; } = new[]
    {
        Sc03h,
        new PrinterProfile { Key = "GB01", DisplayName = "GB01", NamePrefix = "GB01" },
        new PrinterProfile { Key = "GB02", DisplayName = "GB02", NamePrefix = "GB02" },
        new PrinterProfile { Key = "GB03", DisplayName = "GB03 (novo)", NamePrefix = "GB03", IsNewKind = true },
        new PrinterProfile { Key = "GT01", DisplayName = "GT01", NamePrefix = "GT01" },
        new PrinterProfile { Key = "MX05", DisplayName = "MX05", NamePrefix = "MX05", ProblemFeeding = true },
        new PrinterProfile { Key = "MX06", DisplayName = "MX06", NamePrefix = "MX06", ProblemFeeding = true },
        new PrinterProfile { Key = "MX08", DisplayName = "MX08", NamePrefix = "MX08", ProblemFeeding = true },
        new PrinterProfile { Key = "MX09", DisplayName = "MX09", NamePrefix = "MX09", ProblemFeeding = true },
        new PrinterProfile { Key = "MX10", DisplayName = "MX10", NamePrefix = "MX10", ProblemFeeding = true },
        new PrinterProfile { Key = "MX11", DisplayName = "MX11", NamePrefix = "MX11" },
        new PrinterProfile { Key = "MXTP", DisplayName = "MXTP", NamePrefix = "MXTP" },
        new PrinterProfile { Key = "PD01", DisplayName = "PD01", NamePrefix = "PD01" },
        new PrinterProfile { Key = "YT01", DisplayName = "YT01", NamePrefix = "YT01" },
        GenericCat,
    };

    /// <summary>
    /// Resolve o perfil pelo nome anunciado usando o prefixo mais longo que casa.
    /// A SC03h é reconhecida por "SC03h" e aceita sufixos como "-5718".
    /// </summary>
    public static PrinterProfile Resolve(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return GenericCat;
        }

        PrinterProfile? best = null;
        foreach (var profile in All)
        {
            if (profile.NamePrefix.Length == 0)
            {
                continue;
            }

            if (deviceName.StartsWith(profile.NamePrefix, StringComparison.OrdinalIgnoreCase) &&
                (best is null || profile.NamePrefix.Length > best.NamePrefix.Length))
            {
                best = profile;
            }
        }

        return best ?? GenericCat;
    }
}
