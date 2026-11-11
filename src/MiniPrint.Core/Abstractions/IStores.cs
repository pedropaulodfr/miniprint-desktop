using MiniPrint.Core.Configuration;
using MiniPrint.Core.Models;

namespace MiniPrint.Core.Abstractions;

public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}

public interface IPrintHistoryStore
{
    IReadOnlyList<PrintHistoryEntry> Load();

    void Save(IReadOnlyList<PrintHistoryEntry> entries);
}
