namespace MiniPrint.Desktop.Services;

/// <summary>Par (resumo, detalhe) exibido na interface.</summary>
public readonly record struct StatusMessage(string Summary, string Detail);

/// <summary>
/// Textos de estado da tela Impressora e do cabeçalho.
///
/// A regra central: <b>o estado da conexão descreve apenas a conexão</b>. O
/// resultado da busca BLE é outra coisa e aparece na área de descoberta. Antes
/// os dois dividiam as mesmas propriedades, e uma varredura terminando depois
/// da conexão deixava o painel exibindo "Nenhuma impressora compatível
/// encontrada" enquanto a impressora estava conectada e imprimindo — o que é
/// sempre o caso, porque uma impressora BLE conectada deixa de anunciar e por
/// isso nunca aparece na busca.
/// </summary>
public static class PrinterStatusText
{
    public static StatusMessage Disconnected() =>
        new("Desconectado", "Nenhuma impressora conectada.");

    public static StatusMessage Connected(string deviceName, bool simulated) =>
        simulated
            ? new("Simulador conectado", "Os trabalhos serão validados sem imprimir papel.")
            : new("Conectado", $"{NameOrFallback(deviceName)} pronta para receber dados.");

    public static StatusMessage Scanning(int seconds) =>
        new("Pesquisando...", $"Procurando dispositivos BLE por {seconds} segundos...");

    public static StatusMessage ScanFailed(string reason) =>
        new("Falha na pesquisa", reason);

    /// <summary>
    /// Resultado da busca. Quando já existe conexão, a mensagem nunca afirma que
    /// "nenhuma impressora foi encontrada": o equipamento em uso não anuncia
    /// enquanto o enlace está ativo, e simular que ele sumiu é o que confundia.
    /// </summary>
    public static StatusMessage ScanCompleted(
        int compatibleFound,
        int totalFound,
        bool isConnected,
        string connectedName)
    {
        var name = NameOrFallback(connectedName);

        if (isConnected)
        {
            if (compatibleFound > 0)
            {
                return new(
                    $"{compatibleFound} nova(s) impressora(s) compatível(is) encontrada(s)",
                    $"A conexão com {name} continua ativa. Conecte-se à nova para trocar de impressora.");
            }

            return totalFound > 0
                ? new(
                    "Nenhuma outra impressora compatível encontrada",
                    $"{name} não anuncia por Bluetooth enquanto está conectada; a conexão atual continua ativa.")
                : new(
                    "Nenhum outro dispositivo BLE encontrado",
                    $"{name} continua conectada e pronta para imprimir.");
        }

        if (compatibleFound > 0)
        {
            return new(
                $"{compatibleFound} impressora(s) compatível(is) encontrada(s)",
                "Selecione a impressora na lista e clique em Conectar.");
        }

        return totalFound > 0
            ? new(
                "Nenhuma impressora compatível encontrada",
                "Verifique se a impressora está ligada e não conectada ao celular.")
            : new(
                "Nenhum dispositivo BLE encontrado",
                "Verifique se o Bluetooth do computador está ligado e se a impressora está próxima e ligada.");
    }

    private static string NameOrFallback(string deviceName) =>
        string.IsNullOrWhiteSpace(deviceName) ? "A impressora" : deviceName;
}
