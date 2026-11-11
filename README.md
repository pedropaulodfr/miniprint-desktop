# MiniPrint Desktop

Aplicativo desktop para Windows que conecta diretamente por **Bluetooth Low
Energy** à mini impressora térmica **SC03h-5718** (família CAT Printer) e imprime
textos e imagens — sem celular, sem Fun Printer, sem iPrint.

![MiniPrint Desktop](assets/logo-256.png)

---

## Estado do projeto

Este não é um protótipo visual: a comunicação BLE é real e usa as APIs nativas do
Windows (`Windows.Devices.Bluetooth`). O que já foi verificado no equipamento
físico está marcado abaixo.

| Funcionalidade | Estado |
|---|---|
| Descoberta BLE da SC03h-5718 | **validado no equipamento** |
| Conexão GATT (serviço `0xAE30`, escrita `0xAE01`, notificação `0xAE02`) | **validado no equipamento** |
| Leitura de estado (`0xA3`) e versão (`0xA8` → `3.0.48`) | **validado no equipamento** |
| Envio de trabalho de impressão (protocolo completo) | **validado no equipamento** |
| Impressão de texto | **validado no equipamento** (texto legível no papel) |
| Impressão de imagem (PNG/JPG/BMP) | **validado no equipamento** (página de teste impressa) |
| Raster de 384 pontos, ordem dos bits e das linhas | **validado no papel** (régua e texto sem espelhamento) |
| Meio-tom (Floyd-Steinberg, Atkinson, Bayer, limiar) | **validado no papel** (degradê com grão) |
| Controle de fluxo XOFF/XON | implementado; simulador e testes unitários |
| Modo simulado (sem papel) | funcional |
| Estado do equipamento (`0xA3`: papel, tampa, cabeçote, bateria fraca) | **validado no equipamento** |
| **Modelos de documento** (criar, campos dinâmicos, preencher, imprimir) | **funcional** — ver [`docs/MODELOS.md`](docs/MODELOS.md) |
| Histórico, configurações, diagnóstico e exportação de log | funcional |

A tabela de resultados do que foi testado está em
[`docs/RESULTADOS-VALIDACAO.md`](docs/RESULTADOS-VALIDACAO.md) e o roteiro para
completar a validação no papel em [`docs/TESTES-FISICOS.md`](docs/TESTES-FISICOS.md).

---

## Requisitos

- Windows 10 versão 2004 (build 19041) ou superior, ou Windows 11.
- Adaptador Bluetooth com suporte a **Bluetooth Low Energy**.
- Impressora SC03h-5718 (ou outro modelo CAT Printer de 384 pontos).
- Nenhuma instalação de .NET é necessária: o aplicativo é **self-contained**.

---

## Instalação

### Opção 1 — Instalador (recomendado)

Execute `MiniPrint-Setup-1.0.0.exe`.

> **Aviso do Windows:** o instalador não é assinado com certificado comercial, então
> o SmartScreen pode mostrar "O Windows protegeu o seu PC". Clique em
> **Mais informações → Executar assim mesmo**. O mesmo vale para o executável
> portátil na primeira execução.

- Instala em `%LOCALAPPDATA%\Programs\MiniPrint Desktop` (não pede senha de
  administrador).
- Cria atalho no Menu Iniciar e, se marcado, na Área de Trabalho.
- Inclui a ferramenta de diagnóstico em `tools\miniprint-diag.exe`.
- Desinstalar não apaga configurações nem histórico (ficam em
  `%LOCALAPPDATA%\MiniPrintDesktop`).

### Opção 2 — Portátil

Descompacte `MiniPrintDesktop-1.0.0-portable.zip` em qualquer pasta e execute
`MiniPrint.exe`. Não escreve no registro do Windows; os dados continuam em
`%LOCALAPPDATA%\MiniPrintDesktop`.

---

## Primeiros passos

1. Ligue a impressora e coloque papel.
2. **Desconecte a impressora do celular** e feche o Fun Printer/iPrint. Uma
   impressora BLE aceita uma conexão por vez.
