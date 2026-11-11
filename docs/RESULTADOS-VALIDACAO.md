# Resultados da validação

Registro do que foi efetivamente executado, com a evidência de cada item. Nada
aqui foi declarado aprovado sem observação real.

Equipamento: **SC03h-5718**, MAC `18:57:2A:44:E0:BC`, firmware `3.0.48`,
adaptador do PC: Qualcomm Atheros QCA9377 (BLE), Windows 11.

---

## 1. Testes executados no equipamento físico

| # | Teste | Resultado | Evidência |
|---|---|---|---|
| 1 | Descoberta BLE | **APROVADO** | `scan 10` retornou `18:57:2A:44:E0:BC  SC03h-5718  -30 dBm  <== impressora compatível` |
| 2 | Conexão BLE | **APROVADO** | `Conectado. MTU=512, bloco máximo de escrita=509 bytes, notificações=ativas` |
| 3 | Serviços GATT | **APROVADO** | serviço `0xAE30` com `0xAE01` (write-no-resp), `0xAE02` (notify), `0xAE03`, `0xAE04`, `0xAE05`, `0xAE10`; serviço extra `0xAE3A` |
| 4 | Escrita e notificações | **APROVADO** | `0xA3` respondeu `51 78 A3 01 03 00 00 11 29 9D FF`; `0xA8` respondeu `51 78 A8 01 0A 00 81 00 03 33 2E 30 2E 34 38 00 00 FF` (versão `3.0.48`) |
| 5 | Impressão de texto | **ENVIADO — aguarda confirmação visual** | `print --texto` com energia 28672 e avanço 12: 2.426 bytes, 14 escritas, 0,5 s |
| 6 | Impressão de imagem simples | **NÃO EXECUTADO** | depende do roteiro em `TESTES-FISICOS.md` |
| 7 | Impressão de imagem complexa (página de teste) | **APROVADO — conferido no papel** | `print --teste`: 20.290 bytes, 103 escritas BLE, 360 linhas, 3,1 s; página impressa legível e correta (ver 2.5) |
| 8 | Transmissão extensa | **PARCIAL** | 360 linhas (≈ 45 mm de papel) transmitidas sem falha; falta uma imagem longa (por exemplo 2.000 linhas) |
| 9 | Alimentação final | **APROVADO — conferido no papel** | com margem de 132 pontos (4 + 128) o texto fica livre do lacre e pode ser cortado sem atingir a impressão |
| 10 | Recuperação de desconexão | **NÃO EXECUTADO no hardware** | coberto por teste automatizado com falha injetada |

### 1.1 Situação

O **caminho de comunicação está comprovado**: o equipamento aceita os pacotes,
responde aos comandos de estado e versão, e a página de teste saiu no papel com
o conteúdo e a orientação corretos (item 2.5).

O que falta é apenas calibrar dois detalhes subjetivos (intensidade ideal e
unidade do avanço) e repetir os testes 5, 6, 8, 9 e 10 do roteiro.

### 1.2 O que já está provado no papel

- texto legível e **não espelhado**;
- régua completa de 384 pontos, com as marcas de borda nas extremidades;
- degraus de cinza, barras de espessura crescente e degradê com meio-tom;
- orientação das linhas de cima para baixo, sem inversão.

Isso valida o raster, o espelhamento de bits, a ordem das linhas e a largura de
384 pontos — que eram as hipóteses mais arriscadas do protocolo.

### 1.3 Correção encontrada no teste real: margem de corte

O primeiro trabalho impresso pela interface terminou com o texto **encostado no
lacre**: o avanço final era de 4 pontos (≈ 0,5 mm), valor do perfil da SC03h, e
qualquer corte atravessaria a última linha.

**Causa:** o cabeçote térmico fica alguns milímetros antes da guilhotina. Sem
avanço suficiente depois da última linha, o corte cai sobre o texto.

