using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniPrint.Core.Configuration;
using MiniPrint.Core.Templates;

namespace MiniPrint.Desktop.Editing;

/// <summary>
/// Rascunhos do editor visual. São gravados durante a edição e usados para
/// recuperar o trabalho se o aplicativo for fechado de forma inesperada.
///
/// Ficam em uma pasta separada (<c>drafts</c>) e nunca sobrescrevem o
/// <c>templates.json</c>: se o rascunho estiver corrompido, o modelo salvo
/// continua intacto.
/// </summary>
public static class EditorDraftStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public sealed record Draft(DocumentTemplate Template, bool IsNew, DateTimeOffset SavedAt);

    private sealed class Envelope
    {
        public string Format { get; set; } = "miniprint.editor-draft";

        public int Version { get; set; } = 1;

        public bool IsNew { get; set; }

        public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;

        public DocumentTemplate? Template { get; set; }
    }

    public static string Directory => Path.Combine(AppPaths.DataDirectory, "drafts");

    public static string PathFor(Guid id) => Path.Combine(Directory, $"{id:N}.json");

    public static void Save(DocumentTemplate template, bool isNew)
    {
        System.IO.Directory.CreateDirectory(Directory);

        var envelope = new Envelope
        {
            IsNew = isNew,
            SavedAt = DateTimeOffset.Now,
            Template = template,
        };

        var path = PathFor(template.Id);
        var temporary = path + ".tmp";

        File.WriteAllText(temporary, JsonSerializer.Serialize(envelope, Options));
        File.Move(temporary, path, overwrite: true);
    }

    public static Draft? TryLoad(Guid id)
    {
        try
        {
            var path = PathFor(id);
            if (!File.Exists(path))
            {
                return null;
            }

            var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path), Options);
            if (envelope?.Template is null)
            {
                return null;
            }

            return new Draft(envelope.Template, envelope.IsNew, envelope.SavedAt);
        }
        catch
        {
            return null;
        }
    }

    public static void Delete(Guid id)
    {
        try
        {
            var path = PathFor(id);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Um rascunho que não pode ser apagado não deve interromper o editor.
        }
    }

    /// <summary>Remove rascunhos antigos (mais de 14 dias) de modelos já salvos.</summary>
    public static int Cleanup(TimeSpan maxAge)
    {
        var removed = 0;
        if (!System.IO.Directory.Exists(Directory))
        {
            return 0;
        }

        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            try
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > maxAge)
                {
                    File.Delete(file);
                    removed++;
                }
            }
            catch
            {
                // ignorado
            }
        }

        return removed;
    }
}