3. Abra o MiniPrint Desktop.
4. Vá em **Impressora** e clique em **Pesquisar impressoras**.
5. A `SC03h-5718` aparece marcada com o perfil `SC03h` e o MAC
   `18:57:2A:44:E0:BC`.
6. Selecione e clique em **Conectar**. O cabeçalho fica verde com "Conectado".
7. Vá em **Imprimir**, escreva o texto ou escolha uma imagem.
8. Confira a pré-visualização e clique em **Imprimir**.

Se a impressora não aparecer na pesquisa, você ainda pode digitar o MAC no campo
de conexão manual ou usar **Usar impressora salva**.

### Nome e estado da impressora

- O nome definido em **Configurações → Nome** é usado em todo o aplicativo,
  mesmo quando a impressora anuncia sem nome no Bluetooth — é o caso desta
  unidade. Vale para o cabeçalho, a lista de dispositivos e o histórico.
- A aba **Impressora** mostra o **estado do equipamento**, lido pelo comando
  `0xA3`: bateria fraca, sem papel, tampa aberta, cabeçote quente e ocupada. O
  estado é consultado ao conectar, a cada 30 segundos e no botão **Atualizar**.
- O protocolo **não informa percentual de bateria**, só o aviso de bateria
  fraca. Quando ele aparece, o cabeçalho mostra um alerta vermelho.

---

## Imprimir texto

Na aba **Texto**:

- editor com quebras de linha;
- fonte, tamanho, negrito e itálico;
- alinhamento à esquerda, centralizado ou à direita;
- margens independentes (esquerda, direita, superior, inferior);
- espaçamento entre linhas;
- quebra automática de linhas na largura de 384 pontos.

O texto é rasterizado (renderizado como imagem) antes do envio, porque o
equipamento só aceita dados de varredura.

## Imprimir imagem

Na aba **Imagem**, use **Escolher imagem...** ou arraste o arquivo para a janela.
Formatos: **PNG, JPG/JPEG e BMP**.

- redimensionamento automático para 384 pontos preservando a proporção;
- recorte por margens (esquerda, direita, topo, base);
- rotação em 0°, 90°, 180° e 270°;
- brilho, contraste e limiar;
- inversão de cores (negativo);
- **intensidade desta impressão** (energia térmica de 0 a 65535), sem alterar a
  configuração global;
- meio-tom: **Floyd-Steinberg**, **Atkinson**, **Bayer 4×4**, **Bayer 8×8** ou
  **Limiar** simples.

A pré-visualização pode ser salva em PNG e os **pacotes preparados** podem ser
exportados em `.bin` com um resumo em `.txt` — útil para comparar com o tráfego
de outro aplicativo ou anexar em um relato de problema.

## Modelos de documento

A aba **Modelos** permite montar um documento uma vez e reutilizá-lo preenchendo
apenas os dados variáveis — comprovantes, recibos, declarações, etiquetas,
ordens de serviço.

- **Editor visual**: posicione textos, campos, linhas, retângulos e imagens
  livremente no papel de 384 pontos, com arrastar, redimensionar, alinhar,
  distribuir, agrupar, camadas, grade, zoom e undo/redo.
- **Editor de blocos**: o modo anterior, em fluxo, mantido para os modelos já
  existentes (com conversão para o editor visual).
- **Campos dinâmicos**: texto curto, texto longo, número, valor monetário, data,
  CPF, CNPJ, telefone e lista de opções — com máscara, validação e campo
  obrigatório.
- **Preenchimento**: o formulário é gerado automaticamente; a pré-visualização
  usa o mesmo renderizador da impressão.
- **Reuso**: duplicar, exportar e importar modelos em JSON (versão 1 e 2).
- **Privacidade**: por padrão o histórico guarda só o nome do modelo, sem os
  dados preenchidos.

Guia de uso: [`docs/MODELOS.md`](docs/MODELOS.md) · Como o editor funciona por
dentro: [`docs/EDITOR-DE-MODELOS.md`](docs/EDITOR-DE-MODELOS.md).

