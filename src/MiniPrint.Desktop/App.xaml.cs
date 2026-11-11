using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using MiniPrint.Desktop.Services;
using MiniPrint.Desktop.ViewModels;

namespace MiniPrint.Desktop;

public partial class App : Application
{
    private AppServices? _services;
    private StringBuilder? _bindingTrace;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _services?.Log.Error("Aplicativo", $"Tarefa não observada: {args.Exception}");
            args.SetObserved();
        };

        var selfTest = e.Args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase));
        if (selfTest)
        {
            AttachBindingTrace();
        }

        try
        {
            _services = AppServices.CreateDefault();
            var viewModel = new MainViewModel(_services);

            var window = new MainWindow
            {
                DataContext = viewModel,
            };

            window.Closed += async (_, _) =>
            {
                viewModel.PersistState();
                await _services.Printer.DisposeAsync();
            };

            MainWindow = window;
            window.Show();

            if (selfTest)
            {
                RunSelfTest(window, viewModel);
            }
        }
        catch (Exception ex)
        {
            _services?.Log.Error("Aplicativo", $"Falha ao iniciar: {ex}");

            if (!selfTest)
            {
                MiniPrint.Desktop.Windows.MessageDialog.Show(
                    $"Não foi possível iniciar o MiniPrint Desktop.\n\n{ex.Message}",
                    "MiniPrint Desktop",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            Shutdown(1);
        }
    }

    /// <summary>
    /// Modo de verificação automática: abre a janela, deixa o layout e a
    /// pré-visualização rodarem e grava um relatório com os erros de binding
    /// no XAML. Usado na validação da interface antes de publicar.
    /// </summary>
    private void RunSelfTest(Window window, MainViewModel viewModel)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await Task.Delay(2500);

                // Percorre todas as abas para forçar a criação das telas.
                for (var tab = 0; tab < 6; tab++)
                {
                    viewModel.SelectedTab = tab;
                    window.UpdateLayout();
                    await Task.Delay(250);
                }

                // Exercita as três telas da aba Modelos sem gravar no disco:
                // o editor abre um modelo de exemplo e o preenchimento usa o
                // mesmo modelo em memória.
                var probe = Core.Templates.TemplateSamples.CreatePaymentReceipt();
                var editorPreviewOk = false;
                var visualPreviewOk = false;
                var visualElements = 0;
                var fillInputs = 0;

                viewModel.SelectedTab = 2;
                viewModel.Templates.EditTemplate(probe, isNew: true);
                window.UpdateLayout();
                await Task.Delay(900);

                if (viewModel.Templates.Current is TemplateEditorViewModel editor)
                {
                    editorPreviewOk = editor.PreviewImage is not null;
                }

                // Editor visual: abre um modelo convertido e confere a prévia.
                var visual = Imaging.VisualTemplateSamples.Convert(
                    Core.Templates.TemplateSamples.CreatePaymentReceipt());
                viewModel.Templates.EditTemplate(visual, isNew: true);
                window.UpdateLayout();
                await Task.Delay(1200);

                if (viewModel.Templates.Current is VisualEditorViewModel visualEditor)
                {
                    visualPreviewOk = visualEditor.PreviewImage is not null;
                    visualElements = visualEditor.Elements.Count;
                }

                viewModel.Templates.UseTemplate(probe);
                window.UpdateLayout();
                await Task.Delay(600);

                if (viewModel.Templates.Current is TemplateFillViewModel fill)
                {
                    fillInputs = fill.Inputs.Count;
                }

                viewModel.Templates.ShowTemplateList();
                window.UpdateLayout();
                await Task.Delay(300);

                viewModel.SelectedTab = 0;
                window.UpdateLayout();
                await Task.Delay(300);

                var report = BuildSelfTestReport(viewModel, editorPreviewOk, visualPreviewOk, visualElements, fillInputs);
                var reportPath = Path.Combine(
                    Core.Configuration.AppPaths.DataDirectory,
                    "selftest.txt");
                File.WriteAllText(reportPath, report, Encoding.UTF8);

                _services?.Log.Info("Aplicativo", $"Autoverificação gravada em {reportPath}");
                Shutdown(report.StartsWith("FALHA", StringComparison.Ordinal) ? 2 : 0);
            }
            catch (Exception ex)
            {
                _services?.Log.Error("Aplicativo", $"Autoverificação falhou: {ex}");
                Shutdown(3);
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private string BuildSelfTestReport(
        MainViewModel viewModel,
        bool editorPreviewOk,
        bool visualPreviewOk,
        int visualElements,
        int fillInputs)
    {
        var builder = new StringBuilder();
        var bindingErrors = ExtractBindingErrors();
        var templatesOk = editorPreviewOk && visualPreviewOk && visualElements > 0 && fillInputs > 0;

        builder.AppendLine(bindingErrors.Count == 0 && templatesOk ? "OK" : "FALHA");
        builder.AppendLine($"Data: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Abas verificadas: 6");
        builder.AppendLine($"Erros de binding: {bindingErrors.Count}");
        builder.AppendLine($"Pré-visualização: {(viewModel.Print.PreviewImage is null ? "vazia" : "gerada")}");
        builder.AppendLine($"Linhas na pré-visualização: {viewModel.Print.PreviewLines}");
        builder.AppendLine($"Dispositivos listados: {viewModel.Printer.Devices.Count}");
        builder.AppendLine($"Bluetooth: {viewModel.Printer.BluetoothMessage}");
        builder.AppendLine($"Modelos salvos: {viewModel.Templates.Templates.Count}");
        builder.AppendLine($"Editor de modelo: {(editorPreviewOk ? "prévia gerada" : "SEM PRÉVIA")}");
        builder.AppendLine($"Editor visual: {(visualPreviewOk ? "prévia gerada" : "SEM PRÉVIA")} " +
                           $"({visualElements} elemento(s))");
        builder.AppendLine($"Formulário do modelo: {fillInputs} campo(s)");
        builder.AppendLine($"Histórico: {viewModel.History.Entries.Count} registro(s)");
        builder.AppendLine($"Log: {viewModel.Diagnostics.Entries.Count} linha(s)");
        builder.AppendLine();

        if (bindingErrors.Count > 0)
        {
            builder.AppendLine("--- Erros de binding ---");
            foreach (var error in bindingErrors.Take(60))
            {
                builder.AppendLine(error);
            }
        }

        return builder.ToString();
    }

    private List<string> ExtractBindingErrors()
    {
        var errors = new List<string>();
        if (_bindingTrace is null)
        {
            return errors;
        }

        foreach (var line in _bindingTrace.ToString().Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("Cannot find", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("não é possível", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(trimmed);
            }
        }

        return errors;
    }

    private void AttachBindingTrace()
    {
        _bindingTrace = new StringBuilder();
        var writer = new StringWriter(_bindingTrace);
        var listener = new TextWriterTraceListener(writer);

        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _services?.Log.Error("Aplicativo", $"Exceção não tratada: {e.Exception}");

        MiniPrint.Desktop.Windows.MessageDialog.Show(
            $"Ocorreu um erro inesperado, mas o aplicativo continua aberto.\n\n{e.Exception.Message}\n\n" +
            "Detalhes foram gravados no log de diagnóstico.",
            "MiniPrint Desktop",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _services?.Log.Error("Aplicativo", $"Falha fatal: {exception}");
        }
    }
}
