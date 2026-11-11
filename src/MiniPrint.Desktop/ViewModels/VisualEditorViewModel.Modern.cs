using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Threading;
using MiniPrint.Core.Configuration;
using MiniPrint.Core.Templates;
using MiniPrint.Desktop.Editing;

namespace MiniPrint.Desktop.ViewModels;

/// <summary>
/// Parte "moderna" do editor visual: estado dos painéis, encaixe inteligente,
/// seleção múltipla, paleta, campos, produtividade, salvamento automático e
/// pré-visualização. Fica separada para o arquivo principal continuar legível.
/// </summary>
public sealed partial class VisualEditorViewModel
{
    private const int UndoLimit = 120;

    private static readonly TimeSpan MergeWindow = TimeSpan.FromMilliseconds(900);

    private string? _lastMergeKey;
    private DateTime _lastMergeAt = DateTime.MinValue;

    private EditorSettings _editor = new();
    private readonly ObservableCollection<SnapGuide> _guides = new();
    private readonly ObservableCollection<PaletteGroup> _paletteGroups = new();
    private string _paletteSearch = string.Empty;
    private bool _isPickingStyle;
    private ElementStyle? _styleClipboard;
    private bool _isInlineEditing;
    private string _inlineEditText = string.Empty;
    private string _saveStatus = "Salvo";
    private DateTimeOffset? _lastSavedAt;
    private DateTimeOffset? _lastAutoSaveAt;
    private string _findText = string.Empty;
    private string _replaceText = string.Empty;
    private bool _showWelcome;
    private DispatcherTimer? _autoSaveTimer;
    private DispatcherTimer? _panelSaveTimer;
    private bool _suppressSettingsSave;

    public ObservableCollection<PaletteGroup> PaletteGroups => _paletteGroups;

    public ObservableCollection<SnapGuide> Guides => _guides;

    public EditorSettings EditorView => _editor;

    // ---------------------------------------------------------------- painéis

    public double LeftPanelWidth
    {
        get => _editor.LeftPanelWidth;
        set
        {
            var clamped = Math.Clamp(value, 180, 420);
            if (Math.Abs(clamped - _editor.LeftPanelWidth) < 0.5)
            {
                return;
            }

            _editor.LeftPanelWidth = clamped;
            OnPropertyChanged();
            ScheduleSettingsSave();
        }
    }

    public double RightPanelWidth
    {
        get => _editor.RightPanelWidth;
        set
        {
            var clamped = Math.Clamp(value, 260, 520);
            if (Math.Abs(clamped - _editor.RightPanelWidth) < 0.5)
            {
                return;
            }

            _editor.RightPanelWidth = clamped;
            OnPropertyChanged();
            ScheduleSettingsSave();
        }
    }

    public bool IsLeftPanelCollapsed
    {
        get => _editor.LeftPanelCollapsed;
        set
        {
            if (_editor.LeftPanelCollapsed == value)
            {
                return;
            }

            _editor.LeftPanelCollapsed = value;
            OnLeftPanelCollapsedChanged();
        }
    }

    public bool IsRightPanelCollapsed
    {
        get => _editor.RightPanelCollapsed;
        set
        {
            if (_editor.RightPanelCollapsed == value)
            {
                return;
            }

            _editor.RightPanelCollapsed = value;
            OnRightPanelCollapsedChanged();
        }
    }

    private void OnLeftPanelCollapsedChanged()
    {
        OnPropertyChanged(nameof(IsLeftPanelCollapsed));
        OnPropertyChanged(nameof(LeftPanelWidth));
        OnPropertyChanged(nameof(ShowLeftPanel));
        OnPropertyChanged(nameof(LeftPanelToggleGlyph));
        OnPropertyChanged(nameof(LeftPanelToggleHint));
        ScheduleSettingsSave();
    }

    private void OnRightPanelCollapsedChanged()
    {
        OnPropertyChanged(nameof(IsRightPanelCollapsed));
        OnPropertyChanged(nameof(RightPanelWidth));
        OnPropertyChanged(nameof(ShowRightPanel));
        OnPropertyChanged(nameof(RightPanelToggleGlyph));
        OnPropertyChanged(nameof(RightPanelToggleHint));
        ScheduleSettingsSave();
    }

    public bool ShowLeftPanel => !_editor.LeftPanelCollapsed;

    public bool ShowRightPanel => !_editor.RightPanelCollapsed;

    public string LeftPanelToggleGlyph => _editor.LeftPanelCollapsed ? "\uE76C" : "\uE76B";

    public string RightPanelToggleGlyph => _editor.RightPanelCollapsed ? "\uE76B" : "\uE76C";

    public string LeftPanelToggleHint =>
        _editor.LeftPanelCollapsed ? "Mostrar painel de elementos" : "Recolher painel de elementos";

    public string RightPanelToggleHint =>
        _editor.RightPanelCollapsed ? "Mostrar painel de propriedades" : "Recolher painel de propriedades";

    public RelayCommand ToggleLeftPanelCommand { get; private set; } = null!;

    public RelayCommand ToggleRightPanelCommand { get; private set; } = null!;

    public RelayCommand ResetLayoutCommand { get; private set; } = null!;

    public void ToggleLeftPanel() => IsLeftPanelCollapsed = !IsLeftPanelCollapsed;

    public void ToggleRightPanel() => IsRightPanelCollapsed = !IsRightPanelCollapsed;

    public void ResetPanels()
    {
        _editor.LeftPanelWidth = 236;
        _editor.RightPanelWidth = 328;
        _editor.LeftPanelCollapsed = false;
        _editor.RightPanelCollapsed = false;
        OnLeftPanelCollapsedChanged();
        OnRightPanelCollapsedChanged();
        OnPropertyChanged(nameof(LeftPanelWidth));
        OnPropertyChanged(nameof(RightPanelWidth));
        SaveSettingsNow();
    }

    private void ScheduleSettingsSave()
    {
        if (_suppressSettingsSave)
        {
            return;
        }

        _panelSaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _panelSaveTimer.Stop();

        if (_panelSaveTimer.Tag is not null)
        {
            _panelSaveTimer.Tick -= OnPanelSaveTick;
        }

        _panelSaveTimer.Tag = this;
        _panelSaveTimer.Tick += OnPanelSaveTick;
        _panelSaveTimer.Start();
    }

    private void OnPanelSaveTick(object? sender, EventArgs e)
    {
        _panelSaveTimer?.Stop();
        SaveSettingsNow();
    }

    private void SaveSettingsNow()
    {
        if (_suppressSettingsSave || _host.Services is null)
        {
            return;
        }

        _host.Services.Settings.Editor = _editor;
        _host.Services.SaveSettings();
    }

