using System.Text.Json.Serialization;
using MiniPrint.Core.Configuration;

namespace MiniPrint.Core.Templates;

/// <summary>Tipo de elemento do editor visual.</summary>
public enum LayoutElementKind
{
    Texto,
    Campo,
    Imagem,
    Linha,
    Retangulo,
}

/// <summary>O que fazer quando o texto não cabe na caixa do elemento.</summary>
public enum OverflowMode
{
    /// <summary>Quebra em várias linhas e corta o que passar da altura.</summary>
    QuebrarLinha,

    /// <summary>Reduz a fonte até caber.</summary>
    ReduzirFonte,

    /// <summary>Aumenta a altura do elemento.</summary>
    Expandir,

    /// <summary>Quebra em linhas e sinaliza o excesso no editor.</summary>
    Avisar,
}

/// <summary>
/// Elemento posicionado no documento. As coordenadas são em pontos de impressão
/// (1 ponto = 0,125 mm), a mesma unidade usada pelo raster de 384 pontos.
/// </summary>
public sealed class LayoutElement : TemplateObservable
{
    private Guid _id = Guid.NewGuid();
    private LayoutElementKind _kind = LayoutElementKind.Texto;
    private string _name = "Elemento";
    private int _x;
    private int _y;
    private int _width = 200;
    private int _height = 24;
    private bool _visible = true;
    private bool _locked;
    private Guid? _groupId;
    private string _text = string.Empty;
    private string? _fieldKey;
    private string _fontFamily = "Segoe UI";
    private double _fontSize = 11;
    private bool _bold;
    private bool _italic;
    private TextAlignment _alignment = TextAlignment.Esquerda;
    private double _lineSpacing = 1.2;
    private bool _autoHeight;
    private OverflowMode _overflow = OverflowMode.QuebrarLinha;
    private string? _imagePath;
    private string? _imageData;
    private double _brightness = 1.0;
    private double _contrast = 1.0;
    private DitheringMode _dithering = DitheringMode.FloydSteinberg;
    private bool _keepAspect = true;
    private int _rotation;
    private int _thickness = 2;
    private bool _filled;
    private double _cropLeft;
    private double _cropRight;
    private double _cropTop;
    private double _cropBottom;

    public Guid Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public LayoutElementKind Kind
    {
        get => _kind;
        set
        {
            if (SetProperty(ref _kind, value))
            {
                OnPropertyChanged(nameof(KindText));
                OnPropertyChanged(nameof(IsText));
                OnPropertyChanged(nameof(IsField));
                OnPropertyChanged(nameof(IsImage));
                OnPropertyChanged(nameof(IsLine));
                OnPropertyChanged(nameof(IsRectangle));
                OnPropertyChanged(nameof(ShowsText));
            }
        }
    }

    /// <summary>Nome mostrado no painel de camadas.</summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public int X
    {
        get => _x;
        set => SetProperty(ref _x, Math.Max(0, value));
    }

    public int Y
    {
        get => _y;
        set => SetProperty(ref _y, Math.Max(0, value));
    }

    public int Width
    {
        get => _width;
        set => SetProperty(ref _width, Math.Clamp(value, 4, 1024));
    }

    public int Height
    {
        get => _height;
        set => SetProperty(ref _height, Math.Clamp(value, 2, 4096));
    }

    public bool Visible
    {
        get => _visible;
        set => SetProperty(ref _visible, value);
    }

    /// <summary>Elemento travado não pode ser arrastado nem redimensionado.</summary>
    public bool Locked
    {
        get => _locked;
        set => SetProperty(ref _locked, value);
    }

    /// <summary>Identificador do grupo, quando o elemento faz parte de um.</summary>
    public Guid? GroupId
    {
        get => _groupId;
        set
        {
            if (SetProperty(ref _groupId, value))
            {
                OnPropertyChanged(nameof(IsGrouped));
            }
        }
    }

    // ---------- texto ----------

