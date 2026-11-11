using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MiniPrint.Core.Configuration;
using MiniPrint.Core.Models;
using MiniPrint.Core.Templates;
using MiniPrint.Desktop.Services;
using MiniPrint.Imaging;
using Microsoft.Win32;
using TextAlignment = MiniPrint.Core.Configuration.TextAlignment;

namespace MiniPrint.Desktop.ViewModels;

/// <summary>
/// Editor visual de documentos térmicos: posicionamento absoluto, camadas,
/// grupos, alinhamento e undo/redo.
///
/// O documento é sempre mantido em pontos de impressão (1 ponto = 0,125 mm);
/// o zoom afeta apenas a apresentação.
/// </summary>
public sealed partial class VisualEditorViewModel : ViewModelBase
{
    private static readonly JsonSerializerOptions SnapshotOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ITemplateHost _host;
    private readonly DocumentTemplate _template;
    private readonly bool _isNew;
    private readonly DispatcherTimer _previewTimer;

    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private readonly List<LayoutElement> _clipboard = new();

    private string _name;
    private string _description;
    private int _paperWidth;
    private bool _autoHeight;
    private int _documentHeight;
    private int _marginLeft;
    private int _marginRight;
    private int _marginTop;
    private int _marginBottom;
    private int _gridSize;
    private bool _snapToGrid;
    private bool _showGrid = true;
    private double _zoom = 1.0;
    private bool _fitToWidth;

    private LayoutElement? _selectedElement;
    private readonly List<LayoutElement> _selectedElements = new();
    private string _statusMessage = string.Empty;
    private bool _isDirty;
    private bool _isRendering;
    private bool _isPrintPreview;
    private BitmapSource? _previewImage;
    private string _previewInfo = "Gerando pré-visualização...";
    private string _validationText = string.Empty;
    private bool _hasErrors;
    private int _renderGeneration;
    private string? _selectionSnapshot;
    private bool _propertySnapshotTaken;

    /// <summary>Rótulo padrão de um campo recém-criado (usado na normalização).</summary>
    private const string DefaultFieldLabel = "Novo campo";

    public VisualEditorViewModel(ITemplateHost host, DocumentTemplate template, bool isNew)
    {
        _host = host;
        _template = template;
        _isNew = isNew;

        if (template.Layout is null)
        {
            template.Layout = new DocumentLayout();
        }

        _name = template.Name;
        _description = template.Description;
        _paperWidth = template.PaperWidth;
        _autoHeight = template.Layout.AutoHeight;
        _documentHeight = template.Layout.Height;
        _marginLeft = template.Layout.MarginLeft;
        _marginRight = template.Layout.MarginRight;
        _marginTop = template.Layout.MarginTop;
        _marginBottom = template.Layout.MarginBottom;
        _gridSize = template.Layout.GridSize;
        _snapToGrid = template.Layout.SnapToGrid;

        foreach (var element in template.Layout.Elements)
        {
            Elements.Add(element);
        }

        // A lista de campos do documento precisa existir desde a abertura: é
        // dela que saem os botões de inserir campo e o seletor "Campo associado".
        RefreshFieldChoices();
        NormalizeFieldLabels();

        AddTextCommand = new RelayCommand(_ => AddElement(CreateText("Texto", "Digite aqui", 11)));
        AddTitleCommand = new RelayCommand(_ => AddElement(CreateText("Título", "TÍTULO", 15, bold: true, TextAlignment.Centro)));
        AddSubtitleCommand = new RelayCommand(_ => AddElement(CreateText("Subtítulo", "Subtítulo", 12, bold: true)));
        AddParagraphCommand = new RelayCommand(_ => AddElement(CreateParagraph()));
        AddLineCommand = new RelayCommand(_ => AddElement(CreateLine()));
        AddRectangleCommand = new RelayCommand(_ => AddElement(CreateRectangle()));
        AddSignatureCommand = new RelayCommand(_ => AddSignatureBlock());
        AddHeaderCommand = new RelayCommand(_ => AddHeader());
        AddFooterCommand = new RelayCommand(_ => AddFooter());
        AddIdentificationCommand = new RelayCommand(_ => AddIdentificationBlock());
        AddInfoBlockCommand = new RelayCommand(_ => AddInfoBlock());

        AddFieldElementCommand = new RelayCommand(p => AddFieldElement(p as TemplateField));
        NewFieldCommand = new RelayCommand(_ => AddNewField());
        ChooseImageCommand = new RelayCommand(_ => ChooseImage());
        UseSampleImageCommand = new RelayCommand(_ => AddLogoPlaceholder());

        DeleteCommand = new RelayCommand(_ => DeleteSelection(), _ => HasSelection);
        DuplicateCommand = new RelayCommand(_ => DuplicateSelection(), _ => HasSelection);
        CopyCommand = new RelayCommand(_ => CopySelection(), _ => HasSelection);
        CutCommand = new RelayCommand(_ => CutSelection(), _ => HasSelection);
        PasteCommand = new RelayCommand(_ => Paste(), _ => _clipboard.Count > 0);

        BringForwardCommand = new RelayCommand(_ => MoveOrder(1), _ => HasSelection);
        SendBackwardCommand = new RelayCommand(_ => MoveOrder(-1), _ => HasSelection);
        BringToFrontCommand = new RelayCommand(_ => MoveToEdge(true), _ => HasSelection);
        SendToBackCommand = new RelayCommand(_ => MoveToEdge(false), _ => HasSelection);

        AlignLeftCommand = new RelayCommand(_ => Align(ElementAlign.Left), _ => HasSelection);
        AlignCenterCommand = new RelayCommand(_ => Align(ElementAlign.CenterHorizontal), _ => HasSelection);
        AlignRightCommand = new RelayCommand(_ => Align(ElementAlign.Right), _ => HasSelection);
        AlignTopCommand = new RelayCommand(_ => Align(ElementAlign.Top), _ => HasSelection);
        AlignMiddleCommand = new RelayCommand(_ => Align(ElementAlign.CenterVertical), _ => HasSelection);
        AlignBottomCommand = new RelayCommand(_ => Align(ElementAlign.Bottom), _ => HasSelection);
        DistributeVerticalCommand = new RelayCommand(_ => Distribute(vertical: true), _ => Selection.Count > 2);
        DistributeHorizontalCommand = new RelayCommand(_ => Distribute(vertical: false), _ => Selection.Count > 2);

        GroupCommand = new RelayCommand(_ => GroupSelection(), _ => Selection.Count > 1);
        UngroupCommand = new RelayCommand(_ => UngroupSelection(), _ => HasSelection);
        ToggleLockCommand = new RelayCommand(_ => ToggleLock(), _ => HasSelection);

        UndoCommand = new RelayCommand(_ => Undo(), _ => _undo.Count > 0);
        RedoCommand = new RelayCommand(_ => Redo(), _ => _redo.Count > 0);

        SaveCommand = new RelayCommand(_ => Save());
        CancelCommand = new RelayCommand(_ => Cancel());
        PrintCommand = new AsyncRelayCommand(_ => PrintAsync(), _ => !IsRendering, HandleError);
        RefreshPreviewCommand = new RelayCommand(_ => SchedulePreview(immediate: true));

        ZoomInCommand = new RelayCommand(_ => Zoom = Math.Min(3.0, Zoom + 0.1));
        ZoomOutCommand = new RelayCommand(_ => Zoom = Math.Max(0.4, Zoom - 0.1));
        AddInformationCommand = new RelayCommand(_ => AddInfoBlock());
        ToggleVisibilityCommand = new RelayCommand(p => ToggleVisibility(p as LayoutElement));
        ToggleLockElementCommand = new RelayCommand(p => ToggleLockElement(p as LayoutElement));

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _previewTimer.Tick += async (_, _) =>
        {
            _previewTimer.Stop();
            await RenderPreviewAsync();
        };

        // Estado moderno: painéis, paleta, encaixe e salvamento automático.
        InitializeModernEditor();

        // Depois de os comandos existirem, seleciona o primeiro elemento.
        SelectedElement = Elements.FirstOrDefault();
        SchedulePreview(immediate: true);
        UpdateValidation();
    }