    // ------------------------------------------------------------ visualização

    public bool ShowMargins
    {
        get => _editor.ShowMargins;
        set
        {
            if (_editor.ShowMargins == value)
            {
                return;
            }

            _editor.ShowMargins = value;
            OnPropertyChanged();
            ScheduleSettingsSave();
        }
    }

    public bool ShowRulers
    {
        get => _editor.ShowRulers;
        set
        {
            if (_editor.ShowRulers == value)
            {
                return;
            }

            _editor.ShowRulers = value;
            OnPropertyChanged();
            ScheduleSettingsSave();
        }
    }

    public bool ShowGuides
    {
        get => _editor.ShowGuides;
        set
        {
            if (_editor.ShowGuides == value)
            {
                return;
            }

            _editor.ShowGuides = value;
            OnPropertyChanged();
            if (!value)
            {
                ClearGuides();
            }

            ScheduleSettingsSave();
        }
    }

    public bool SnapToObjects
    {
        get => _editor.SnapToObjects;
        set
        {
            if (_editor.SnapToObjects == value)
            {
                return;
            }

            _editor.SnapToObjects = value;
            OnPropertyChanged();
            ScheduleSettingsSave();
        }
    }

    public int SnapTolerance
    {
        get => _editor.SnapTolerance;
        set
        {
            var clamped = Math.Clamp(value, 0, 20);
            if (_editor.SnapTolerance == clamped)
            {
                return;
            }

            _editor.SnapTolerance = clamped;
            OnPropertyChanged();
            ScheduleSettingsSave();
        }
    }

    public string ZoomPercent => $"{Math.Round(Zoom * 100):0}%";

    public RelayCommand ZoomToFitCommand { get; private set; } = null!;

    public RelayCommand Zoom100Command { get; private set; } = null!;

    public RelayCommand ToggleMarginsCommand { get; private set; } = null!;

    public RelayCommand ToggleGuidesCommand { get; private set; } = null!;

    public RelayCommand ToggleRulersCommand { get; private set; } = null!;

    public RelayCommand ToggleGridCommand { get; private set; } = null!;

    public RelayCommand ToggleSnapCommand { get; private set; } = null!;

    /// <summary>Ajusta o zoom à largura disponível (chamado pela view).</summary>
    public void FitCanvasToWidth(double availableWidth)
    {
        if (availableWidth <= 40)
        {
            return;
        }

        Zoom = Math.Clamp((availableWidth - 56) / Math.Max(1, PaperWidth), 0.4, 3.0);
    }

    public void SetZoom(double value) => Zoom = value;

    public void RefreshZoomText()
    {
        OnPropertyChanged(nameof(ZoomPercent));
        OnPropertyChanged(nameof(ZoomText));
    }

    public void SetGuides(IEnumerable<SnapGuide>? guides)
    {
        _guides.Clear();
        if (guides is null || !ShowGuides)
        {
            return;
        }

        foreach (var guide in guides)
        {
            _guides.Add(guide);
        }
    }

    public void ClearGuides() => _guides.Clear();

    public bool HasGuides => _guides.Count > 0;

    // -------------------------------------------------------------- seleção

    public IReadOnlyList<LayoutElement> SelectedElements =>
        _selectedElements.Count > 0
            ? _selectedElements
            : _selectedElement is { } single
                ? new[] { single }
                : Array.Empty<LayoutElement>();

    public bool HasSingleSelection => SelectedElements.Count == 1;

    public bool HasMultipleSelection => SelectedElements.Count > 1;

    public int SelectionCountValue => SelectedElements.Count;

    public string SelectionCountText => SelectionCountValue switch
    {
        0 => "Nenhum elemento selecionado",
        1 => "1 elemento selecionado",
        _ => $"{SelectionCountValue} elementos selecionados",
    };

    /// <summary>
    /// Seleciona com os modificadores do padrão de editores gráficos:
    /// Shift acrescenta, Ctrl alterna.
    /// </summary>
    public void SelectElement(LayoutElement? element, bool additive = false, bool toggle = false)
    {
        if (element is null)
        {
            SelectedElement = null;
            return;
        }

        if (!additive && !toggle)
        {
            SelectedElement = element;
            return;
        }

        var current = SelectedElements.ToList();
        if (toggle)
        {
            if (!current.Remove(element))
            {
                current.Add(element);
            }
        }
        else if (!current.Contains(element))
        {
            current.Add(element);
        }

        if (current.Count == 0)
        {
            SelectedElement = null;
            return;
        }

        SelectMany(current);
    }

    /// <summary>Seleciona tudo o que intersecta a área arrastada no canvas.</summary>
    public void SelectInRect(double x, double y, double width, double height)
    {
        var hit = Elements
            .Where(e => e.Visible
                        && e.X < x + width
                        && e.Right > x
                        && e.Y < y + height
                        && e.Bottom > y)
            .ToList();

        if (hit.Count == 0)
        {
            SelectedElement = null;
            StatusMessage = "Nenhum elemento na área selecionada.";
            return;
        }

        SelectMany(hit);
        StatusMessage = $"{hit.Count} elemento(s) selecionado(s).";
    }

    public void ClearSelection() => SelectedElement = null;

    private void RaiseSelectionSummary()
    {
        OnPropertyChanged(nameof(SelectedElements));
        OnPropertyChanged(nameof(SelectionCountValue));
        OnPropertyChanged(nameof(SelectionCountText));
        OnPropertyChanged(nameof(HasSingleSelection));
        OnPropertyChanged(nameof(HasMultipleSelection));
    }

    // ---------------------------------------------------------------- paleta

    public string PaletteSearch
    {
        get => _paletteSearch;
        set
        {
            if (SetProperty(ref _paletteSearch, value))
            {
                RebuildPalette();
            }
        }
    }

    public RelayCommand InsertPaletteItemCommand { get; private set; } = null!;

    private void BuildPalette()
    {
        RebuildPalette();
    }

    private void RebuildPalette()
    {
        var search = (_paletteSearch ?? string.Empty).Trim();
        _paletteGroups.Clear();

        foreach (var definition in PaletteCatalog.Groups)
        {
            var items = definition.Items
                .Where(item => search.Length == 0 || item.Matches(search))
                .ToList();

            if (items.Count == 0)
            {
                continue;
            }

            _paletteGroups.Add(new PaletteGroup(definition.Name, definition.Glyph, items, isExpanded: true));
        }

        OnPropertyChanged(nameof(PaletteGroups));
        OnPropertyChanged(nameof(HasPaletteResults));
    }

    public bool HasPaletteResults => _paletteGroups.Count > 0;

    public RelayCommand ClearPaletteSearchCommand { get; private set; } = null!;

