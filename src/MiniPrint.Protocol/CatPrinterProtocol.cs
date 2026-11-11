namespace MiniPrint.Protocol;

/// <summary>
/// Constantes do protocolo da família "CAT Printer", extraídas de
/// implementações abertas que suportam a SC03h (ver docs/PROTOCOLO.md).
/// </summary>
public static class CatPrinterProtocol
{
    /// <summary>Serviço GATT da impressora (16 bits: 0xAE30).</summary>
    public static readonly Guid ServiceUuid = new("0000ae30-0000-1000-8000-00805f9b34fb");

    /// <summary>Característica de escrita dos dados (0xAE01).</summary>
    public static readonly Guid WriteCharacteristicUuid = new("0000ae01-0000-1000-8000-00805f9b34fb");

    /// <summary>Característica de notificações (0xAE02).</summary>
    public static readonly Guid NotifyCharacteristicUuid = new("0000ae02-0000-1000-8000-00805f9b34fb");

    /// <summary>Prefixo/STX de todo frame.</summary>
    public const byte Prefix0 = 0x51;

    public const byte Prefix1 = 0x78;

    /// <summary>Sufixo/ETX de todo frame.</summary>
    public const byte Suffix = 0xFF;

    /// <summary>Direção PC -&gt; impressora (byte 3 do frame).</summary>
    public const byte DirectionToPrinter = 0x00;

    /// <summary>Tamanho máximo do campo de dados de um frame (campo de 1 byte).</summary>
    public const int MaxPayloadSize = 255;

    /// <summary>Pixels por linha confirmados para a SC03h.</summary>
    public const int DefaultPaperWidth = 384;

    /// <summary>Frame exato enviado pela impressora para pausar o fluxo (XOFF).</summary>
    public static ReadOnlySpan<byte> FlowPause => new byte[]
    {
        0x51, 0x78, 0xAE, 0x01, 0x01, 0x00, 0x10, 0x70, 0xFF,
    };

    /// <summary>Frame exato enviado pela impressora para retomar o fluxo (XON).</summary>
    public static ReadOnlySpan<byte> FlowResume => new byte[]
    {
        0x51, 0x78, 0xAE, 0x01, 0x01, 0x00, 0x00, 0x00, 0xFF,
    };
}

/// <summary>Códigos de comando do protocolo.</summary>
public enum CatPrinterCommand : byte
{
    /// <summary>Avança o papel (payload: 2 bytes, quantidade em pontos, LE).</summary>
    FeedPaper = 0xA1,

    /// <summary>Dados de varredura não comprimidos (payload: 1 linha em bytes).</summary>
    DrawBitmap = 0xA2,

    /// <summary>Consulta/atualiza o estado do dispositivo (payload: 0x00).</summary>
    DeviceState = 0xA3,

    /// <summary>Qualidade / DPI aproximado. Referências usam 0x32 (50).</summary>
    SetQuality = 0xA4,

    /// <summary>Controle de "lattice" (payload fixo de 11 bytes; início e fim).</summary>
    ControlLattice = 0xA6,

    /// <summary>Informações de versão do dispositivo.</summary>
    DeviceInfo = 0xA8,

    /// <summary>Atualiza o dispositivo após configurar parâmetros.</summary>
    UpdateDevice = 0xA9,

    /// <summary>Retrai o papel (payload: 2 bytes, quantidade em pontos, LE).</summary>
    RetractPaper = 0xA0,

    /// <summary>Pausa (0x10) e retomada (0x00) de fluxo, espelhados pela impressora.</summary>
    FlowControl = 0xAE,

    /// <summary>Energia térmica (payload: 2 bytes, 0x0001-0xFFFF, LE).</summary>
    SetEnergy = 0xAF,

    /// <summary>Velocidade de alimentação/térmica (payload: 1 byte; menor = mais rápido).</summary>
    SetSpeed = 0xBD,

    /// <summary>Modo de desenho (payload: 1 byte). Ambigüidade documentada.</summary>
    DrawingMode = 0xBE,
}
