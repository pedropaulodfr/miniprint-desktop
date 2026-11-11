using System.Text;

namespace MiniPrint.Protocol;

/// <summary>Resposta de estado do equipamento (comando 0xA3).</summary>
public sealed record DeviceStatusResponse(byte[] Payload)
{
    /// <summary>Dados exatamente como recebidos, em hexadecimal.</summary>
    public string Hex => CatPrinterFrame.ToHex(Payload);

    public int Length => Payload.Length;

    /// <summary>
    /// Primeiro byte da resposta. O significado não é documentado pelo
    /// fabricante; observado 0x00 com a impressora ociosa. Exposto como dado
    /// bruto para não inventar semântica.
    /// </summary>
    public byte? StatusByte => Payload.Length > 0 ? Payload[0] : null;
}

/// <summary>
/// Estado decodificado do equipamento.
///
/// O primeiro byte é um conjunto de bits documentado na wiki do projeto
/// fulda1/Thermal_Printer (protocolo "cat printer"). Os bytes 2 e 3 não têm
/// significado publicado — são expostos crus, sem inferência.
/// </summary>
public sealed record DeviceStatus(byte Flags, byte Byte2, byte Byte3)
{
    /// <summary>Sem papel ou porta do papel aberta (bit 0).</summary>
    public bool PaperOut => (Flags & 0x01) != 0;

    /// <summary>Tampa aberta (bit 1).</summary>
    public bool CoverOpen => (Flags & 0x02) != 0;

    /// <summary>Cabeçote quente (bit 2).</summary>
    public bool HeadHot => (Flags & 0x04) != 0;

    /// <summary>Bateria fraca (bit 3).</summary>
    public bool LowBattery => (Flags & 0x08) != 0;

    /// <summary>Ocupada imprimindo (bit 7).</summary>
    public bool Busy => (Flags & 0x80) != 0;

    /// <summary>Nenhum bit de alerta ligado.</summary>
    public bool IsOk => (Flags & 0x8F) == 0;

    /// <summary>Resumo legível para a interface.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (PaperOut)
            {
                parts.Add("sem papel");
            }

            if (CoverOpen)
            {
                parts.Add("tampa aberta");
            }

            if (HeadHot)
            {
                parts.Add("cabeçote quente");
            }

            if (LowBattery)
            {
                parts.Add("bateria fraca");
            }

            if (Busy)
            {
                parts.Add("ocupada");
            }

            return parts.Count == 0 ? "pronta" : string.Join(", ", parts);
        }
    }

    public string RawText => $"flags 0x{Flags:X2} · byte2 0x{Byte2:X2} · byte3 0x{Byte3:X2}";
}

/// <summary>Informações de versão (comando 0xA8).</summary>
public sealed record DeviceInfoResponse(string? Version, byte[] Payload)
{
    public string Hex => CatPrinterFrame.ToHex(Payload);
}

/// <summary>
/// Interpretação conservadora das notificações enviadas pela impressora.
///
/// Só modela o que foi observado no equipamento real: o frame de versão
/// (0xA8) devolve a string "3.0.48" e o frame 0xA3 devolve 3 bytes cujo
/// significado o fabricante não documenta.
/// </summary>
public static class CatPrinterResponses
{
    /// <summary>Direção impressora -&gt; PC nas respostas.</summary>
    public const byte DirectionFromPrinter = 0x01;

    /// <summary>Separa todas as respostas completas contidas em uma notificação.</summary>
    public static IReadOnlyList<ParsedFrame> ParseAll(ReadOnlySpan<byte> notification)
    {
        var frames = new List<ParsedFrame>();
        var offset = 0;
        while (offset < notification.Length)
        {
            if (notification[offset] != CatPrinterProtocol.Prefix0)
            {
                offset++;
                continue;
            }

            try
            {
                if (!CatPrinterFrame.TryParse(notification[offset..], out var frame, out var consumed))
                {
                    break;
                }

                frames.Add(frame);
                offset += consumed;
            }
            catch (InvalidDataException)
            {
                offset++;
            }
        }

        return frames;
    }

    /// <summary>Verdadeiro quando a notificação é o frame de pausa de fluxo.</summary>
    public static bool IsFlowPause(ReadOnlySpan<byte> data) =>
        data.SequenceEqual(CatPrinterProtocol.FlowPause);

    /// <summary>Verdadeiro quando a notificação é o frame de retomada de fluxo.</summary>
    public static bool IsFlowResume(ReadOnlySpan<byte> data) =>
        data.SequenceEqual(CatPrinterProtocol.FlowResume);

    public static DeviceStatusResponse? TryParseDeviceStatus(ParsedFrame frame) =>
        frame.Command == (byte)CatPrinterCommand.DeviceState
            ? new DeviceStatusResponse(frame.Payload)
            : null;

    /// <summary>
    /// Decodifica o estado a partir do payload do 0xA3. Retorna nulo quando o
    /// payload está vazio.
    /// </summary>
    public static DeviceStatus? TryParseStatus(ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0)
        {
            return null;
        }

        return new DeviceStatus(
            payload[0],
            payload.Length > 1 ? payload[1] : (byte)0,
            payload.Length > 2 ? payload[2] : (byte)0);
    }

    /// <summary>Decodifica o estado a partir de uma notificação completa.</summary>
    public static DeviceStatus? TryParseStatusFromNotification(byte[] notification)
    {
        foreach (var frame in ParseAll(notification))
        {
            if (frame.Command == (byte)CatPrinterCommand.DeviceState)
            {
                return TryParseStatus(frame.Payload);
            }
        }

        return null;
    }

    public static DeviceInfoResponse? TryParseDeviceInfo(ParsedFrame frame)
    {
        if (frame.Command != (byte)CatPrinterCommand.DeviceInfo)
        {
            return null;
        }

        return new DeviceInfoResponse(ExtractVersion(frame.Payload), frame.Payload);
    }

    /// <summary>
    /// Extrai a maior sequência imprimível do payload. Na SC03h real a
    /// resposta do 0xA8 contém <c>81 00 03 "3.0.48" 00</c>.
    /// </summary>
    public static string? ExtractVersion(ReadOnlySpan<byte> payload)
    {
        var best = new StringBuilder();
        var current = new StringBuilder();

        foreach (var b in payload)
        {
            if (b >= 0x20 && b <= 0x7E)
            {
                current.Append((char)b);
            }
            else
            {
                if (current.Length > best.Length)
                {
                    best = new StringBuilder(current.ToString());
                }

                current.Clear();
            }
        }

        if (current.Length > best.Length)
        {
            best = current;
        }

        var text = best.ToString().Trim();
        return text.Length >= 3 ? text : null;
    }
}
