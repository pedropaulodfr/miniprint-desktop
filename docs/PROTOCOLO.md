# Protocolo da impressora SC03h-5718 (família CAT Printer)

Este documento descreve **o que foi efetivamente observado** no equipamento
físico e o que veio de implementações abertas de referência. Cada afirmação traz
a sua origem, e o que permanece desconhecido está marcado como tal — nada aqui
foi inventado.

---

## 1. Identificação do equipamento

| Propriedade | Valor | Origem |
|---|---|---|
| Nome Bluetooth | `SC03h-5718` | anúncio BLE observado |
| Endereço MAC | `18:57:2A:44:E0:BC` | anúncio BLE observado |
| Tipo de conexão | Bluetooth Low Energy (GATT) | serviços observados |
| Largura de impressão | 384 pontos por linha | modelo e referências |
| Firmware reportado | `3.0.48` | resposta ao comando `0xA8` |
| MTU (PDU máxima) | 512 bytes | `GattSession.MaxPduSize` |

---

## 2. Camada BLE

### 2.1 Serviço e características

Observado no equipamento com `miniprint-diag inspect 18:57:2A:44:E0:BC`:

| UUID | Propriedades | Papel |
|---|---|---|
| `0000ae30-0000-1000-8000-00805f9b34fb` | serviço | serviço principal da impressora |
| `0000ae01-0000-1000-8000-00805f9b34fb` | write-no-resp | **escrita dos dados e comandos** |
| `0000ae02-0000-1000-8000-00805f9b34fb` | notify | **XOFF/XON e respostas** |
| `0000ae03-0000-1000-8000-00805f9b34fb` | write-no-resp | não utilizada (opcional no aparelho) |
| `0000ae04-0000-1000-8000-00805f9b34fb` | notify | não utilizada |
| `0000ae05-0000-1000-8000-00805f9b34fb` | indicate | não utilizada |
| `0000ae10-0000-1000-8000-00805f9b34fb` | read, write | não utilizada |

Além desses, o aparelho expõe o serviço genérico `0x1800` (nome do dispositivo,
`0x2A00`) e um serviço secundário `0000ae3a-...` com `0xAE3B` (write-no-resp) e
`0xAE3C` (notify), que **não** é usado pelo protocolo de impressão conhecido.

### 2.2 Fragmentação

O fluxo de bytes é contínuo: a impressora remonta os frames recebidos, então uma
escrita BLE pode cortar um frame no meio. As duas implementações de referência
fazem exatamente isso (o bitbank2 envia blocos de 20 bytes em Arduino).

Valores usados neste projeto:

- bloco padrão de **200 bytes** por escrita (valor da referência Cat-Printer);
- intervalo de **20 ms** entre escritas — sem confirmação, o enlace perde dados
  se os pacotes forem enviados rápido demais;
- o bloco é limitado automaticamente a `MTU - 3` quando o MTU negociado é menor.

---

## 3. Formato do frame

```
 0x51 0x78 │ cmd │ 0x00 │ len_lo │ len_hi │ dados... │ crc8 │ 0xFF
 prefixo   │     │ dir  │ tamanho (LE)     │ payload  │      │ fim
```

| Campo | Tamanho | Observação |
|---|---|---|
| prefixo | 2 | sempre `51 78` (STX) |
| `cmd` | 1 | código do comando |
| direção | 1 | `0x00` = PC → impressora; `0x01` = impressora → PC |
| tamanho | 2 | little-endian; o byte alto é sempre `0x00` (máx. 255 bytes) |
| payload | 0–255 | dados do comando |
| checksum | 1 | CRC-8 do **payload apenas** |
| fim | 1 | sempre `0xFF` (ETX) |

### 3.1 Checksum

CRC-8 com polinômio `0x07`, valor inicial `0`, sem reflexão, calculado sobre o
payload. Âncoras verificadas:

| Dados | CRC | Como foi confirmado |
|---|---|---|
| `00` | `0x00` | frame `0xA3` conhecido |
| `10` | `0x70` | frame de XOFF do próprio equipamento |
| `32` | `0x9E` | frame `0xA4` enviado à SC03h real |
| payload do lattice start | `0xA1` | frame do bitbank2 + CRC do equipamento |
| payload do lattice end | `0x11` | idem |
| `00 11 29` | `0x9D` | **resposta real** da SC03h ao `0xA3` |

O último item é a prova de que o equipamento usa este mesmo algoritmo: o CRC
calculado coincide com o byte que ele enviou.

---

## 4. Comandos

