using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MiniPrint.Core.Configuration;

namespace MiniPrint.Core.Templates;

/// <summary>Tipos de campo dinâmico suportados por um modelo.</summary>
public enum TemplateFieldType
{
    TextoCurto,
    TextoLongo,
    Numero,
    Monetario,
    Data,
    Cpf,
    Cnpj,
    Telefone,
    Lista,
}

/// <summary>Tipos de bloco que compõem o documento.</summary>
public enum TemplateBlockKind
{
    /// <summary>Parágrafo de texto, possivelmente com campos {{chave}}.</summary>
    Texto,

    /// <summary>Linha divisória.</summary>
    Divisor,

    /// <summary>Espaço em branco, em pontos.</summary>
    Espaco,
}

public enum DividerStyle
{
    /// <summary>Linha de hífens.</summary>
    Traco,

    /// <summary>Linha de sinais de igual.</summary>
    Igual,

    /// <summary>Linha de sublinhados.</summary>
    Sublinhado,

    /// <summary>Regra sólida desenhada.</summary>
    LinhaSolida,
}

/// <summary>Classe base com notificação de alteração, usada pelas telas WPF.</summary>
public abstract class TemplateObservable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

/// <summary>
/// Campo dinâmico de um modelo. O <see cref="Key"/> é o nome interno usado no
/// texto do documento na forma <c>{{chave}}</c>.
/// </summary>
public sealed class TemplateField : TemplateObservable
{
    private string _key = string.Empty;
    private string _label = string.Empty;
    private TemplateFieldType _type = TemplateFieldType.TextoCurto;
    private string _placeholder = string.Empty;
    private bool _required;
    private string _defaultValue = string.Empty;
    private List<string> _options = new();

    public string Key
    {
        get => _key;
        set
        {
            if (SetProperty(ref _key, value))
            {
                OnPropertyChanged(nameof(TypeText));
            }
        }
    }

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public TemplateFieldType Type
    {
        get => _type;
        set
        {
            if (SetProperty(ref _type, value))
            {
                OnPropertyChanged(nameof(TypeText));
                OnPropertyChanged(nameof(IsList));
            }
        }
    }

    /// <summary>Texto de orientação mostrado no formulário.</summary>
    public string Placeholder
    {
        get => _placeholder;
        set => SetProperty(ref _placeholder, value);
    }

    public bool Required
    {
        get => _required;
        set => SetProperty(ref _required, value);
    }

    public string DefaultValue
    {
        get => _defaultValue;
        set => SetProperty(ref _defaultValue, value);
    }

    /// <summary>Opções válidas quando <see cref="Type"/> é <see cref="TemplateFieldType.Lista"/>.</summary>
    public List<string> Options
    {
        get => _options;
        set => SetProperty(ref _options, value ?? new List<string>());
    }

    [JsonIgnore]
    public string TypeText => Type switch
    {
        TemplateFieldType.TextoCurto => "Texto curto",
        TemplateFieldType.TextoLongo => "Texto longo",
        TemplateFieldType.Numero => "Número",
        TemplateFieldType.Monetario => "Valor monetário",
        TemplateFieldType.Data => "Data",
        TemplateFieldType.Cpf => "CPF",
        TemplateFieldType.Cnpj => "CNPJ",
        TemplateFieldType.Telefone => "Telefone",
        TemplateFieldType.Lista => "Lista de opções",
        _ => Type.ToString(),
    };

    [JsonIgnore]
    public bool IsList => Type == TemplateFieldType.Lista;

    /// <summary>Opções em texto separado por vírgulas, para edição na interface.</summary>
    [JsonIgnore]
    public string OptionsText
    {
        get => string.Join(", ", Options);
        set
        {
            Options = (value ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            OnPropertyChanged();
        }
    }

    public TemplateField Clone() => new()
    {
        Key = Key,
        Label = Label,
        Type = Type,
        Placeholder = Placeholder,
        Required = Required,
        DefaultValue = DefaultValue,
        Options = new List<string>(Options),
    };
}

/// <summary>Bloco do documento (parágrafo, divisor ou espaço).</summary>
public sealed class TemplateBlock : TemplateObservable
{
    private TemplateBlockKind _kind = TemplateBlockKind.Texto;
    private string _text = string.Empty;
    private int _fontSize;
    private bool _bold;
    private bool _italic;
    private TextAlignment? _alignment;
    private int _spacingBefore;
    private int _spacingAfter;
    private DividerStyle _dividerStyle = DividerStyle.Traco;
    private int _height = 12;

