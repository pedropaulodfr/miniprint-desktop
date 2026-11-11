using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using MiniPrint.Core.Models;

namespace MiniPrint.Desktop.ViewModels;

public sealed class HistoryViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private string _statusMessage = string.Empty;

    public HistoryViewModel(AppServices services)
    {
        _services = services;

        ClearCommand = new RelayCommand(Clear, () => Entries.Count > 0);
        ExportCommand = new RelayCommand(ExportCsv, () => Entries.Count > 0);
        RefreshCommand = new RelayCommand(Load);

        Load();
    }

    public ObservableCollection<PrintHistoryEntry> Entries { get; } = new();

    public RelayCommand ClearCommand { get; }

    public RelayCommand ExportCommand { get; }

    public RelayCommand RefreshCommand { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsEmpty => Entries.Count == 0;

    public void Load()
    {
        Entries.Clear();
        foreach (var entry in _services.HistoryStore.Load().OrderByDescending(e => e.Timestamp))
        {
            Entries.Add(entry);
        }

        UpdateStatus();
    }

    public void Record(PrintHistoryEntry entry)
    {
        Entries.Insert(0, entry);

        while (Entries.Count > 500)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }

        Persist();
        UpdateStatus();
    }

    private void Clear()
    {
        if (Entries.Count == 0)
        {
            return;
        }

        var confirm = MiniPrint.Desktop.Windows.MessageDialog.Show(
            "Limpar todo o histórico de impressões?",
            "MiniPrint Desktop",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        Entries.Clear();
        Persist();
        UpdateStatus();
        StatusMessage = "Histórico limpo.";
    }

    private void ExportCsv()
    {
        try
        {
            var path = Path.Combine(
                Core.Configuration.AppPaths.ExportsDirectory,
                $"miniprint-historico-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

            var builder = new StringBuilder();
            builder.AppendLine("Data;Conteudo;Tipo;Status;Dispositivo;Linhas;Bytes;Duracao(s);Erro");
            foreach (var entry in Entries)
            {
                builder.AppendLine(string.Join(';',
                    entry.TimestampText,
                    Escape(entry.Title),
                    entry.KindText,
                    entry.StatusText,
                    Escape(entry.DeviceName),
                    entry.LinesPrinted,
                    entry.BytesSent,
                    entry.DurationSeconds.ToString("0.0"),
                    Escape(entry.ErrorMessage ?? string.Empty)));
            }

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
            StatusMessage = $"Histórico exportado em {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Falha ao exportar: {ex.Message}";
        }
    }

    private static string Escape(string value) => value.Replace(';', ',').Replace('\n', ' ');

    private void Persist()
    {
        try
        {
            _services.HistoryStore.Save(Entries.ToList());
        }
        catch (Exception ex)
        {
            _services.Log.Error("Histórico", $"Falha ao salvar: {ex.Message}");
        }
    }

    private void UpdateStatus()
    {
        OnPropertyChanged(nameof(IsEmpty));
        ClearCommand.RaiseCanExecuteChanged();
        ExportCommand.RaiseCanExecuteChanged();

        if (Entries.Count == 0)
        {
            StatusMessage = "Nenhum trabalho registrado ainda.";
            return;
        }

        var real = Entries.Count(e => !e.WasSimulated && e.Status == PrintJobStatus.Concluido);
        var simulated = Entries.Count(e => e.WasSimulated);
        StatusMessage = $"{Entries.Count} registro(s) — {real} impressão(ões) real(is), " +
                        $"{simulated} simulação(ões).";
    }
}
