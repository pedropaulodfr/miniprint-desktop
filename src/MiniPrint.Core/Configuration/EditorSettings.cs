namespace MiniPrint.Core.Configuration;

/// <summary>
/// Preferências da interface do editor visual de modelos. Ficam no mesmo
/// arquivo de configuração do aplicativo e sobrevivem ao fechamento.
/// </summary>
public sealed class EditorSettings
{
    /// <summary>Largura do painel de elementos, em pixels de tela.</summary>
    public double LeftPanelWidth { get; set; } = 236;

    /// <summary>Largura do painel de propriedades, em pixels de tela.</summary>
    public double RightPanelWidth { get; set; } = 328;

    public bool LeftPanelCollapsed { get; set; }

    public bool RightPanelCollapsed { get; set; }

    /// <summary>Mostra o retângulo tracejado da área imprimível.</summary>
    public bool ShowMargins { get; set; } = true;

    /// <summary>Mostra as réguas no topo e à esquerda do papel.</summary>
    public bool ShowRulers { get; set; } = true;

    /// <summary>Mostra as guias de alinhamento durante arraste/redimensionamento.</summary>
    public bool ShowGuides { get; set; } = true;

    public bool ShowGrid { get; set; } = true;

    public bool SnapToGrid { get; set; } = true;

    /// <summary>Encaixa nas bordas e centros dos outros elementos.</summary>
    public bool SnapToObjects { get; set; } = true;

    /// <summary>Distância, em pontos, a partir da qual o encaixe atua.</summary>
    public int SnapTolerance { get; set; } = 4;

    public int GridSize { get; set; } = 8;

    /// <summary>Salva um rascunho do documento enquanto ele é editado.</summary>
    public bool AutoSave { get; set; } = true;

    /// <summary>Intervalo entre rascunhos automáticos, em segundos.</summary>
    public int AutoSaveSeconds { get; set; } = 20;

    /// <summary>A orientação de primeiro uso já foi mostrada.</summary>
    public bool WelcomeShown { get; set; }

    /// <summary>Modelos marcados como favoritos na paleta, por id de ferramenta.</summary>
    public List<string> FavoriteTools { get; set; } = new();

    /// <summary>Aplica limites seguros (usado quando o arquivo traz valores estranhos).</summary>
    public void Normalize()
    {
        LeftPanelWidth = Math.Clamp(LeftPanelWidth, 180, 420);
        RightPanelWidth = Math.Clamp(RightPanelWidth, 260, 520);
        GridSize = Math.Clamp(GridSize, 2, 64);
        SnapTolerance = Math.Clamp(SnapTolerance, 0, 20);
        AutoSaveSeconds = Math.Clamp(AutoSaveSeconds, 5, 300);
    }

    public EditorSettings Clone() => new()
    {
        LeftPanelWidth = LeftPanelWidth,
        RightPanelWidth = RightPanelWidth,
        LeftPanelCollapsed = LeftPanelCollapsed,
        RightPanelCollapsed = RightPanelCollapsed,
        ShowMargins = ShowMargins,
        ShowRulers = ShowRulers,
        ShowGuides = ShowGuides,
        ShowGrid = ShowGrid,
        SnapToGrid = SnapToGrid,
        SnapToObjects = SnapToObjects,
        SnapTolerance = SnapTolerance,
        GridSize = GridSize,
        AutoSave = AutoSave,
        AutoSaveSeconds = AutoSaveSeconds,
        WelcomeShown = WelcomeShown,
        FavoriteTools = new List<string>(FavoriteTools),
    };
}
