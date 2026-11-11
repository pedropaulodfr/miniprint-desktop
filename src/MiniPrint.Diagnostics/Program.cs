using System.Text;
using MiniPrint.Bluetooth;
using MiniPrint.Core.Abstractions;
using MiniPrint.Core.Configuration;
using MiniPrint.Core.Logging;
using MiniPrint.Core.Models;
using MiniPrint.Core.Templates;
using MiniPrint.Imaging;
using MiniPrint.Protocol;
using MiniPrint.Protocol.Simulation;

namespace MiniPrint.Diagnostics;

/// <summary>
/// Ferramenta de linha de comando usada nas etapas de validação física.
/// Não imprime nada sem a opção --confirmar.
/// </summary>
internal static class Program
{
    private static readonly DiagnosticLog Log = new(persistToDisk: false);

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        TrySetUtf8Console();

        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "ajuda";
        var rest = args.Skip(1).ToArray();

        if (args.Contains("--verbose", StringComparer.OrdinalIgnoreCase))
        {
            Log.EntryAdded += (_, entry) => Console.WriteLine($"   [{entry.LevelText}] {entry.Category}: {entry.Message}");
        }

        try
        {
            return command switch
            {
                "scan" or "procurar" => await ScanAsync(rest),
                "inspect" or "servicos" => await InspectAsync(rest),
                "probe" or "sonda" => await ProbeAsync(rest),
                "print" or "imprimir" => await PrintAsync(rest),
                "feed" or "avancar" => await FeedAsync(rest),
                "template" or "modelo" => await TemplateAsync(rest),
                "simulate" or "simular" => await SimulateAsync(rest),
                "encode" or "converter" => EncodeImage(rest),
                "raw" or "cru" => await RawAsync(rest),
                "ajuda" or "help" or "--help" or "-h" => Help(),
                _ => Unknown(command),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"ERRO: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static int Help()
    {
        Console.WriteLine("""
            MiniPrint Desktop - ferramenta de diagnóstico
            =============================================

            Uso: miniprint-diag <comando> [opções]

            COMANDOS
              scan [segundos]              Procura dispositivos BLE nas proximidades (padrão 8 s).
              inspect <MAC>                Lista serviços e características GATT (Teste 3).
              probe <MAC>                  Conecta, envia 0xA3/0xA8 e mostra as notificações (Teste 4).
              print <MAC> [arquivo]        Imprime imagem/texto. Exige --confirmar.
                    --texto "..."          Imprime um texto em vez de arquivo.
                    --teste                Usa a página de diagnóstico embutida.
                    --seguranca <pontos>   Margem extra de avanço (padrão 128 ≈ 16 mm).
              feed <MAC> [pontos]          Avança o papel sem imprimir (padrão 128).
              simulate [--texto "..."]     Roda o pipeline completo no simulador (sem hardware).
              template [--exemplo 1|2]     Renderiza um modelo de exemplo em PNG.
                    --out x.png            Onde salvar a pré-visualização.
                    --exportar modelo.json Grava o modelo no formato de importação.
                    --validar modelo.json  Confere um arquivo antes de importar.
                    --imprimir             Envia o modelo para a impressora (exige --confirmar).
              encode <imagem> [--out x.pbm] Converte imagem e mostra o resultado (sem hardware).
              raw <MAC> <bytes hex>        Envia bytes crus na característica 0xAE01.

            OPÇÕES GERAIS
              --dump <arquivo>             Grava todo o tráfego BLE enviado em arquivo binário.
              --dither <modo>              FloydSteinberg | Atkinson | Bayer4 | Bayer8 | Limiar
              --energia <0-65535>          Energia térmica (comando 0xAF).
              --avancar <linhas>           Linhas de alimentação final (padrão 4).
              --verbose                    Mostra o log de diagnóstico na tela.

            Exemplos
              miniprint-diag scan 10
              miniprint-diag inspect 18:57:2A:44:E0:BC
              miniprint-diag print 18:57:2A:44:E0:BC --teste --confirmar --dump trafego.bin
            """);
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Comando desconhecido: {command}. Use 'ajuda'.");
        return 2;
    }

    private static async Task<int> ScanAsync(string[] args)
    {
        var seconds = 8.0;
        if (args.Length > 0 && double.TryParse(args[0], out var parsed))
        {
            seconds = Math.Clamp(parsed, 1, 60);
        }

        var status = await BluetoothSupport.CheckAsync();
        Console.WriteLine($"Bluetooth: {status.Availability} - {status.Message}");
        if (!status.CanScan)
        {
            return 3;
        }

        var discovery = new BlePrinterDiscovery(Log);
        var seen = new Dictionary<ulong, PrinterDevice>();

        Console.WriteLine($"Procurando dispositivos BLE por {seconds:0.#} s...\n");
        var progress = new Progress<PrinterDevice>(device =>
        {
            lock (seen)
            {
                var isNew = !seen.ContainsKey(device.Address);
                seen[device.Address] = device;
                if (isNew)
                {
                    var tag = BlePrinterDiscovery.IsCompatible(device) ? "  <== impressora compatível" : string.Empty;
                    Console.WriteLine($"  {device.AddressText}  {device.DisplayName,-32} {device.Rssi,5} dBm{tag}");
                }
            }
        });

        var devices = await discovery.ScanAsync(TimeSpan.FromSeconds(seconds), progress);

        Console.WriteLine();
        Console.WriteLine($"Total: {devices.Count} dispositivo(s).");
        var printers = devices.Where(BlePrinterDiscovery.IsCompatible).ToList();
        if (printers.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("Nenhuma impressora compatível encontrada.");
            Console.WriteLine("Verifique se ela está ligada, fora do alcance do celular e sem conexão ativa.");
            return 4;
        }

        Console.WriteLine($"Impressoras compatíveis: {printers.Count}");
        foreach (var printer in printers)
        {
            Console.WriteLine($"  -> {printer.Name} ({printer.AddressText}) perfil {printer.ModelHint}");
        }

        return 0;
    }

    private static async Task<int> InspectAsync(string[] args)
    {
        if (!TryParseMac(args, out var address))
        {
            return 2;
        }

        Console.WriteLine($"Inspecionando GATT de {PrinterDevice.FormatAddress(address)}...");
        var inspection = await GattInspector.InspectAsync(address);

        Console.WriteLine($"Dispositivo: {inspection.DeviceName}");
        Console.WriteLine($"Status: {(inspection.Success ? "ok" : "falha")} - {inspection.Message}");
        Console.WriteLine($"MTU (PDU máx.): {inspection.MaxPduSize}");
        Console.WriteLine($"Serviço 0xAE30: {(inspection.HasPrinterService ? "presente" : "AUSENTE")}, " +
                          $"0xAE01: {(inspection.HasWriteCharacteristic ? "presente" : "AUSENTE")}, " +
                          $"0xAE02: {(inspection.HasNotifyCharacteristic ? "presente" : "AUSENTE")}");
        Console.WriteLine();

        foreach (var service in inspection.Services)
        {
            var marker = service.Uuid == CatPrinterProtocol.ServiceUuid ? " <<< serviço da impressora" : string.Empty;
            Console.WriteLine($"Serviço {service.Uuid}{marker}");
            foreach (var characteristic in service.Characteristics)
            {
                var flags = characteristic.Uuid == CatPrinterProtocol.WriteCharacteristicUuid ? "  [escrita]"
                    : characteristic.Uuid == CatPrinterProtocol.NotifyCharacteristicUuid ? "  [notificação]"
                    : string.Empty;
                Console.WriteLine($"    {characteristic.Uuid}  ({characteristic.Properties}){flags}");
            }
        }

        return inspection.Success ? 0 : 1;
    }

    private static async Task<int> ProbeAsync(string[] args)
    {
        if (!TryParseMac(args, out var address))
        {
            return 2;
        }

        var settings = new PrinterSettings();
        var device = new PrinterDevice { Address = address, Name = "probe" };

        await using var transport = new BlePrinterTransport(Log);
        await transport.ConnectAsync(device);

        Console.WriteLine($"Conectado: {transport.DeviceName}, MTU {transport.Mtu}, " +
                          $"bloco máximo {transport.MaxWritePayload} bytes.");

        await using var client = new CatPrinterClient(transport, Log);

        var requests = new (string Label, byte[] Frame)[]
        {
            ("0xA3 estado", CatPrinterFrame.Build(CatPrinterCommand.DeviceState, 0x00)),
            ("0xA8 info", CatPrinterFrame.Build(CatPrinterCommand.DeviceInfo, 0x00)),
            ("0xA4 qualidade", CatPrinterFrame.Build(CatPrinterCommand.SetQuality, 0x32)),
            ("0xA9 update", CatPrinterFrame.Build(CatPrinterCommand.UpdateDevice, 0x00)),
        };

        foreach (var (label, frame) in requests)
        {
            Console.WriteLine();
            Console.WriteLine($"== {label} ==");
            var replies = await client.ProbeAsync(frame, settings);
            if (replies.Count == 0)
            {
                Console.WriteLine("   (sem resposta em 3 s)");
            }

            foreach (var reply in replies)
            {
                Console.WriteLine($"   <- {reply.Length} bytes: {CatPrinterFrame.ToHex(reply, 96)}");
            }
        }

        return 0;
    }

    private static async Task<int> TemplateAsync(string[] args)
    {
        var which = GetOptionValue(args, "--exemplo") ?? "1";
        var samples = TemplateSamples.All();
        var index = int.TryParse(which, out var parsedIndex)
            ? Math.Clamp(parsedIndex - 1, 0, samples.Count - 1)
            : 0;

        var flowTemplate = samples[index];
        var template = args.Contains("--visual", StringComparer.OrdinalIgnoreCase)
            ? VisualTemplateSamples.Convert(flowTemplate)
            : flowTemplate;

        // Exportar o modelo no mesmo formato usado pelo aplicativo.
        var exportPath = GetOptionValue(args, "--exportar");
        if (exportPath is not null)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(exportPath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(exportPath, TemplateSerializer.Serialize(template));
            Console.WriteLine($"Modelo \"{template.Name}\" exportado em {Path.GetFullPath(exportPath)}");
            return 0;
        }

        // Validar um arquivo antes de importar.
        var validatePath = GetOptionValue(args, "--validar");
        if (validatePath is not null)
        {
            if (!File.Exists(validatePath))
            {
                Console.Error.WriteLine($"Arquivo não encontrado: {validatePath}");
                return 2;
            }

            var validation = TemplateSerializer.Deserialize(File.ReadAllText(validatePath));
            if (validation.Success && validation.Template is not null)
            {
                var shape = validation.Template.IsVisual
                    ? $"{validation.Template.Layout!.Elements.Count} elemento(s)"
                    : $"{validation.Template.Blocks.Count} bloco(s)";

                Console.WriteLine($"Arquivo válido: \"{validation.Template.Name}\" " +
                                  $"({validation.Template.Fields.Count} campo(s), {shape}).");
                return 0;
            }

            Console.Error.WriteLine("Arquivo inválido:");
            foreach (var error in validation.Errors)
            {
                Console.Error.WriteLine("  • " + error);
            }

            return 1;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["nome_pagador"] = "Ana Maria Silva",
            ["documento"] = "123.456.789-09",
            ["valor"] = "150",
            ["descricao"] = "Serviço de manutenção preventiva",
            ["forma_pagamento"] = "PIX",
            ["data_pagamento"] = DateTime.Now.ToString("dd/MM/yyyy"),
            ["cliente"] = "João Pereira",
            ["cpf"] = "123.456.789-09",
            ["telefone"] = "(11) 98765-4321",
            ["servico"] = "Instalação elétrica",
            ["data"] = DateTime.Now.ToString("dd/MM/yyyy"),
        };

        var bitmap = template.IsVisual
            ? VisualTemplateRenderer.Render(template, values)
            : TemplateRenderer.Render(template, values);
        Console.WriteLine($"Modelo: {template.Name} ({(template.IsVisual ? "visual" : "blocos")})");
        Console.WriteLine($"Imagem: {bitmap.Width}x{bitmap.Height} pontos " +
                          $"({bitmap.Height * 0.125:0.0} mm de papel aprox.)");

        var outPath = GetOptionValue(args, "--out") ?? Path.Combine(
            AppPaths.ExportsDirectory,
            $"modelo-{TemplateFieldFormatter.SuggestKey(template.Name)}.png");

        using (var preview = ImageProcessor.ToPreview(bitmap))
        {
            preview.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
        }

        Console.WriteLine($"Pré-visualização salva em {Path.GetFullPath(outPath)}");

        if (!args.Contains("--imprimir", StringComparer.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (!args.Contains("--confirmar", StringComparer.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Recusado: use --confirmar junto de --imprimir (gasta papel).");
            return 2;
        }

        if (!TryFindMac(args, out var address))
        {
            return 2;
        }

        var settings = new PrinterSettings();
        var jobOptions = ParseJobOptions(args, settings);
        var document = new PrintDocument
        {
            Bitmap = bitmap,
            Kind = PrintJobKind.Modelo,
            Title = template.Name,
        };

        await using var transport = new BlePrinterTransport(Log, settings.WriteWithResponse);
        await transport.ConnectAsync(new PrinterDevice { Address = address, Name = template.Name });
        await using var client = new CatPrinterClient(transport, Log);

        var result = await client.PrintAsync(document, jobOptions, settings);
        Console.WriteLine(result.Success
            ? $"Impresso: {result.BytesSent} bytes, {result.LinesPrinted} linhas."
            : $"FALHOU: {result.ErrorMessage}");

        return result.Success ? 0 : 1;
    }

    private static async Task<int> FeedAsync(string[] args)
    {
        if (!TryParseMac(args, out var address))
        {
            return 2;
        }

        var points = 128;
        if (args.Length > 1 && int.TryParse(args[1], out var parsed))
        {
            points = Math.Clamp(parsed, 1, 5000);
        }

        var settings = new PrinterSettings();
        var device = new PrinterDevice { Address = address, Name = "feed" };

        await using var transport = new BlePrinterTransport(Log);
        await transport.ConnectAsync(device);
        await using var client = new CatPrinterClient(transport, Log);

        var speed = CatPrinterFrame.Build(CatPrinterCommand.SetSpeed, 8);
        var feed = CatPrinterFrame.Build(
            CatPrinterCommand.FeedPaper,
            (byte)(points & 0xFF),
            (byte)(points >> 8));

        var payload = new byte[speed.Length + feed.Length];
        speed.CopyTo(payload, 0);
        feed.CopyTo(payload, speed.Length);

        await client.SendRawAsync(payload, settings);

        Console.WriteLine($"Papel avançado em {points} pontos (≈ {points * 0.125:0.0} mm).");
        return 0;
    }

    private static async Task<int> PrintAsync(string[] args)
    {
        if (!TryParseMac(args, out var address))
        {
            return 2;
        }

        if (!args.Contains("--confirmar", StringComparer.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Recusado: este comando gasta papel. Repita com --confirmar.");
            return 2;
        }

        var settings = new PrinterSettings();
        var job = ParseJobOptions(args, settings);
        var image = ParseImageOptions(args, settings);

        PrintDocument document;
        var textIndex = Array.IndexOf(args, "--texto");
        var usePattern = args.Contains("--teste", StringComparer.OrdinalIgnoreCase);

        if (textIndex >= 0 && args.Length > textIndex + 1)
        {
            var text = args[textIndex + 1];
            var bitmap = TextRenderer.Render(text, new TextSettings(), settings.PaperWidth);
            document = new PrintDocument { Bitmap = bitmap, Kind = PrintJobKind.Text, Title = "texto" };
        }
        else if (usePattern || args.Length <= 1)
        {
            var bitmap = TestPatterns.DiagnosticPage(settings.PaperWidth, image.Dithering);
            document = new PrintDocument { Bitmap = bitmap, Kind = PrintJobKind.TestPattern, Title = "página de teste" };
        }
        else
        {
            var path = FindExistingFile(args);
            if (path is null)
            {
                Console.Error.WriteLine("Arquivo de imagem não encontrado.");
                return 2;
            }

            var bitmap = ImageProcessor.ProcessFile(path, image);
            document = new PrintDocument { Bitmap = bitmap, Kind = PrintJobKind.Image, Title = Path.GetFileName(path) };
        }

        Console.WriteLine($"Imagem a imprimir: {document.Bitmap.Width}x{document.Bitmap.Height} pontos.");

        var device = new PrinterDevice { Address = address, Name = settings.DeviceName };
        await using var transport = new BlePrinterTransport(Log, settings.WriteWithResponse);
        await transport.ConnectAsync(device);
        Console.WriteLine($"Conectado. MTU {transport.Mtu}.");

        var dumpPath = GetOptionValue(args, "--dump");
        IPrinterTransport effective = dumpPath is not null
            ? new TeeTransport(transport, dumpPath)
            : transport;

        await using var client = new CatPrinterClient(effective, Log);
        var progress = new Progress<PrintProgress>(p =>
        {
            Console.Write($"\r{p.Message,-60}");
        });

        var result = await client.PrintAsync(document, job, settings, progress);
        Console.WriteLine();
        Console.WriteLine(result.Success
            ? $"Concluído: {result.BytesSent} bytes, {result.PacketsSent} escritas, {result.Duration.TotalSeconds:0.0} s."
            : $"FALHOU: {result.ErrorMessage}");

        if (dumpPath is not null)
        {
            Console.WriteLine($"Tráfego gravado em {Path.GetFullPath(dumpPath)}");
        }

        return result.Success ? 0 : 1;
    }

    private static async Task<int> SimulateAsync(string[] args)
    {
        var settings = new PrinterSettings { UseSimulator = true };
        var job = ParseJobOptions(args, settings);
        var image = ParseImageOptions(args, settings);

        var textIndex = Array.IndexOf(args, "--texto");
        PrintDocument document;
        if (textIndex >= 0 && args.Length > textIndex + 1)
        {
            var bitmap = TextRenderer.Render(args[textIndex + 1], new TextSettings(), settings.PaperWidth);
            document = new PrintDocument { Bitmap = bitmap, Kind = PrintJobKind.Text, Title = "texto" };
        }
        else
        {
            var bitmap = TestPatterns.DiagnosticPage(settings.PaperWidth, image.Dithering);
            document = new PrintDocument { Bitmap = bitmap, Kind = PrintJobKind.TestPattern, Title = "teste" };
        }

        var plan = CatPrinterSequence.BuildJob(document, job);
        Console.WriteLine($"Frames: {plan.PacketCount}, bytes: {plan.TotalBytes}, linhas: {plan.ImageLines}");

        var simulator = new SimulatedPrinterTransport();
        await simulator.ConnectAsync(new PrinterDevice { Name = "simulador" });
        await using var client = new CatPrinterClient(simulator, Log);
        var result = await client.PrintAsync(document, job, settings);

        Console.WriteLine(result.Success ? "Simulação concluída." : $"Simulação falhou: {result.ErrorMessage}");
        Console.WriteLine(simulator.Report.Summary);

        foreach (var warning in simulator.Report.Warnings)
        {
            Console.WriteLine($"  AVISO: {warning}");
        }

        foreach (var error in simulator.Report.Errors)
        {
            Console.WriteLine($"  ERRO: {error}");
        }

        var printed = simulator.DecodePrintedBitmap(settings.PaperWidth);
        if (printed is not null)
        {
            using var preview = ImageProcessor.ToPreview(printed);
            var outPath = GetOptionValue(args, "--out") ?? Path.Combine(AppPaths.ExportsDirectory, "simulacao.png");
            preview.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine($"Pré-visualização salva em {Path.GetFullPath(outPath)}");
        }

        return simulator.Report.HasProblems ? 1 : 0;
    }

    private static int EncodeImage(string[] args)
    {
        var path = FindExistingFile(args);
        if (path is null)
        {
            Console.Error.WriteLine("Informe o caminho de uma imagem PNG/JPG/BMP existente.");
            return 2;
        }

        var settings = new PrinterSettings();
        var image = ParseImageOptions(args, settings);
        var job = ParseJobOptions(args, settings);
        var bitmap = ImageProcessor.ProcessFile(path, image);

        Console.WriteLine($"Convertida para {bitmap.Width}x{bitmap.Height} pontos " +
                          $"({bitmap.Bits.Length} bytes de dados de imagem).");

        var document = new PrintDocument { Bitmap = bitmap, Kind = PrintJobKind.Image, Title = path };
        var plan = CatPrinterSequence.BuildJob(document, job);
        Console.WriteLine($"Sequência: {plan.PacketCount} frames, {plan.TotalBytes} bytes.");

        var outPath = GetOptionValue(args, "--out") ?? Path.Combine(
            AppPaths.ExportsDirectory,
            Path.GetFileNameWithoutExtension(path) + ".png");

        using (var preview = ImageProcessor.ToPreview(bitmap))
        {
            preview.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
        }

        Console.WriteLine($"Pré-visualização 1:1 salva em {Path.GetFullPath(outPath)}");
        return 0;
    }

    private static async Task<int> RawAsync(string[] args)
    {
        if (!TryParseMac(args, out var address) || args.Length < 2)
        {
            return 2;
        }

        var hex = string.Concat(args[1].Where(Uri.IsHexDigit));
        if (hex.Length % 2 != 0)
        {
            Console.Error.WriteLine("Bytes hexadecimais incompletos.");
            return 2;
        }

        var bytes = Convert.FromHexString(hex);
        var settings = new PrinterSettings();
        var device = new PrinterDevice { Address = address, Name = "raw" };

        await using var transport = new BlePrinterTransport(Log);
        await transport.ConnectAsync(device);
        await using var client = new CatPrinterClient(transport, Log);
        await client.SendRawAsync(bytes, settings);

        Console.WriteLine($"Enviados {bytes.Length} bytes.");
        return 0;
    }

    private static CatPrinterJobOptions ParseJobOptions(string[] args, PrinterSettings settings)
    {
        var energy = settings.ImageEnergy;
        var energyText = GetOptionValue(args, "--energia");
        if (energyText is not null && ushort.TryParse(energyText, out var parsedEnergy))
        {
            energy = parsedEnergy;
        }

        var feed = settings.FeedLines;
        var feedText = GetOptionValue(args, "--avancar");
        if (feedText is not null && int.TryParse(feedText, out var parsedFeed))
        {
            feed = Math.Clamp(parsedFeed, 0, 1000);
        }

        var safety = settings.SafetyFeedPoints;
        var safetyText = GetOptionValue(args, "--seguranca");
        if (safetyText is not null && int.TryParse(safetyText, out var parsedSafety))
        {
            safety = Math.Clamp(parsedSafety, 0, 2000);
        }

        return new CatPrinterJobOptions
        {
            Profile = PrinterProfile.Resolve(settings.DeviceName),
            PaperWidth = settings.PaperWidth,
            Speed = settings.Speed,
            Energy = energy,
            DrawingMode = settings.DrawingMode,
            FeedLines = feed,
            SafetyFeedPoints = safety,
        };
    }

    private static ImagePrintOptions ParseImageOptions(string[] args, PrinterSettings settings)
    {
        var image = new ImageSettings();
        var dither = GetOptionValue(args, "--dither");
        if (dither is not null && Enum.TryParse<DitheringMode>(dither, ignoreCase: true, out var mode))
        {
            image.Dithering = mode;
        }

        var options = ImagePrintOptions.FromSettings(image, settings.PaperWidth);
        return options;
    }

    /// <summary>Procura um endereço MAC em qualquer posição dos argumentos.</summary>
    private static bool TryFindMac(string[] args, out ulong address)
    {
        foreach (var arg in args)
        {
            if (!arg.StartsWith("--", StringComparison.Ordinal) &&
                PrinterDevice.TryParseAddress(arg, out address))
            {
                return true;
            }
        }

        address = 0;
        Console.Error.WriteLine("Informe o endereço MAC (AA:BB:CC:DD:EE:FF).");
        return false;
    }

    private static bool TryParseMac(string[] args, out ulong address)
    {
        address = 0;
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Informe o endereço MAC (AA:BB:CC:DD:EE:FF).");
            return false;
        }

        if (!PrinterDevice.TryParseAddress(args[0], out address))
        {
            Console.Error.WriteLine($"Endereço inválido: {args[0]}");
            return false;
        }

        return true;
    }

    private static string? GetOptionValue(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && args.Length > index + 1 ? args[index + 1] : null;
    }

    private static string? FindExistingFile(string[] args)
    {
        foreach (var arg in args)
        {
            if (!arg.StartsWith("--", StringComparison.Ordinal) && File.Exists(arg))
            {
                return Path.GetFullPath(arg);
            }
        }

        return null;
    }

    private static void TrySetUtf8Console()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (Exception)
        {
            // Console redirecionado: mantém o padrão.
        }
    }
}
