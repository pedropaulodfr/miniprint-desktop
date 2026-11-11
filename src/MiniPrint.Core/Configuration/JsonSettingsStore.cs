using System.Text.Json;
using MiniPrint.Core.Abstractions;
using MiniPrint.Core.Configuration;

namespace MiniPrint.Core.Configuration;

/// <summary>Caminhos usados pelo aplicativo (somente dados do próprio app).</summary>
public static class AppPaths
{
    public const string AppFolderName = "MiniPrintDesktop";

    public static string DataDirectory
    {
        get
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(baseDir, AppFolderName);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string HistoryFile => Path.Combine(DataDirectory, "history.json");

    public static string TemplatesFile => Path.Combine(DataDirectory, "templates.json");

    public static string ExportsDirectory
    {
        get
        {
            var dir = Path.Combine(DataDirectory, "exports");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}

/// <summary>Persistência simples em JSON com gravação atômica.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly object _gate = new();

    public JsonSettingsStore(string? path = null)
    {
        _path = path ?? AppPaths.SettingsFile;
    }

    public AppSettings Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return new AppSettings();
                }

                var json = File.ReadAllText(_path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
                return settings ?? new AppSettings();
            }
            catch (Exception)
            {
                // Arquivo corrompido não deve impedir o app de abrir.
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(settings, Options);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
        }
    }
}

public sealed class JsonPrintHistoryStore : IPrintHistoryStore
{
    private const int MaxEntries = 500;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _gate = new();

    public JsonPrintHistoryStore(string? path = null)
    {
        _path = path ?? AppPaths.HistoryFile;
    }

    public IReadOnlyList<MiniPrint.Core.Models.PrintHistoryEntry> Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return Array.Empty<MiniPrint.Core.Models.PrintHistoryEntry>();
                }

                var json = File.ReadAllText(_path);
                var list = JsonSerializer.Deserialize<List<MiniPrint.Core.Models.PrintHistoryEntry>>(json, Options);
                return list ?? new List<MiniPrint.Core.Models.PrintHistoryEntry>();
            }
            catch (Exception)
            {
                return Array.Empty<MiniPrint.Core.Models.PrintHistoryEntry>();
            }
        }
    }

    public void Save(IReadOnlyList<MiniPrint.Core.Models.PrintHistoryEntry> entries)
    {
        lock (_gate)
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var trimmed = entries.Take(MaxEntries).ToList();
            var json = JsonSerializer.Serialize(trimmed, Options);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
        }
    }
}
