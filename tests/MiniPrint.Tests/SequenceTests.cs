using MiniPrint.Core.Models;
using MiniPrint.Protocol;
using MiniPrint.Protocol.Simulation;
using Xunit;

namespace MiniPrint.Tests;

public class SequenceTests
{
    private static PrintDocument Document(int lines = 10) => new()
    {
        Bitmap = new MonochromeBitmap(384, lines, new byte[48 * lines]),
        Kind = PrintJobKind.Image,
        Title = "teste",
    };

    [Fact]
    public void Preamble_FollowsReferenceOrder()
    {
        var options = new CatPrinterJobOptions();
        var frames = CatPrinterSequence.BuildPreamble(options);

        var commands = frames.Select(f => f[2]).ToArray();
        Assert.Equal(new byte[]
        {
            0xA3, // estado
            0xA3, // início (frame idêntico, conforme referência)
            0xA4, // qualidade
            0xBD, // velocidade
            0xAF, // energia
            0xBE, // modo de desenho
            0xA9, // atualiza dispositivo
            0xA6, // início do lattice
        }, commands);
    }

    [Fact]
    public void Preamble_NewKindModel_Uses0x12Prefix()
    {
        var options = new CatPrinterJobOptions { Profile = PrinterProfile.All.First(p => p.Key == "GB03") };
        var frames = CatPrinterSequence.BuildPreamble(options);

        // O frame de início dos modelos novos começa com 0x12 e é 1 byte maior.
        var startFrame = frames[1];
        Assert.Equal(0x12, startFrame[0]);
        Assert.Equal(0x51, startFrame[1]);
        Assert.Equal(10, startFrame.Length);
    }

    [Fact]
    public void Preamble_CanOmitDeviceStateQuery()
    {
        var options = new CatPrinterJobOptions { QueryDeviceState = false };
        var frames = CatPrinterSequence.BuildPreamble(options);
        Assert.Equal(7, frames.Count);
    }

    [Fact]
    public void Epilogue_FeedsConfiguredLinesPlusSafetyMargin()
    {
        var options = new CatPrinterJobOptions { FeedLines = 4, SafetyFeedPoints = 128 };
        var frames = CatPrinterSequence.BuildEpilogue(options);

        var feed = frames.Single(f => f[2] == 0xA1);
        var total = feed[6] | (feed[7] << 8);

        // 4 linhas finais + 128 pontos de margem para o texto passar do lacre.
        Assert.Equal(132, total);
    }

    [Fact]
    public void Epilogue_DefaultOptions_FeedEnoughToClearTheTearBar()
    {
        var options = new CatPrinterJobOptions();

        // A margem padrão precisa ser maior que alguns milímetros, senão o
        // corte atravessa a última linha impressa.
        Assert.True(options.SafetyFeedPoints >= 96,
            $"Margem de segurança pequena demais: {options.SafetyFeedPoints} pontos.");
        Assert.True(options.TotalFeedPoints >= 100);
    }

    [Fact]
    public void Epilogue_ZeroFeedAndNoSafety_SendsNoFeedCommand()
    {
        var options = new CatPrinterJobOptions { FeedLines = 0, SafetyFeedPoints = 0 };
        var frames = CatPrinterSequence.BuildEpilogue(options);
        Assert.DoesNotContain(frames, f => f[2] == 0xA1);
    }

    [Fact]
    public void Epilogue_ProblemFeedingModel_UsesBlankLines()
    {
        var profile = PrinterProfile.All.First(p => p.Key == "MX05");
        var options = new CatPrinterJobOptions { Profile = profile, FeedLines = 4, SafetyFeedPoints = 124 };
        var frames = CatPrinterSequence.BuildEpilogue(options);

        Assert.DoesNotContain(frames, f => f[2] == 0xA1);
        Assert.Equal(128, frames.Count(f => f[2] == 0xA2));
    }

    [Fact]
    public void Job_SendsOneFramePerImageLine()
    {
        var document = Document(17);
        var options = new CatPrinterJobOptions();
        var frames = CatPrinterSequence.EnumerateJobFrames(document, options).ToList();

        var dataFrames = frames.Count(f => f[2] == 0xA2);
        Assert.Equal(17, dataFrames);
    }

    [Fact]
    public void Job_EveryFrameIsValid()
    {
        var document = Document(5);
        var plan = CatPrinterSequence.BuildJob(document, new CatPrinterJobOptions());

        foreach (var frame in plan.Frames)
        {
            Assert.True(CatPrinterFrame.TryParse(frame, out var parsed, out var consumed));
            Assert.Equal(frame.Length, consumed);
            Assert.InRange(parsed.Payload.Length, 0, 255);
        }
    }

    [Fact]
    public void Job_PlanReportsByteCount()
    {
        var document = Document(10);
        var plan = CatPrinterSequence.BuildJob(document, new CatPrinterJobOptions());

        Assert.Equal(plan.Frames.Sum(f => f.Length), plan.TotalBytes);
        Assert.Equal(10, plan.ImageLines);
    }

    [Fact]
    public void Job_DataFrameCarriesOneScanline()
    {
        var document = Document(1);
        var plan = CatPrinterSequence.BuildJob(document, new CatPrinterJobOptions());
        var dataFrame = plan.Frames.First(f => f[2] == 0xA2);

        // 6 bytes de cabeçalho + 48 de linha + CRC + sufixo.
        Assert.Equal(56, dataFrame.Length);
        Assert.Equal(48, dataFrame[4]);
    }
}
