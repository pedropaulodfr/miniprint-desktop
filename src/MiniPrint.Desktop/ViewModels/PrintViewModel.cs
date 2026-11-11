using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MiniPrint.Core.Configuration;
using MiniPrint.Core.Models;
using MiniPrint.Desktop.Services;
using MiniPrint.Imaging;
using MiniPrint.Protocol;
using Microsoft.Win32;
using TextAlignment = MiniPrint.Core.Configuration.TextAlignment;

namespace MiniPrint.Desktop.ViewModels;

public sealed class PrintViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Func<bool> _isPrinterConnected;
    private readonly Action<PrintHistoryEntry> _recordJob;

    private readonly DispatcherTimer _previewTimer;
    private readonly DispatcherTimer _saveTimer;
    private CancellationTokenSource? _printCts;
    private int _renderGeneration;

    private MonochromeBitmap? _previewBitmap;
    private bool _previewDirty = true;
    private bool _isRendering;
    private bool _isPrinting;
    private double _progress;
    private string _progressMessage = string.Empty;
    private string _statusMessage = "Pronto para imprimir.";
    private string _previewInfo = "A pré-visualização aparece aqui.";
    private int _contentTab;

    // Texto
    private string _text = string.Empty;
    private string _fontFamily = "Segoe UI";
    private double _fontSize = 14;
    private bool _bold;
    private bool _italic;
    private TextAlignment _alignment = TextAlignment.Esquerda;
    private int _marginLeft = 10;
    private int _marginRight = 10;
    private int _marginTop = 6;
    private int _marginBottom = 6;
    private double _lineSpacing = 1.15;
    private bool _wordWrap = true;

    // Imagem
    private string? _imagePath;
    private string _imageFileName = string.Empty;
    private BitmapSource? _originalImage;
    private double _brightness = 1.0;
    private double _contrast = 1.0;
    private int _threshold = 128;
    private int _rotation;
    private bool _invert;
    private double _cropLeft;
    private double _cropRight;
    private double _cropTop;
    private double _cropBottom;
    private DitheringMode _dithering = DitheringMode.FloydSteinberg;
    private FitMode _fit = FitMode.PreencherLargura;
    private bool _useCustomEnergy;
    private int _energyValue = 0x4000;

    private BitmapSource? _previewImage;
    private int _previewLines;

    public PrintViewModel(
        AppServices services,
        Func<bool> isPrinterConnected,
        Action<PrintHistoryEntry> recordJob)
    {
        _services = services;
        _isPrinterConnected = isPrinterConnected;
        _recordJob = recordJob;

        LoadFromSettings();

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _previewTimer.Tick += async (_, _) =>
        {
            _previewTimer.Stop();
            await RenderPreviewAsync();
        };

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            PersistToSettings();
            _services.SaveSettings();
        };

        ChooseImageCommand = new AsyncRelayCommand(_ => ChooseImageAsync(), null, HandleError);
        ClearImageCommand = new RelayCommand(ClearImage);
        RefreshPreviewCommand = new AsyncRelayCommand(_ => RenderPreviewAsync(), null, HandleError);
        PrintCommand = new AsyncRelayCommand(_ => PrintAsync(null), CanPrint, HandleError);
        PrintTestCommand = new AsyncRelayCommand(_ => PrintTestPageAsync(), CanPrint, HandleError);
        CancelCommand = new RelayCommand(CancelPrint, () => IsPrinting);
        SavePreviewCommand = new AsyncRelayCommand(_ => SavePreviewAsync(), _ => _previewBitmap is not null, HandleError);
        SaveJobBytesCommand = new AsyncRelayCommand(_ => SaveJobBytesAsync(), _ => _previewBitmap is not null, HandleError);

        _ = RenderPreviewAsync();
    }

    public ObservableCollection<string> FontFamilies { get; } = new()
    {
        "Segoe UI", "Arial", "Calibri", "Consolas", "Courier New", "Georgia",
        "Tahoma", "Times New Roman", "Trebuchet MS", "Verdana",
    };

    public IReadOnlyList<TextAlignment> Alignments { get; } =
        new[] { TextAlignment.Esquerda, TextAlignment.Centro, TextAlignment.Direita };

    public IReadOnlyList<DitheringMode> DitheringModes { get; } =
        new[]
        {
            DitheringMode.FloydSteinberg, DitheringMode.Atkinson,
            DitheringMode.Bayer4, DitheringMode.Bayer8, DitheringMode.Limiar,
        };

    public IReadOnlyList<FitMode> FitModes { get; } =
        new[] { FitMode.PreencherLargura, FitMode.TamanhoOriginal };

    public IReadOnlyList<int> Rotations { get; } = new[] { 0, 90, 180, 270 };

    public AsyncRelayCommand ChooseImageCommand { get; }

    public RelayCommand ClearImageCommand { get; }

    public AsyncRelayCommand RefreshPreviewCommand { get; }

    public AsyncRelayCommand PrintCommand { get; }

    public AsyncRelayCommand PrintTestCommand { get; }

    public RelayCommand CancelCommand { get; }

    public AsyncRelayCommand SavePreviewCommand { get; }

    public AsyncRelayCommand SaveJobBytesCommand { get; }

    // ---------- estado ----------

    public int ContentTab
    {
        get => _contentTab;
        set
        {
            if (SetProperty(ref _contentTab, value))
            {
                InvalidatePreview();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string PreviewInfo
    {
        get => _previewInfo;
        private set => SetProperty(ref _previewInfo, value);
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
                OnPropertyChanged(nameof(PrintButtonText));
                CancelCommand.RaiseCanExecuteChanged();
                PrintCommand.RaiseCanExecuteChanged();
                PrintTestCommand.RaiseCanExecuteChanged();
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

    public BitmapSource? PreviewImage
    {
        get => _previewImage;
        private set => SetProperty(ref _previewImage, value);
    }

    public int PreviewLines
    {
        get => _previewLines;
        private set => SetProperty(ref _previewLines, value);
    }

    public int PaperWidth => _services.PaperWidth;

    public double PreviewScale
    {
        get => _previewScale;
        set => SetProperty(ref _previewScale, Math.Clamp(value, 0.5, 3));
    }

    private double _previewScale = 1.5;

    public bool HasImage => _imagePath is not null;

    // ---------- texto ----------

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value))
            {
                _services.Settings.LastText = value;
                ScheduleSave();
                OnParameterChanged();
            }
        }
    }

    public string FontFamilyName
    {
        get => _fontFamily;
        set => SetParam(ref _fontFamily, value);
    }

    public double FontSize
    {
        get => _fontSize;
        set => SetParam(ref _fontSize, Math.Clamp(value, 6, 48));
    }

    public bool Bold
    {
        get => _bold;
        set => SetParam(ref _bold, value);
    }

    public bool Italic
    {
        get => _italic;
        set => SetParam(ref _italic, value);
    }

    public TextAlignment Alignment
    {
        get => _alignment;
        set => SetParam(ref _alignment, value);
    }

    public int MarginLeft
    {
        get => _marginLeft;
        set => SetParam(ref _marginLeft, Math.Clamp(value, 0, 100));
    }

    public int MarginRight
    {
        get => _marginRight;
        set => SetParam(ref _marginRight, Math.Clamp(value, 0, 100));
    }

    public int MarginTop
    {
        get => _marginTop;
        set => SetParam(ref _marginTop, Math.Clamp(value, 0, 100));
    }

    public int MarginBottom
    {
        get => _marginBottom;
        set => SetParam(ref _marginBottom, Math.Clamp(value, 0, 100));
    }

    public double LineSpacing
    {
        get => _lineSpacing;
        set => SetParam(ref _lineSpacing, Math.Clamp(value, 0.8, 2.5));
    }

    public bool WordWrap
    {
        get => _wordWrap;
        set => SetParam(ref _wordWrap, value);
    }

    // ---------- imagem ----------

    public string ImageFileName
    {
        get => _imageFileName;
        private set => SetProperty(ref _imageFileName, value);
    }

    public BitmapSource? OriginalImage
    {
        get => _originalImage;
        private set => SetProperty(ref _originalImage, value);
    }

    public double Brightness
    {
        get => _brightness;
        set => SetParam(ref _brightness, Math.Clamp(Math.Round(value, 2), 0.4, 1.6));
    }

    public double Contrast
    {
        get => _contrast;
        set => SetParam(ref _contrast, Math.Clamp(Math.Round(value, 2), 0.4, 2.0));
    }

    public int Threshold
    {
        get => _threshold;
        set => SetParam(ref _threshold, Math.Clamp(value, 1, 254));
    }

    public int Rotation
    {
        get => _rotation;
        set => SetParam(ref _rotation, value);
    }

    public bool Invert
    {
        get => _invert;
        set => SetParam(ref _invert, value);
    }

    public double CropLeft
    {
        get => _cropLeft;
        set => SetParam(ref _cropLeft, Math.Clamp(Math.Round(value, 2), 0, 0.45));
    }

    public double CropRight
    {
        get => _cropRight;
        set => SetParam(ref _cropRight, Math.Clamp(Math.Round(value, 2), 0, 0.45));
    }

    public double CropTop
    {
        get => _cropTop;
        set => SetParam(ref _cropTop, Math.Clamp(Math.Round(value, 2), 0, 0.45));
    }

    public double CropBottom
    {
        get => _cropBottom;
        set => SetParam(ref _cropBottom, Math.Clamp(Math.Round(value, 2), 0, 0.45));
    }

    public DitheringMode Dithering
    {
        get => _dithering;
        set => SetParam(ref _dithering, value);
    }

    public FitMode Fit
    {
        get => _fit;
        set => SetParam(ref _fit, value);
    }

    /// <summary>Usa a energia personalizada nesta impressão, em vez da configuração global.</summary>
    public bool UseCustomEnergy
    {
        get => _useCustomEnergy;
        set => SetParam(ref _useCustomEnergy, value);
    }

    /// <summary>Intensidade de impressão (energia térmica, 0 a 65535).</summary>
    public int EnergyValue
    {
        get => _energyValue;
        set => SetParam(ref _energyValue, Math.Clamp(value, 0, 65535));
    }

    // ---------- comandos ----------

    private bool CanPrint(object? parameter) =>
        !IsPrinting && !IsRendering;

    private async Task ChooseImageAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolher imagem",
            Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp|PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|Bitmap (*.bmp)|*.bmp",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await LoadImageAsync(dialog.FileName);
    }

    private async Task LoadImageAsync(string path)
    {
        try
        {
            var original = PreviewRenderer.TryLoadFile(path);
            if (original is null)
            {
                StatusMessage = "Não foi possível abrir a imagem selecionada.";
                return;
            }

            _imagePath = path;
            ImageFileName = Path.GetFileName(path);
            OriginalImage = original;
            OnPropertyChanged(nameof(HasImage));
            ContentTab = 1;

            StatusMessage = $"Imagem carregada: {ImageFileName}";
            InvalidatePreview();
            await RenderPreviewAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Falha ao carregar imagem: {ex.Message}";
        }
    }

    /// <summary>Usado pelo arrastar-e-soltar da janela.</summary>
    public async Task LoadDroppedFileAsync(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".png" or ".jpg" or ".jpeg" or ".bmp")
        {
            await LoadImageAsync(path);
        }
        else if (extension is ".txt")
        {
            Text = await File.ReadAllTextAsync(path);
            ContentTab = 0;
        }
    }

    private void ClearImage()
    {
        _imagePath = null;
        ImageFileName = string.Empty;
        OriginalImage = null;
        OnPropertyChanged(nameof(HasImage));
        InvalidatePreview();
        StatusMessage = "Imagem removida.";
    }

    private void CancelPrint()
    {
        _printCts?.Cancel();
        ProgressMessage = "Cancelando...";
    }

    private async Task PrintTestPageAsync()
    {
        var bitmap = TestPatterns.DiagnosticPage(_services.PaperWidth, Dithering);
        var document = new PrintDocument
        {
            Bitmap = bitmap,
            Kind = PrintJobKind.TestPattern,
            Title = "Página de diagnóstico",
        };

        await ExecutePrintAsync(document);
    }

    private async Task PrintAsync(object? parameter)
    {
        MonochromeBitmap? bitmap;
        try
        {
            bitmap = await EnsureBitmapAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Falha ao preparar a imagem: {ex.Message}";
            return;
        }

        if (bitmap is null)
        {
            return;
        }

        var kind = ContentTab == 1 && HasImage ? PrintJobKind.Image : PrintJobKind.Text;
        var title = kind == PrintJobKind.Image
            ? ImageFileName
            : BuildTextTitle();

        var document = new PrintDocument
        {
            Bitmap = bitmap,
            Kind = kind,
            Title = string.IsNullOrWhiteSpace(title) ? "Documento" : title,
        };

        await ExecutePrintAsync(document);
    }

    private async Task ExecutePrintAsync(PrintDocument document)
    {
        if (!_isPrinterConnected())
        {
            StatusMessage = "Conecte a impressora antes de imprimir.";
            return;
        }

        IsPrinting = true;
        Progress = 0;
        ProgressMessage = "Preparando impressão...";
        StatusMessage = "Enviando trabalho...";
        _printCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<PrintProgress>(p =>
            {
                Progress = p.Fraction;
                ProgressMessage = p.Message;
            });

            var result = await _services.Printer.PrintAsync(
                document,
                _services.Settings.Printer,
                progress,
                _printCts.Token,
                UseCustomEnergy ? (ushort)EnergyValue : null);

            var entry = new PrintHistoryEntry
            {
                Title = document.Title,
                Kind = document.Kind,
                Status = result.Status,
                ErrorMessage = result.ErrorMessage,
                WasSimulated = result.WasSimulated,
                DeviceName = _services.Printer.DeviceName,
                LinesPrinted = result.LinesPrinted,
                BytesSent = result.BytesSent,
                DurationSeconds = result.Duration.TotalSeconds,
            };

            _recordJob(entry);

            if (result.Success)
            {
                Progress = 1;
                ProgressMessage = result.WasSimulated
                    ? "Simulação concluída (nenhum papel impresso)"
                    : "Impressão concluída";

                StatusMessage = result.WasSimulated
                    ? $"Simulação validada: {result.LinesPrinted} linhas, {result.BytesSent} bytes. Nenhum papel foi impresso."
                    : $"Impressão concluída em {result.Duration.TotalSeconds:0.0} s ({result.LinesPrinted} linhas).";

                if (result.WasSimulated && _services.Printer.LastSimulation is { } report)
                {
                    StatusMessage += " " + report.Summary;
                    foreach (var error in report.Errors)
                    {
                        _services.Log.Error("Simulador", error);
                    }
                }
            }
            else
            {
                ProgressMessage = "Falha na impressão";
                StatusMessage = $"Falha: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro inesperado: {ex.Message}";
            _services.Log.Error("Impressão", ex.ToString());
        }
        finally
        {
            IsPrinting = false;
            _printCts?.Dispose();
            _printCts = null;
        }
    }

    private async Task<MonochromeBitmap?> EnsureBitmapAsync()
    {
        if (_previewDirty || _previewBitmap is null)
        {
            await RenderPreviewAsync();
        }

        return _previewBitmap;
    }

    private string BuildTextTitle()
    {
        var firstLine = Text.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return "Texto";
        }

        firstLine = firstLine.Trim();
        return firstLine.Length <= 40 ? firstLine : firstLine[..40] + "…";
    }

    // ---------- renderização da pré-visualização ----------

    private void SetParam<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (SetProperty(ref field, value, name))
        {
            OnParameterChanged();
        }
    }

    private void OnParameterChanged()
    {
        InvalidatePreview();
        ScheduleSave();
    }

    private void InvalidatePreview()
    {
        _previewDirty = true;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private async Task RenderPreviewAsync()
    {
        var generation = ++_renderGeneration;
        IsRendering = true;
        PreviewInfo = "Gerando pré-visualização...";

        try
        {
            var snapshot = new RenderRequest(
                _contentTab,
                _text,
                BuildTextSettings(),
                _imagePath,
                BuildImageOptions(),
                _services.PaperWidth);

            var bitmap = await Task.Run(() => BuildBitmap(snapshot));

            if (generation != _renderGeneration)
            {
                return;
            }

            _previewBitmap = bitmap;
            _previewDirty = false;
            PreviewImage = PreviewRenderer.ToBitmapSource(bitmap);
            PreviewLines = bitmap.Height;
            PreviewInfo = $"{bitmap.Width} pontos de largura × {bitmap.Height} linhas " +
                          $"({bitmap.Height * 0.125:0.0} mm de papel aprox.)";

            StatusMessage = _contentTab == 1 && _imagePath is null
                ? "Escolha uma imagem para imprimir."
                : "Pré-visualização atualizada.";
        }
        catch (Exception ex)
        {
            if (generation == _renderGeneration)
            {
                PreviewImage = null;
                PreviewLines = 0;
                PreviewInfo = $"Não foi possível gerar a pré-visualização: {ex.Message}";
                StatusMessage = PreviewInfo;
            }
        }
        finally
        {
            if (generation == _renderGeneration)
            {
                IsRendering = false;
                PrintCommand.RaiseCanExecuteChanged();
                PrintTestCommand.RaiseCanExecuteChanged();
                SavePreviewCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private static MonochromeBitmap BuildBitmap(RenderRequest request)
    {
        if (request.Tab == 1 && request.ImagePath is not null)
        {
            return ImageProcessor.ProcessFile(request.ImagePath, request.ImageOptions);
        }

        return TextRenderer.Render(
            request.Text,
            request.TextSettings,
            request.PaperWidth);
    }

    private TextSettings BuildTextSettings() => new()
    {
        FontFamily = _fontFamily,
        FontSize = _fontSize,
        Bold = _bold,
        Italic = _italic,
        Alignment = _alignment,
        MarginLeft = _marginLeft,
        MarginRight = _marginRight,
        MarginTop = _marginTop,
        MarginBottom = _marginBottom,
        LineSpacing = _lineSpacing,
        WordWrap = _wordWrap,
    };

    private ImagePrintOptions BuildImageOptions() => new()
    {
        PaperWidth = _services.PaperWidth,
        Dithering = _dithering,
        Threshold = _threshold,
        Invert = _invert,
        Brightness = _brightness,
        Contrast = _contrast,
        Rotation = _rotation,
        CropLeft = _cropLeft,
        CropRight = _cropRight,
        CropTop = _cropTop,
        CropBottom = _cropBottom,
        Fit = _fit,
        MarginLeft = 0,
        MarginRight = 0,
    };

    private async Task SaveJobBytesAsync()
    {
        var bitmap = await EnsureBitmapAsync();
        if (bitmap is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Salvar dados preparados para impressão",
            Filter = "Dados binários (*.bin)|*.bin",
            FileName = "miniprint-trabalho.bin",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var kind = ContentTab == 1 && HasImage ? PrintJobKind.Image : PrintJobKind.Text;
        var printer = _services.Settings.Printer;
        var energy = UseCustomEnergy
            ? (ushort)EnergyValue
            : (kind == PrintJobKind.Text ? printer.TextEnergy : printer.ImageEnergy);
        var options = CatPrinterJobOptions.FromSettings(printer, null, energy);

        var document = new PrintDocument
        {
            Bitmap = bitmap,
            Kind = kind,
            Title = "exportação de pacotes",
        };

        try
        {
            var (bytes, summary) = await Task.Run(() =>
            {
                var plan = CatPrinterSequence.BuildJob(document, options);
                var all = new byte[plan.TotalBytes];
                var offset = 0;
                var builder = new StringBuilder();

                builder.AppendLine("MiniPrint Desktop - dados preparados para impressão");
                builder.AppendLine($"Imagem: {bitmap.Width} x {bitmap.Height} pontos");
                builder.AppendLine($"Frames: {plan.PacketCount}");
                builder.AppendLine($"Bytes: {plan.TotalBytes}");
                builder.AppendLine($"Energia: 0x{energy:X4}   Velocidade: {options.Speed}");
                builder.AppendLine($"Avanço final: {options.FeedLines}");
                builder.AppendLine();
                builder.AppendLine("Índice  Cmd    Tam  Primeiros bytes");

                var index = 0;
                foreach (var frame in plan.Frames)
                {
                    Buffer.BlockCopy(frame, 0, all, offset, frame.Length);
                    offset += frame.Length;

                    if (index < 40)
                    {
                        builder.AppendLine(
                            $"{index,6}  0x{frame[2]:X2}  {frame[4],3}  {CatPrinterFrame.ToHex(frame, 16)}");
                    }

                    index++;
                }

                if (plan.PacketCount > 40)
                {
                    builder.AppendLine($"... ({plan.PacketCount - 40} frames omitidos)");
                }

                return (all, builder.ToString());
            });

            await File.WriteAllBytesAsync(dialog.FileName, bytes);
            var summaryPath = Path.ChangeExtension(dialog.FileName, ".txt");
            await File.WriteAllTextAsync(summaryPath, summary, new UTF8Encoding(false));

            StatusMessage = $"Pacotes salvos: {bytes.Length} bytes em {dialog.FileName} " +
                            $"(resumo em {Path.GetFileName(summaryPath)}).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Falha ao salvar os pacotes: {ex.Message}";
        }
    }

    private async Task SavePreviewAsync()
    {
        if (_previewBitmap is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Salvar pré-visualização",
            Filter = "Imagem PNG (*.png)|*.png",
            FileName = "miniprint-previsualizacao.png",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await Task.Run(() => PreviewRenderer.SavePng(_previewBitmap, dialog.FileName));
            StatusMessage = $"Pré-visualização salva em {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Falha ao salvar: {ex.Message}";
        }
    }

    // ---------- persistência ----------

    private void LoadFromSettings()
    {
        var text = _services.Settings.Text;
        var image = _services.Settings.Image;

        _text = _services.Settings.LastText;
        _fontFamily = text.FontFamily;
        _fontSize = text.FontSize;
        _bold = text.Bold;
        _italic = text.Italic;
        _alignment = text.Alignment;
        _marginLeft = text.MarginLeft;
        _marginRight = text.MarginRight;
        _marginTop = text.MarginTop;
        _marginBottom = text.MarginBottom;
        _lineSpacing = text.LineSpacing;
        _wordWrap = text.WordWrap;

        _brightness = image.Brightness;
        _contrast = image.Contrast;
        _threshold = image.Threshold;
        _rotation = image.Rotation;
        _invert = image.Invert;
        _dithering = image.Dithering;
        _useCustomEnergy = image.OverrideEnergy;
        _energyValue = image.CustomEnergy;

        if (string.IsNullOrWhiteSpace(_text))
        {
            _text = "MiniPrint Desktop\n\nDigite seu texto aqui." + Environment.NewLine +
                    Environment.NewLine +
                    "A pré-visualização ao lado mostra exatamente o que será impresso.";
        }
    }

    public void PersistToSettings()
    {
        var text = _services.Settings.Text;
        text.FontFamily = _fontFamily;
        text.FontSize = _fontSize;
        text.Bold = _bold;
        text.Italic = _italic;
        text.Alignment = _alignment;
        text.MarginLeft = _marginLeft;
        text.MarginRight = _marginRight;
        text.MarginTop = _marginTop;
        text.MarginBottom = _marginBottom;
        text.LineSpacing = _lineSpacing;
        text.WordWrap = _wordWrap;

        var image = _services.Settings.Image;
        image.Brightness = _brightness;
        image.Contrast = _contrast;
        image.Threshold = _threshold;
        image.Rotation = _rotation;
        image.Invert = _invert;
        image.Dithering = _dithering;
        image.OverrideEnergy = _useCustomEnergy;
        image.CustomEnergy = (ushort)Math.Clamp(_energyValue, 0, 65535);

        _services.Settings.LastText = _text;
    }

    private void HandleError(Exception ex)
    {
        _services.Log.Error("UI", $"Erro: {ex.Message}");
        MiniPrint.Desktop.Windows.MessageDialog.Show(ex.Message, "MiniPrint Desktop", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private sealed record RenderRequest(
        int Tab,
        string Text,
        TextSettings TextSettings,
        string? ImagePath,
        ImagePrintOptions ImageOptions,
        int PaperWidth);
}