    public TemplateBlockKind Kind
    {
        get => _kind;
        set
        {
            if (SetProperty(ref _kind, value))
            {
                OnPropertyChanged(nameof(KindText));
                OnPropertyChanged(nameof(IsText));
                OnPropertyChanged(nameof(IsDivider));
                OnPropertyChanged(nameof(IsSpacer));
                OnPropertyChanged(nameof(PreviewText));
            }
        }
    }

    /// <summary>Texto do parágrafo. Pode conter campos na forma <c>{{chave}}</c>.</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value))
            {
                OnPropertyChanged(nameof(PreviewText));
            }
        }
    }

    /// <summary>Tamanho da fonte em pontos. Zero usa o padrão do modelo.</summary>
    public int FontSize
    {
        get => _fontSize;
        set => SetProperty(ref _fontSize, value);
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

    /// <summary>Alinhamento próprio. Nulo usa o padrão do modelo.</summary>
    public TextAlignment? Alignment
    {
        get => _alignment;
        set => SetProperty(ref _alignment, value);
    }

    /// <summary>Espaço antes do bloco, em pontos.</summary>
    public int SpacingBefore
    {
        get => _spacingBefore;
        set => SetProperty(ref _spacingBefore, value);
    }

    /// <summary>Espaço depois do bloco, em pontos.</summary>
    public int SpacingAfter
    {
        get => _spacingAfter;
        set => SetProperty(ref _spacingAfter, value);
    }

    public DividerStyle DividerStyle
    {
        get => _dividerStyle;
        set => SetProperty(ref _dividerStyle, value);
    }

    /// <summary>Altura do espaço em branco, em pontos.</summary>
    public int Height
    {
        get => _height;
        set
        {
            if (SetProperty(ref _height, value))
            {
                OnPropertyChanged(nameof(PreviewText));
            }
        }
    }

    [JsonIgnore]
    public string KindText => Kind switch
    {
        TemplateBlockKind.Texto => "Texto",
        TemplateBlockKind.Divisor => "Divisor",
        TemplateBlockKind.Espaco => "Espaço",
        _ => Kind.ToString(),
    };

    [JsonIgnore]
    public bool IsText => Kind == TemplateBlockKind.Texto;

    [JsonIgnore]
    public bool IsDivider => Kind == TemplateBlockKind.Divisor;

    [JsonIgnore]
    public bool IsSpacer => Kind == TemplateBlockKind.Espaco;

    /// <summary>Resumo do bloco para a lista do editor, com os campos entre «».</summary>
    [JsonIgnore]
    public string PreviewText
    {
        get
        {
            switch (Kind)
            {
                case TemplateBlockKind.Divisor:
                    return "— divisor —";
                case TemplateBlockKind.Espaco:
                    return $"— espaço de {Height} pt —";
                default:
                    if (string.IsNullOrWhiteSpace(Text))
                    {
                        return "(parágrafo vazio)";
                    }

                    var friendly = TemplateComposer.TokenRegex.Replace(
                        Text, m => "«" + m.Groups["key"].Value + "»");

                    return friendly.Length > 90 ? friendly[..90] + "…" : friendly;
            }
        }
    }

    public TemplateBlock Clone() => new()
    {
        Kind = Kind,
        Text = Text,
        FontSize = FontSize,
        Bold = Bold,
        Italic = Italic,
        Alignment = Alignment,
        SpacingBefore = SpacingBefore,
        SpacingAfter = SpacingAfter,
        DividerStyle = DividerStyle,
        Height = Height,
    };
}

