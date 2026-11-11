using MiniPrint.Core.Models;
using Xunit;

namespace MiniPrint.Tests;

public class DeviceAndSettingsTests
{
    [Fact]
    public void FormatAddress_ProducesMacNotation()
    {
        var address = 0x18572A44E0BCUL;
        Assert.Equal("18:57:2A:44:E0:BC", PrinterDevice.FormatAddress(address));
    }

    [Theory]
    [InlineData("18:57:2A:44:E0:BC", "18:57:2A:44:E0:BC")]
    [InlineData("18-57-2A-44-E0-BC", "18:57:2A:44:E0:BC")]
    [InlineData("18572A44E0BC", "18:57:2A:44:E0:BC")]
    [InlineData("18572a44e0bc", "18:57:2A:44:E0:BC")]
    public void TryParseAddress_AcceptsCommonFormats(string input, string expected)
    {
        Assert.True(PrinterDevice.TryParseAddress(input, out var address));
        Assert.Equal(expected, PrinterDevice.FormatAddress(address));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("18:57:2A")]
    [InlineData("zz:zz:zz:zz:zz:zz")]
    public void TryParseAddress_RejectsInvalidInput(string? input)
    {
        Assert.False(PrinterDevice.TryParseAddress(input, out _));
    }

    [Fact]
    public void RoundTripAddress_PreservesValue()
    {
        const ulong address = 0x18572A44E0BCUL;
        Assert.True(PrinterDevice.TryParseAddress(PrinterDevice.FormatAddress(address), out var parsed));
        Assert.Equal(address, parsed);
    }

    [Fact]
    public void ResolveProfile_IdentifiesSc03hByPrefix()
    {
        var profile = PrinterProfile.Resolve("SC03h-5718");
        Assert.Equal("SC03h", profile.Key);
        Assert.Equal(384, profile.PaperWidth);
        Assert.False(profile.IsNewKind);
    }

    [Fact]
    public void ResolveProfile_FallsBackToGeneric()
    {
        var profile = PrinterProfile.Resolve("Xyz-123");
        Assert.Equal(PrinterProfile.GenericCat.Key, profile.Key);
    }

    [Fact]
    public void ResolveProfile_MatchesLongestPrefix()
    {
        // MXTP não deve ser confundido com MX05, e vice-versa.
        Assert.Equal("MXTP", PrinterProfile.Resolve("MXTP-01").Key);
        Assert.Equal("MX05", PrinterProfile.Resolve("MX05").Key);
    }

    [Fact]
    public void DefaultSettings_MatchPhysicalPrinter()
    {
        var settings = new Core.Configuration.PrinterSettings();
        Assert.Equal(384, settings.PaperWidth);
        Assert.Equal(4, settings.FeedLines);
        Assert.Equal(128, settings.SafetyFeedPoints);
        Assert.Equal("SC03h-5718", settings.DeviceName);
        Assert.Equal("18:57:2A:44:E0:BC", settings.DeviceMac);
        Assert.False(settings.UseSimulator);
    }

    [Fact]
    public void SettingsStore_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"miniprint-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new Core.Configuration.JsonSettingsStore(path);
            var settings = new Core.Configuration.AppSettings();
            settings.Printer.FeedLines = 9;
            settings.Image.Threshold = 77;
            settings.LastText = "olá";

            store.Save(settings);
            var loaded = store.Load();

            Assert.Equal(9, loaded.Printer.FeedLines);
            Assert.Equal(77, loaded.Image.Threshold);
            Assert.Equal("olá", loaded.LastText);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void SettingsStore_CorruptedFileFallsBackToDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"miniprint-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ isto não é json");
        try
        {
            var store = new Core.Configuration.JsonSettingsStore(path);
            var settings = store.Load();
            Assert.Equal(384, settings.Printer.PaperWidth);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HistoryStore_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"miniprint-history-{Guid.NewGuid():N}.json");
        try
        {
            var store = new Core.Configuration.JsonPrintHistoryStore(path);
            var entry = new PrintHistoryEntry
            {
                Title = "foto.png",
                Kind = PrintJobKind.Image,
                Status = PrintJobStatus.Concluido,
                WasSimulated = false,
                LinesPrinted = 120,
            };

            store.Save(new[] { entry });
            var loaded = store.Load();

            var single = Assert.Single(loaded);
            Assert.Equal("foto.png", single.Title);
            Assert.Equal("Concluído", single.StatusText);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void HistoryEntry_SimulatedDoesNotClaimRealPrint()
    {
        var entry = new PrintHistoryEntry
        {
            Status = PrintJobStatus.Concluido,
            WasSimulated = true,
        };

        Assert.Equal("Simulado", entry.StatusText);
    }

    [Fact]
    public void DiagnosticLog_KeepsEntriesAndExports()
    {
        var log = new Core.Logging.DiagnosticLog(persistToDisk: false);
        log.Info("teste", "linha 1");
        log.Error("teste", "linha 2");

        Assert.Equal(2, log.Count);

        var path = Path.Combine(Path.GetTempPath(), $"miniprint-log-{Guid.NewGuid():N}.txt");
        try
        {
            var exported = log.Export(path);
            var content = File.ReadAllText(exported);

            Assert.Contains("linha 1", content);
            Assert.Contains("linha 2", content);
            Assert.Contains("MiniPrint Desktop", content);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void MonochromeBitmap_ValidatesBufferSize()
    {
        Assert.Throws<ArgumentException>(() => new MonochromeBitmap(384, 2, new byte[10]));
    }

    [Fact]
    public void MonochromeBitmap_StrideRoundsUp()
    {
        var bitmap = new MonochromeBitmap(10, 1, new byte[2]);
        Assert.Equal(2, bitmap.Stride);
    }
}