    /// <summary>Insere uma ferramenta da paleta (clique ou arrastar-e-soltar).</summary>
    public void InsertTool(string toolId, int? x = null, int? y = null)
    {
        if (string.IsNullOrWhiteSpace(toolId))
        {
            return;
        }

        if (toolId.StartsWith(PaletteCatalog.FieldPrefix, StringComparison.Ordinal))
        {
            var key = toolId[PaletteCatalog.FieldPrefix.Length..];
            var field = _template.FindField(key);
            if (field is not null)
            {
                AddFieldElementAt(field, x, y, pushUndo: false);
            }

            return;
        }

        var id = toolId;
        var selection = SelectedElement;
        var insertX = x ?? (selection?.X ?? Layout.MarginLeft);
        var insertY = y ?? (selection is null ? NextFreeY() : selection.Bottom + 6);

        if (insertY > Layout.EffectiveHeight - 8)
        {
            insertY = Math.Max(Layout.MarginTop, Layout.EffectiveHeight - 8);
        }

        switch (id)
        {
            case "text":
                AddAt(CreateText("Texto", "Digite aqui", 11), insertX, insertY);
                break;
            case "title":
                AddAt(CreateText("Título", "TÍTULO", 15, bold: true, TextAlignment.Centro), insertX, insertY);
                break;
            case "subtitle":
                AddAt(CreateText("Subtítulo", "Subtítulo", 12, bold: true), insertX, insertY);
                break;
            case "paragraph":
                AddAt(CreateParagraph(), insertX, insertY);
                break;
            case "dynamicText":
                BeginInlineInsert("Texto com campo: use {{chave}} no conteúdo.", insertX, insertY);
                break;
            case "line":
                AddAt(CreateLine(), insertX, insertY);
                break;
            case "rect":
                AddAt(CreateRectangle(), insertX, insertY);
                break;
            case "rectFilled":
                var filled = CreateRectangle();
                filled.Filled = true;
                AddAt(filled, insertX, insertY);
                break;
            case "spacer":
                AddAt(CreateSpacer(), insertX, insertY);
                break;
            case "image":
                ChooseImage();
                break;
            case "field-new":
                RequestFieldEditor(null);
                break;
            case "field-text":
                QuickAddField("Texto", TemplateFieldType.TextoCurto, x, y);
                break;
            case "field-money":
                QuickAddField("Valor", TemplateFieldType.Monetario, x, y);
                break;
            case "field-date":
                QuickAddField("Data", TemplateFieldType.Data, x, y);
                break;
            case "field-doc":
                QuickAddField("CPF/CNPJ", TemplateFieldType.Cpf, x, y);
                break;
            case "field-phone":
                QuickAddField("Telefone", TemplateFieldType.Telefone, x, y);
                break;
            case "field-list":
                QuickAddField("Opção", TemplateFieldType.Lista, x, y);
                break;
            case "header":
                AddHeader();
                break;
            case "footer":
                AddFooter();
                break;
            case "signature":
                AddSignatureBlock();
                break;
            case "info":
                AddInfoBlock();
                break;
            case "identification":
                AddIdentificationBlock();
                break;
            default:
                StatusMessage = $"Ferramenta desconhecida: {id}";
                break;
        }
    }

    private LayoutElement CreateSpacer() =>
        new()
        {
            Kind = LayoutElementKind.Texto,
            Name = "Espaçador",
            X = Layout.MarginLeft,
            Y = NextFreeY(),
            Width = Layout.ContentWidth,
            Height = 16,
            Text = " ",
            FontSize = 8,
            AutoHeight = false,
        };

    /// <summary>Adiciona um elemento controlando se a operação entra no histórico.</summary>
    private void AddElement(LayoutElement element, bool pushUndo)
    {
        if (pushUndo)
        {
            PushUndo();
        }

        Elements.Add(element);
        Layout.Elements = Elements.ToList();
        SelectedElement = element;
        MarkDirty();
        RefreshLayers();
        SchedulePreview();
        StatusMessage = $"{element.KindText} adicionado.";
    }
    private void AddAt(LayoutElement element, int x, int y)
    {
        element.X = Math.Clamp(x, 0, Math.Max(0, Layout.Width - 24));
        element.Y = Math.Max(0, y);
        AddElement(element);
    }

    private void BeginInlineInsert(string text, int x, int y)
    {
        var element = CreateText("Texto", text, 11);
        element.X = Math.Clamp(x, 0, Math.Max(0, Layout.Width - 24));
        element.Y = Math.Max(0, y);
        AddElement(element);
    }

    // -------------------------------------------------------------- campos

    public RelayCommand NewFieldDialogCommand { get; private set; } = null!;

    public RelayCommand EditFieldDialogCommand { get; private set; } = null!;

    public RelayCommand DeleteFieldCommand { get; private set; } = null!;

    public RelayCommand InsertFieldAtEndCommand { get; private set; } = null!;

    /// <summary>Pedido de abertura do diálogo de campo (a view escuta e abre).</summary>
    public event EventHandler<TemplateField?>? FieldEditorRequested;

    /// <summary>Pedido de abertura da busca/substituição.</summary>
    public event EventHandler? FindReplaceRequested;

    /// <summary>Pedido de confirmação (para operações destrutivas).</summary>
    public Func<string, string, bool>? Confirm { get; set; }

    public string FieldCountText => $"{_template.Fields.Count} campo(s)";

    public int CountFieldUsage(TemplateField field) =>
        Elements.Count(e =>
            e.IsField && string.Equals(e.FieldKey, field.Key, StringComparison.OrdinalIgnoreCase));

    public TemplateField CreateField(
        string label,
        TemplateFieldType type,
        bool required,
        string placeholder,
        string defaultValue,
        string? existingKey = null)
    {
        var cleanLabel = NextFreeFieldLabel(string.IsNullOrWhiteSpace(label) ? "Campo" : label.Trim(), existingKey);
        var key = string.IsNullOrWhiteSpace(existingKey)
            ? TemplateFieldFormatter.SuggestKey(cleanLabel)
            : existingKey!;

        var suffix = 2;
        var baseKey = key;
        while (_template.Fields.Any(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)))
        {
            key = $"{baseKey}_{suffix++}";
        }

        var field = new TemplateField
        {
            Key = key,
            Label = cleanLabel,
            Type = type,
            Required = required,
            Placeholder = placeholder?.Trim() ?? string.Empty,
            DefaultValue = defaultValue?.Trim() ?? string.Empty,
        };

