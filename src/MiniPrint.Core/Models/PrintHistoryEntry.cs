namespace MiniPrint.Core.Models;

/// <summary>Registro de um trabalho de impressão no histórico.</summary>
public sealed class PrintHistoryEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    /// <summary>Nome do arquivo, título do texto ou identificação do padrão de teste.</summary>
    public string Title { get; init; } = string.Empty;

    public PrintJobKind Kind { get; init; }

    public PrintJobStatus Status { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>Verdadeiro quando o trabalho foi enviado ao simulador, não ao hardware.</summary>
    public bool WasSimulated { get; init; }

    public string DeviceName { get; init; } = string.Empty;

    public int LinesPrinted { get; init; }

    public int BytesSent { get; init; }

    public double DurationSeconds { get; init; }

    public string KindText => Kind switch
    {
        PrintJobKind.Text => "Texto",
        PrintJobKind.Image => "Imagem",
        PrintJobKind.TestPattern => "Teste",
        PrintJobKind.Modelo => "Modelo",
        _ => Kind.ToString(),
    };

    public string StatusText => Status switch
    {
        PrintJobStatus.Concluido => WasSimulated ? "Simulado" : "Concluído",
        PrintJobStatus.Falhou => "Falhou",
        PrintJobStatus.Cancelado => "Cancelado",
        PrintJobStatus.Enviando => "Enviando",
        PrintJobStatus.Preparando => "Preparando",
        _ => "Pendente",
    };

    public string TimestampText => Timestamp.LocalDateTime.ToString("dd/MM/yyyy HH:mm:ss");
}