**Correção aplicada:** o avanço final passou a somar a margem de segurança:

```
0xA1 (4 linhas finais + 128 pontos de margem = 132 pontos ≈ 16,5 mm)
```

128 pontos é o mesmo valor que a referência Cat-Printer envia ao final de cada
trabalho, e é ajustável em **Configurações → Margem de segurança**. Também foi
adicionado um botão **Avançar papel** (aba Impressora) e o comando
`miniprint-diag feed <MAC> <pontos>` para posicionar o papel sem imprimir.

| Antes | Depois |
|---|---|
| `0xA1 04 00` (0,5 mm) — corte no texto | `0xA1 84 00` (16,5 mm) — texto livre do lacre |

**Confirmado no papel:** com 132 pontos o texto fica inteiro acima do lacre e o
corte pode ser feito sem atingir a impressão. O valor 128 de margem passa a ser
o padrão do aplicativo.

---

## 2. Evidência bruta capturada

### 2.1 Descoberta

```
Bluetooth: Disponivel - Bluetooth disponível (a463a1509193).
Procurando dispositivos BLE por 10 s...

  18:57:2A:44:E0:BC  SC03h-5718                         -30 dBm  <== impressora compatível
  07:97:95:BE:4F:5A  (sem nome) 07:97:95:BE:4F:5A       -37 dBm
  04:57:91:24:AF:6D  LE-Philips TAH6509                 -28 dBm

Total: 3 dispositivo(s).
Impressoras compatíveis: 1
  -> SC03h-5718 (18:57:2A:44:E0:BC) perfil SC03h
```

### 2.2 Árvore GATT

```
Serviço 00001800-0000-1000-8000-00805f9b34fb
    00002a00-0000-1000-8000-00805f9b34fb  (read, write)
Serviço 0000ae30-0000-1000-8000-00805f9b34fb <<< serviço da impressora
    0000ae01-0000-1000-8000-00805f9b34fb  (write-no-resp)  [escrita]
    0000ae02-0000-1000-8000-00805f9b34fb  (notify)  [notificação]
    0000ae03-0000-1000-8000-00805f9b34fb  (write-no-resp)
    0000ae04-0000-1000-8000-00805f9b34fb  (notify)
    0000ae05-0000-1000-8000-00805f9b34fb  (indicate)
    0000ae10-0000-1000-8000-00805f9b34fb  (read, write)
Serviço 0000ae3a-0000-1000-8000-00805f9b34fb
    0000ae3b-0000-1000-8000-00805f9b34fb  (write-no-resp)
    0000ae3c-0000-1000-8000-00805f9b34fb  (notify)
```

### 2.3 Sondas de comando

```
== 0xA3 estado ==
   BLE> sonda (9 bytes): 51 78 A3 00 01 00 00 00 FF
   BLE< notificação (11 bytes): 51 78 A3 01 03 00 00 11 29 9D FF

== 0xA8 info ==
   BLE> sonda (9 bytes): 51 78 A8 00 01 00 00 00 FF
   BLE< notificação (18 bytes): 51 78 A8 01 0A 00 81 00 03 33 2E 30 2E 34 38 00 00 FF

== 0xA4 qualidade ==
   BLE> sonda (9 bytes): 51 78 A4 00 01 00 32 9E FF
   (sem resposta em 3 s)

== 0xA9 update ==
   BLE> sonda (9 bytes): 51 78 A9 00 01 00 00 00 FF
   (sem resposta em 3 s)
```

O checksum `0x9D` da resposta ao `0xA3` foi recalculado pelo aplicativo sobre o
payload `00 11 29` e coincide — confirmação de que o CRC-8 do protocolo é o
mesmo usado pelo equipamento.

### 2.4 Envio de impressão (página de teste)

```
Enviando dados (360/360 linhas)
Finalizando (avanço de papel)
Impressão concluída
Trabalho concluído: 20290 bytes em 103 escritas BLE, 360 linhas, 3351 ms.
Concluído: 20290 bytes, 103 escritas, 3,4 s.
```