| Código | Nome | Payload | Status |
|---|---|---|---|
| `0xA0` | retrair papel | 2 bytes LE (pontos) | referência (não testado no aparelho) |
| `0xA1` | avançar papel | 2 bytes LE (pontos) | **confirmado no uso físico** |
| `0xA2` | dados de varredura | 1 linha de `largura/8` bytes | **confirmado no uso físico** |
| `0xA3` | estado do dispositivo | `0x00` | **confirmado** (o aparelho responde) |
| `0xA4` | qualidade / DPI | `0x32` | enviado; sem resposta (normal) |
| `0xA6` | controle de "lattice" | 11 bytes fixos | enviado; sem resposta |
| `0xA8` | informações do dispositivo | `0x00` | **confirmado** (devolve versão) |
| `0xA9` | atualizar dispositivo | `0x00` | enviado; sem resposta |
| `0xAE` | controle de fluxo | `0x10` (pausa) / `0x00` (retoma) | **protocolo do equipamento** |
| `0xAF` | energia térmica | 2 bytes LE (1–0xFFFF) | enviado (ver limitações) |
| `0xBD` | velocidade | 1 byte (menor = mais rápido) | enviado |
| `0xBE` | modo de desenho | 1 byte (ver limitações) | enviado |

### 4.1 Payloads fixos

Início do "lattice" (`0xA6`):

```
AA 55 17 38 44 5F 5F 5F 44 38 2C      (CRC 0xA1)
```

Fim do "lattice" (`0xA6`):

```
AA 55 17 00 00 00 00 00 00 00 17      (CRC 0x11)
```

---

## 5. Sequência de impressão

Ordem usada pela referência Cat-Printer e replicada aqui:

1. `0xA3 00` — consulta de estado;
2. `0xA3 00` — "iniciar impressão" (o frame é idêntico ao anterior; a referência
   envia os dois);
3. `0xA4 32` — qualidade / DPI;
4. `0xBD <velocidade>` — velocidade (padrão 32);
5. `0xAF <energia LE>` — energia térmica;
6. `0xBE <modo>` — modo de desenho;
7. `0xA9 00` — atualizar dispositivo;
8. `0xA6 <lattice start>`;
9. `0xA2 <linha>` — **repetido uma vez por linha da imagem**;
10. `0xA6 <lattice end>`;
11. `0xBD <velocidade de avanço>` — 8 nas referências;
12. `0xA1 <linhas LE>` — avanço final;
13. `0xA3 00` — consulta de estado final.

Modelos marcados como "novos" (GB03) recebem o passo 2 com o prefixo `0x12`. A
SC03h **não** é um modelo novo.

> **Validado no equipamento:** esta sequência foi enviada à SC03h-5718 e a página
> de diagnóstico saiu no papel corretamente — texto legível, régua de 384 pontos
> completa, degraus de cinza e degradê com meio-tom. Ver
> [`RESULTADOS-VALIDACAO.md`](RESULTADOS-VALIDACAO.md), item 2.5.

### 5.1 Avanço final e o lacre

O cabeçote fica alguns milímetros antes do lacre (a guilhotina serrilhada). Se o
trabalho terminar sem avanço suficiente, **o corte atravessa a última linha
impressa** — foi exatamente o que aconteceu no primeiro teste com o valor de 4
pontos do perfil (≈ 0,5 mm).

Por isso o comando `0xA1` recebe a soma:

```
0xA1 (4 + 128 = 132 pontos ≈ 16,5 mm)
```

- **4** é o valor de "linhas de alimentação final" do perfil da SC03h;
- **128** é a margem de segurança (≈ 16 mm), o mesmo valor que a referência
  Cat-Printer usa no fim de cada trabalho.

O valor é ajustável; se o papel ainda for cortado no texto, aumentar para
200–250 pontos resolve. O comando aceita 16 bits (até 65.535 pontos ≈ 8,2 m).

---

## 6. Imagem (raster)

- Largura: **384 pontos** = **48 bytes por linha**.
- Cada linha vai em **um frame `0xA2`** de 48 bytes (56 no total). Ambas as
  implementações de referência fazem uma linha por frame; não foi testado
  agrupar várias linhas em um frame, então não fazemos isso.
- Formato interno: 1 bit por ponto, bit 7 = ponto mais à esquerda (igual ao PBM
  P4). Antes de enviar, **cada byte é espelhado** (os 8 bits invertem de ordem),
  porque a impressora trata o bit 0 como o ponto mais à esquerda.
- Linhas são enviadas de cima para baixo.
- Não há compressão: o comando `0xA2` é "dados não comprimidos". Compressão
  aparece na referência apenas como tarefa não implementada; não foi usada.