## Ajuste de qualidade

Em **Configurações**:

| Campo | Padrão | Efeito |
|---|---|---|
| Largura de impressão | 384 | pontos por linha |
| Linhas de alimentação final | 4 | avanço de papel no fim |
| **Margem de segurança** | **128 pontos (≈16 mm)** | **avanço extra para o texto passar do lacre antes do corte** |
| Velocidade (0xBD) | 32 | menor = mais rápido; afeta o aquecimento |
| Energia para imagens (0xAF) | 16384 | mais energia = mais escuro |
| Energia para texto (0xAF) | 24576 | idem, para texto |
| Bloco por escrita | 200 bytes | fragmentação do envio BLE |
| Intervalo entre pacotes | 20 ms | evita perda de dados |
| Timeout de comunicação | 15000 ms | tempo máximo de espera |

### Margem de segurança para o corte

A impressora imprime com o cabeçote a alguns milímetros do lacre. Se o trabalho
terminar logo depois da última linha, o corte atravessa o texto. Por isso o
avanço final soma dois valores:

```
avanço total (0xA1) = linhas de alimentação final + margem de segurança
                    = 4 + 128 = 132 pontos ≈ 16,5 mm
```

Se mesmo assim o texto for cortado, aumente a **margem de segurança** para 200
ou 250. Se quiser economizar papel, reduza. A aba **Impressora** tem um botão
**Avançar papel** para posicionar o papel sem imprimir.

## Histórico

A aba **Histórico** lista data, conteúdo, tipo, status, dispositivo, linhas,
bytes e erro de cada trabalho. Impressões no simulador aparecem como
**"Simulado"** e nunca são contadas como impressão real. É possível limpar e
exportar em CSV.

## Diagnóstico

A aba **Diagnóstico** registra:

- dispositivos BLE encontrados, MAC e RSSI;
- serviços e características GATT;
- MTU negociado;
- pacotes enviados e respostas recebidas (hexadecimal);
- erros, desconexões e tempo de transmissão.

Botões **Ler estado (0xA3)** e **Ler versão (0xA8)** consultam o aparelho.
**Exportar TXT** gera um arquivo pronto para enviar junto de um relato de
problema.

## Modo simulado

Em **Impressora**, marque **Modo simulado**. Nesse modo:

- a conexão é simulada;
- todos os pacotes são montados e **validados** (prefixo, CRC, sequência,
  controle de fluxo);
- nenhum dado vai para o hardware e nenhum papel é impresso;
- o relatório mostra frames, bytes, linhas, avanço e erros encontrados.

O cabeçalho exibe **MODO SIMULADO** para não haver confusão com impressão real.

---

## Ferramenta de linha de comando

`tools\miniprint-diag.exe` serve para validar o equipamento passo a passo.

```
miniprint-diag.exe scan 10
miniprint-diag.exe inspect 18:57:2A:44:E0:BC
miniprint-diag.exe probe 18:57:2A:44:E0:BC --verbose
miniprint-diag.exe print 18:57:2A:44:E0:BC --teste --confirmar
miniprint-diag.exe print 18:57:2A:44:E0:BC "foto.jpg" --dither FloydSteinberg --confirmar
miniprint-diag.exe simulate --texto "teste"
miniprint-diag.exe encode "foto.png"
```

Comandos que gastam papel exigem `--confirmar`.

---

## Estrutura do projeto

```
MiniPrint-Desktop/
├── src/
│   ├── MiniPrint.Core/          modelos, configurações, histórico, log, templates
│   ├── MiniPrint.Protocol/      frames, CRC-8, raster, sequência, cliente, simulador
│   ├── MiniPrint.Imaging/       texto, imagem, modelos, dithering, 384 pontos
│   ├── MiniPrint.Bluetooth/     descoberta BLE e transporte GATT (WinRT)
│   ├── MiniPrint.Desktop/       interface WPF (MVVM)
│   └── MiniPrint.Diagnostics/   ferramenta de linha de comando
├── tests/MiniPrint.Tests/       222 testes automatizados
├── docs/                        protocolo, modelos, testes físicos, diagnóstico, arquitetura
├── installer/                   script do Inno Setup
└── assets/                      ícone e logotipo
```