        PushUndo("field-create");
        _template.Fields.Add(field);
        RefreshFieldChoices();
        OnPropertyChanged(nameof(FieldCountText));
        MarkDirty();
        SchedulePreview();
        return field;
    }

    /// <summary>
    /// Devolve um rótulo livre para o campo ("CPF/CNPJ", "CPF/CNPJ 2"...).
    /// Ao editar (<paramref name="existingKey"/> preenchido) o rótulo é mantido.
    /// </summary>
    private string NextFreeFieldLabel(string label, string? existingKey)
    {
        if (!string.IsNullOrWhiteSpace(existingKey))
        {
            return label;
        }

        if (!_template.Fields.Any(f => string.Equals(f.Label, label, StringComparison.OrdinalIgnoreCase)))
        {
            return label;
        }

        var index = 2;
        while (_template.Fields.Any(f =>
                   string.Equals(f.Label, $"{label} {index}", StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }

        return $"{label} {index}";
    }
    public void UpdateField(
        TemplateField field,
        string label,
        TemplateFieldType type,
        bool required,
        string placeholder,
        string defaultValue)
    {
        PushUndo($"field:{field.Key}");
        field.Label = string.IsNullOrWhiteSpace(label) ? field.Key : label.Trim();
        field.Type = type;
        field.Required = required;
        field.Placeholder = placeholder?.Trim() ?? string.Empty;
        field.DefaultValue = defaultValue?.Trim() ?? string.Empty;

        ApplyFieldLabel(field);
        RefreshFieldChoices();
        RaiseFieldProperties();
        OnPropertyChanged(nameof(FieldCountText));
        MarkDirty();
        SchedulePreview();
    }

    /// <summary>Quantos elementos usam o campo (para confirmar a exclusão).</summary>
    public int DeleteField(TemplateField field, bool alsoDeleteElements)
    {
        var usage = Elements.Where(e =>
            e.IsField && string.Equals(e.FieldKey, field.Key, StringComparison.OrdinalIgnoreCase)).ToList();

        PushUndo($"field-del:{field.Key}");
        foreach (var element in usage)
        {
            if (alsoDeleteElements)
            {
                Elements.Remove(element);
            }
            else
            {
                element.FieldKey = null;
                element.Kind = LayoutElementKind.Texto;
            }
        }

        _template.Fields.Remove(field);
        Layout.Elements = Elements.ToList();
        RefreshFieldChoices();
        RefreshLayers();
        OnPropertyChanged(nameof(FieldCountText));
        RaiseFieldProperties();
        MarkDirty();
        SchedulePreview();
        return usage.Count;
    }

    public void AddFieldElementAt(TemplateField field, int? x = null, int? y = null, bool pushUndo = true)
    {
        var element = new LayoutElement
        {
            Kind = LayoutElementKind.Campo,
            Name = string.IsNullOrWhiteSpace(field.Label) ? field.Key : field.Label,
            X = Math.Clamp(x ?? Layout.MarginLeft, 0, Math.Max(0, Layout.Width - 24)),
            Y = Math.Max(0, y ?? NextFreeY()),
            Width = Layout.ContentWidth,
            Height = (int)Math.Ceiling(11 * 1.8),
            Text = string.IsNullOrWhiteSpace(field.Label) ? field.Key : field.Label,
            FieldKey = field.Key,
            FontSize = 11,
            AutoHeight = true,
        };

        AddElement(element, pushUndo);
        StatusMessage = $"Campo \"{field.Label}\" inserido.";
    }

    private void RequestFieldEditor(TemplateField? field) =>
        FieldEditorRequested?.Invoke(this, field);

    // -------------------------------------------------------------- produtos

    public RelayCommand CopyStyleCommand { get; private set; } = null!;

    public RelayCommand PasteStyleCommand { get; private set; } = null!;

    public RelayCommand FindReplaceCommand { get; private set; } = null!;

    public RelayCommand DistributeHorizontalEvenCommand { get; private set; } = null!;

    public RelayCommand DistributeVerticalEvenCommand { get; private set; } = null!;

    public bool HasCopiedStyle => _styleClipboard is not null;

    public bool IsPickingStyle
    {
        get => _isPickingStyle;
        private set
        {
            if (SetProperty(ref _isPickingStyle, value))
            {
                OnPropertyChanged(nameof(StyleHint));
            }
        }
    }

    public string StyleHint => _isPickingStyle
        ? "Clique em um elemento para copiar o estilo"
        : HasCopiedStyle
            ? "Estilo copiado: selecione elementos e clique em \"Aplicar estilo\""
            : "Selecione um elemento e clique em \"Copiar estilo\"";

    public void CopyStyle()
    {
        if (SelectedElement is not { } element)
        {
            StatusMessage = "Selecione um elemento para copiar o estilo.";
            return;
        }

        _styleClipboard = ElementStyle.From(element);
        IsPickingStyle = false;
        OnPropertyChanged(nameof(HasCopiedStyle));
        OnPropertyChanged(nameof(StyleHint));
        StatusMessage = "Estilo copiado. Selecione outros elementos e aplique.";
    }

    public void ApplyStyle()
    {
        if (_styleClipboard is null)
        {
            StatusMessage = "Copie o estilo de um elemento primeiro.";
            return;
        }

        var targets = SelectedElements;
        if (targets.Count == 0)
        {
            StatusMessage = "Selecione um ou mais elementos para aplicar o estilo.";
            return;
        }

        PushUndo("style");
        foreach (var element in targets)
        {
            _styleClipboard.ApplyTo(element);
        }

        MarkDirty();
        SchedulePreview();
        StatusMessage = $"Estilo aplicado a {targets.Count} elemento(s).";
    }

    public void StartStylePick() => CopyStyle();

    /// <summary>Substitui textos fixos do documento (não mexe em chaves de campos).</summary>
    public int ReplaceInDocument(string find, string replacement, bool matchCase, bool replaceAll, out int affected)
    {
        affected = 0;
        if (string.IsNullOrEmpty(find))
        {
            return 0;
        }

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var matches = 0;
        var changed = false;

        foreach (var element in Elements.Where(e => e.Kind == LayoutElementKind.Texto && !string.IsNullOrEmpty(e.Text)))
        {
            var text = element.Text;
            var index = text.IndexOf(find, comparison);
            if (index < 0)
            {
                continue;
            }

            if (replaceAll)
            {
                var count = 0;
                var start = 0;
                while (start <= text.Length)
                {
                    var at = text.IndexOf(find, start, comparison);
                    if (at < 0)
                    {
                        break;
                    }

                    count++;
                    start = at + find.Length;
                    if (count > 500)
                    {
                        break;
                    }
                }

                matches += count;
                affected++;
                element.Text = ReplaceAllSafe(text, find, replacement, comparison);
                changed = true;
            }
            else
            {
                matches++;
                affected++;
                element.Text = string.Concat(text.AsSpan(0, index), replacement, text.AsSpan(index + find.Length));
                changed = true;
                break;
            }
        }

        if (changed)
        {
            PushUndo("replace");
            MarkDirty();
            SchedulePreview();
            SelectedText = SelectedElement?.Text ?? string.Empty;
        }

        return matches;
    }

    private static string ReplaceAllSafe(string text, string find, string replacement, StringComparison comparison)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        var start = 0;
        while (true)
        {
            var at = text.IndexOf(find, start, comparison);
            if (at < 0)
            {
                builder.Append(text, start, text.Length - start);
                break;
            }

            builder.Append(text, start, at - start);
            builder.Append(replacement);
            start = at + find.Length;
        }

        return builder.ToString();
    }

    public int CountMatches(string find, bool matchCase)
    {
        if (string.IsNullOrEmpty(find))
        {
            return 0;
        }

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return Elements
            .Where(e => e.Kind == LayoutElementKind.Texto && !string.IsNullOrEmpty(e.Text))
            .Sum(e => CountOccurrences(e.Text, find, comparison));
    }

    private static int CountOccurrences(string text, string find, StringComparison comparison)
    {
        var count = 0;
        var start = 0;
        while (true)
        {
            var at = text.IndexOf(find, start, comparison);
            if (at < 0)
            {
                break;
            }

            count++;
            start = at + find.Length;
        }

        return count;
    }

    /// <summary>Distribui os elementos selecionados com espaçamento uniforme.</summary>
    public void DistributeEvenly(bool vertical)
    {
        var items = Selection.Where(e => !e.Locked).OrderBy(e => vertical ? e.Y : e.X).ToList();
        if (items.Count < 3)
        {
            StatusMessage = "Selecione ao menos três elementos para distribuir.";
            return;
        }

        PushUndo(vertical ? "dist-v" : "dist-h");

        var first = items[0];
        var last = items[^1];
        var start = vertical ? first.Bottom : first.Right;
        var end = vertical ? last.Y : last.X;

        var totalSize = items.Skip(1).Take(items.Count - 2)
            .Sum(e => vertical ? e.Height : e.Width);

        var gap = Math.Max(0, (end - start - totalSize) / Math.Max(1, items.Count - 1));
        var cursor = start;

        for (var i = 1; i < items.Count - 1; i++)
        {
            cursor += gap;
            if (vertical)
            {
                items[i].Y = cursor;
                cursor += items[i].Height;
            }
            else
            {
                items[i].X = cursor;
                cursor += items[i].Width;
            }
        }

        MarkDirty();
        SchedulePreview();
        StatusMessage = "Espaçamento uniforme aplicado.";
    }

    // ------------------------------------------------------------- documento

    public string SaveStatusText
    {
        get => _saveStatus;
        private set => SetProperty(ref _saveStatus, value);
    }

    public bool AutoSaveEnabled
    {
        get => _editor.AutoSave;
        set
        {
            if (_editor.AutoSave == value)
            {
                return;
            }

            _editor.AutoSave = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AutoSaveText));
            ConfigureAutoSaveTimer();
            ScheduleSettingsSave();
        }
    }

    public string AutoSaveText => AutoSaveEnabled
        ? _lastAutoSaveAt is { } at
            ? $"Rascunho salvo às {at:HH:mm}"
            : $"Rascunho automático a cada {_editor.AutoSaveSeconds}s"
        : "Rascunho automático desligado";

    public bool ShowWelcome
    {
        get => _showWelcome;
        private set => SetProperty(ref _showWelcome, value);
    }

    public RelayCommand DismissWelcomeCommand { get; private set; } = null!;

    public RelayCommand SaveDraftCommand { get; private set; } = null!;

    public string LastSavedText => _lastSavedAt is { } at
        ? $"Salvo às {at:HH:mm}"
        : "Ainda não salvo";

    private void ConfigureAutoSaveTimer()
    {
        if (_autoSaveTimer is not null)
        {
            _autoSaveTimer.Stop();
            _autoSaveTimer.Tick -= OnAutoSaveTick;
        }

        if (!AutoSaveEnabled)
        {
            _autoSaveTimer = null;
            return;
        }

        _autoSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(_editor.AutoSaveSeconds, 5, 300)),
        };
        _autoSaveTimer.Tick += OnAutoSaveTick;
        _autoSaveTimer.Start();
    }

    private void OnAutoSaveTick(object? sender, EventArgs e)
    {
        if (!IsDirty)
        {
            return;
        }

        SaveDraft();
    }

    /// <summary>Grava um rascunho para recuperação em caso de fechamento inesperado.</summary>
    public void SaveDraft()
    {
        try
        {
            var template = BuildWorkingCopy();
            EditorDraftStore.Save(template, _isNew);
            _lastAutoSaveAt = DateTimeOffset.Now;
            SaveStatusText = $"Rascunho {_lastAutoSaveAt:HH:mm}";
            OnPropertyChanged(nameof(AutoSaveText));
        }
        catch (Exception ex)
        {
            // Falha de rascunho nunca pode atrapalhar a edição.
            _host.Services.Log.Warn("Editor visual", $"Falha ao gravar rascunho: {ex.Message}");
        }
    }

    private void DiscardDraft()
    {
        try
        {
            EditorDraftStore.Delete(_template.Id);
        }
        catch
        {
            // ignorado de propósito
        }
    }

    private void RaiseHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    // ------------------------------------------------------------ inline edit

    public bool IsInlineEditing
    {
        get => _isInlineEditing;
        private set
        {
            if (SetProperty(ref _isInlineEditing, value))
            {
                OnPropertyChanged(nameof(InlineEditText));
            }
        }
    }

    public string InlineEditText
    {
        get => _inlineEditText;
        set => SetProperty(ref _inlineEditText, value);
    }

    public bool CanInlineEdit => SelectedElement is { Kind: LayoutElementKind.Texto };

    public void BeginInlineEdit()
    {
        if (SelectedElement is not { Kind: LayoutElementKind.Texto } element)
        {
            return;
        }

        InlineEditText = element.Text;
        IsInlineEditing = true;
    }

    public void CommitInlineEdit()
    {
        if (!IsInlineEditing)
        {
            return;
        }

        if (SelectedElement is { } element)
        {
            PushUndo("inline");
            element.Text = InlineEditText;
            MarkDirty();
            SchedulePreview();
        }

        IsInlineEditing = false;
    }

    public void CancelInlineEdit() => IsInlineEditing = false;

    // ------------------------------------------------------------- inicialização

    private void InitializeModernEditor()
    {
        _editor = _host.Services?.Settings?.Editor ?? new EditorSettings();
        _editor.Normalize();
        _suppressSettingsSave = true;

        _showWelcome = !_editor.WelcomeShown;

        ToggleLeftPanelCommand = new RelayCommand(ToggleLeftPanel);
        ToggleRightPanelCommand = new RelayCommand(ToggleRightPanel);
        ResetLayoutCommand = new RelayCommand(ResetPanels);

        ZoomToFitCommand = new RelayCommand(_ => Zoom = Math.Clamp(Zoom, 0.4, 3.0));
        Zoom100Command = new RelayCommand(_ => Zoom = 1.0);
        ToggleMarginsCommand = new RelayCommand(_ => ShowMargins = !ShowMargins);
        ToggleGuidesCommand = new RelayCommand(_ => ShowGuides = !ShowGuides);
        ToggleRulersCommand = new RelayCommand(_ => ShowRulers = !ShowRulers);
        ToggleGridCommand = new RelayCommand(_ => ShowGrid = !ShowGrid);
        ToggleSnapCommand = new RelayCommand(_ => SnapToGrid = !SnapToGrid);

        InsertPaletteItemCommand = new RelayCommand(p =>
        {
            switch (p)
            {
                case PaletteItem item:
                    InsertTool(item.Id);
                    break;
                case string id:
                    InsertTool(id);
                    break;
            }
        });

        ClearPaletteSearchCommand = new RelayCommand(_ => PaletteSearch = string.Empty);

        NewFieldDialogCommand = new RelayCommand(_ => RequestFieldEditor(null));
        RemoveFieldCommand = new RelayCommand(p =>
        {
            if (p is TemplateField field)
            {
                RemoveFieldFromUi(field);
            }
        });
        DeleteUnusedFieldsCommand = new RelayCommand(_ => RemoveUnusedFieldsFromUi(), _ => HasUnusedFields);
        EditFieldDialogCommand = new RelayCommand(_ => RequestFieldEditor(SelectedField));
        DeleteFieldCommand = new RelayCommand(_ => DeleteFieldFromUi());
        InsertFieldAtEndCommand = new RelayCommand(p =>
        {
            if (p is TemplateField field)
            {
                AddFieldElementAt(field);
            }
        });

        CopyStyleCommand = new RelayCommand(CopyStyle);
        PasteStyleCommand = new RelayCommand(_ => ApplyStyle(), _ => HasCopiedStyle && HasSelection);
        FindReplaceCommand = new RelayCommand(_ => FindReplaceRequested?.Invoke(this, EventArgs.Empty));
        DistributeHorizontalEvenCommand = new RelayCommand(_ => DistributeEvenly(vertical: false), _ => Selection.Count > 2);
        DistributeVerticalEvenCommand = new RelayCommand(_ => DistributeEvenly(vertical: true), _ => Selection.Count > 2);

        DismissWelcomeCommand = new RelayCommand(_ =>
        {
            ShowWelcome = false;
            _editor.WelcomeShown = true;
            SaveSettingsNow();
        });

        SaveDraftCommand = new RelayCommand(_ => SaveDraft());

        BuildPalette();
        RefreshLayers();
        ConfigureAutoSaveTimer();
        _suppressSettingsSave = false;
        RaiseHistoryChanged();
    }

    public RelayCommand RemoveFieldCommand { get; private set; } = null!;

    public RelayCommand DeleteUnusedFieldsCommand { get; private set; } = null!;

    public int UnusedFieldCount => _template.Fields.Count(f => !_template.IsFieldUsed(f.Key));

    public bool HasUnusedFields => UnusedFieldCount > 0;

    public string UnusedFieldsText => HasUnusedFields ? $"{UnusedFieldCount} sem uso" : string.Empty;

    private void RemoveFieldFromUi(TemplateField field)
    {
        var usage = CountFieldUsage(field);
        var message = usage == 0
            ? $"Excluir o campo \"{field.Label}\"?"
            : $"O campo \"{field.Label}\" é usado em {usage} elemento(s)." + Environment.NewLine + Environment.NewLine +
              "O campo e esses elementos serão removidos. Continuar?";

        if (Confirm is not null && !Confirm("Excluir campo", message))
        {
            return;
        }

        var removed = DeleteField(field, alsoDeleteElements: true);
        StatusMessage = removed > 0
            ? $"Campo excluído e {removed} elemento(s) removido(s)."
            : "Campo excluído.";
    }

    private void RemoveUnusedFieldsFromUi()
    {
        var unused = _template.Fields.Where(f => !_template.IsFieldUsed(f.Key)).ToList();
        if (unused.Count == 0)
        {
            return;
        }

        if (Confirm is not null
            && !Confirm("Limpar campos", $"{unused.Count} campo(s) sem uso serão excluídos. Continuar?"))
        {
            return;
        }

        PushUndo("cleanup-fields");
        foreach (var field in unused)
        {
            _template.Fields.Remove(field);
        }

        RefreshFieldChoices();
        RaiseFieldProperties();
        MarkDirty();
        SchedulePreview();
        StatusMessage = $"{unused.Count} campo(s) sem uso removido(s).";
    }

    /// <summary>
    /// Depois de excluir elementos, remove (com confirmação) os campos que
    /// ficaram sem uso. Antes eles continuavam na lista e no formulário de
    /// preenchimento mesmo sem existir no documento.
    /// </summary>
    private void PruneFieldsAfterDelete(IReadOnlyCollection<string> candidateKeys)
    {
        if (candidateKeys.Count == 0)
        {
            return;
        }

        var orphans = _template.Fields
            .Where(f => candidateKeys.Contains(f.Key, StringComparer.OrdinalIgnoreCase))
            .Where(f => !_template.IsFieldUsed(f.Key))
            .ToList();

        if (orphans.Count == 0)
        {
            return;
        }

        var names = string.Join(", ", orphans.Select(f => $"\"{f.Label}\""));
        var message = orphans.Count == 1
            ? $"O campo {names} não é mais usado no documento. Excluir também?"
            : $"Os campos {names} não são mais usados no documento. Excluir também?";

        if (Confirm is not null && !Confirm("Excluir campos sem uso", message))
        {
            return;
        }

        foreach (var field in orphans)
        {
            _template.Fields.Remove(field);
        }

        RefreshFieldChoices();
        RaiseFieldProperties();
        MarkDirty();
        SchedulePreview();
        StatusMessage = orphans.Count == 1
            ? "Campo sem uso removido."
            : $"{orphans.Count} campos sem uso removidos.";
    }
    private void DeleteFieldFromUi()
    {
        if (SelectedField is not { } field)
        {
            StatusMessage = "Selecione um elemento de campo para editar o campo.";
            return;
        }

        var usage = CountFieldUsage(field);
        var message = usage == 0
            ? $"Excluir o campo \"{field.Label}\"? Ele não é usado no documento."
            : $"O campo \"{field.Label}\" é usado em {usage} elemento(s)." + Environment.NewLine + Environment.NewLine +
              "Os elementos serão removidos do documento. Continuar?";

        if (Confirm is not null && !Confirm("Excluir campo", message))
        {
            return;
        }

        var removed = DeleteField(field, alsoDeleteElements: true);
        StatusMessage = removed > 0
            ? $"Campo excluído e {removed} elemento(s) removido(s)."
            : "Campo excluído.";
    }

    private void OnDocumentSaved()
    {
        _lastSavedAt = DateTimeOffset.Now;
        SaveStatusText = "Salvo";
        DiscardDraft();
        OnPropertyChanged(nameof(LastSavedText));
    }

    // ------------------------------------------------------------- auxiliares

    /// <summary>
    /// Move a seleção inteira de modo que o elemento âncora fique em (x, y).
    /// Usado pelo arraste do canvas (inclusive com vários elementos).
    /// </summary>
    public void MoveSelectionTo(int x, int y, LayoutElement anchor)
    {
        var dx = x - anchor.X;
        var dy = y - anchor.Y;
        if (dx == 0 && dy == 0)
        {
            return;
        }

        foreach (var target in Selection.Where(e => !e.Locked))
        {
            target.X = Math.Clamp(
                target.X + dx, 0, Math.Max(0, Layout.Width - Math.Min(target.Width, Layout.Width)));
            target.Y = Math.Max(0, target.Y + dy);
        }

        OnPropertyChanged(nameof(SelectionBounds));
    }
    public bool CanPaste => _clipboard.Count > 0;

    public void GroupSelectionPublic() => GroupSelection();

    public void UngroupSelectionPublic() => UngroupSelection();

    public void AlignLeft() => Align(ElementAlign.Left);

    public void AlignCenterHorizontal() => Align(ElementAlign.CenterHorizontal);

    public void AlignRight() => Align(ElementAlign.Right);

    public void AlignTop() => Align(ElementAlign.Top);

    public void AlignCenterVertical() => Align(ElementAlign.CenterVertical);

    public void AlignBottom() => Align(ElementAlign.Bottom);

    /// <summary>Move uma camada na lista (arrastar-e-soltar do painel).</summary>
    public void MoveLayer(int fromDisplayIndex, int toDisplayIndex)
    {
        var layers = Layers.ToList();
        if (fromDisplayIndex < 0 || fromDisplayIndex >= layers.Count
            || toDisplayIndex < 0 || toDisplayIndex >= layers.Count)
        {
            return;
        }

        var element = layers[fromDisplayIndex];
        layers.RemoveAt(fromDisplayIndex);
        layers.Insert(toDisplayIndex, element);

        PushUndo("layer-order");

        // A lista de camadas mostra o topo primeiro; a ordem de desenho é o inverso.
        var drawOrder = Enumerable.Reverse(layers).ToList();
        Elements.Clear();
        foreach (var item in drawOrder)
        {
            Elements.Add(item);
        }

        Layout.Elements = Elements.ToList();
        RefreshLayers();
        OnPropertyChanged(nameof(Elements));
        MarkDirty();
        SchedulePreview();
        StatusMessage = "Ordem das camadas alterada.";
    }

    /// <summary>Zoom para o controle deslizante (40 a 300).</summary>
    public double ZoomPercentNumber
    {
        get => Math.Round(Zoom * 100);
        set
        {
            var zoom = Math.Clamp(value / 100.0, 0.4, 3.0);
            if (Math.Abs(zoom - Zoom) < 0.001)
            {
                return;
            }

            Zoom = zoom;
            OnPropertyChanged();
        }
    }

    public string SelectedFieldKeyHint =>
        SelectedField is { } field ? $"chave interna: {field.Key}" : string.Empty;

    public string ValidationSummary
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ValidationText))
            {
                return "Documento válido";
            }

            if (HasErrors)
            {
                return "Há erros que impedem salvar";
            }

            var firstLine = ValidationText.Split('\n')[0].Trim();
            return firstLine.Length > 70 ? firstLine[..70] + "…" : firstLine;
        }
    }

    private string _layerSearch = string.Empty;

    public string LayerSearch
    {
        get => _layerSearch;
        set
        {
            if (SetProperty(ref _layerSearch, value))
            {
                RefreshLayerFilter();
            }
        }
    }

    public ObservableCollection<LayoutElement> FilteredLayers { get; } = new();

    public LayoutElement? SelectedLayer
    {
        get => SelectedElement;
        set
        {
            if (value is not null && !ReferenceEquals(value, SelectedElement))
            {
                SelectedElement = value;
            }
        }
    }

    private void RefreshLayerFilter()
    {
        var search = (_layerSearch ?? string.Empty).Trim();
        FilteredLayers.Clear();

        foreach (var element in Layers)
        {
            if (search.Length == 0
                || element.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || element.KindText.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                FilteredLayers.Add(element);
            }
        }

        OnPropertyChanged(nameof(FilteredLayers));
    }

    /// <summary>
    /// Cria um campo novo a partir da paleta. Nunca reaproveita um campo
    /// existente: dois CPF/CNPJ no documento são dois campos independentes
    /// (reaproveitar fazia renomear um renomear o outro).
    /// </summary>
    private void QuickAddField(string label, TemplateFieldType type, int? x, int? y)
    {
        var field = CreateField(label, type, required: false, placeholder: string.Empty, defaultValue: string.Empty);
        AddFieldElementAt(field, x, y, pushUndo: false);
    }

    private void OnDocumentDirty()
    {
        SaveStatusText = "Alterações não salvas";
        IsPickingStyle = false;
    }
}