---

## 7. Controle de fluxo

A impressora pausa o envio quando o buffer enche, enviando notificações que são
frames completos:

| Frame | Significado |
|---|---|
| `51 78 AE 01 01 00 10 70 FF` | **XOFF** — pare de enviar |
| `51 78 AE 01 01 00 00 00 FF` | **XON** — pode continuar |

O aplicativo verifica o estado antes de cada escrita BLE e espera a retomada.
Se a pausa durar mais que o timeout configurado (padrão 15 s), o trabalho falha
com mensagem clara em vez de travar.

---

## 8. Respostas observadas no equipamento real

### 8.1 Estado (`0xA3`)

Enviado `51 78 A3 00 01 00 00 00 FF`, recebido:

```
51 78 A3 01 03 00 00 11 29 9D FF
```

- direção `0x01` (impressora → PC), 3 bytes de payload: `00 11 29`;
- CRC `0x9D` confere com o cálculo sobre o payload.

O **primeiro byte** é um conjunto de bits documentado na wiki do projeto
[fulda1/Thermal_Printer](https://github.com/fulda1/Thermal_Printer/wiki/Cat-printer-protocol):

| Bit | Valor | Significado |
|---|---|---|
| 0 | `0x01` | sensor de papel — sem papel ou porta aberta |
| 1 | `0x02` | tampa aberta |
| 2 | `0x04` | cabeçote quente |
| 3 | `0x08` | **bateria fraca** |
| 7 | `0x80` | ocupada (imprimindo) |

Com a impressora ociosa e com papel, o valor observado foi `0x00`.

Os **bytes 2 e 3** (`0x11` e `0x29` nesta unidade) continuam sem significado
publicado. A wiki registra `0x25` como terceiro byte em outras unidades, então
não há como afirmar que sejam percentual de bateria ou temperatura — o
aplicativo mostra esses bytes crus e **não** apresenta percentual de carga.

> Não existe, no protocolo, leitura de percentual de bateria: apenas o aviso de
> bateria fraca. O MiniPrint Desktop exibe esse aviso na aba Impressora e no
> cabeçalho.

### 8.2 Informações (`0xA8`)

Enviado `51 78 A8 00 01 00 00 00 FF`, recebido:

```
51 78 A8 01 0A 00 81 00 03 33 2E 30 2E 34 38 00 00 FF
```

Payload: `81 00 03` seguido de **`3.0.48`** em ASCII e `00`. O aplicativo extrai
a versão pela maior sequência imprimível do payload.

---

## 9. O que permanece desconhecido

| Ponto | Situação |
|---|---|
| Bytes 2 e 3 do `0xA3` | sem significado publicado; exibidos crus |
| Percentual de bateria | não existe no protocolo; só o aviso de bateria fraca |
| Semântica do `0xBE` | as referências divergem: Cat-Printer envia `1` sempre; bitbank2 envia `0` para imagem. O app usa `1` e permite trocar nas configurações |
| Unidade do avanço (`0xA1`) | o valor é tratado como pontos/linhas; a referência usa 128. O app usa 4 por padrão (pedido do projeto) e permite ajustar |
| Efeito real de `0xAF` e `0xBD` | os comandos são aceitos, mas o efeito não é mensurável sem inspeção visual do papel |
| Compressão de imagem | não implementada em nenhuma referência para a SC03h; **não** foi usada |
| Comandos `0xA0`, `0xA3` com outros payloads | não exercitados |

---

## 10. Referências

| Projeto | Licença | O que foi usado |
|---|---|---|
| [NaitLee/Cat-Printer](https://github.com/NaitLee/Cat-Printer) — `printer_lib/commander.py`, `printer_lib/models.py` | CC0-1.0 nos módulos citados | formato do frame, CRC, tabela de comandos, ordem da sequência, lista de modelos |
| [bitbank2/Thermal_Printer](https://github.com/bitbank2/Thermal_Printer) | Apache-2.0 | confirmação independente dos UUIDs, dos frames fixos, do espelhamento de bits e do CRC |

A implementação em C# deste projeto é original: foram reutilizados **fatos de
protocolo** (bytes, UUIDs, ordem de comandos), não código-fonte. Onde as duas
referências divergem, a divergência está documentada e exposta como configuração
em vez de ser escondida.

O repositório `NaitLee/Cat-Printer` como um todo é GPL-3.0; apenas os módulos de
protocolo citados são CC0. Nenhum arquivo do projeto foi copiado.
