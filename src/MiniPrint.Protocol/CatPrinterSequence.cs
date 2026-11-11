using MiniPrint.Core.Configuration;
using MiniPrint.Core.Models;

namespace MiniPrint.Protocol;

/// <summary>Parâmetros para montar a sequência de comandos de um trabalho.</summary>
public sealed class CatPrinterJobOptions
{
    public PrinterProfile Profile { get; init; } = PrinterProfile.Sc03h;

    public int PaperWidth { get; init; } = CatPrinterProtocol.DefaultPaperWidth;

    public byte Speed { get; init; } = 32;

    /// <summary>Energia térmica do trabalho (comando 0xAF).</summary>
    public ushort Energy { get; init; } = 0x4000;

    public byte DrawingMode { get; init; } = 1;

    /// <summary>Pontos de avanço de papel no final.</summary>
    public int FeedLines { get; init; } = 4;

    /// <summary>
    /// Pontos extras de avanço para que a última linha impressa passe do lacre
    /// antes do corte. Somado a <see cref="FeedLines"/> no comando 0xA1.
    /// </summary>
    public int SafetyFeedPoints { get; init; } = 128;

    /// <summary>Total de pontos avançados no fim do trabalho.</summary>
    public int TotalFeedPoints => Math.Clamp(FeedLines + SafetyFeedPoints, 0, 0xFFFF);

    /// <summary>Velocidade usada antes do avanço final (0xBD).</summary>
    public byte FeedSpeed { get; init; } = 8;

    public bool IncludeLatticeStart { get; init; } = true;

    public bool IncludeLatticeEnd { get; init; } = true;

    /// <summary>Envia a consulta de estado (0xA3) antes de configurar.</summary>
    public bool QueryDeviceState { get; init; } = true;

    /// <summary>
    /// Cria as opções a partir das configurações salvas. Para texto, passe
    /// <c>settings.TextEnergy</c> em <paramref name="energy"/>; para imagens, o
    /// padrão já usa <c>settings.ImageEnergy</c>.
    /// </summary>
    public static CatPrinterJobOptions FromSettings(
        PrinterSettings settings,
        PrinterProfile? profile = null,
        ushort? energy = null)
    {
        profile ??= PrinterProfile.Resolve(settings.DeviceName);
        return new CatPrinterJobOptions
        {
            Profile = profile,
            PaperWidth = settings.PaperWidth,
            Speed = settings.Speed,
            Energy = energy ?? settings.ImageEnergy,
            DrawingMode = settings.DrawingMode,
            FeedLines = settings.FeedLines,
            SafetyFeedPoints = settings.SafetyFeedPoints,
            IncludeLatticeStart = settings.SendStartLattice,
            IncludeLatticeEnd = settings.SendEndLattice,
        };
    }
}

/// <summary>Estatísticas de um trabalho montado (para pré-visualização e testes).</summary>
public sealed record CatPrinterJobPlan(
    IReadOnlyList<byte[]> Frames,
    int ImageLines,
    int TotalBytes)
{
    public int PacketCount => Frames.Count;
}

/// <summary>
/// Monta a sequência completa de frames de um trabalho de impressão.
///
/// A ordem é a mesma usada pela referência Cat-Printer, que já imprime
/// corretamente em equipamentos SC03h:
///
/// <list type="number">
/// <item>0xA3 estado; 0xA3 início (frame idêntico, conforme a referência);</item>
/// <item>0xA4 qualidade (0x32); 0xBD velocidade; 0xAF energia; 0xBE modo;</item>
/// <item>0xA9 atualiza dispositivo; 0xA6 início do "lattice";</item>
/// <item>0xA2 uma vez por linha da imagem;</item>
/// <item>0xA6 fim do "lattice"; 0xBD velocidade de avanço; 0xA1 avanço; 0xA3 estado.</item>
/// </list>
/// </summary>
public static class CatPrinterSequence
{
    /// <summary>Payload fixo de início de "lattice" (11 bytes).</summary>
    public static readonly byte[] LatticeStartPayload =
    {
        0xAA, 0x55, 0x17, 0x38, 0x44, 0x5F, 0x5F, 0x5F, 0x44, 0x38, 0x2C,
    };

    /// <summary>Payload fixo de fim de "lattice" (11 bytes).</summary>
    public static readonly byte[] LatticeEndPayload =
    {
        0xAA, 0x55, 0x17, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x17,
    };