### 2.5 Conferência no papel (página de teste)

A página de diagnóstico foi impressa duas vezes (envio inicial e reenvio a
pedido). Análise do papel:

| Elemento impresso | Esperado | Observado |
|---|---|---|
| Título "MiniPrint Desktop" | legível, não espelhado | **correto** |
| Linha "Teste de impressão - 384 pontos por linha" | legível | **correto** |
| Régua (marcas a cada 32, números a cada 64) | 0 até 352 | **correto** (0 a 320 visíveis, 352 na borda) |
| Barras de borda (2 pontos em cada extremidade) | encostadas nas laterais | **correto** — largura de 384 pontos confirmada |
| 8 degraus de cinza | do claro ao escuro | **correto** |
| Xadrez de 1 pixel | textura quadriculada | **correto** (aparece como textura fina; a foto reduz a resolução) |
| Linhas de espessura crescente | 1 a 5 pontos | **correto** |
| Texto de referência (maiúsculas, minúsculas, dígitos) | legível | **correto** |
| Degradê com meio-tom | grão, não bloco chapado | **correto** |
| Rótulo "Meio-tom: FloydSteinberg" | legível | **correto** |

Conclusões que isso fecha:

1. **Orientação do raster** — texto não está espelhado nem de cabeça para baixo.
2. **Ordem dos bits** — o espelhamento por byte aplicado em `CatPrinterRaster`
   está correto.
3. **Largura** — os 384 pontos são usados de ponta a ponta.
4. **Meio-tom** — o dithering chega ao papel com grão.
5. **Sequência de comandos** — o aparelho aceitou a preparação, os dados e o
   encerramento, e imprimiu.

Ajuste recomendado (opcional): a impressão saiu em tom médio. Para texto mais
escuro, use **Energia para texto** em 28000–32000 nas configurações.

---

## 2.6 Modelos de documento

O modelo de exemplo "Comprovante de Pagamento" foi renderizado e enviado ao
equipamento pelo mesmo pipeline das outras telas:

```
Modelo: Comprovante de Pagamento
Imagem: 384x400 pontos (50,0 mm de papel aprox.)
Impresso: 22530 bytes, 400 linhas.
```

A imagem gerada (384 pontos de largura) contém título centralizado em negrito,
divisores de `=` e `-`, os seis campos preenchidos com máscara (CPF
`123.456.789-09`, valor `150,00`, data `05/11/2026`), a linha de assinatura com
sublinhados e o rodapé centralizado.

> **Achado corrigido durante a implementação:** sublinhados e traços finos
> desapareciam na binarização. O limiar de conversão para 1 bit foi ajustado de
> 128 para 180, o que também deixou o texto mais escuro no papel. O ajuste vale
> para texto livre, imagens e modelos.

A conferência visual do comprovante no papel está pendente.

---

## 2.7 Editor visual

O editor visual foi exercitado de duas formas:

**Autoverificação da interface** — abre um modelo convertido para o editor
visual, monta o canvas com os elementos posicionados e gera a prévia:

```
Editor visual: prévia gerada (14 elemento(s))
Erros de binding: 0
```

**Impressão no equipamento** — o mesmo comprovante, agora em layout absoluto,
foi enviado à SC03h:

```
Modelo: Comprovante de Pagamento (visual)
Imagem: 384x330 pontos (41,3 mm de papel aprox.)
Impresso: 18610 bytes, 330 linhas.
```

A imagem renderizada mantém título centralizado, divisores, os seis campos
preenchidos com máscara e a linha de assinatura. A conferência visual no papel
está pendente.

## 3. Testes automatizados

```
dotnet test tests\MiniPrint.Tests\MiniPrint.Tests.csproj
Passed! - Failed: 0, Passed: 222, Skipped: 0, Total: 222
```

