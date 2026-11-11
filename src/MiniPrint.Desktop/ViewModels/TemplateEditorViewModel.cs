using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MiniPrint.Core.Configuration;
using MiniPrint.Core.Templates;
using MiniPrint.Desktop.Services;
using MiniPrint.Imaging;
using TextAlignment = MiniPrint.Core.Configuration.TextAlignment;

namespace MiniPrint.Desktop.ViewModels;

/// <summary>Edição de um modelo: blocos do documento, campos e pré-visualização.</summary>
public sealed class TemplateEditorViewModel : ViewModelBase
{
    private readonly ITemplateHost _host;
    private readonly DocumentTemplate _template;
    private readonly bool _isNew;
    private readonly DispatcherTimer _previewTimer;

    private string _name;
    private string _description;
    private int _fontSize;
    private bool _bold;
    private TextAlignment _alignment;
    private double _lineSpacing;
    private int _marginLeft;
    private int _marginRight;
    private int _marginTop;
    private int _marginBottom;
    private int _paperWidth;

    private TemplateBlock? _selectedBlock;
    private TemplateField? _selectedField;

    private string _newFieldLabel = string.Empty;
    private TemplateFieldType _newFieldType = TemplateFieldType.TextoCurto;
    private string _newFieldOptions = string.Empty;

    private BitmapSource? _previewImage;
    private string _previewInfo = "Gerando pré-visualização...";
    private string _validationText = string.Empty;
    private bool _hasErrors;
    private bool _isRendering;
    private int _renderGeneration;

