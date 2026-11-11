# Arquitetura

## 1. Visão geral

```
┌──────────────────────────────────────────────────────────────┐
│ MiniPrint.Desktop (WPF, MVVM)                                │
│   Views XAML · ViewModels · PrinterService · PreviewRenderer  │
└───────────────┬──────────────────────────────────────────────┘
                │
     ┌──────────┴───────────┬───────────────────┐
     ▼                      ▼                   ▼
┌─────────────┐     ┌──────────────┐    ┌──────────────┐
│ Bluetooth   │     │  Imaging     │    │  Protocol    │
│ (WinRT GATT)│     │ (GDI+)       │    │ (frames)     │
└──────┬──────┘     └──────┬───────┘    └──────┬───────┘
       │                   │                   │
       └───────────────────┴───────────────────┘
                           ▼
                    ┌─────────────┐
                    │    Core     │
                    │ modelos,    │
                    │ interfaces  │
                    └─────────────┘
```

Dependências apontam sempre para `Core`. Nenhum módulo de domínio conhece WPF;
`Protocol` não conhece Windows.

## 2. Módulos

| Projeto | TFM | Responsabilidade |
|---|---|---|
| `MiniPrint.Core` | `net8.0` | entidades, interfaces (`IPrinterTransport`, `IPrinterDiscovery`), configurações, histórico, log, modelos de documento |
| `MiniPrint.Protocol` | `net8.0` | CRC-8, frames, raster, sequência de comandos, cliente com controle de fluxo, simulador |
| `MiniPrint.Imaging` | `net8.0-windows` | texto → raster, imagem → raster, modelos → raster, brilho/contraste, recorte, rotação, dithering |
| `MiniPrint.Bluetooth` | `net8.0-windows10.0.19041.0` | descoberta BLE, transporte GATT, inspeção de serviços |
| `MiniPrint.Desktop` | `net8.0-windows10.0.19041.0` + WPF | interface, MVVM, orquestração |
| `MiniPrint.Diagnostics` | `net8.0-windows10.0.19041.0` | CLI de validação |
| `MiniPrint.Tests` | `net8.0-windows10.0.19041.0` | 102 testes automatizados |

### Por que `net8.0-windows10.0.19041.0`

As APIs `Windows.Devices.Bluetooth` são projeções WinRT. No .NET moderno elas
exigem um TFM com versão de SDK do Windows, o que traz o pacote
`Microsoft.Windows.SDK.NET.Ref` automaticamente. WPF convive com isso sem
problema: o aplicativo é um executável WPF comum que referencia as projeções.

Windows 10 build 19041 (2004) é o mínimo porque é a versão mais antiga com o
conjunto completo de APIs GATT usado aqui (`GattSession.MaxPduSize`,
`WriteValueWithResultAsync`).

## 3. Fluxo de um trabalho de impressão

```
Usuário clica Imprimir
        │
        ▼
PrintViewModel.EnsureBitmapAsync()
        │  (rasteriza texto ou processa imagem; roda em Task.Run)
        ▼
PrintDocument { Bitmap, Kind, Title }
        │
        ▼
PrinterService.PrintAsync(document, settings)
        │  escolhe energia (texto/imagem), limita o bloco ao MTU
        ▼
CatPrinterClient.PrintAsync(...)
        │
        ├── BuildPreamble()      0xA3, 0xA3, 0xA4, 0xBD, 0xAF, 0xBE, 0xA9, 0xA6
        ├── por linha: 0xA2      (48 bytes espelhados)
        ├── BuildEpilogue()      0xA6, 0xBD, 0xA1, 0xA3
        │
        ▼
ChunkWriter → WriteChunkAsync → IPrinterTransport.WriteAsync
                              → BLE (0xAE01) ou simulador
```

Cada escrita passa antes por `WaitWhilePausedAsync`, que consulta a flag de XOFF
alimentada pelas notificações de `0xAE02`.

## 4. Concorrência

- Toda a comunicação é assíncrona (`async`/`await`), sem bloqueio de thread.
- `CatPrinterClient` usa um `SemaphoreSlim(1,1)` com `WaitAsync(0)`: um segundo
  trabalho é recusado imediatamente com mensagem, em vez de enfileirar.
- `BlePrinterTransport` serializa escritas com outro semáforo, porque a
  característica GATT não aceita escritas simultâneas.
- A renderização da pré-visualização usa `Task.Run` e um contador de geração:
  resultados antigos são descartados quando os parâmetros mudam.
- Alterações de parâmetro são agrupadas por um `DispatcherTimer` de 280 ms, para
  não reprocessar a cada tecla.
- Eventos vindos do Bluetooth são marshalados para o dispatcher da interface.

## 5. Tratamento de erros

| Camada | Estratégia |
|---|---|
| transporte | `TimeoutException`/`IOException` com mensagem em português |
| cliente | `PrintAsync` **nunca lança**: devolve `PrintJobResult` com status e mensagem |
| serviço | estado de conexão atualizado; `StateChanged` para a interface |
| interface | comandos assíncronos capturam exceções e mostram aviso; log sempre |
| aplicativo | `DispatcherUnhandledException` + `UnhandledException` → log, sem fechar |

## 6. Simulador

`SimulatedPrinterTransport` implementa `IPrinterTransport` e faz o papel do
equipamento:

- remonta frames vindos de escritas fragmentadas;
- valida prefixo, tamanho, CRC e sufixo;
- registra comandos, energia, velocidade, modo, avanço e linhas;
- emite XOFF/XON reais para exercitar o controle de fluxo;
- permite injetar falha de comunicação e desconexão;
- reconstrói o bitmap que teria sido impresso.

Como implementa a mesma interface, todo o caminho de impressão é idêntico ao do
hardware — o que muda é apenas o transporte.

## 7. Extensibilidade para outras CAT Printers

Os modelos ficam em `PrinterProfile` (chave, largura, prefixo de nome,
`IsNewKind`, `ProblemFeeding`, energias sugeridas). Para adicionar um modelo:

1. acrescente uma entrada em `PrinterProfile.All`;
2. informe a largura em pontos e o prefixo do nome anunciado;
3. se o modelo for "novo" (aceita o prefixo `0x12`), marque `IsNewKind`;
4. se ele não tratar o comando de avanço, marque `ProblemFeeding`.

O restante do fluxo (interface, histórico, diagnóstico) já funciona.

## 7.1 Modelos de documento

A funcionalidade de modelos foi encaixada na arquitetura existente, sem criar
uma segunda camada de impressão.

```
DocumentTemplate ──► TemplateComposer ──► TemplateRenderer ──► MonochromeBitmap
 (Core)               (Core: substitui)     (Imaging)             │
                                                                  ▼
                                            PrinterService.PrintAsync (mesmo pipeline)
```

| Camada | Componentes |
|---|---|
| Core | `DocumentTemplate`, `TemplateBlock`, `TemplateField`, `TemplateComposer` (substituição `{{chave}}`), `TemplateFieldFormatter` (máscaras e validação), `TemplateValidator`, `TemplateSerializer` (exportar/importar), `JsonTemplateStore`, `TemplateSamples` |
| Imaging | `TemplateRenderer` — reutiliza `TextRenderer.CreateFont`/`LayoutLines` e a mesma binarização, garantindo que a pré-visualização seja idêntica ao papel |
| Desktop | `TemplatesViewModel` (hospedeiro), `TemplateListViewModel`, `TemplateEditorViewModel`, `TemplateFillViewModel` |

O documento gerado é um `MonochromeBitmap` de 384 pontos, exatamente como o
texto e a imagem. A impressão usa `PrinterService.PrintAsync` com
`PrintJobKind.Modelo`, que consome a energia de texto. **Não existe caminho
alternativo de Bluetooth.**

### 7.2 Editor visual

Modelos com `DocumentTemplate.Layout` preenchido usam posicionamento absoluto
(coordenadas em pontos de impressão). Os antigos, com `Blocks`, continuam no
fluxo de blocos — o mesmo arquivo atende aos dois formatos.

| Camada | Componentes |
|---|---|
| Core | `DocumentLayout`, `LayoutElement`, `OverflowMode`, validação de layout |
| Imaging | `VisualTemplateRenderer` (texto com quebra/redução/corte, imagens em 1 bit, linhas e retângulos), `TemplateLayoutConverter` (blocos → layout) |
| Desktop | `VisualEditorViewModel` (seleção, grupos, alinhamento, camadas, undo/redo por snapshot) e `VisualEditorView` (canvas com `LayoutTransform` para zoom) |

O canvas usa `LayoutTransform` com o fator de zoom: o espaço de coordenadas
continua em pontos, então arrastar e redimensionar não precisam de conversão e o
zoom nunca altera o layout salvo. O undo guarda snapshots do `DocumentLayout`
(JSON), cobrindo posição, tamanho, propriedades, agrupamento e ordem.

Persistência: `%LOCALAPPDATA%\MiniPrintDesktop\templates.json`, com gravação
atômica, no mesmo padrão do histórico e das configurações.

## 8. Decisões e trade-offs

| Decisão | Motivo |
|---|---|
| Uma linha por frame `0xA2` | é o que as duas referências fazem; agrupar linhas não foi testado no aparelho |
| 200 bytes por escrita | valor da referência Cat-Printer, estável sem confirmação |
| 20 ms entre escritas | evita perda de dados em `write without response` |
| Energia separada para texto e imagem | texto com mais energia fica mais legível; imagem com menos preserva detalhe |
| `0xBE` configurável | as referências divergem; esconder a divergência seria pior |
| Bytes do `0xA3` exibidos crus | o fabricante não documenta; inventar semântica induziria a erro |
| System.Drawing (GDI+) | estável no Windows, sem dependência nativa extra, boa qualidade de rasterização de texto |
| Simulador no projeto Protocol | permite testar o protocolo sem Windows nem hardware |
| WPF em vez de WinUI | as APIs BLE funcionam igual, e WPF é maduro para MVVM e distribuição self-contained |

## 9. Testes

`MiniPrint.Tests` cobre protocolo, raster, sequência, simulador, cliente,
imagens, configurações, histórico e o serviço usado pela interface. Os valores
esperados vêm de três fontes: as tabelas de referência, os frames fixos do
bitbank2 e **respostas reais capturadas da SC03h** — incluindo o CRC `0x9D` da
resposta ao comando `0xA3`.

A interface tem verificação própria: `MiniPrint.exe --selftest` abre a janela,
percorre as cinco abas e grava os erros de binding do XAML.
