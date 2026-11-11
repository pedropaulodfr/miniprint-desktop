namespace MiniPrint.Core.Models;

public enum DiagnosticLevel
{
    Info,
    Aviso,
    Erro,
    Trafego,
}

/// <summary>Linha do log de diagnóstico exibida na tela e exportada em TXT.</summary>
public sealed record DiagnosticEntry(DateTimeOffset Timestamp, DiagnosticLevel Level, string Category, string Message)
{
    public string TimestampText => Timestamp.LocalDateTime.ToString("HH:mm:ss.fff");

    public string LevelText => Level switch
    {
        DiagnosticLevel.Aviso => "AVISO",
        DiagnosticLevel.Erro => "ERRO",
        DiagnosticLevel.Trafego => "TRÁF",
        _ => "INFO",
    };

    public override string ToString() =>
        $"{Timestamp.LocalDateTime:yyyy-MM-dd HH:mm:ss.fff} [{LevelText,-5}] [{Category}] {Message}";
}