    /// <summary>Frames enviados antes dos dados da imagem.</summary>
    public static IReadOnlyList<byte[]> BuildPreamble(CatPrinterJobOptions options)
    {
        var frames = new List<byte[]>();

        if (options.QueryDeviceState)
        {
            frames.Add(CatPrinterFrame.Build(CatPrinterCommand.DeviceState, 0x00));
        }

        // "Iniciar impressão": a referência envia um frame com o mesmo conteúdo
        // da consulta de estado. Modelos novos (GB03) exigem o prefixo 0x12.
        frames.Add(options.Profile.IsNewKind
            ? CatPrinterFrame.BuildWithPrefix(0x12, CatPrinterCommand.DeviceState, 0x00)
            : CatPrinterFrame.Build(CatPrinterCommand.DeviceState, 0x00));

        // Qualidade / DPI: 0x32 nas duas referências para esta família.
        frames.Add(CatPrinterFrame.Build(CatPrinterCommand.SetQuality, 0x32));

        frames.Add(CatPrinterFrame.Build(CatPrinterCommand.SetSpeed, options.Speed));

        frames.Add(CatPrinterFrame.Build(
            CatPrinterCommand.SetEnergy,
            (byte)(options.Energy & 0xFF),
            (byte)(options.Energy >> 8)));

        frames.Add(CatPrinterFrame.Build(CatPrinterCommand.DrawingMode, options.DrawingMode));

        frames.Add(CatPrinterFrame.Build(CatPrinterCommand.UpdateDevice, 0x00));

        if (options.IncludeLatticeStart)
        {
            frames.Add(CatPrinterFrame.Build(CatPrinterCommand.ControlLattice, LatticeStartPayload));
        }

        return frames;
    }

    /// <summary>Frames enviados depois dos dados da imagem.</summary>
    public static IReadOnlyList<byte[]> BuildEpilogue(CatPrinterJobOptions options)
    {
        var frames = new List<byte[]>();

        if (options.IncludeLatticeEnd)
        {
            frames.Add(CatPrinterFrame.Build(CatPrinterCommand.ControlLattice, LatticeEndPayload));
        }

        var totalFeed = options.TotalFeedPoints;
        if (totalFeed > 0)
        {
            frames.Add(CatPrinterFrame.Build(CatPrinterCommand.SetSpeed, options.FeedSpeed));

            if (options.Profile.ProblemFeeding)
            {
                // Modelos MX05..MX10 não tratam bem o avanço direto: a
                // referência envia linhas em branco no lugar do comando 0xA1.
                var blankLines = Math.Clamp(totalFeed, 1, 1024);
                foreach (var blank in CatPrinterRaster.BlankLines(options.PaperWidth, blankLines))
                {
                    frames.Add(CatPrinterFrame.Build(CatPrinterCommand.DrawBitmap, blank));
                }
            }
            else
            {
                frames.Add(CatPrinterFrame.Build(
                    CatPrinterCommand.FeedPaper,
                    (byte)(totalFeed & 0xFF),
                    (byte)(totalFeed >> 8)));
            }
        }

        frames.Add(CatPrinterFrame.Build(CatPrinterCommand.DeviceState, 0x00));
        return frames;
    }

    /// <summary>Sequência completa, gerada sob demanda (uma linha por frame 0xA2).</summary>
    public static IEnumerable<byte[]> EnumerateJobFrames(PrintDocument document, CatPrinterJobOptions options)
    {
        foreach (var frame in BuildPreamble(options))
        {
            yield return frame;
        }

        foreach (var line in CatPrinterRaster.EncodeScanlines(document.Bitmap, options.PaperWidth))
        {
            yield return CatPrinterFrame.Build(CatPrinterCommand.DrawBitmap, line);
        }

        foreach (var frame in BuildEpilogue(options))
        {
            yield return frame;
        }
    }

    /// <summary>Materializa a sequência completa (usada em testes e no modo simulado).</summary>
    public static CatPrinterJobPlan BuildJob(PrintDocument document, CatPrinterJobOptions options)
    {
        var frames = EnumerateJobFrames(document, options).ToList();
        var bytes = frames.Sum(f => f.Length);
        return new CatPrinterJobPlan(frames, document.Bitmap.Height, bytes);
    }
}
