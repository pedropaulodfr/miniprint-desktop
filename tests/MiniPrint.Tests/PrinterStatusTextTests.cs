using MiniPrint.Desktop.Services;
using Xunit;

namespace MiniPrint.Tests;

/// <summary>
/// Regressão do painel "Estado da conexão": o resultado de uma busca BLE não
/// pode descrever a conexão. Uma impressora conectada deixa de anunciar, então
/// uma varredura terminada depois de conectar anunciava "nenhuma impressora
/// compatível encontrada" enquanto a impressora estava imprimindo normalmente.
/// </summary>
public class PrinterStatusTextTests
{
    [Fact]
    public void ScanCompletingWhileConnected_NeverClaimsNoPrinterWasFound()
    {
        var message = PrinterStatusText.ScanCompleted(
            compatibleFound: 0,
            totalFound: 2,
            isConnected: true,
            connectedName: "iPrint");

        Assert.DoesNotContain("Nenhuma impressora compatível encontrada", message.Summary);
        Assert.Contains("Nenhuma outra impressora", message.Summary);
        Assert.Contains("iPrint", message.Detail);
        Assert.Contains("conexão atual continua ativa", message.Detail);
    }

    [Fact]
    public void ScanWithNothingNearbyWhileConnected_KeepsTheConnectedPrinterRelevant()
    {
        var message = PrinterStatusText.ScanCompleted(
            compatibleFound: 0,
            totalFound: 0,
            isConnected: true,
            connectedName: "iPrint");

        Assert.DoesNotContain("Nenhum dispositivo BLE encontrado", message.Summary);
        Assert.Contains("iPrint", message.Detail);
    }

    [Fact]
    public void ScanWhileConnected_ReportsNewPrintersWhenThereAreAny()
    {
        var message = PrinterStatusText.ScanCompleted(
            compatibleFound: 2,
            totalFound: 4,
            isConnected: true,
            connectedName: "iPrint");

        Assert.Contains("2", message.Summary);
        Assert.Contains("A conexão com iPrint continua ativa", message.Detail);
    }

    [Fact]
    public void ScanWithoutConnection_KeepsTheTroubleshootingHint()
    {
        var message = PrinterStatusText.ScanCompleted(
            compatibleFound: 0,
            totalFound: 2,
            isConnected: false,
            connectedName: string.Empty);

        Assert.Equal("Nenhuma impressora compatível encontrada", message.Summary);
        Assert.Contains("não conectada ao celular", message.Detail);
    }

    [Fact]
    public void ScanWithoutConnection_FindsPrinters()
    {
        var message = PrinterStatusText.ScanCompleted(
            compatibleFound: 1,
            totalFound: 3,
            isConnected: false,
            connectedName: string.Empty);

        Assert.Equal("1 impressora(s) compatível(is) encontrada(s)", message.Summary);
        Assert.Contains("clique em Conectar", message.Detail);
    }

    [Fact]
    public void ScanWithoutConnectionAndWithoutDevices_ExplainsBluetooth()
    {
        var message = PrinterStatusText.ScanCompleted(
            compatibleFound: 0,
            totalFound: 0,
            isConnected: false,
            connectedName: string.Empty);

        Assert.Equal("Nenhum dispositivo BLE encontrado", message.Summary);
        Assert.Contains("Bluetooth", message.Detail);
    }

    [Fact]
    public void ConnectedStatus_NamesThePrinterAndStaysConnectionOnly()
    {
        var message = PrinterStatusText.Connected("iPrint", simulated: false);

        Assert.Equal("Conectado", message.Summary);
        Assert.Contains("iPrint", message.Detail);
        Assert.DoesNotContain("impressora compatível", message.Detail);
    }

    [Fact]
    public void ConnectedStatus_DistinguishesTheSimulator()
    {
        var message = PrinterStatusText.Connected("iPrint", simulated: true);

        Assert.Equal("Simulador conectado", message.Summary);
        Assert.Contains("sem imprimir papel", message.Detail);
    }

    [Fact]
    public void DisconnectedStatus_IsStable()
    {
        var message = PrinterStatusText.Disconnected();

        Assert.Equal("Desconectado", message.Summary);
        Assert.Equal("Nenhuma impressora conectada.", message.Detail);
    }

    [Fact]
    public void MissingDeviceName_FallsBackToAGenericSubject()
    {
        var message = PrinterStatusText.Connected("   ", simulated: false);

        Assert.Equal("Conectado", message.Summary);
        Assert.StartsWith("A impressora", message.Detail);
    }
}
