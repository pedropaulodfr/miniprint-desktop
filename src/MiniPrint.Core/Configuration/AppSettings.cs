namespace MiniPrint.Core.Configuration;

public enum DitheringMode
{
    /// <summary>Binarização por limiar global (sem difusão de erro).</summary>
    Limiar,

    FloydSteinberg,

    Atkinson,

    /// <summary>Matriz ordenada Bayer 4x4 (padrão de meio-tom).</summary>
    Bayer4,

    /// <summary>Matriz ordenada Bayer 8x8 (padrão de meio-tom mais fino).</summary>
    Bayer8,
}

public enum TextAlignment
{
    Esquerda,
    Centro,
    Direita,
}

/// <summary>
/// Configuração da impressora. Os valores iniciais correspondem à SC03h-5718
/// informada pelo usuário: 384 pontos por linha e 4 linhas de alimentação final.
/// </summary>
public sealed class PrinterSettings
{
    public string DeviceName { get; set; } = "SC03h-5718";

    public string DeviceMac { get; set; } = "18:57:2A:44:E0:BC";

    /// <summary>Largura útil em pontos. Confirmado em 384 para a SC03h.</summary>
    public int PaperWidth { get; set; } = 384;

    /// <summary>Linhas (pontos) de avanço de papel ao final do trabalho.</summary>
    public int FeedLines { get; set; } = 4;

    /// <summary>
    /// Pontos extras de avanço para que a última linha impressa passe da
    /// guilhotina/lacre antes do corte. Sem isso o texto é cortado.
    /// 128 pontos ≈ 16 mm (mesmo valor usado pela referência Cat-Printer).
    /// </summary>
    public int SafetyFeedPoints { get; set; } = 128;

    /// <summary>Valor enviado ao comando 0xBD. Menor = mais rápido e mais claro.</summary>
    public byte Speed { get; set; } = 32;

    /// <summary>Energia térmica (comando 0xAF) para imagens. Faixa 0x0000-0xFFFF.</summary>
    public ushort ImageEnergy { get; set; } = 0x4000;

    /// <summary>Energia térmica (comando 0xAF) para texto. Faixa 0x0000-0xFFFF.</summary>
    public ushort TextEnergy { get; set; } = 0x6000;

    /// <summary>
    /// Byte do comando 0xBE. A referência Cat-Printer envia 1 sempre; a
    /// referência bitbank2 envia 0 para imagem. Exponível porque é ambíguo.
    /// </summary>
    public byte DrawingMode { get; set; } = 1;

    /// <summary>Tamanho do bloco enviado por escrita BLE (bytes).</summary>
    public int ChunkSize { get; set; } = 200;

    /// <summary>Intervalo entre escritas BLE. Evita perder dados sem confirmação.</summary>
    public int InterChunkDelayMs { get; set; } = 20;

    /// <summary>Tempo máximo de espera por uma resposta/liberação de fluxo.</summary>
    public int CommandTimeoutMs { get; set; } = 15000;

    /// <summary>Reconectar automaticamente após queda inesperada.</summary>
    public bool AutoReconnect { get; set; } = true;

    public bool SendStartLattice { get; set; } = true;

    public bool SendEndLattice { get; set; } = true;

    /// <summary>Usa confirmação (write with response) em vez de write without response.</summary>
    public bool WriteWithResponse { get; set; }

    /// <summary>Quando verdadeiro, nenhum dado vai para o hardware.</summary>
    public bool UseSimulator { get; set; }

    public PrinterSettings Clone() => (PrinterSettings)MemberwiseClone();
}

public sealed class ImageSettings
{
    public double Brightness { get; set; } = 1.0;

    public double Contrast { get; set; } = 1.0;

    public int Threshold { get; set; } = 128;

    public DitheringMode Dithering { get; set; } = DitheringMode.FloydSteinberg;

    public bool Grayscale { get; set; } = true;

    public bool Invert { get; set; }

    /// <summary>Rotação aplicada antes do redimensionamento (0, 90, 180, 270).</summary>
    public int Rotation { get; set; }

    /// <summary>Margem lateral em pontos, aplicada à esquerda.</summary>
    public int MarginLeft { get; set; }

    public int MarginRight { get; set; }

    /// <summary>Se verdadeiro, estica a imagem até a largura útil (não preserva proporção).</summary>
    public bool StretchToWidth { get; set; }

    /// <summary>Usa uma energia própria para o próximo trabalho, em vez da configurada.</summary>
    public bool OverrideEnergy { get; set; }

    /// <summary>Energia personalizada (0x0000-0xFFFF) quando <see cref="OverrideEnergy"/> está ativo.</summary>
    public ushort CustomEnergy { get; set; } = 0x4000;
}

public sealed class TextSettings
{
    public string FontFamily { get; set; } = "Segoe UI";

    public double FontSize { get; set; } = 14;

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public TextAlignment Alignment { get; set; } = TextAlignment.Esquerda;

    public int MarginLeft { get; set; } = 10;

    public int MarginRight { get; set; } = 10;

    public int MarginTop { get; set; } = 6;

    public int MarginBottom { get; set; } = 6;

    /// <summary>Multiplicador do espaçamento entre linhas.</summary>
    public double LineSpacing { get; set; } = 1.15;

    public bool WordWrap { get; set; } = true;
}

/// <summary>Configuração persistida do aplicativo.</summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    public PrinterSettings Printer { get; set; } = new();

    public ImageSettings Image { get; set; } = new();

    public TextSettings Text { get; set; } = new();

    /// <summary>Preferências do editor visual de modelos (painéis, guias, encaixe).</summary>
    public EditorSettings Editor { get; set; } = new();

    public bool DarkTheme { get; set; }

    /// <summary>
    /// Guarda os valores preenchidos de um modelo no histórico de impressões.
    /// Desligado por padrão para não registrar dados pessoais sem necessidade.
    /// </summary>
    public bool StoreTemplateDataInHistory { get; set; }

    /// <summary>Último texto digitado, restaurado ao reabrir.</summary>
    public string LastText { get; set; } = string.Empty;
}