/// <summary>Modelo de documento reutilizável, com blocos fixos e campos dinâmicos.</summary>
public sealed class DocumentTemplate
{
    public int SchemaVersion { get; set; } = 1;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public int PaperWidth { get; set; } = 384;

    public int FontSize { get; set; } = 12;

    public bool Bold { get; set; }

    public TextAlignment Alignment { get; set; } = TextAlignment.Esquerda;

    public double LineSpacing { get; set; } = 1.2;

    public int MarginLeft { get; set; } = 12;

    public int MarginRight { get; set; } = 12;

    public int MarginTop { get; set; } = 8;

    public int MarginBottom { get; set; } = 8;

    public List<TemplateBlock> Blocks { get; set; } = new();

    public List<TemplateField> Fields { get; set; } = new();

    /// <summary>
    /// Layout absoluto do editor visual. Quando presente, o documento é
    /// renderizado por posição; caso contrário, usa o fluxo de blocos.
    /// </summary>
    public DocumentLayout? Layout { get; set; }

    [JsonIgnore]
    public bool IsVisual => Layout is not null;

    [JsonIgnore]
    public string EditorModeText => IsVisual ? "Editor visual" : "Fluxo de blocos";

    [JsonIgnore]
    public string CreatedAtText => CreatedAt.LocalDateTime.ToString("dd/MM/yyyy HH:mm");

    [JsonIgnore]
    public string UpdatedAtText => UpdatedAt.LocalDateTime.ToString("dd/MM/yyyy HH:mm");

    [JsonIgnore]
    public int FieldCount => Fields.Count;

    [JsonIgnore]
    public string Summary =>
        $"{FieldCount} campo(s) · {Blocks.Count} bloco(s) · atualizado em {UpdatedAtText}";

    /// <summary>Cópia profunda. Com <paramref name="newIdentity"/>, gera novo Id e datas.</summary>
    public DocumentTemplate Clone(bool newIdentity = false)
    {
        var now = DateTimeOffset.Now;
        return new DocumentTemplate
        {
            SchemaVersion = SchemaVersion,
            Id = newIdentity ? Guid.NewGuid() : Id,
            Name = Name,
            Description = Description,
            CreatedAt = newIdentity ? now : CreatedAt,
            UpdatedAt = newIdentity ? now : UpdatedAt,
            PaperWidth = PaperWidth,
            FontSize = FontSize,
            Bold = Bold,
            Alignment = Alignment,
            LineSpacing = LineSpacing,
            MarginLeft = MarginLeft,
            MarginRight = MarginRight,
            MarginTop = MarginTop,
            MarginBottom = MarginBottom,
            Blocks = Blocks.Select(b => b.Clone()).ToList(),
            Fields = Fields.Select(f => f.Clone()).ToList(),
            Layout = Layout?.Clone(),
        };
    }

    /// <summary>
    /// Verdadeiro quando o campo aparece em algum elemento do layout ou em algum
    /// bloco de texto. Campos sem uso não são exibidos no preenchimento, porque
    /// não têm onde ser impressos.
    /// </summary>
    public bool IsFieldUsed(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (Layout is not null)
        {
            foreach (var element in Layout.Elements)
            {
                if (element.Kind == LayoutElementKind.Campo
                    && string.Equals(element.FieldKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (element.Kind == LayoutElementKind.Texto && ContainsToken(element.Text, key))
                {
                    return true;
                }
            }
        }

        foreach (var block in Blocks)
        {
            if (block.Kind == TemplateBlockKind.Texto && ContainsToken(block.Text, key))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Chaves de campos que não aparecem no documento.</summary>
    public IReadOnlyList<TemplateField> UnusedFields =>
        Fields.Where(field => !IsFieldUsed(field.Key)).ToList();

    private static bool ContainsToken(string? text, string key)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (Match match in TemplateComposer.TokenRegex.Matches(text))
        {
            if (string.Equals(match.Groups["key"].Value, key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
    /// <summary>Procura um campo pela chave, sem diferenciar maiúsculas.</summary>
    public TemplateField? FindField(string key) =>
        Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
}