A camada de protocolo não conhece o Windows: ela fala com uma interface
(`IPrinterTransport`) que pode ser o Bluetooth real ou o simulador. Por isso o
mesmo código é testado sem hardware.

## Compilar do código-fonte

Requer o **.NET SDK 8.0** (ou superior).

```powershell
dotnet build MiniPrint.sln -c Release
dotnet test tests\MiniPrint.Tests\MiniPrint.Tests.csproj
dotnet run --project src\MiniPrint.Desktop
```

Para gerar os artefatos de distribuição:

```powershell
# aplicativo (arquivo único, self-contained)
dotnet publish src\MiniPrint.Desktop\MiniPrint.Desktop.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -o publish\singlefile

# ferramenta de diagnóstico
dotnet publish src\MiniPrint.Diagnostics\MiniPrint.Diagnostics.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -o publish\tools

# instalador (requer Inno Setup 6 ou 7)
& "tools\InnoSetup\ISCC.exe" installer\MiniPrint.iss
```

Verificação automática da interface (abre a janela, percorre as cinco abas e
grava um relatório de erros de binding):

```powershell
publish\singlefile\MiniPrint.exe --selftest
# relatório em %LOCALAPPDATA%\MiniPrintDesktop\selftest.txt
```

---

## Limitações conhecidas

- A página de teste saiu correta no papel (texto, régua de 384 pontos, degraus de
  cinza e degradê). Faltam apenas os testes 6, 8, 9 e 10 do roteiro
  `docs/TESTES-FISICOS.md` — imagem simples, imagem longa, medição do avanço e
  queda de conexão durante o envio.
- A intensidade ideal varia com o papel e com o desgaste do cabeçote: a página de
  teste saiu em tom médio com energia 24576. Para texto mais escuro, use
  28000–32000 em **Configurações → Energia para texto**.
- O significado dos três bytes da resposta `0xA3` não é documentado pelo
  fabricante; o app mostra os valores crus.
- O byte do comando `0xBE` é ambíguo entre as implementações de referência e
  está exposto nas configurações.
- A unidade do comando de avanço de papel é tratada como pontos; o valor ideal
  pode variar por unidade.
- Não há suporte a compressão de imagem (não usada pela SC03h).
- Apenas impressoras de 384 pontos foram consideradas no fluxo da interface.

## Solução de problemas

Veja [`docs/DIAGNOSTICO.md`](docs/DIAGNOSTICO.md). Resumo:

| Sintoma | Causa provável | Ação |
|---|---|---|
| Impressora não aparece | conectada ao celular, desligada ou longe | desconecte do celular, aproxime, pesquise de novo |
| "Falha ao descobrir serviços GATT" | outro app usando a impressora | feche Fun Printer/iPrint e o Bluetooth do telefone |
| Impressão sai clara | energia baixa | aumente "Energia para imagens" |
| Impressão sai borrada | energia alta ou velocidade baixa | reduza energia, aumente a velocidade |
| Impressão para no meio | fluxo pausado | veja o log de diagnóstico; aumente o timeout |
| Imagem espelhada | orientação do raster | relate com o log exportado |

---

## Licença e créditos

Código deste projeto: **MIT** (ver `LICENSE.txt`).

O protocolo foi levantado a partir de implementações de código aberto — o
detalhamento e as licenças estão em [`docs/PROTOCOLO.md`](docs/PROTOCOLO.md) e
em `LICENSE.txt`:

- [NaitLee/Cat-Printer](https://github.com/NaitLee/Cat-Printer) (módulos de
  protocolo em CC0-1.0);
- [bitbank2/Thermal_Printer](https://github.com/bitbank2/Thermal_Printer)
  (Apache-2.0).

Nenhum código desses projetos foi copiado; foram usados fatos de protocolo
(identificadores, bytes de comando, formato de frame) confirmados no equipamento
físico.
