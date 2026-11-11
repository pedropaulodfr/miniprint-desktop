using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using MiniPrint.Core.Models;
using MiniPrint.Core.Templates;
using MiniPrint.Desktop.Services;
using MiniPrint.Imaging;

namespace MiniPrint.Desktop.ViewModels;

/// <summary>Um campo do formulário de preenchimento, gerado a partir do modelo.</summary>
public sealed class TemplateFieldInput : ViewModelBase
{
    private readonly TemplateField _field;
    private string _value;
    private string? _error;
    private DateTime? _dateValue;

    public TemplateFieldInput(TemplateField field)
    {
        _field = field;
        _value = field.DefaultValue ?? string.Empty;

        if (field.Type == TemplateFieldType.Data &&
            TemplateFieldFormatter.TryParseDate(_value, out var parsed))
        {
            _dateValue = parsed;
        }
    }

    public TemplateField Field => _field;

    public string Key => _field.Key;

    public string Label => string.IsNullOrWhiteSpace(_field.Label) ? _field.Key : _field.Label;

    public string Placeholder => _field.Placeholder;

    public bool Required => _field.Required;

    public string RequiredMark => Required ? " *" : string.Empty;

    public IReadOnlyList<string> Options => _field.Options;

    public bool IsList => _field.Type == TemplateFieldType.Lista;

    public bool IsDate => _field.Type == TemplateFieldType.Data;

    public bool IsLongText => _field.Type == TemplateFieldType.TextoLongo;

    /// <summary>Campos com máscara são formatados ao sair do campo.</summary>
    public bool IsMasked => _field.Type is TemplateFieldType.Cpf or TemplateFieldType.Cnpj
        or TemplateFieldType.Telefone or TemplateFieldType.Monetario;

    public bool IsPlainText => !IsList && !IsDate && !IsLongText && !IsMasked;

    /// <summary>Valor como o usuário digitou (ou já mascarado).</summary>
    public string Value
    {
        get => _value;
        set
        {
            var formatted = IsMasked && !string.IsNullOrWhiteSpace(value)
                ? TemplateFieldFormatter.ApplyMask(_field.Type, value)
                : value;

            if (SetProperty(ref _value, formatted ?? string.Empty))
            {
                Error = null;
            }
        }
    }

    public DateTime? DateValue
    {
        get => _dateValue;
        set
        {
            if (SetProperty(ref _dateValue, value))
            {
                _value = value?.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR")) ?? string.Empty;
                OnPropertyChanged(nameof(Value));
                Error = null;
            }
        }
    }