    /// <summary>Texto fixo do elemento. Em elementos de campo, é o rótulo exibido.</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value))
            {
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    /// <summary>Chave do campo dinâmico associado (elementos do tipo Campo).</summary>
    public string? FieldKey
    {
        get => _fieldKey;
        set
        {
            if (SetProperty(ref _fieldKey, value))
            {
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    public string FontFamily
    {
        get => _fontFamily;
        set => SetProperty(ref _fontFamily, string.IsNullOrWhiteSpace(value) ? "Segoe UI" : value);
    }

    public double FontSize
    {
        get => _fontSize;
        set => SetProperty(ref _fontSize, Math.Clamp(value, 5, 48));
    }

    public bool Bold
    {
        get => _bold;
        set => SetProperty(ref _bold, value);
    }

    public bool Italic
    {
        get => _italic;
        set => SetProperty(ref _italic, value);
    }

    public TextAlignment Alignment
    {
        get => _alignment;
        set => SetProperty(ref _alignment, value);
    }

    public double LineSpacing
    {
        get => _lineSpacing;
        set => SetProperty(ref _lineSpacing, Math.Clamp(value, 0.8, 2.5));
    }

    /// <summary>Altura acompanha o conteúdo (recalculada pelo editor).</summary>
    public bool AutoHeight
    {
        get => _autoHeight;
        set => SetProperty(ref _autoHeight, value);
    }

    public OverflowMode Overflow
    {
        get => _overflow;
        set => SetProperty(ref _overflow, value);
    }

    // ---------- imagem ----------

    public string? ImagePath
    {
        get => _imagePath;
        set => SetProperty(ref _imagePath, value);
    }

    /// <summary>Imagem embutida em base64 (PNG), para o modelo ser portátil.</summary>
    public string? ImageData
    {
        get => _imageData;
        set
        {
            if (SetProperty(ref _imageData, value))
            {
                OnPropertyChanged(nameof(HasImage));
            }
        }
    }

    public double Brightness
    {
        get => _brightness;
        set => SetProperty(ref _brightness, Math.Clamp(Math.Round(value, 2), 0.4, 1.6));
    }

    public double Contrast
    {
        get => _contrast;
        set => SetProperty(ref _contrast, Math.Clamp(Math.Round(value, 2), 0.4, 2.0));
    }

    public DitheringMode Dithering
    {
        get => _dithering;
        set => SetProperty(ref _dithering, value);
    }

    public bool KeepAspect
    {
        get => _keepAspect;
        set => SetProperty(ref _keepAspect, value);
    }

    public int Rotation
    {
        get => _rotation;
        set => SetProperty(ref _rotation, ((value % 360) + 360) % 360);
    }

    // ---------- recorte (frações de 0 a 0,45) ----------

    public double CropLeft
    {
        get => _cropLeft;
        set => SetProperty(ref _cropLeft, Math.Clamp(Math.Round(value, 2), 0, 0.45));
    }

    public double CropRight
    {
        get => _cropRight;
        set => SetProperty(ref _cropRight, Math.Clamp(Math.Round(value, 2), 0, 0.45));
    }

    public double CropTop
    {
        get => _cropTop;
        set => SetProperty(ref _cropTop, Math.Clamp(Math.Round(value, 2), 0, 0.45));
    }

    public double CropBottom
    {
        get => _cropBottom;
        set => SetProperty(ref _cropBottom, Math.Clamp(Math.Round(value, 2), 0, 0.45));
    }

    // ---------- linhas e retângulos ----------

    public int Thickness
    {
        get => _thickness;
        set => SetProperty(ref _thickness, Math.Clamp(value, 1, 32));
    }

    /// <summary>Retângulo preenchido ou apenas contorno.</summary>
    public bool Filled
    {
        get => _filled;
        set => SetProperty(ref _filled, value);
    }

    // ---------- derivados ----------

    [JsonIgnore]
    public string KindText => Kind switch
    {
        LayoutElementKind.Texto => "Texto",
        LayoutElementKind.Campo => "Campo",
        LayoutElementKind.Imagem => "Imagem",
        LayoutElementKind.Linha => "Linha",
        LayoutElementKind.Retangulo => "Retângulo",
        _ => Kind.ToString(),
    };

    /// <summary>Ícone (fonte Segoe MDL2 Assets) usado na lista de camadas.</summary>
    [JsonIgnore]
    public string KindGlyph => Kind switch
    {
        LayoutElementKind.Texto => "\uE8D2",
        LayoutElementKind.Campo => "\uE8A5",
        LayoutElementKind.Imagem => "\uEB9F",
        LayoutElementKind.Linha => "\uE738",
        LayoutElementKind.Retangulo => "\uE739",
        _ => "\uE7C3",
    };

    [JsonIgnore]
    public bool IsText => Kind == LayoutElementKind.Texto;

    [JsonIgnore]
    public bool IsField => Kind == LayoutElementKind.Campo;

    [JsonIgnore]
    public bool IsImage => Kind == LayoutElementKind.Imagem;

    [JsonIgnore]
    public bool IsLine => Kind == LayoutElementKind.Linha;

    [JsonIgnore]
    public bool IsRectangle => Kind == LayoutElementKind.Retangulo;

    [JsonIgnore]
    public bool ShowsText => IsText || IsField;

    [JsonIgnore]
    public bool IsGrouped => GroupId is not null;

    /// <summary>Seleção no editor (não é gravada no arquivo).</summary>
    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private bool _isSelected;

    [JsonIgnore]
    public bool HasImage => !string.IsNullOrEmpty(ImageData);

    /// <summary>Texto mostrado no canvas: em campos, o rótulo entre «».</summary>
    [JsonIgnore]
    public string DisplayText => IsField
        ? $"«{Text}»"
        : Text;

    [JsonIgnore]
    public int Right => X + Width;

    [JsonIgnore]
    public int Bottom => Y + Height;

    public LayoutElement Clone() => new()
    {
        Id = Guid.NewGuid(),
        Kind = Kind,
        Name = Name,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        Visible = Visible,
        Locked = Locked,
        GroupId = GroupId,
        Text = Text,
        FieldKey = FieldKey,
        FontFamily = FontFamily,
        FontSize = FontSize,
        Bold = Bold,
        Italic = Italic,
        Alignment = Alignment,
        LineSpacing = LineSpacing,
        AutoHeight = AutoHeight,
        Overflow = Overflow,
        ImagePath = ImagePath,
        ImageData = ImageData,
        Brightness = Brightness,
        Contrast = Contrast,
        Dithering = Dithering,
        KeepAspect = KeepAspect,
        Rotation = Rotation,
        Thickness = Thickness,
        Filled = Filled,
        CropLeft = CropLeft,
        CropRight = CropRight,
        CropTop = CropTop,
        CropBottom = CropBottom,
    };

    /// <summary>Cópia preservando o identificador (usada pelo undo/redo).</summary>
    public LayoutElement CloneWithSameId()
    {
        var copy = Clone();
        copy.Id = Id;
        return copy;
    }
}

/// <summary>
/// Documento com posicionamento absoluto. A largura é a do papel (384 pontos) e
/// a altura cresce conforme os elementos, quando <see cref="AutoHeight"/> está ativo.
/// </summary>
public sealed class DocumentLayout
{
    public int Width { get; set; } = 384;

    /// <summary>Altura fixa em pontos. Zero usa a altura automática.</summary>
    public int Height { get; set; }

    public bool AutoHeight { get; set; } = true;

    public int MarginLeft { get; set; } = 8;

    public int MarginRight { get; set; } = 8;

    public int MarginTop { get; set; } = 8;

    public int MarginBottom { get; set; } = 12;

    /// <summary>Grade do editor (não afeta a impressão).</summary>
    public int GridSize { get; set; } = 8;

    public bool SnapToGrid { get; set; } = true;

    /// <summary>Elementos na ordem de renderização: o primeiro é desenhado antes.</summary>
    public List<LayoutElement> Elements { get; set; } = new();

    /// <summary>Largura útil entre as margens laterais.</summary>
    [JsonIgnore]
    public int ContentWidth => Math.Max(8, Width - MarginLeft - MarginRight);

    [JsonIgnore]
    public int ContentLeft => MarginLeft;

    /// <summary>Altura necessária para desenhar todos os elementos visíveis.</summary>
    [JsonIgnore]
    public int ContentBottom
    {
        get
        {
            var bottom = 0;
            foreach (var element in Elements)
            {
                if (element.Visible)
                {
                    bottom = Math.Max(bottom, element.Bottom);
                }
            }

            return bottom;
        }
    }

    /// <summary>Altura efetiva do documento.</summary>
    public int EffectiveHeight =>
        AutoHeight
            ? Math.Max(MarginTop + 8, ContentBottom + MarginBottom)
            : Math.Max(Height, MarginTop + MarginBottom + 8);

    public DocumentLayout Clone() => new()
    {
        Width = Width,
        Height = Height,
        AutoHeight = AutoHeight,
        MarginLeft = MarginLeft,
        MarginRight = MarginRight,
        MarginTop = MarginTop,
        MarginBottom = MarginBottom,
        GridSize = GridSize,
        SnapToGrid = SnapToGrid,
        Elements = Elements.Select(e => e.CloneWithSameId()).ToList(),
    };
}
