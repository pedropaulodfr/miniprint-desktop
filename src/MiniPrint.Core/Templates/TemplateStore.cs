using System.Text.Json;
using System.Text.Json.Serialization;
using MiniPrint.Core.Configuration;

namespace MiniPrint.Core.Templates;

/// <summary>Persistência dos modelos de documento.</summary>
public interface ITemplateStore
{
    IReadOnlyList<DocumentTemplate> Load();

    void Save(IReadOnlyList<DocumentTemplate> templates);
}

/// <summary>Armazenamento em JSON, no mesmo padrão das configurações e do histórico.</summary>
public sealed class JsonTemplateStore : ITemplateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly object _gate = new();

    public JsonTemplateStore(string? path = null)
    {
        _path = path ?? AppPaths.TemplatesFile;
    }

    public IReadOnlyList<DocumentTemplate> Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return Array.Empty<DocumentTemplate>();
                }

                var json = File.ReadAllText(_path);
                var list = JsonSerializer.Deserialize<List<DocumentTemplate>>(json, Options);
                if (list is null)
                {
                    return Array.Empty<DocumentTemplate>();
                }

                foreach (var template in list)
                {
                    template.Blocks ??= new List<TemplateBlock>();
                    template.Fields ??= new List<TemplateField>();
                }

                return list;
            }
            catch (Exception)
            {
                // Arquivo corrompido não pode impedir o aplicativo de abrir.
                return Array.Empty<DocumentTemplate>();
            }
        }
    }

    public void Save(IReadOnlyList<DocumentTemplate> templates)
    {
        lock (_gate)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(templates, Options);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
        }
    }
}
