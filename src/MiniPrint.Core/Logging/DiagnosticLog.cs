using System.Collections.Concurrent;
using System.Text;
using MiniPrint.Core.Configuration;
using MiniPrint.Core.Models;

namespace MiniPrint.Core.Logging;

/// <summary>
/// Log de diagnóstico em memória, com exportação para TXT.
/// Guarda um número limitado de linhas para não crescer indefinidamente.
/// </summary>
public sealed class DiagnosticLog
{
    private const int MaxEntries = 5000;

    private readonly ConcurrentQueue<DiagnosticEntry> _entries = new();
    private readonly object _fileGate = new();
    private readonly bool _persistToDisk;

    public DiagnosticLog(bool persistToDisk = true)
    {
        _persistToDisk = persistToDisk;
    }

    public event EventHandler<DiagnosticEntry>? EntryAdded;

    public int Count => _entries.Count;

    public IReadOnlyList<DiagnosticEntry> Snapshot() => _entries.ToArray();

    public void Info(string category, string message) => Add(DiagnosticLevel.Info, category, message);

    public void Warn(string category, string message) => Add(DiagnosticLevel.Aviso, category, message);

    public void Error(string category, string message) => Add(DiagnosticLevel.Erro, category, message);

    public void Traffic(string category, string message) => Add(DiagnosticLevel.Trafego, category, message);

    public void Add(DiagnosticLevel level, string category, string message)
    {
        var entry = new DiagnosticEntry(DateTimeOffset.Now, level, category, message);
        _entries.Enqueue(entry);
        while (_entries.Count > MaxEntries && _entries.TryDequeue(out _))
        {
            // descarta os mais antigos
        }

        EntryAdded?.Invoke(this, entry);

        if (_persistToDisk)
        {
            AppendToFile(entry);
        }
    }

    private void AppendToFile(DiagnosticEntry entry)
    {
        try
        {
            lock (_fileGate)
            {
                var path = Path.Combine(AppPaths.DataDirectory, "diagnostico.log");
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 4 * 1024 * 1024)
                {
                    var backup = path + ".old";
                    File.Copy(path, backup, overwrite: true);
                    File.Delete(path);
                }

                File.AppendAllText(path, entry + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Nunca deixar o log derrubar a impressão.
        }
    }

    /// <summary>Grava o conteúdo atual (ou um recorte) em um arquivo TXT.</summary>
    public string Export(string? destinationPath = null)
    {
        destinationPath ??= Path.Combine(
            AppPaths.ExportsDirectory,
            $"miniprint-diagnostico-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var sb = new StringBuilder();
        sb.AppendLine("MiniPrint Desktop - Log de diagnóstico");
        sb.AppendLine($"Gerado em: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}");
        sb.AppendLine($"Sistema: {Environment.OSVersion} / .NET {Environment.Version}");
        sb.AppendLine($"Máquina: {Environment.MachineName}");
        sb.AppendLine($"Processador: {Environment.ProcessorCount} núcleos");
        sb.AppendLine(new string('-', 100));
        foreach (var entry in _entries)
        {
            sb.AppendLine(entry.ToString());
        }

        sb.AppendLine(new string('-', 100));
        sb.AppendLine($"Total de linhas: {_entries.Count}");
        File.WriteAllText(destinationPath, sb.ToString(), new UTF8Encoding(true));
        return destinationPath;
    }
}