    // ---------- coleções e estado ----------

    public DocumentLayout Layout => _template.Layout!;

    public ObservableCollection<LayoutElement> Elements { get; } = new();

    /// <summary>Camadas na ordem de exibição (topo primeiro).</summary>
    public ObservableCollection<LayoutElement> Layers { get; } = new();

    public ObservableCollection<TemplateField> FieldChoices { get; } = new();

    public IReadOnlyList<TextAlignment> Alignments { get; } =
        new[] { TextAlignment.Esquerda, TextAlignment.Centro, TextAlignment.Direita };

    public IReadOnlyList<OverflowMode> OverflowModes { get; } =
        new[] { OverflowMode.QuebrarLinha, OverflowMode.ReduzirFonte, OverflowMode.Expandir, OverflowMode.Avisar };

    public IReadOnlyList<DitheringMode> DitheringModes { get; } =
        new[]
        {
            DitheringMode.FloydSteinberg, DitheringMode.Atkinson,
            DitheringMode.Bayer4, DitheringMode.Bayer8, DitheringMode.Limiar,
        };

    public IReadOnlyList<int> Rotations { get; } = new[] { 0, 90, 180, 270 };

    public IReadOnlyList<string> FontFamilies { get; } = new[]
    {
        "Segoe UI", "Arial", "Calibri", "Consolas", "Courier New", "Georgia",
        "Tahoma", "Times New Roman", "Trebuchet MS", "Verdana",
    };

    public RelayCommand AddTextCommand { get; }

    public RelayCommand AddTitleCommand { get; }

    public RelayCommand AddSubtitleCommand { get; }

    public RelayCommand AddParagraphCommand { get; }

    public RelayCommand AddLineCommand { get; }

    public RelayCommand AddRectangleCommand { get; }

    public RelayCommand AddSignatureCommand { get; }

    public RelayCommand AddHeaderCommand { get; }

    public RelayCommand AddFooterCommand { get; }

    public RelayCommand AddIdentificationCommand { get; }

    public RelayCommand AddInfoBlockCommand { get; }

    public RelayCommand AddFieldElementCommand { get; }

    public RelayCommand NewFieldCommand { get; }

    public RelayCommand ChooseImageCommand { get; }

    public RelayCommand UseSampleImageCommand { get; }

    public RelayCommand DeleteCommand { get; }

    public RelayCommand DuplicateCommand { get; }

    public RelayCommand CopyCommand { get; }

    public RelayCommand CutCommand { get; }

    public RelayCommand PasteCommand { get; }

    public RelayCommand BringForwardCommand { get; }

    public RelayCommand SendBackwardCommand { get; }

    public RelayCommand BringToFrontCommand { get; }

    public RelayCommand SendToBackCommand { get; }

    public RelayCommand AlignLeftCommand { get; }

    public RelayCommand AlignCenterCommand { get; }

    public RelayCommand AlignRightCommand { get; }

    public RelayCommand AlignTopCommand { get; }

    public RelayCommand AlignMiddleCommand { get; }

    public RelayCommand AlignBottomCommand { get; }

    public RelayCommand DistributeVerticalCommand { get; }

    public RelayCommand DistributeHorizontalCommand { get; }

    public RelayCommand GroupCommand { get; }

    public RelayCommand UngroupCommand { get; }

    public RelayCommand ToggleLockCommand { get; }

    public RelayCommand UndoCommand { get; }

    public RelayCommand RedoCommand { get; }

    public RelayCommand SaveCommand { get; }

    public RelayCommand CancelCommand { get; }

    public AsyncRelayCommand PrintCommand { get; }

    public RelayCommand RefreshPreviewCommand { get; }

    public RelayCommand ZoomInCommand { get; }

    public RelayCommand ZoomOutCommand { get; }

    public RelayCommand AddInformationCommand { get; }

    public RelayCommand ToggleVisibilityCommand { get; }

    public RelayCommand ToggleLockElementCommand { get; }

    /// <summary>Margens como espessura, para desenhar o limite da área útil.</summary>
    public Thickness MarginThickness =>
        new(MarginLeft, MarginTop, MarginRight, MarginBottom);