/// <summary>Estilo visual copiável entre elementos.</summary>
public sealed record ElementStyle
{
    public string? FontFamily { get; init; }

    public double FontSize { get; init; }

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public TextAlignment Alignment { get; init; }

    public double LineSpacing { get; init; }

    public OverflowMode Overflow { get; init; }

    public int Thickness { get; init; }

    public bool Filled { get; init; }

    public static ElementStyle From(LayoutElement element) => new()
    {
        FontFamily = element.FontFamily,
        FontSize = element.FontSize,
        Bold = element.Bold,
        Italic = element.Italic,
        Alignment = element.Alignment,
        LineSpacing = element.LineSpacing,
        Overflow = element.Overflow,
        Thickness = element.Thickness,
        Filled = element.Filled,
    };

    public void ApplyTo(LayoutElement element)
    {
        element.FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? element.FontFamily : FontFamily!;
        element.FontSize = FontSize;
        element.Bold = Bold;
        element.Italic = Italic;
        element.Alignment = Alignment;
        element.LineSpacing = LineSpacing;
        element.Overflow = Overflow;

        if (element.Kind is LayoutElementKind.Linha or LayoutElementKind.Retangulo)
        {
            element.Thickness = Thickness;
            element.Filled = Filled;
        }
    }
}

/// <summary>Uma entrada da paleta de elementos.</summary>
public sealed class PaletteItem
{
    public required string Id { get; init; }