    public TemplateEditorViewModel(ITemplateHost host, DocumentTemplate template, bool isNew)
    {
        _host = host;
        _template = template;
        _isNew = isNew;

        _name = template.Name;
        _description = template.Description;
        _fontSize = template.FontSize;
        _bold = template.Bold;
        _alignment = template.Alignment;
        _lineSpacing = template.LineSpacing;
        _marginLeft = template.MarginLeft;
        _marginRight = template.MarginRight;
        _marginTop = template.MarginTop;
        _marginBottom = template.MarginBottom;
        _paperWidth = template.PaperWidth;

        foreach (var block in template.Blocks)
        {
            Blocks.Add(block);
        }

        foreach (var field in template.Fields)
        {
            Fields.Add(field);
        }

        _selectedBlock = Blocks.FirstOrDefault();
        _selectedField = Fields.FirstOrDefault();

        AddTextBlockCommand = new RelayCommand(_ => AddBlock(TemplateBlockKind.Texto));
        AddDividerCommand = new RelayCommand(_ => AddBlock(TemplateBlockKind.Divisor));
        AddSpacerCommand = new RelayCommand(_ => AddBlock(TemplateBlockKind.Espaco));
        RemoveBlockCommand = new RelayCommand(_ => RemoveBlock(), _ => SelectedBlock is not null);
        MoveBlockUpCommand = new RelayCommand(_ => MoveBlock(-1), _ => CanMoveBlock(-1));
        MoveBlockDownCommand = new RelayCommand(_ => MoveBlock(1), _ => CanMoveBlock(1));
        DuplicateBlockCommand = new RelayCommand(_ => DuplicateBlock(), _ => SelectedBlock is not null);

        AddFieldCommand = new RelayCommand(_ => AddField());
        RemoveFieldCommand = new RelayCommand(_ => RemoveField(), _ => SelectedField is not null);
        MoveFieldUpCommand = new RelayCommand(_ => MoveField(-1), _ => CanMoveField(-1));
        MoveFieldDownCommand = new RelayCommand(_ => MoveField(1), _ => CanMoveField(1));

        SaveCommand = new RelayCommand(_ => Save());
        CancelCommand = new RelayCommand(_ => Cancel());
        ConvertToVisualCommand = new RelayCommand(_ => ConvertToVisual());
        RefreshPreviewCommand = new RelayCommand(_ => SchedulePreview(immediate: true));

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
        _previewTimer.Tick += async (_, _) =>
        {
            _previewTimer.Stop();
            await RenderPreviewAsync();
        };

        Blocks.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(BlockCount));
            RaiseBlockCommands();
            SchedulePreview();
        };

        Fields.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(FieldCount));
            OnPropertyChanged(nameof(HasFields));
            RaiseFieldCommands();
            SchedulePreview();
        };

        SchedulePreview(immediate: true);
        UpdateValidation();
    }

    public string Title => _isNew ? "Novo modelo" : $"Editar: {_template.Name}";

    public ObservableCollection<TemplateBlock> Blocks { get; } = new();

    public ObservableCollection<TemplateField> Fields { get; } = new();

    public IReadOnlyList<TextAlignment> Alignments { get; } =
        new[] { TextAlignment.Esquerda, TextAlignment.Centro, TextAlignment.Direita };

    public IReadOnlyList<TemplateFieldType> FieldTypes { get; } = new[]
    {
        TemplateFieldType.TextoCurto,
        TemplateFieldType.TextoLongo,
        TemplateFieldType.Numero,
        TemplateFieldType.Monetario,
        TemplateFieldType.Data,
        TemplateFieldType.Cpf,
        TemplateFieldType.Cnpj,
        TemplateFieldType.Telefone,
        TemplateFieldType.Lista,
    };

    public IReadOnlyList<DividerStyle> DividerStyles { get; } = new[]
    {
        DividerStyle.Traco, DividerStyle.Igual, DividerStyle.Sublinhado, DividerStyle.LinhaSolida,
    };

    public RelayCommand AddTextBlockCommand { get; }

    public RelayCommand AddDividerCommand { get; }

    public RelayCommand AddSpacerCommand { get; }

    public RelayCommand RemoveBlockCommand { get; }

    public RelayCommand MoveBlockUpCommand { get; }

    public RelayCommand MoveBlockDownCommand { get; }

    public RelayCommand DuplicateBlockCommand { get; }

    public RelayCommand AddFieldCommand { get; }

    public RelayCommand RemoveFieldCommand { get; }

    public RelayCommand MoveFieldUpCommand { get; }

    public RelayCommand MoveFieldDownCommand { get; }

    public RelayCommand SaveCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand ConvertToVisualCommand { get; }

    public RelayCommand RefreshPreviewCommand { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(Title));
                SchedulePreview();
            }
        }
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public int FontSize
    {
        get => _fontSize;
        set
        {
            if (SetProperty(ref _fontSize, Math.Clamp(value, 6, 48)))
            {
                SchedulePreview();
            }
        }
    }

    public bool Bold
    {
        get => _bold;
        set
        {
            if (SetProperty(ref _bold, value))
            {
                SchedulePreview();
            }
        }
    }

    public TextAlignment Alignment
    {
        get => _alignment;
        set
        {
            if (SetProperty(ref _alignment, value))
            {
                SchedulePreview();
            }
        }
    }

    public double LineSpacing
    {
        get => _lineSpacing;
        set
        {
            if (SetProperty(ref _lineSpacing, Math.Clamp(value, 0.8, 2.5)))
            {
                SchedulePreview();
            }
        }
    }

    public int MarginLeft
    {
        get => _marginLeft;
        set
        {
            if (SetProperty(ref _marginLeft, Math.Clamp(value, 0, 120)))
            {
                SchedulePreview();
            }
        }
    }

    public int MarginRight
    {
        get => _marginRight;
        set
        {
            if (SetProperty(ref _marginRight, Math.Clamp(value, 0, 120)))
            {
                SchedulePreview();
            }
        }
    }

    public int MarginTop
    {
        get => _marginTop;
        set
        {
            if (SetProperty(ref _marginTop, Math.Clamp(value, 0, 120)))
            {
                SchedulePreview();
            }
        }
    }

    public int MarginBottom
    {
        get => _marginBottom;
        set
        {
            if (SetProperty(ref _marginBottom, Math.Clamp(value, 0, 120)))
            {
                SchedulePreview();
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
                SchedulePreview();
            }
        }
    }

    public TemplateBlock? SelectedBlock
    {
        get => _selectedBlock;
        set
        {
            if (SetProperty(ref _selectedBlock, value))
            {
                RaiseBlockCommands();
                OnPropertyChanged(nameof(HasSelectedBlock));
                OnPropertyChanged(nameof(SelectedBlockText));
                OnPropertyChanged(nameof(IsTextBlock));
                OnPropertyChanged(nameof(IsDividerBlock));
                OnPropertyChanged(nameof(IsSpacerBlock));
            }
        }
    }

    public TemplateField? SelectedField
    {
        get => _selectedField;
        set
        {
            if (SetProperty(ref _selectedField, value))
            {
                RaiseFieldCommands();
                OnPropertyChanged(nameof(HasSelectedField));
            }
        }
    }

    public string SelectedBlockText
    {
        get => SelectedBlock?.Text ?? string.Empty;
        set
        {
            if (SelectedBlock is null || SelectedBlock.Text == value)
            {
                return;
            }

            SelectedBlock.Text = value;
            OnPropertyChanged();
            SchedulePreview();
        }
    }

    public bool HasSelectedBlock => SelectedBlock is not null;

    public bool IsTextBlock => SelectedBlock?.Kind == TemplateBlockKind.Texto;

    public bool IsDividerBlock => SelectedBlock?.Kind == TemplateBlockKind.Divisor;

    public bool IsSpacerBlock => SelectedBlock?.Kind == TemplateBlockKind.Espaco;

    public bool HasSelectedField => SelectedField is not null;

    public bool HasFields => Fields.Count > 0;

    public int BlockCount => Blocks.Count;

    public int FieldCount => Fields.Count;

    public string NewFieldLabel
    {
        get => _newFieldLabel;
        set => SetProperty(ref _newFieldLabel, value);
    }

    public TemplateFieldType NewFieldType
    {
        get => _newFieldType;
        set => SetProperty(ref _newFieldType, value);
    }

    public string NewFieldOptions
    {
        get => _newFieldOptions;
        set => SetProperty(ref _newFieldOptions, value);
    }

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

    public bool IsRendering
    {
        get => _isRendering;
        private set => SetProperty(ref _isRendering, value);
    }

    /// <summary>Chaves dos campos, usadas pela caixa de inserção no editor.</summary>
    public IReadOnlyList<TemplateField> FieldChoices => Fields.ToList();

    // ---------- blocos ----------

    private void AddBlock(TemplateBlockKind kind)
    {
        var block = new TemplateBlock { Kind = kind };

        if (kind == TemplateBlockKind.Texto)
        {
            block.Text = string.Empty;
        }
        else if (kind == TemplateBlockKind.Divisor)
        {
            block.DividerStyle = DividerStyle.Traco;
        }
        else
        {
            block.Height = 12;
        }

        var index = SelectedBlock is null ? Blocks.Count : Blocks.IndexOf(SelectedBlock) + 1;
        Blocks.Insert(Math.Clamp(index, 0, Blocks.Count), block);
        SelectedBlock = block;
    }

    private void DuplicateBlock()
    {
        if (SelectedBlock is null)
        {
            return;
        }

        var copy = SelectedBlock.Clone();
        var index = Blocks.IndexOf(SelectedBlock);
        Blocks.Insert(Math.Clamp(index + 1, 0, Blocks.Count), copy);
        SelectedBlock = copy;
    }

    private void RemoveBlock()
    {
        if (SelectedBlock is null)
        {
            return;
        }

        var index = Blocks.IndexOf(SelectedBlock);
        Blocks.Remove(SelectedBlock);
        SelectedBlock = Blocks.Count == 0
            ? null
            : Blocks[Math.Clamp(index, 0, Blocks.Count - 1)];
    }

    private bool CanMoveBlock(int delta)
    {
        if (SelectedBlock is null)
        {
            return false;
        }

        var target = Blocks.IndexOf(SelectedBlock) + delta;
        return target >= 0 && target < Blocks.Count;
    }

    private void MoveBlock(int delta)
    {
        if (SelectedBlock is null)
        {
            return;
        }

        var index = Blocks.IndexOf(SelectedBlock);
        var target = index + delta;
        if (target < 0 || target >= Blocks.Count)
        {
            return;
        }

        Blocks.Move(index, target);
        SelectedBlock = Blocks[target];
        RaiseBlockCommands();
        SchedulePreview();
    }

    private void RaiseBlockCommands()
    {
        RemoveBlockCommand.RaiseCanExecuteChanged();
        MoveBlockUpCommand.RaiseCanExecuteChanged();
        MoveBlockDownCommand.RaiseCanExecuteChanged();
        DuplicateBlockCommand.RaiseCanExecuteChanged();
    }

    // ---------- campos ----------

    private void AddField()
    {
        var label = string.IsNullOrWhiteSpace(NewFieldLabel) ? "Novo campo" : NewFieldLabel.Trim();
        var key = TemplateFieldFormatter.SuggestKey(label);

        // Garante chave única.
        var suffix = 2;
        var baseKey = key;
        while (Fields.Any(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)))
        {
            key = $"{baseKey}_{suffix++}";
        }

        var field = new TemplateField
        {
            Key = key,
            Label = label,
            Type = NewFieldType,
            Options = NewFieldOptions
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
        };

        Fields.Add(field);
        SelectedField = field;

        NewFieldLabel = string.Empty;
        NewFieldOptions = string.Empty;
        OnPropertyChanged(nameof(FieldChoices));
        SchedulePreview();
    }

    private void RemoveField()
    {
        if (SelectedField is null)
        {
            return;
        }

        var field = SelectedField;
        var index = Fields.IndexOf(field);

        var used = Blocks
            .Where(b => b.Kind == TemplateBlockKind.Texto)
            .Any(b => TemplateComposer.ExtractTokens(b.Text)
                .Contains(field.Key, StringComparer.OrdinalIgnoreCase));

        if (used)
        {
            var confirm = MiniPrint.Desktop.Windows.MessageDialog.Show(
                $"O campo \"{field.Label}\" é usado no documento. Remover mesmo assim?",
                "MiniPrint Desktop",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }
        }

        Fields.Remove(field);
        SelectedField = Fields.Count == 0 ? null : Fields[Math.Clamp(index, 0, Fields.Count - 1)];
        OnPropertyChanged(nameof(FieldChoices));
        SchedulePreview();
    }

    private bool CanMoveField(int delta)
    {
        if (SelectedField is null)
        {
            return false;
        }

        var target = Fields.IndexOf(SelectedField) + delta;
        return target >= 0 && target < Fields.Count;
    }

    private void MoveField(int delta)
    {
        if (SelectedField is null)
        {
            return;
        }

        var index = Fields.IndexOf(SelectedField);
        var target = index + delta;
        if (target < 0 || target >= Fields.Count)
        {
            return;
        }

        Fields.Move(index, target);
        SelectedField = Fields[target];
        RaiseFieldCommands();
        OnPropertyChanged(nameof(FieldChoices));
        SchedulePreview();
    }

    private void RaiseFieldCommands()
    {
        RemoveFieldCommand.RaiseCanExecuteChanged();
        MoveFieldUpCommand.RaiseCanExecuteChanged();
        MoveFieldDownCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Insere <c>{{chave}}</c> no bloco selecionado, na posição do cursor.</summary>
    public void InsertFieldToken(string key, int caretIndex)
    {
        if (SelectedBlock is null || SelectedBlock.Kind != TemplateBlockKind.Texto)
        {
            AddBlock(TemplateBlockKind.Texto);
        }

        if (SelectedBlock is null)
        {
            return;
        }

        var text = TemplateComposer.InsertField(SelectedBlock.Text, key, caretIndex);
        SelectedBlock.Text = text;
        OnPropertyChanged(nameof(SelectedBlockText));
        SchedulePreview();
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
        copy.FontSize = FontSize;
        copy.Bold = Bold;
        copy.Alignment = Alignment;
        copy.LineSpacing = LineSpacing;
        copy.MarginLeft = MarginLeft;
        copy.MarginRight = MarginRight;
        copy.MarginTop = MarginTop;
        copy.MarginBottom = MarginBottom;
        copy.PaperWidth = PaperWidth;
        copy.Blocks = Blocks.ToList();
        copy.Fields = Fields.ToList();
        return copy;
    }

    private Dictionary<string, string> BuildDemoValues()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in Fields)
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

            var bitmap = await Task.Run(() => TemplateRenderer.Render(template, values));
            if (generation != _renderGeneration)
            {
                return;
            }

            PreviewImage = PreviewRenderer.ToBitmapSource(bitmap);
            PreviewInfo = $"{bitmap.Width} pontos × {bitmap.Height} linhas " +
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
        SaveCommand.RaiseCanExecuteChanged();

        if (result.Errors.Count > 0)
        {
            ValidationText = string.Join(Environment.NewLine, result.Errors.Select(e => "• " + e));
        }
        else if (result.Warnings.Count > 0)
        {
            ValidationText = string.Join(Environment.NewLine, result.Warnings.Select(w => "• " + w));
        }
        else
        {
            ValidationText = "Modelo válido.";
        }
    }

    // ---------- salvar / cancelar ----------

    private void Save()
    {
        var template = BuildWorkingCopy();
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
        _host.SaveTemplate(template, _isNew);
    }

    private void Cancel()
    {
        _host.ShowTemplateList();
    }

    /// <summary>
    /// Converte o modelo de blocos em layout absoluto e abre no editor visual.
    /// O conteúdo, os campos e a formatação são preservados.
    /// </summary>
    private void ConvertToVisual()
    {
        var confirm = MiniPrint.Desktop.Windows.MessageDialog.Show(
            "Abrir este modelo no editor visual?" + Environment.NewLine + Environment.NewLine +
            "O conteúdo é convertido para posicionamento livre. Nada é gravado até você salvar.",
            "MiniPrint Desktop",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var converted = TemplateLayoutConverter.ToVisual(BuildWorkingCopy());
        _host.EditTemplate(converted, _isNew);
    }
}