Cobrem:

- CRC-8 contra valores de referência **e** contra as respostas reais do aparelho;
- formato de frame (montagem, leitura, CRC corrompido, payload grande);
- raster: 384 pontos → 48 bytes por linha, espelhamento de bits, MSB→LSB;
- sequência completa de comandos (ordem, avanço, lattice, modelos especiais);
- simulador: validação de frames, remontagem entre escritas, CRC inválido;
- cliente: progresso, XOFF/XON, timeout de fluxo, cancelamento, trabalho
  concorrente, falha injetada, desconexão;
- imagens: 384 pontos, proporção, rotação, recorte, margens, inversão, os cinco
  algoritmos de meio-tom, determinismo;
- texto: largura, tinta, alinhamento, múltiplas linhas, texto vazio;
- serviço da interface: conexão simulada, energia por tipo de trabalho, avanço;
- configurações e histórico: gravação, leitura, arquivo corrompido;
- **modelos de documento**: criação, edição, duplicação, exclusão, persistência,
  campos dinâmicos, máscaras (CPF/CNPJ/telefone/valor), validação de
  obrigatórios, substituição de campos, renderização em 384 pontos, exportação e
  importação com validação estrutural;
- **editor visual**: layout e coordenadas, altura automática, limite de largura,
  renderização de texto e imagem, elementos ocultos, conversão de blocos para
  layout, validação de campos no layout, serialização do formato versão 2 e
  compatibilidade com arquivos versão 1.

## 4. Validação da interface

```
MiniPrint.exe --selftest
OK
Abas verificadas: 6
Erros de binding: 0
Pré-visualização: gerada
Linhas na pré-visualização: 246
Modelos salvos: 0
```

O modo `--selftest` abre a janela real, percorre as seis telas, abre o editor de
modelos e a tela de preenchimento com um modelo de exemplo em memória, gera a
pré-visualização e grava um relatório com qualquer erro de binding do XAML. Foi
executado no build de depuração, no build publicado em pasta e no executável de
arquivo único.

## 5. Validação do pacote de instalação

| Item | Resultado |
|---|---|
| Compilação do instalador (Inno Setup 7.1.1) | OK — `MiniPrint-Setup-1.0.0.exe`, 97,3 MB |
| Instalação silenciosa | OK — arquivos, atalhos no Menu Iniciar |
| Execução do app instalado | OK — autoverificação sem erros |
| Desinstalação | OK — pasta e atalhos removidos |

---

## 6. O que falta para fechar a validação

O essencial está fechado: comunicação, protocolo, raster, impressão no papel e
modelos de documento foram implementados e exercitados. Restam cinco itens de
calibração e conferência:

1. **Teste 6** — imprimir uma imagem real (foto ou logotipo) e conferir a
   orientação e o recorte.
2. **Teste 8** — imprimir uma imagem longa (≈ 2.000 linhas) para exercitar o
   controle de fluxo em transmissão extensa.
3. **Teste 9** — medir no papel a diferença entre 4 e 20 pontos de avanço e
   calibrar o campo se necessário.
4. **Teste 10** — desligar a impressora durante o envio e confirmar a mensagem
   de erro e a reconexão.
5. **Modelos** — conferir no papel o comprovante impresso e testar um modelo
   criado do zero pela interface.
6. **Editor visual** — abrir **Modelos → Novo documento**, arrastar e
   redimensionar elementos, testar undo/redo e imprimir o resultado.

Se algum item falhar, o ajuste é localizado:

| Sintoma | Onde ajustar |
|---|---|
| imagem espelhada | `CatPrinterRaster.EncodeScanlines` (espelhamento de bits) |
| imagem de cabeça para baixo | ordem das linhas em `CatPrinterSequence` |
| impressão clara ou escura | energia nas configurações (ou por impressão) |
| avanço de papel errado | campo "Linhas de alimentação final" |