    public string Title => string.IsNullOrWhiteSpace(_name) ? "Novo documento" : _name;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(Title));
                MarkDirty();
            }
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            if (SetProperty(ref _description, value))
            {
                MarkDirty();
            }
        }
    }

    public int PaperWidth
    {
        get => _paperWidth;
        set
        {
            if (SetProperty(ref _paperWidth, Math.Clamp(value, 64, 576)))
            {
                Layout.Width = _paperWidth;
                ClampElements();
                MarkDirty();
                SchedulePreview();
            }
        }
    }

    public bool AutoHeight
    {
        get => _autoHeight;
        set
        {
            if (SetProperty(ref _autoHeight, value))
            {
                Layout.AutoHeight = value;
                MarkDirty();
                SchedulePreview();
            }
        }
    }

    public int DocumentHeight
    {
        get => _documentHeight;
        set
        {
            if (SetProperty(ref _documentHeight, Math.Clamp(value, 16, 4000)))
            {
                Layout.Height = _documentHeight;
                MarkDirty();
                SchedulePreview();
            }
        }
    }

    public int MarginLeft
    {
        get => _marginLeft;
        set => SetMargin(value, v => _marginLeft = v, nameof(MarginLeft));
    }

    public int MarginRight
    {
        get => _marginRight;
        set => SetMargin(value, v => _marginRight = v, nameof(MarginRight));
    }

    public int MarginTop
    {
        get => _marginTop;
        set => SetMargin(value, v => _marginTop = v, nameof(MarginTop));
    }

    public int MarginBottom
    {
        get => _marginBottom;
        set => SetMargin(value, v => _marginBottom = v, nameof(MarginBottom));
    }

    public int GridSize
    {
        get => _gridSize;
        set
        {
            if (SetProperty(ref _gridSize, Math.Clamp(value, 2, 64)))
            {
                Layout.GridSize = _gridSize;
                OnPropertyChanged(nameof(GridBrush));
            }
        }
    }

    public bool SnapToGrid
    {
        get => _snapToGrid;
        set
        {
            if (SetProperty(ref _snapToGrid, value))
            {
                Layout.SnapToGrid = value;
            }
        }
    }

    public bool ShowGrid
    {
        get => _showGrid;
        set => SetProperty(ref _showGrid, value);
    }

    public double Zoom
    {
        get => _zoom;
        set
        {
            if (SetProperty(ref _zoom, Math.Clamp(Math.Round(value, 2), 0.4, 3.0)))
            {
                OnPropertyChanged(nameof(ZoomText));
                OnPropertyChanged(nameof(ZoomPercent));
                OnPropertyChanged(nameof(ZoomPercentNumber));
            }
        }
    }

    public string ZoomText => $"{Zoom * 100:0}%";

    public bool FitToWidth
    {
        get => _fitToWidth;
        set => SetProperty(ref _fitToWidth, value);
    }

    public LayoutElement? SelectedElement
    {
        get => _selectedElement;
        set
        {
            if (ReferenceEquals(_selectedElement, value) && _selectedElements.Count == 0)
            {
                return;
            }

            ClearSelectionFlags();
            _selectedElements.Clear();
            _selectedElement = value;

            if (value is not null)
            {
                foreach (var element in Selection)
                {
                    element.IsSelected = true;
                }
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectionCount));
            OnPropertyChanged(nameof(SelectedText));
            OnPropertyChanged(nameof(SelectionBounds));
            RaiseKindProperties();
            RaiseFieldProperties();
            RaiseSelectionCommands();

            // Guarda o estado para o undo de edições de propriedade.
            _selectionSnapshot = Snapshot();
            _propertySnapshotTaken = false;

            if (value is not null)
            {
                value.PropertyChanged += OnSelectedElementChanged;
            }

            OnPropertyChanged(nameof(SelectedLayer));
            OnPropertyChanged(nameof(ValidationSummary));
            RaiseSelectionSummary();
            UpdateValidation();
        }
    }

    /// <summary>Seleciona vários elementos (usado por Ctrl+A e pelo painel de camadas).</summary>
    public void SelectMany(IEnumerable<LayoutElement> elements)
    {
        var list = elements.Where(e => e.Visible).ToList();
        if (list.Count == 0)
        {
            return;
        }

        ClearSelectionFlags();
        _selectedElements.Clear();
        _selectedElements.AddRange(list);
        _selectedElement = list[0];

        foreach (var element in list)
        {
            element.IsSelected = true;
            element.PropertyChanged += OnSelectedElementChanged;
        }

        OnPropertyChanged(nameof(SelectedElement));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionCount));
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(SelectionBounds));
        RaiseKindProperties();
        RaiseFieldProperties();
        RaiseSelectionSummary();
        RaiseSelectionCommands();
        _selectionSnapshot = Snapshot();
        _propertySnapshotTaken = false;
    }

    private void RaiseKindProperties()
    {
        OnPropertyChanged(nameof(SelectedIsText));
        OnPropertyChanged(nameof(SelectedIsField));
        OnPropertyChanged(nameof(SelectedShowsText));
        OnPropertyChanged(nameof(SelectedIsImage));
        OnPropertyChanged(nameof(SelectedIsLine));
        OnPropertyChanged(nameof(SelectedIsRect));
    }

    public bool HasSelection => _selectedElement is not null;

    public bool SelectedIsText => _selectedElement?.IsText == true;

    public bool SelectedIsField => _selectedElement?.IsField == true;

    public bool SelectedShowsText => _selectedElement?.ShowsText == true;

    public bool SelectedIsImage => _selectedElement?.IsImage == true;

    public bool SelectedIsLine => _selectedElement?.IsLine == true;

    public bool SelectedIsRect => _selectedElement?.IsRectangle == true;

    public int SelectionCount => Selection.Count;

    public string SelectedText
    {
        get => _selectedElement?.Text ?? string.Empty;
        set
        {
            if (_selectedElement is null || _selectedElement.Text == value)
            {
                return;
            }

            EnsurePropertyUndo();
            _selectedElement.Text = value;
            OnPropertyChanged();
            UpdateAutoHeight(_selectedElement);
            MarkDirty();
            SchedulePreview();
        }
    }

    /// <summary>Elementos selecionados (inclui o grupo inteiro, se houver).</summary>
    public IReadOnlyList<LayoutElement> Selection
    {
        get
        {
            if (_selectedElements.Count > 0)
            {
                return _selectedElements;
            }

            if (_selectedElement is null)
            {
                return Array.Empty<LayoutElement>();
            }

            if (_selectedElement.GroupId is { } groupId)
            {
                var members = Elements.Where(e => e.GroupId == groupId).ToList();
                if (members.Count > 0)
                {
                    return members;
                }
            }

            return new[] { _selectedElement };
        }
    }

    public string SelectionBounds
    {
        get
        {
            var selection = Selection;
            if (selection.Count == 0)
            {
                return string.Empty;
            }

            var left = selection.Min(e => e.X);
            var top = selection.Min(e => e.Y);
            var right = selection.Max(e => e.Right);
            var bottom = selection.Max(e => e.Bottom);
            return $"x {left}  y {top}  ·  {right - left} × {bottom - top} pontos";
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
            {
                OnPropertyChanged(nameof(DirtyText));
            }
        }
    }

    public string DirtyText => IsDirty ? "Alterações não salvas" : "Tudo salvo";

    public bool IsRendering
    {
        get => _isRendering;
        private set => SetProperty(ref _isRendering, value);
    }

    /// <summary>Modo de impressão: mostra apenas o resultado final.</summary>
    public bool IsPrintPreview
    {
        get => _isPrintPreview;
        set
        {
            if (SetProperty(ref _isPrintPreview, value))
            {
                OnPropertyChanged(nameof(IsEditMode));
                SchedulePreview(immediate: true);
            }
        }
    }

    public bool IsEditMode => !IsPrintPreview;

    public BitmapSource? PreviewImage
    {
        get => _previewImage;
        private set => SetProperty(ref _previewImage, value);
    }

    public string PreviewInfo
    {
        get => _previewInfo;
        private set => SetProperty(ref _previewInfo, value);
    }

    public string ValidationText
    {
        get => _validationText;
        private set => SetProperty(ref _validationText, value);
    }

    public bool HasErrors
    {
        get => _hasErrors;
        private set => SetProperty(ref _hasErrors, value);
    }

    public int CanvasHeight => Math.Max(Layout.EffectiveHeight, 60);

    public int ContentLeft => Layout.MarginLeft;

    public int ContentWidth => Layout.ContentWidth;

    /// <summary>Pincel da grade, gerado a partir do tamanho configurado.</summary>
    public System.Windows.Media.Brush GridBrush
    {
        get
        {
            var step = Math.Max(2, GridSize);
            var group = new System.Windows.Media.DrawingGroup();
            var pen = new System.Windows.Media.Pen(
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(28, 15, 118, 110)), 0.5);
            group.Children.Add(new System.Windows.Media.GeometryDrawing(
                System.Windows.Media.Brushes.Transparent, pen,
                new System.Windows.Media.RectangleGeometry(new Rect(0, 0, step, step))));
            var brush = new System.Windows.Media.DrawingBrush(group)
            {
                TileMode = System.Windows.Media.TileMode.Tile,
                Viewport = new Rect(0, 0, step, step),
                ViewportUnits = System.Windows.Media.BrushMappingMode.Absolute,
            };
            brush.Freeze();
            return brush;
        }
    }

    // ---------- adicionar elementos ----------

    private void AddElement(LayoutElement element)
    {
        PushUndo();
        Elements.Add(element);
        Layout.Elements = Elements.ToList();
        SelectedElement = element;
        MarkDirty();
        RefreshLayers();
        SchedulePreview();
        StatusMessage = $"{element.KindText} adicionado.";
    }

    private LayoutElement CreateText(
        string name,
        string text,
        double size,
        bool bold = false,
        TextAlignment align = TextAlignment.Esquerda)
    {
        var element = new LayoutElement
        {
            Kind = LayoutElementKind.Texto,
            Name = name,
            X = Layout.MarginLeft,
            Y = NextFreeY(),
            Width = Layout.ContentWidth,
            Height = (int)Math.Ceiling(size * 1.8),
            Text = text,
            FontSize = size,
            Bold = bold,
            Alignment = align,
            AutoHeight = true,
        };

        return element;
    }

    private LayoutElement CreateParagraph() =>
        new()
        {
            Kind = LayoutElementKind.Texto,
            Name = "Parágrafo",
            X = Layout.MarginLeft,
            Y = NextFreeY(),
            Width = Layout.ContentWidth,
            Height = 48,
            Text = "Escreva o texto do parágrafo. Ele quebra automaticamente na largura do papel.",
            FontSize = 10,
            AutoHeight = true,
        };

    private LayoutElement CreateLine() =>
        new()
        {
            Kind = LayoutElementKind.Linha,
            Name = "Linha",
            X = Layout.MarginLeft,
            Y = NextFreeY(),
            Width = Layout.ContentWidth,
            Height = 4,
            Thickness = 2,
        };

    private LayoutElement CreateRectangle() =>
        new()
        {
            Kind = LayoutElementKind.Retangulo,
            Name = "Retângulo",
            X = Layout.MarginLeft,
            Y = NextFreeY(),
            Width = Math.Min(200, Layout.ContentWidth),
            Height = 40,
            Thickness = 2,
            Filled = false,
        };

    private void AddSignatureBlock()
    {
        PushUndo();
        var y = NextFreeY() + 16;

        var line = new LayoutElement
        {
            Kind = LayoutElementKind.Linha,
            Name = "Linha de assinatura",
            X = Layout.MarginLeft + 60,
            Y = y,
            Width = Math.Max(80, Layout.ContentWidth - 120),
            Height = 3,
            Thickness = 1,
        };

        var caption = new LayoutElement
        {
            Kind = LayoutElementKind.Texto,
            Name = "Legenda da assinatura",
            X = Layout.MarginLeft,
            Y = y + 6,
            Width = Layout.ContentWidth,
            Height = 16,
            Text = "Assinatura",
            FontSize = 9,
            Alignment = TextAlignment.Centro,
        };

        Elements.Add(line);
        Elements.Add(caption);
        Layout.Elements = Elements.ToList();
        SelectedElement = line;
        MarkDirty();
        RefreshLayers();
        SchedulePreview();
        StatusMessage = "Área de assinatura adicionada.";
    }

    private void AddHeader()
    {
        PushUndo();
        var title = CreateText("Título do cabeçalho", "DOCUMENTO", 15, bold: true, TextAlignment.Centro);
        title.Y = Layout.MarginTop;
        title.Height = 22;

        var line = new LayoutElement
        {
            Kind = LayoutElementKind.Linha,
            Name = "Linha do cabeçalho",
            X = Layout.MarginLeft,
            Y = title.Y + title.Height + 4,
            Width = Layout.ContentWidth,
            Height = 3,
            Thickness = 2,
        };

        Elements.Add(title);
        Elements.Add(line);
        Layout.Elements = Elements.ToList();
        SelectedElement = title;
        MarkDirty();
        RefreshLayers();
        SchedulePreview();
        StatusMessage = "Cabeçalho adicionado.";
    }

    private void AddFooter()
    {
        PushUndo();
        var line = new LayoutElement
        {
            Kind = LayoutElementKind.Linha,
            Name = "Linha do rodapé",
            X = Layout.MarginLeft,
            Y = NextFreeY(),
            Width = Layout.ContentWidth,
            Height = 3,
            Thickness = 1,
        };

        var text = new LayoutElement
        {
            Kind = LayoutElementKind.Texto,
            Name = "Texto do rodapé",
            X = Layout.MarginLeft,
            Y = line.Y + 6,
            Width = Layout.ContentWidth,
            Height = 16,
            Text = "Obrigado pela preferência!",
            FontSize = 9,
            Alignment = TextAlignment.Centro,
        };

        Elements.Add(line);
        Elements.Add(text);
        Layout.Elements = Elements.ToList();
        SelectedElement = text;
        MarkDirty();
        RefreshLayers();
        SchedulePreview();
        StatusMessage = "Rodapé adicionado.";
    }

    private void AddIdentificationBlock()
    {
        PushUndo();
        var y = NextFreeY();

        var caption = new LayoutElement
        {
            Kind = LayoutElementKind.Texto,
            Name = "Rótulo de identificação",
            X = Layout.MarginLeft,
            Y = y,
            Width = 110,
            Height = 16,
            Text = "Documento:",
            FontSize = 10,
        };

        Elements.Add(caption);
        Layout.Elements = Elements.ToList();
        SelectedElement = caption;
        MarkDirty();
        RefreshLayers();
        SchedulePreview();
        StatusMessage = "Bloco de identificação adicionado.";
    }

    private void AddInfoBlock()
    {
        PushUndo();
        var y = NextFreeY();
        var box = new LayoutElement
        {
            Kind = LayoutElementKind.Retangulo,
            Name = "Bloco de informações",
            X = Layout.MarginLeft,
            Y = y,
            Width = Layout.ContentWidth,
            Height = 60,
            Thickness = 1,
            Filled = false,
        };

        Elements.Add(box);
        Layout.Elements = Elements.ToList();
        SelectedElement = box;
        MarkDirty();
        RefreshLayers();
        SchedulePreview();
        StatusMessage = "Bloco de informações adicionado.";
    }

    private void AddFieldElement(TemplateField? field)
    {
        if (field is null)
        {
            StatusMessage = "Cadastre um campo antes de inserir no documento.";
            return;
        }

        var element = new LayoutElement
        {
            Kind = LayoutElementKind.Campo,
            Name = field.Label,
            X = Layout.MarginLeft,
            Y = NextFreeY(),
            Width = Layout.ContentWidth,
            Height = (int)Math.Ceiling(11 * 1.8),
            Text = field.Label,
            FieldKey = field.Key,
            FontSize = 11,
            AutoHeight = true,
        };

        AddElement(element);
    }

    private void AddNewField()
    {
        var label = NextFieldLabel();
        var key = TemplateFieldFormatter.SuggestKey(label);
        var suffix = 2;
        var baseKey = key;
        while (FieldChoices.Any(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)))
        {
            key = $"{baseKey}_{suffix++}";
        }

        var field = new TemplateField
        {
            Key = key,
            Label = label,
            Type = TemplateFieldType.TextoCurto,
        };

        _template.Fields.Add(field);
        RefreshFieldChoices();
        MarkDirty();

        var element = new LayoutElement
        {
            Kind = LayoutElementKind.Campo,
            Name = field.Label,
            X = Layout.MarginLeft,
            Y = NextFreeY(),
            Width = Layout.ContentWidth,
            Height = 20,
            Text = field.Label,
            FieldKey = field.Key,
            FontSize = 11,
            AutoHeight = true,
        };

        AddElement(element);
        StatusMessage = "Campo criado. Ajuste o rótulo e o tipo em Propriedades → Campo.";
    }

    /// <summary>Rótulo livre para um campo novo: "Campo 1", "Campo 2"...</summary>
    private string NextFieldLabel()
    {
        var index = _template.Fields.Count + 1;
        while (_template.Fields.Any(f =>
                   string.Equals(f.Label, $"Campo {index}", StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }

        return $"Campo {index}";
    }

    private void AddLogoPlaceholder()
    {
        var element = new LayoutElement
        {
            Kind = LayoutElementKind.Imagem,
            Name = "Logotipo",
            X = Layout.MarginLeft,
            Y = NextFreeY(),
            Width = 120,
            Height = 60,
            KeepAspect = true,
        };

        AddElement(element);
        StatusMessage = "Elemento de imagem adicionado. Escolha o arquivo nas propriedades.";
    }

    private void ChooseImage()
    {
        var target = _selectedElement is { Kind: LayoutElementKind.Imagem } element
            ? element
            : null;

        if (target is null)
        {
            target = new LayoutElement
            {
                Kind = LayoutElementKind.Imagem,
                Name = "Imagem",
                X = Layout.MarginLeft,
                Y = NextFreeY(),
                Width = 160,
                Height = 80,
                KeepAspect = true,
            };

            PushUndo();
            Elements.Add(target);
            Layout.Elements = Elements.ToList();
        }

        var dialog = new OpenFileDialog
        {
            Title = "Escolher imagem",
            Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
        {
            RefreshLayers();
            return;
        }

        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);
            target.ImageData = Convert.ToBase64String(bytes);
            target.ImagePath = dialog.FileName;
            target.Name = Path.GetFileName(dialog.FileName);
            SelectedElement = target;
            MarkDirty();
            RefreshLayers();
            SchedulePreview();
            StatusMessage = $"Imagem \"{Path.GetFileName(dialog.FileName)}\" adicionada.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Falha ao carregar a imagem: {ex.Message}";
        }
    }

    private int NextFreeY()
    {
        var bottom = Layout.MarginTop;
        foreach (var element in Elements)
        {
            if (element.Visible)
            {
                bottom = Math.Max(bottom, element.Bottom);
            }
        }

        return Math.Min(bottom + 4, 3000);
    }

    // ---------- edição ----------

    public void DeleteSelection()
    {
        if (!HasSelection)
        {
            return;
        }

        PushUndo();

        // Campos usados pelos elementos excluídos podem ficar sem uso.
        var candidateFields = Selection
            .Where(e => e.IsField && !string.IsNullOrWhiteSpace(e.FieldKey))
            .Select(e => e.FieldKey!)
            .ToList();

        foreach (var element in Selection.ToList())
        {
            Elements.Remove(element);
        }

        Layout.Elements = Elements.ToList();
        SelectedElement = Elements.LastOrDefault();
        MarkDirty();
        RefreshLayers();
        SchedulePreview();
        PruneFieldsAfterDelete(candidateFields);
        StatusMessage = "Elemento(s) excluído(s).";
    }

    public void DuplicateSelection()
    {
        if (!HasSelection)
        {
            return;
        }

        PushUndo();
        var copies = new List<LayoutElement>();
        foreach (var element in Selection.ToList())
        {
            var copy = element.Clone();
            copy.X += 8;
            copy.Y += 8;
            copy.Name = element.Name + " (cópia)";
            Elements.Add(copy);
            copies.Add(copy);
        }

        Layout.Elements = Elements.ToList();
        SelectedElement = copies[^1];
        MarkDirty();
        RefreshLayers();
        SchedulePreview();
        StatusMessage = "Elemento(s) duplicado(s).";
    }

    public void CopySelection()
    {
        _clipboard.Clear();
        _clipboard.AddRange(Selection.Select(e => e.Clone()));
        PasteCommand.RaiseCanExecuteChanged();
        StatusMessage = $"{_clipboard.Count} elemento(s) copiado(s).";
    }

    public void CutSelection()
    {
        CopySelection();
        DeleteSelection();
    }

    public void Paste()
    {
        if (_clipboard.Count == 0)
        {
            return;
        }

        PushUndo();
        var pasted = new List<LayoutElement>();
        foreach (var element in _clipboard)
        {
            var copy = element.Clone();
            copy.X += 8;
            copy.Y += 8;
            Elements.Add(copy);
            pasted.Add(copy);
        }

        Layout.Elements = Elements.ToList();
        SelectedElement = pasted[^1];
        MarkDirty();
        RefreshLayers();
        SchedulePreview();
        StatusMessage = "Conteúdo colado.";
    }

    public void SelectAll()
    {
        if (Elements.Count == 0)
        {
            return;
        }

        SelectMany(Elements);
        StatusMessage = $"{Elements.Count} elemento(s) selecionado(s).";
    }

    public void MoveSelection(int deltaX, int deltaY)
    {
        if (!HasSelection)
        {
            return;
        }

        PushUndo();
        foreach (var element in Selection)
        {
            if (element.Locked)
            {
                continue;
            }

            element.X = Math.Clamp(element.X + deltaX, 0, Math.Max(0, Layout.Width - 4));
            element.Y = Math.Max(0, element.Y + deltaY);
        }

        OnPropertyChanged(nameof(SelectionBounds));
        MarkDirty();
        SchedulePreview();
    }

    private void MoveOrder(int delta)
    {
        if (!HasSelection)
        {
            return;
        }

        PushUndo();
        var element = _selectedElement!;
        var index = Elements.IndexOf(element);
        var target = Math.Clamp(index + delta, 0, Elements.Count - 1);
        if (target != index)
        {
            Elements.Move(index, target);
            Layout.Elements = Elements.ToList();
            RefreshLayers();
            MarkDirty();
            SchedulePreview();
        }
    }

    private void MoveToEdge(bool front)
    {
        if (!HasSelection)
        {
            return;
        }

        PushUndo();
        var element = _selectedElement!;
        Elements.Remove(element);
        if (front)
        {
            Elements.Add(element);
        }
        else
        {
            Elements.Insert(0, element);
        }

        Layout.Elements = Elements.ToList();
        SelectedElement = element;
        RefreshLayers();
        MarkDirty();
        SchedulePreview();
    }

    // ---------- alinhamento ----------

    private enum ElementAlign
    {
        Left,
        CenterHorizontal,
        Right,
        Top,
        CenterVertical,
        Bottom,
    }

    private void Align(ElementAlign alignment)
    {
        if (!HasSelection)
        {
            return;
        }

        PushUndo();
        var selection = Selection;

        // Com um elemento, alinha à área útil; com vários, ao bloco da seleção.
        var left = selection.Count > 1 ? selection.Min(e => e.X) : Layout.MarginLeft;
        var right = selection.Count > 1 ? selection.Max(e => e.Right) : Layout.MarginLeft + Layout.ContentWidth;
        var top = selection.Count > 1 ? selection.Min(e => e.Y) : Layout.MarginTop;
        var bottom = selection.Count > 1
            ? selection.Max(e => e.Bottom)
            : Math.Max(Layout.MarginTop + 8, Layout.EffectiveHeight - Layout.MarginBottom);

        foreach (var element in selection)
        {
            switch (alignment)
            {
                case ElementAlign.Left:
                    element.X = left;
                    break;
                case ElementAlign.CenterHorizontal:
                    element.X = left + ((right - left - element.Width) / 2);
                    break;
                case ElementAlign.Right:
                    element.X = right - element.Width;
                    break;
                case ElementAlign.Top:
                    element.Y = top;
                    break;
                case ElementAlign.CenterVertical:
                    element.Y = top + ((bottom - top - element.Height) / 2);
                    break;
                case ElementAlign.Bottom:
                    element.Y = bottom - element.Height;
                    break;
            }

            element.X = Math.Max(0, element.X);
            element.Y = Math.Max(0, element.Y);
        }

        OnPropertyChanged(nameof(SelectionBounds));
        MarkDirty();
        SchedulePreview();
        StatusMessage = "Alinhamento aplicado.";
    }

    private void Distribute(bool vertical)
    {
        var selection = Selection.OrderBy(e => vertical ? e.Y : e.X).ToList();
        if (selection.Count < 3)
        {
            return;
        }

        PushUndo();

        if (vertical)
        {
            var top = selection[0].Y;
            var bottom = selection[^1].Y;
            var step = (double)(bottom - top) / (selection.Count - 1);

            for (var i = 1; i < selection.Count - 1; i++)
            {
                selection[i].Y = (int)Math.Round(top + (step * i));
            }
        }
        else
        {
            var left = selection[0].X;
            var right = selection[^1].X;
            var step = (double)(right - left) / (selection.Count - 1);

            for (var i = 1; i < selection.Count - 1; i++)
            {
                selection[i].X = (int)Math.Round(left + (step * i));
            }
        }

        OnPropertyChanged(nameof(SelectionBounds));
        MarkDirty();
        SchedulePreview();
        StatusMessage = "Elementos distribuídos.";
    }

    private void GroupSelection()
    {
        var selection = Selection;
        if (selection.Count < 2)
        {
            return;
        }

        PushUndo();
        var groupId = Guid.NewGuid();
        foreach (var element in selection)
        {
            element.GroupId = groupId;
        }

        MarkDirty();
        RefreshLayers();
        StatusMessage = "Elementos agrupados.";
    }

    private void UngroupSelection()
    {
        if (!HasSelection)
        {
            return;
        }

        PushUndo();
        foreach (var element in Selection)
        {
            element.GroupId = null;
        }

        MarkDirty();
        RefreshLayers();
        StatusMessage = "Grupo desfeito.";
    }

    private void ToggleLock()
    {
        if (!HasSelection)
        {
            return;
        }

        PushUndo();
        var locked = !Selection.All(e => e.Locked);
        foreach (var element in Selection)
        {
            element.Locked = locked;
        }

        MarkDirty();
        RefreshLayers();
        StatusMessage = locked ? "Posição bloqueada." : "Posição liberada.";
    }

    // ---------- undo / redo ----------

    /// <summary>
    /// Fotografia do estado editável: documento (nome, papel, margens), campos e
    /// layout. Antes era só o layout, então criar/editar/excluir campos e mudar
    /// o papel não podia ser desfeito.
    /// </summary>
    private string Snapshot() => JsonSerializer.Serialize(BuildWorkingCopy(), SnapshotOptions);

    /// <summary>
    /// Guarda o estado atual antes de uma alteração. Com <paramref name="mergeKey"/>,
    /// alterações da mesma natureza em sequência (digitar, clicar nas setas)
    /// viram um único passo do histórico.
    /// </summary>
    public void PushUndo(string? mergeKey = null)
    {
        var now = DateTime.UtcNow;
        if (mergeKey is not null
            && string.Equals(mergeKey, _lastMergeKey, StringComparison.Ordinal)
            && now - _lastMergeAt < MergeWindow)
        {
            _lastMergeAt = now;
            return;
        }

        _lastMergeKey = mergeKey;
        _lastMergeAt = now;

        _undo.Push(Snapshot());
        _redo.Clear();

        while (_undo.Count > UndoLimit)
        {
            var trimmed = _undo.ToArray().Take(UndoLimit).Reverse().ToList();
            _undo.Clear();
            foreach (var item in trimmed)
            {
                _undo.Push(item);
            }
        }

        RaiseUndoCommands();
        RaiseHistoryChanged();
    }

    private void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Push(Snapshot());
        Restore(_undo.Pop());
        _lastMergeKey = null;
        StatusMessage = "Alteração desfeita.";
        RaiseHistoryChanged();
    }

    private void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Push(Snapshot());
        Restore(_redo.Pop());
        _lastMergeKey = null;
        StatusMessage = "Alteração refeita.";
        RaiseHistoryChanged();
    }

    private void Restore(string snapshot)
    {
        var template = JsonSerializer.Deserialize<DocumentTemplate>(snapshot, SnapshotOptions);
        if (template?.Layout is null)
        {
            return;
        }

        var selectedId = _selectedElement?.Id;

        _template.Layout = template.Layout;
        _template.Fields = template.Fields ?? new List<TemplateField>();
        _name = template.Name;
        _description = template.Description;
        _paperWidth = template.PaperWidth;
        _autoHeight = template.Layout.AutoHeight;
        _documentHeight = template.Layout.Height;
        _marginLeft = template.Layout.MarginLeft;
        _marginRight = template.Layout.MarginRight;
        _marginTop = template.Layout.MarginTop;
        _marginBottom = template.Layout.MarginBottom;
        _gridSize = template.Layout.GridSize;
        _snapToGrid = template.Layout.SnapToGrid;

        Layout.Elements = template.Layout.Elements;
        Elements.Clear();
        foreach (var element in template.Layout.Elements)
        {
            Elements.Add(element);
        }

        ClearSelectionFlags();
        _selectedElements.Clear();
        _selectedElement = Elements.FirstOrDefault(e => e.Id == selectedId) ?? Elements.LastOrDefault();
        if (_selectedElement is not null)
        {
            foreach (var element in Selection)
            {
                element.IsSelected = true;
            }
        }

        RefreshFieldChoices();
        RaiseDocumentProperties();
        OnPropertyChanged(nameof(SelectedElement));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(SelectionBounds));
        OnPropertyChanged(nameof(CanvasHeight));
        RefreshLayers();
        RaiseSelectionCommands();
        RaiseUndoCommands();
        RaiseFieldProperties();

        // Depois de desfazer, a próxima edição volta a gerar um passo novo.
        _propertySnapshotTaken = false;
        _selectionSnapshot = Snapshot();

        MarkDirty();
        SchedulePreview();
    }

    /// <summary>Notifica tudo o que depende das propriedades do documento.</summary>
    private void RaiseDocumentProperties()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(PaperWidth));
        OnPropertyChanged(nameof(AutoHeight));
        OnPropertyChanged(nameof(DocumentHeight));
        OnPropertyChanged(nameof(MarginLeft));
        OnPropertyChanged(nameof(MarginRight));
        OnPropertyChanged(nameof(MarginTop));
        OnPropertyChanged(nameof(MarginBottom));
        OnPropertyChanged(nameof(MarginThickness));
        OnPropertyChanged(nameof(GridSize));
        OnPropertyChanged(nameof(SnapToGrid));
        OnPropertyChanged(nameof(CanvasHeight));
        OnPropertyChanged(nameof(ContentLeft));
        OnPropertyChanged(nameof(ContentWidth));
    }

    private void RaiseUndoCommands()
    {
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
    }

    private void RaiseSelectionCommands()
    {
        // Ainda no construtor, antes de os comandos existirem.
        if (DeleteCommand is null)
        {
            return;
        }

        DeleteCommand.RaiseCanExecuteChanged();
        DuplicateCommand.RaiseCanExecuteChanged();
        CopyCommand.RaiseCanExecuteChanged();
        CutCommand.RaiseCanExecuteChanged();
        BringForwardCommand.RaiseCanExecuteChanged();
        SendBackwardCommand.RaiseCanExecuteChanged();
        BringToFrontCommand.RaiseCanExecuteChanged();
        SendToBackCommand.RaiseCanExecuteChanged();
        AlignLeftCommand.RaiseCanExecuteChanged();
        AlignCenterCommand.RaiseCanExecuteChanged();
        AlignRightCommand.RaiseCanExecuteChanged();
        AlignTopCommand.RaiseCanExecuteChanged();
        AlignMiddleCommand.RaiseCanExecuteChanged();
        AlignBottomCommand.RaiseCanExecuteChanged();
        DistributeVerticalCommand.RaiseCanExecuteChanged();
        DistributeHorizontalCommand.RaiseCanExecuteChanged();
        GroupCommand.RaiseCanExecuteChanged();
        UngroupCommand.RaiseCanExecuteChanged();
        ToggleLockCommand.RaiseCanExecuteChanged();
        PasteCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// O primeiro ajuste de propriedade depois de selecionar empilha o estado
    /// anterior, para o undo funcionar também nas edições de propriedade.
    /// </summary>
    private void EnsurePropertyUndo()
    {
        if (_propertySnapshotTaken || _selectionSnapshot is null)
        {
            return;
        }

        _undo.Push(_selectionSnapshot);
        _redo.Clear();
        _propertySnapshotTaken = true;
        RaiseUndoCommands();
    }

    private void OnSelectedElementChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Primeira edição depois de selecionar: guarda o estado anterior.
        EnsurePropertyUndo();

        if (e.PropertyName is nameof(LayoutElement.X) or nameof(LayoutElement.Y))
        {
            OnPropertyChanged(nameof(SelectionBounds));
        }

        if (e.PropertyName is nameof(LayoutElement.Text) or nameof(LayoutElement.FontSize)
            or nameof(LayoutElement.Width) or nameof(LayoutElement.AutoHeight))
        {
            if (sender is LayoutElement element)
            {
                UpdateAutoHeight(element);
            }
        }

        if (sender is LayoutElement changed)
        {
            if (e.PropertyName is nameof(LayoutElement.Name) or nameof(LayoutElement.Text))
            {
                // Renomear a camada de um campo é o jeito natural de dar nome a ele.
                if (changed.IsField)
                {
                    SyncFieldLabelFromElement(
                        changed,
                        e.PropertyName == nameof(LayoutElement.Name) ? changed.Name : changed.Text);
                }
            }
            else if (e.PropertyName == nameof(LayoutElement.FieldKey))
            {
                RaiseFieldProperties();
                if (changed.IsField
                    && !string.IsNullOrWhiteSpace(changed.FieldKey)
                    && _template.FindField(changed.FieldKey) is { } field)
                {
                    var label = string.IsNullOrWhiteSpace(field.Label) ? field.Key : field.Label.Trim();
                    changed.Name = label;
                    changed.Text = label;
                }
            }
        }

        MarkDirty();
        SchedulePreview();
    }

    /// <summary>Chamado pelo canvas ao iniciar um arraste ou redimensionamento.</summary>
    public void BeginInteraction()
    {
        PushUndo();
        _propertySnapshotTaken = true;
    }

    /// <summary>Move o elemento (e o grupo dele) para a posição indicada.</summary>
    public void MoveElementTo(LayoutElement element, int x, int y)
    {
        var targets = element.GroupId is not null || _selectedElements.Count > 1
            ? Selection
            : new[] { element };

        var dx = x - element.X;
        var dy = y - element.Y;

        foreach (var target in targets)
        {
            if (target.Locked)
            {
                continue;
            }

            target.X = Math.Clamp(target.X + dx, 0, Math.Max(0, Layout.Width - Math.Min(target.Width, Layout.Width)));
            target.Y = Math.Max(0, target.Y + dy);
        }

        OnPropertyChanged(nameof(SelectionBounds));
    }

    /// <summary>Redimensiona o elemento respeitando os limites do papel.</summary>
    public void ResizeElement(LayoutElement element, int x, int y, int width, int height)
    {
        if (element.Locked)
        {
            return;
        }

        var maxRight = Layout.Width;
        var newX = Math.Clamp(x, 0, Math.Max(0, maxRight - 8));
        var newWidth = Math.Clamp(width, 8, Math.Max(8, maxRight - newX));

        element.X = newX;
        element.Width = newWidth;
        element.Y = Math.Max(0, y);
        element.Height = Math.Clamp(height, 4, 4000);

        OnPropertyChanged(nameof(SelectionBounds));
        OnPropertyChanged(nameof(CanvasHeight));
    }

    /// <summary>Chamado pelo canvas ao terminar o arraste.</summary>
    public void EndInteraction()
    {
        Layout.Elements = Elements.ToList();
        OnPropertyChanged(nameof(SelectionBounds));
        OnPropertyChanged(nameof(CanvasHeight));
        MarkDirty();
        SchedulePreview();
    }

    /// <summary>Aplica a grade ao valor, quando ativa.</summary>
    public int Snap(int value) =>
        SnapToGrid && GridSize > 1
            ? (int)Math.Round((double)value / GridSize) * GridSize
            : value;

    // ---------- camadas ----------

    public void RefreshLayers()
    {
        Layers.Clear();
        foreach (var element in Elements.Reverse())
        {
            Layers.Add(element);
        }

        RefreshLayerFilter();
        OnPropertyChanged(nameof(CanvasHeight));
        OnPropertyChanged(nameof(ContentLeft));
        OnPropertyChanged(nameof(ContentWidth));
    }

    private void ClearSelectionFlags()
    {
        foreach (var element in Elements)
        {
            element.IsSelected = false;
            element.PropertyChanged -= OnSelectedElementChanged;
        }
    }

    public void ToggleVisibility(LayoutElement? element)
    {
        if (element is null)
        {
            return;
        }

        PushUndo();
        element.Visible = !element.Visible;
        MarkDirty();
        RefreshLayers();
        SchedulePreview();
    }

    public void ToggleLockElement(LayoutElement? element)
    {
        if (element is null)
        {
            return;
        }

        PushUndo();
        element.Locked = !element.Locked;
        MarkDirty();
        RefreshLayers();
    }

    public void SelectElement(LayoutElement? element) => SelectedElement = element;

    // ---------- documento ----------

    private void SetMargin(int value, Action<int> setter, string propertyName)
    {
        var clamped = Math.Clamp(value, 0, 120);
        var current = propertyName switch
        {
            nameof(MarginLeft) => _marginLeft,
            nameof(MarginRight) => _marginRight,
            nameof(MarginTop) => _marginTop,
            _ => _marginBottom,
        };

        if (current == clamped)
        {
            return;
        }

        setter(clamped);
        Layout.MarginLeft = _marginLeft;
        Layout.MarginRight = _marginRight;
        Layout.MarginTop = _marginTop;
        Layout.MarginBottom = _marginBottom;

        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(ContentLeft));
        OnPropertyChanged(nameof(ContentWidth));
        MarkDirty();
        SchedulePreview();
    }

    private void ClampElements()
    {
        foreach (var element in Elements)
        {
            if (element.X + element.Width > Layout.Width)
            {
                element.X = Math.Max(0, Layout.Width - element.Width);
            }

            if (element.X < 0)
            {
                element.X = 0;
            }
        }
    }

    private void UpdateAutoHeight(LayoutElement element)
    {
        if (!element.AutoHeight || !element.ShowsText)
        {
            return;
        }

        var measured = VisualTemplateRenderer.MeasureTextHeight(element, BuildWorkingCopy());
        element.Height = Math.Max(6, measured);
        OnPropertyChanged(nameof(CanvasHeight));
    }

    /// <summary>Recalcula a altura de todos os textos com altura automática.</summary>
    public void RecalculateAutoHeights()
    {
        var template = BuildWorkingCopy();
        foreach (var element in Elements.Where(e => e.AutoHeight && e.ShowsText))
        {
            element.Height = Math.Max(6, VisualTemplateRenderer.MeasureTextHeight(element, template));
        }

        OnPropertyChanged(nameof(CanvasHeight));
    }

    private void MarkDirty()
    {
        IsDirty = true;
        OnPropertyChanged(nameof(CanvasHeight));
        OnDocumentDirty();
    }

    // ---------- campos ----------

    public void RefreshFieldChoices()
    {
        FieldChoices.Clear();
        foreach (var field in _template.Fields)
        {
            FieldChoices.Add(field);
        }

        OnPropertyChanged(nameof(FieldChoices));
        OnPropertyChanged(nameof(FieldCountText));
        OnPropertyChanged(nameof(UnusedFieldCount));
        OnPropertyChanged(nameof(HasUnusedFields));
        OnPropertyChanged(nameof(UnusedFieldsText));
    }

    /// <summary>Campo dinâmico do elemento selecionado (nulo quando não é um campo).</summary>
    public TemplateField? SelectedField =>
        _selectedElement is { Kind: LayoutElementKind.Campo } element
        && !string.IsNullOrWhiteSpace(element.FieldKey)
            ? _template.FindField(element.FieldKey)
            : null;

    public bool HasSelectedField => SelectedField is not null;

    /// <summary>Tipos de dado disponíveis para um campo dinâmico.</summary>
    public IReadOnlyList<TemplateFieldTypeChoice> FieldTypes => TemplateFieldTypeChoice.All;

    /// <summary>Rótulo do campo: é o nome mostrado no formulário de preenchimento.</summary>
    public string SelectedFieldLabel
    {
        get => SelectedField?.Label ?? string.Empty;
        set
        {
            var field = SelectedField;
            if (field is null || string.Equals(field.Label, value, StringComparison.Ordinal))
            {
                return;
            }

            EnsurePropertyUndo();
            field.Label = value ?? string.Empty;
            ApplyFieldLabel(field);
            OnPropertyChanged();
            MarkDirty();
            SchedulePreview();
        }
    }

    public TemplateFieldType SelectedFieldType
    {
        get => SelectedField?.Type ?? TemplateFieldType.TextoCurto;
        set
        {
            var field = SelectedField;
            if (field is null || field.Type == value)
            {
                return;
            }

            EnsurePropertyUndo();
            field.Type = value;
            RaiseFieldProperties();
            MarkDirty();
            SchedulePreview();
        }
    }

    public bool SelectedFieldRequired
    {
        get => SelectedField?.Required == true;
        set
        {
            var field = SelectedField;
            if (field is null || field.Required == value)
            {
                return;
            }

            EnsurePropertyUndo();
            field.Required = value;
            RaiseFieldProperties();
            MarkDirty();
        }
    }

    private void RaiseFieldProperties()
    {
        OnPropertyChanged(nameof(SelectedField));
        OnPropertyChanged(nameof(HasSelectedField));
        OnPropertyChanged(nameof(SelectedFieldLabel));
        OnPropertyChanged(nameof(SelectedFieldType));
        OnPropertyChanged(nameof(SelectedFieldRequired));
    }

    /// <summary>
    /// Leva o rótulo do campo para todos os elementos que o exibem: o nome da
    /// camada, o texto no canvas e o formulário de preenchimento são a mesma coisa.
    /// </summary>
    private void ApplyFieldLabel(TemplateField field)
    {
        // Sem Trim(): o rótulo é reaplicado a cada tecla digitada e aparar o
        // texto aqui faria o cursor do campo pular enquanto o usuário digita.
        var label = string.IsNullOrWhiteSpace(field.Label) ? field.Key : field.Label;

        foreach (var element in Elements)
        {
            if (!string.Equals(element.FieldKey, field.Key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(element.Text, label, StringComparison.Ordinal))
            {
                element.Text = label;
            }

            if (!string.Equals(element.Name, label, StringComparison.Ordinal))
            {
                element.Name = label;
            }
        }
    }

    /// <summary>Renomear a camada (ou o texto do campo) renomeia o campo do modelo.</summary>
    private void SyncFieldLabelFromElement(LayoutElement element, string? label)
    {
        if (string.IsNullOrWhiteSpace(element.FieldKey))
        {
            return;
        }

        var field = _template.FindField(element.FieldKey);
        if (field is null)
        {
            return;
        }

        var candidate = label ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate) || string.Equals(field.Label, candidate, StringComparison.Ordinal))
        {
            return;
        }

        field.Label = candidate;
        ApplyFieldLabel(field);
        RaiseFieldProperties();
        SchedulePreview();
    }

    /// <summary>
    /// Modelos criados antes desta correção continuaram com "Novo campo" no
    /// formulário mesmo depois de renomear a camada: adota o texto do elemento.
    /// </summary>
    private void NormalizeFieldLabels()
    {
        foreach (var field in _template.Fields)
        {
            if (!string.IsNullOrWhiteSpace(field.Label) &&
                !string.Equals(field.Label, DefaultFieldLabel, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var label = Elements
                .Where(e => e.IsField
                            && string.Equals(e.FieldKey, field.Key, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(e.Text))
                .Select(e => e.Text.Trim())
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(label))
            {
                field.Label = label;
                ApplyFieldLabel(field);
            }
        }
    }

    // ---------- pré-visualização ----------

    private void SchedulePreview(bool immediate = false)
    {
        _previewTimer.Stop();
        if (immediate)
        {
            _ = RenderPreviewAsync();
        }
        else
        {
            _previewTimer.Start();
        }

        UpdateValidation();
    }

    private DocumentTemplate BuildWorkingCopy()
    {
        var copy = _template.Clone();
        copy.Name = Name;
        copy.Description = Description;
        copy.PaperWidth = PaperWidth;
        copy.Layout = Layout;
        copy.Layout.Elements = Elements.ToList();
        return copy;
    }

    private Dictionary<string, string> BuildDemoValues()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in _template.Fields)
        {
            var value = !string.IsNullOrWhiteSpace(field.DefaultValue)
                ? field.DefaultValue
                : field.Type switch
                {
                    TemplateFieldType.Lista when field.Options.Count > 0 => field.Options[0],
                    TemplateFieldType.Data => DateTime.Now.ToString("dd/MM/yyyy"),
                    TemplateFieldType.Monetario => "150,00",
                    _ => $"«{field.Label}»",
                };

            values[field.Key] = value;
        }

        return values;
    }

    private async Task RenderPreviewAsync()
    {
        var generation = ++_renderGeneration;
        IsRendering = true;

        try
        {
            var template = BuildWorkingCopy();
            var values = BuildDemoValues();

            var bitmap = await Task.Run(() => VisualTemplateRenderer.Render(template, values));
            if (generation != _renderGeneration)
            {
                return;
            }

            PreviewImage = PreviewRenderer.ToBitmapSource(bitmap);
            PreviewInfo = $"{bitmap.Width} × {bitmap.Height} pontos " +
                          $"({bitmap.Height * 0.125:0.0} mm de papel aprox.)";
        }
        catch (Exception ex)
        {
            if (generation == _renderGeneration)
            {
                PreviewImage = null;
                PreviewInfo = $"Não foi possível gerar a pré-visualização: {ex.Message}";
            }
        }
        finally
        {
            if (generation == _renderGeneration)
            {
                IsRendering = false;
            }
        }
    }

    private void UpdateValidation()
    {
        var template = BuildWorkingCopy();
        var result = TemplateValidator.ValidateStructure(template);

        HasErrors = !result.IsValid;
        ValidationText = result.Errors.Count > 0
            ? string.Join(Environment.NewLine, result.Errors.Select(e => "• " + e))
            : result.Warnings.Count > 0
                ? string.Join(Environment.NewLine, result.Warnings.Take(4).Select(w => "• " + w))
                : "Documento válido.";

        OnPropertyChanged(nameof(ValidationSummary));
    }

    // ---------- salvar / imprimir ----------

    private void Save()
    {
        RecalculateAutoHeights();

        var template = BuildWorkingCopy();
        foreach (var field in template.Fields)
        {
            field.Label = field.Label.Trim();
        }

        var result = TemplateValidator.ValidateStructure(template);

        if (!result.IsValid)
        {
            MiniPrint.Desktop.Windows.MessageDialog.Show(
                "Corrija os pontos abaixo antes de salvar:" + Environment.NewLine + Environment.NewLine +
                string.Join(Environment.NewLine, result.Errors.Select(e => "• " + e)),
                "MiniPrint Desktop",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        template.Name = template.Name.Trim();
        template.PaperWidth = PaperWidth;
        template.Layout!.AutoHeight = AutoHeight;
        template.Layout.Height = AutoHeight ? 0 : DocumentHeight;

        IsDirty = false;
        OnDocumentSaved();
        _host.SaveTemplate(template, _isNew);
    }

    private void Cancel()
    {
        if (IsDirty)
        {
            var confirm = MiniPrint.Desktop.Windows.MessageDialog.Show(
                "Descartar as alterações não salvas?",
                "MiniPrint Desktop",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }
        }

        _host.ShowTemplateList();
    }

    private async Task PrintAsync()
    {
        if (!_host.Services.Printer.IsConnected)
        {
            MiniPrint.Desktop.Windows.MessageDialog.Show(
                "Conecte a impressora antes de imprimir.",
                "MiniPrint Desktop",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            RecalculateAutoHeights();
            var template = BuildWorkingCopy();
            var values = BuildDemoValues();
            var bitmap = await Task.Run(() => VisualTemplateRenderer.Render(template, values));

            var document = new PrintDocument
            {
                Bitmap = bitmap,
                Kind = PrintJobKind.Modelo,
                Title = template.Name,
            };

            var result = await _host.Services.Printer.PrintAsync(
                document, _host.Services.Settings.Printer);

            _host.Services.JobRecorded?.Invoke(new PrintHistoryEntry
            {
                Title = document.Title,
                Kind = PrintJobKind.Modelo,
                Status = result.Status,
                ErrorMessage = result.ErrorMessage,
                WasSimulated = result.WasSimulated,
                DeviceName = _host.Services.Printer.DeviceName,
                LinesPrinted = result.LinesPrinted,
                BytesSent = result.BytesSent,
                DurationSeconds = result.Duration.TotalSeconds,
            });

            StatusMessage = result.Success
                ? "Documento de teste impresso (valores de exemplo)."
                : $"Falha na impressão: {result.ErrorMessage}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao imprimir: {ex.Message}";
        }
    }

    private void HandleError(Exception ex)
    {
        StatusMessage = ex.Message;
        _host.Services.Log.Error("Editor visual", ex.ToString());
    }
}