    public string? Error
    {
        get => _error;
        set
        {
            if (SetProperty(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>Valida e devolve a mensagem de erro (ou nulo).</summary>
    public string? Validate()
    {
        var error = TemplateFieldFormatter.Validate(_field, _value);
        Error = error;
        return error;
    }

    public void Reset()
    {
        Value = _field.DefaultValue ?? string.Empty;
        DateValue = null;
        Error = null;
    }

    public string FormattedValue => TemplateFieldFormatter.Format(_field, _value);
}

/// <summary>Preenchimento dos campos de um modelo, pré-visualização e impressão.</summary>
public sealed class TemplateFillViewModel : ViewModelBase
{
    private readonly ITemplateHost _host;
    private readonly DocumentTemplate _template;

    private BitmapSource? _previewImage;
    private string _previewInfo = "Preencha os campos e clique em Visualizar documento.";
    private string _statusMessage = string.Empty;
    private bool _hasPreview;
    private bool _isRendering;
    private bool _isPrinting;
    private double _progress;
    private string _progressMessage = string.Empty;
    private bool _storeDataInHistory;
    private int _renderGeneration;

    public TemplateFillViewModel(ITemplateHost host, DocumentTemplate template)
    {
        _host = host;
        _template = template;
        _storeDataInHistory = host.Services.Settings.StoreTemplateDataInHistory;

        // Campos sem uso no documento não aparecem no formulário: não há onde
        // imprimi-los e, antes, eles continuavam aqui depois de excluídos.
        foreach (var field in template.Fields.Where(f => template.IsFieldUsed(f.Key)))
        {
            Inputs.Add(new TemplateFieldInput(field));
        }

        PreviewCommand = new AsyncRelayCommand(_ => PreviewAsync(), null, HandleError);
        PrintCommand = new AsyncRelayCommand(_ => PrintAsync(), _ => !IsPrinting, HandleError);
        ClearCommand = new RelayCommand(_ => Clear());
        BackCommand = new RelayCommand(_ => _host.ShowTemplateList());

        StatusMessage = Inputs.Count == 0
            ? "Este modelo não tem campos: o documento é sempre igual."
            : $"{Inputs.Count} campo(s) para preencher.";
    }

    public string TemplateName => _template.Name;

    public string TemplateDescription => _template.Description;

    public ObservableCollection<TemplateFieldInput> Inputs { get; } = new();

    public AsyncRelayCommand PreviewCommand { get; }

    public AsyncRelayCommand PrintCommand { get; }

    public RelayCommand ClearCommand { get; }

    public RelayCommand BackCommand { get; }

    public bool HasFields => Inputs.Count > 0;

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

    public bool HasPreview
    {
        get => _hasPreview;
        private set
        {
            if (SetProperty(ref _hasPreview, value))
            {
                OnPropertyChanged(nameof(HasNoPreview));
            }
        }
    }

    public bool HasNoPreview => !HasPreview;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsRendering
    {
        get => _isRendering;
        private set => SetProperty(ref _isRendering, value);
    }

    public bool IsPrinting
    {
        get => _isPrinting;
        private set
        {
            if (SetProperty(ref _isPrinting, value))
            {
                PrintCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(PrintButtonText));
            }
        }
    }

    public string PrintButtonText => IsPrinting ? "Enviando..." : "Imprimir";

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public string ProgressMessage
    {
        get => _progressMessage;
        private set => SetProperty(ref _progressMessage, value);
    }

    /// <summary>Guarda os dados preenchidos no histórico (desligado por padrão).</summary>
    public bool StoreDataInHistory
    {
        get => _storeDataInHistory;
        set
        {
            if (SetProperty(ref _storeDataInHistory, value))
            {
                _host.Services.Settings.StoreTemplateDataInHistory = value;
                _host.Services.SaveSettings();
            }
        }
    }

    public string ZoomHint => "Use a roda do mouse ou o controle de zoom para ampliar.";

    // ---------- validação e composição ----------

    private Dictionary<string, string> CollectValues()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in Inputs)
        {
            values[input.Key] = input.Value;
        }

        return values;
    }

    private bool ValidateAll(out List<TemplateFieldInput> invalid)
    {
        invalid = new List<TemplateFieldInput>();
        foreach (var input in Inputs)
        {
            if (input.Validate() is not null)
            {
                invalid.Add(input);
            }
        }

        if (invalid.Count == 0)
        {
            return true;
        }

        StatusMessage = $"Preencha corretamente {invalid.Count} campo(s) destacado(s).";
        return false;
    }

    private MonochromeBitmap Render()
    {
        var values = CollectValues();

        // Modelos visuais usam posicionamento absoluto; os antigos, o fluxo de blocos.
        return _template.IsVisual
            ? VisualTemplateRenderer.Render(_template, values)
            : TemplateRenderer.Render(_template, values);
    }

    private string BuildHistoryTitle()
    {
        if (!StoreDataInHistory)
        {
            return _template.Name;
        }

        var parts = Inputs
            .Select(i => i.FormattedValue)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Take(3)
            .ToList();

        if (parts.Count == 0)
        {
            return _template.Name;
        }

        var summary = string.Join(" · ", parts);
        if (summary.Length > 70)
        {
            summary = summary[..70] + "…";
        }

        return $"{_template.Name} — {summary}";
    }

    // ---------- comandos ----------

    private async Task PreviewAsync()
    {
        if (!ValidateAll(out _))
        {
            HasPreview = false;
            return;
        }

        var generation = ++_renderGeneration;
        IsRendering = true;
        StatusMessage = "Gerando documento...";

        try
        {
            var bitmap = await Task.Run(Render);
            if (generation != _renderGeneration)
            {
                return;
            }

            PreviewImage = PreviewRenderer.ToBitmapSource(bitmap);
            HasPreview = true;
            PreviewInfo = $"{bitmap.Width} pontos × {bitmap.Height} linhas " +
                          $"({bitmap.Height * 0.125:0.0} mm de papel aprox.)";
            StatusMessage = "Documento gerado. Confira a pré-visualização antes de imprimir.";
        }
        catch (Exception ex)
        {
            PreviewImage = null;
            HasPreview = false;
            PreviewInfo = $"Falha ao gerar o documento: {ex.Message}";
            StatusMessage = PreviewInfo;
        }
        finally
        {
            IsRendering = false;
        }
    }

    private async Task PrintAsync()
    {
        if (!ValidateAll(out _))
        {
            HasPreview = false;
            return;
        }

        if (!_host.Services.Printer.IsConnected)
        {
            StatusMessage = "Conecte a impressora antes de imprimir.";
            MiniPrint.Desktop.Windows.MessageDialog.Show(
                "Conecte a impressora na aba Impressora antes de imprimir este documento.",
                "MiniPrint Desktop",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        IsPrinting = true;
        Progress = 0;
        ProgressMessage = "Gerando documento...";

        try
        {
            var bitmap = await Task.Run(Render);
            PreviewImage = PreviewRenderer.ToBitmapSource(bitmap);
            HasPreview = true;

            var document = new PrintDocument
            {
                Bitmap = bitmap,
                Kind = PrintJobKind.Modelo,
                Title = BuildHistoryTitle(),
            };

            ProgressMessage = "Enviando para a impressora...";

            var progress = new Progress<PrintProgress>(p =>
            {
                Progress = p.Fraction;
                ProgressMessage = p.Message;
            });

            var result = await _host.Services.Printer.PrintAsync(
                document,
                _host.Services.Settings.Printer,
                progress);

            var entry = new PrintHistoryEntry
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
            };

            _host.Services.JobRecorded?.Invoke(entry);

            if (result.Success)
            {
                Progress = 1;
                StatusMessage = result.WasSimulated
                    ? "Simulação concluída: nenhum papel foi impresso."
                    : $"Documento impresso em {result.Duration.TotalSeconds:0.0} s.";
                ProgressMessage = result.WasSimulated ? "Simulação concluída" : "Impressão concluída";
            }
            else
            {
                StatusMessage = $"Falha na impressão: {result.ErrorMessage}";
                ProgressMessage = "Falha na impressão";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao imprimir: {ex.Message}";
            _host.Services.Log.Error("Modelos", StatusMessage);
        }
        finally
        {
            IsPrinting = false;
        }
    }

    private void Clear()
    {
        foreach (var input in Inputs)
        {
            input.Reset();
        }

        HasPreview = false;
        PreviewImage = null;
        PreviewInfo = "Preencha os campos e clique em Visualizar documento.";
        StatusMessage = "Campos limpos.";
    }

    private void HandleError(Exception ex)
    {
        StatusMessage = ex.Message;
        _host.Services.Log.Error("Modelos", ex.ToString());
    }

    /// <summary>Resumo dos valores preenchidos, usado em diagnóstico.</summary>
    public string DescribeValues()
    {
        var builder = new StringBuilder();
        foreach (var input in Inputs)
        {
            builder.AppendLine($"{input.Key}={input.Value}");
        }

        return builder.ToString();
    }
}