    public required string Label { get; init; }

    public string Glyph { get; init; } = string.Empty;

    public string Hint { get; init; } = string.Empty;

    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();

    public bool Matches(string search) =>
        Label.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Hint.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Keywords.Any(k => k.Contains(search, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Grupo recolhível da paleta.</summary>
public sealed class PaletteGroup : ViewModelBase
{
    private bool _isExpanded;

    public PaletteGroup(string name, string glyph, IReadOnlyList<PaletteItem> items, bool isExpanded)
    {
        Name = name;
        Glyph = glyph;
        Items = items;
        _isExpanded = isExpanded;
    }

    public string Name { get; }

    public string Glyph { get; }

    public IReadOnlyList<PaletteItem> Items { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}

/// <summary>Conteúdo estático da paleta.</summary>
public static class PaletteCatalog
{
    public const string FieldPrefix = "field:";

    public static IReadOnlyList<PaletteGroupTemplate> Groups { get; } = new[]
    {
        new PaletteGroupTemplate("Texto", "\uE8D2", new[]
        {
            new PaletteItem { Id = "text", Label = "Texto simples", Glyph = "\uE8D2", Hint = "Caixa de texto livre", Keywords = new[] { "texto", "escrever" } },
            new PaletteItem { Id = "title", Label = "Título", Glyph = "\uE8C1", Hint = "Texto grande em negrito centralizado", Keywords = new[] { "titulo", "cabecalho" } },
            new PaletteItem { Id = "subtitle", Label = "Subtítulo", Glyph = "\uE8D3", Hint = "Texto médio em negrito", Keywords = new[] { "subtitulo" } },
            new PaletteItem { Id = "paragraph", Label = "Parágrafo", Glyph = "\uE8E9", Hint = "Texto que quebra em várias linhas", Keywords = new[] { "paragrafo", "texto longo" } },
            new PaletteItem { Id = "dynamicText", Label = "Texto dinâmico", Glyph = "\uE943", Hint = "Texto com {{campo}} no conteúdo", Keywords = new[] { "dinamico", "campo", "variavel" } },
        }),
        new PaletteGroupTemplate("Campos", "\uE8A5", new[]
        {
            new PaletteItem { Id = "field-new", Label = "Novo campo...", Glyph = "\uE710", Hint = "Cria um campo e o insere no documento", Keywords = new[] { "campo", "novo", "criar" } },
            new PaletteItem { Id = "field-text", Label = "Campo de texto", Glyph = "\uE8A5", Hint = "Valor de texto curto", Keywords = new[] { "campo", "texto" } },
            new PaletteItem { Id = "field-money", Label = "Campo monetário", Glyph = "\uE8C7", Hint = "Valor em reais, formatado", Keywords = new[] { "campo", "valor", "dinheiro", "monetario" } },
            new PaletteItem { Id = "field-date", Label = "Campo de data", Glyph = "\uE787", Hint = "Data com seletor", Keywords = new[] { "campo", "data" } },
            new PaletteItem { Id = "field-doc", Label = "CPF / CNPJ", Glyph = "\uE8D7", Hint = "Documento com máscara e validação", Keywords = new[] { "campo", "cpf", "cnpj" } },
            new PaletteItem { Id = "field-phone", Label = "Telefone", Glyph = "\uE717", Hint = "Telefone com máscara", Keywords = new[] { "campo", "telefone", "fone" } },
            new PaletteItem { Id = "field-list", Label = "Lista de opções", Glyph = "\uE8FD", Hint = "Escolha entre opções cadastradas", Keywords = new[] { "campo", "lista", "opcoes" } },
        }),
        new PaletteGroupTemplate("Elementos gráficos", "\uE7FB", new[]
        {
            new PaletteItem { Id = "line", Label = "Linha", Glyph = "\uE738", Hint = "Linha horizontal", Keywords = new[] { "linha", "divisor" } },
            new PaletteItem { Id = "rect", Label = "Retângulo", Glyph = "\uE739", Hint = "Contorno de retângulo", Keywords = new[] { "retangulo", "caixa", "moldura" } },
            new PaletteItem { Id = "rectFilled", Label = "Retângulo preenchido", Glyph = "\uE73A", Hint = "Bloco preto", Keywords = new[] { "retangulo", "preenchido", "bloco" } },
            new PaletteItem { Id = "spacer", Label = "Espaçador", Glyph = "\uE8A1", Hint = "Espaço em branco entre elementos", Keywords = new[] { "espaco", "espacador" } },
            new PaletteItem { Id = "image", Label = "Imagem...", Glyph = "\uEB9F", Hint = "Logotipo ou figura (PNG, JPG, BMP)", Keywords = new[] { "imagem", "logo", "figura" } },
        }),
        new PaletteGroupTemplate("Estruturas prontas", "\uE8F1", new[]
        {
            new PaletteItem { Id = "header", Label = "Cabeçalho", Glyph = "\uE8A4", Hint = "Título centralizado com linha", Keywords = new[] { "cabecalho", "titulo", "estrutura" } },
            new PaletteItem { Id = "footer", Label = "Rodapé", Glyph = "\uE8A3", Hint = "Linha com texto de rodapé", Keywords = new[] { "rodape", "estrutura" } },
            new PaletteItem { Id = "signature", Label = "Área de assinatura", Glyph = "\uE70F", Hint = "Linha e legenda de assinatura", Keywords = new[] { "assinatura", "estrutura" } },
            new PaletteItem { Id = "info", Label = "Bloco de informações", Glyph = "\uE8A5", Hint = "Caixa com rótulos e campos", Keywords = new[] { "informacoes", "bloco", "estrutura" } },
            new PaletteItem { Id = "identification", Label = "Bloco de identificação", Glyph = "\uE77B", Hint = "Nome, documento e contato", Keywords = new[] { "identificacao", "cliente", "estrutura" } },
        }),
    };
}

public sealed class PaletteGroupTemplate
{
    public PaletteGroupTemplate(string name, string glyph, IReadOnlyList<PaletteItem> items)
    {
        Name = name;
        Glyph = glyph;
        Items = items;
    }

    public string Name { get; }

    public string Glyph { get; }

    public IReadOnlyList<PaletteItem> Items { get; }
}
