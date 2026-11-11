# Roteiro de testes com a impressora física

Este roteiro existe porque **nenhuma validação de impressão térmica é conclusiva
sem papel na mão**. Os testes abaixo devem ser executados na SC03h-5718
(`18:57:2A:44:E0:BC`) e o resultado anotado na tabela final.

Tempo total estimado: 15 a 25 minutos.

---

## Antes de começar

1. Ligue a impressora e carregue papel.
2. **Desconecte a impressora do celular.** Se o app Fun Printer/iPrint estiver
   aberto (ou o Bluetooth do telefone conectado à impressora), o Windows não
   conseguirá falar com ela.
3. Confirme que o Bluetooth do computador está ligado.
4. Feche outros aplicativos que possam estar usando a impressora.

Todos os comandos abaixo usam a ferramenta de diagnóstico:

```
miniprint-diag.exe <comando>
```

Ela fica na pasta de instalação do MiniPrint, em `tools\miniprint-diag.exe`.
No modo portátil, na mesma pasta do `MiniPrint.exe`.

---

## Teste 1 — Descoberta

**Objetivo:** encontrar a impressora pelo Bluetooth.

```powershell
miniprint-diag.exe scan 10
```

**O que observar:** a lista deve mostrar `18:57:2A:44:E0:BC  SC03h-5718` com a
marcação `<== impressora compatível`.

**Aprovado quando:** a SC03h aparece na lista em até 10 segundos.

| Resultado | |
|---|---|
| Encontrada? | ( ) sim ( ) não |
| RSSI mostrado | ______ dBm |
| Nome exibido | ______________ |

---

## Teste 2 — Conexão

**Objetivo:** estabelecer o enlace BLE.

```powershell
miniprint-diag.exe probe 18:57:2A:44:E0:BC --verbose
```

**O que observar:** a linha `Conectado: ... MTU 512 ...` e, em seguida, as
respostas dos comandos.

**Aprovado quando:** conecta sem erro e mostra o MTU.

| Resultado | |
|---|---|
| Conectou? | ( ) sim ( ) não |
| MTU | ______ |
| Mensagem de erro (se houve) | ______________________ |

---

## Teste 3 — Serviços e características

**Objetivo:** confirmar o serviço `0xAE30` e as características `0xAE01`/`0xAE02`.

```powershell
miniprint-diag.exe inspect 18:57:2A:44:E0:BC
```

**O que observar:** `Serviço 0xAE30: presente, 0xAE01: presente, 0xAE02: presente`
e a árvore GATT completa.

**Aprovado quando:** os três itens aparecem como "presente".

| Resultado | |
|---|---|
| Serviço 0xAE30 | ( ) presente ( ) ausente |
| Característica 0xAE01 | ( ) presente ( ) ausente |
| Característica 0xAE02 | ( ) presente ( ) ausente |
| MTU | ______ |

---

## Teste 4 — Comunicação (escrita e notificações)

**Objetivo:** validar escrita e leitura de notificações.

```powershell
miniprint-diag.exe probe 18:57:2A:44:E0:BC --verbose
```

**O que observar:**

- `0xA3` deve responder algo como `51 78 A3 01 03 00 00 11 29 9D FF`;
- `0xA8` deve responder com a versão (ex.: `... 33 2E 30 2E 34 38 ...` = `3.0.48`);
- `0xA4` e `0xA9` normalmente **não** respondem — isso é esperado.

**Aprovado quando:** chega pelo menos uma resposta ao `0xA3` e a versão do `0xA8`
é legível.

| Resultado | |
|---|---|
| Resposta do 0xA3 (hex) | ______________________________ |
| Versão do firmware | ______________ |
| 0xA4/0xA9 sem resposta? | ( ) sim, esperado ( ) não |

> Se a versão vier diferente de `3.0.48`, anote: o firmware pode variar entre
> unidades e isso ajuda a explicar diferenças de comportamento.

---

## Teste 5 — Impressão de texto

**Objetivo:** imprimir uma mensagem curta.

```powershell
miniprint-diag.exe print 18:57:2A:44:E0:BC --texto "MiniPrint Desktop - teste de texto" --confirmar
```

**O que observar:** o papel deve sair com o texto legível, alinhado à esquerda.

**Aprovado quando:** o texto sai legível e sem cortar as bordas.

| Resultado | |
|---|---|
| Imprimiu? | ( ) sim ( ) não |
| Texto legível? | ( ) sim ( ) parcial ( ) não |
| Saiu cortado nas laterais? | ( ) não ( ) sim |
| Observações | ______________________________ |

---

## Teste 6 — Impressão de imagem simples

**Objetivo:** imprimir uma imagem em preto e branco.

```powershell
miniprint-diag.exe print 18:57:2A:44:E0:BC "C:\caminho\logo.png" --dither Limiar --confirmar
```

Use uma imagem com formas pretas grandes (logotipo, texto grande).

**O que observar:** a imagem deve sair com a mesma orientação da tela (sem
espelhamento e sem invertida).

**Aprovado quando:** a imagem não está espelhada, nem de cabeça para baixo, nem
com o preto e o branco trocados.

| Resultado | |
|---|---|
| Imprimiu? | ( ) sim ( ) não |
| Orientação correta? | ( ) sim ( ) espelhada ( ) invertida |
| Largura correta (sem corte)? | ( ) sim ( ) não |

---

## Teste 7 — Impressão de imagem complexa

**Objetivo:** avaliar o meio-tom em uma imagem com cinzas.

```powershell
miniprint-diag.exe print 18:57:2A:44:E0:BC --teste --dither FloydSteinberg --energia 16384 --confirmar
```

A página de teste embutida contém régua de 384 pontos, degraus de cinza, xadrez
de 1 pixel e um degradê.

**O que observar:**

- a régua deve marcar de 0 a 352 e as bordas pretas devem encostar nas laterais;
- o xadrez de 1 pixel deve sair quadriculado (se sair em faixas, há problema de
  orientação de bits);
- o degradê deve sair com grão, não como bloco único.

**Aprovado quando:** a régua está completa, o xadrez é quadriculado e o degradê
tem meio-tom.

| Resultado | |
|---|---|
| Régua completa (0 a 352)? | ( ) sim ( ) não |
| Xadrez correto? | ( ) sim ( ) não |
| Degradê com meio-tom? | ( ) sim ( ) não |
| Impressão clara demais / escura demais? | ( ) clara ( ) boa ( ) escura |

---

## Teste 8 — Transmissão extensa

**Objetivo:** validar fragmentação e controle de fluxo em um trabalho maior.

Imprima uma imagem alta (por exemplo, uma foto redimensionada ou uma captura de
tela longa):

```powershell
miniprint-diag.exe print 18:57:2A:44:E0:BC "C:\caminho\foto.jpg" --dump trafego.bin --confirmar
```

**O que observar:** a impressão deve sair contínua, sem trechos em branco no
meio nem travamento. O arquivo `trafego.bin` guarda exatamente o que foi enviado
— guarde-o se algo der errado.

**Aprovado quando:** a imagem inteira sai sem falhas e o comando termina com
"Concluído".

| Resultado | |
|---|---|
| Terminou sem erro? | ( ) sim ( ) não |
| Houve falha no meio? | ( ) não ( ) sim — onde: __________ |
| Tempo total (s) | ______ |
| Tamanho do tráfego (bytes) | ______ |

---

## Teste 9 — Alimentação final de papel

**Objetivo:** verificar as linhas de avanço no fim do trabalho e a margem de
segurança do corte.

O aplicativo avança **linhas de alimentação final + margem de segurança**
(padrão 4 + 128 = 132 pontos ≈ 16,5 mm). Para conferir, use valores bem
diferentes:

```powershell
miniprint-diag.exe print 18:57:2A:44:E0:BC --texto "A" --seguranca 20 --confirmar
miniprint-diag.exe print 18:57:2A:44:E0:BC --texto "B" --seguranca 250 --confirmar
```

**O que observar:** o segundo trabalho deve deixar um espaço em branco bem maior
depois da letra. No primeiro, o texto provavelmente ficará no lacre; no segundo,
deve sobrar papel suficiente para cortar sem atingir a letra.

Também é possível avançar papel sem imprimir:

```powershell
miniprint-diag.exe feed 18:57:2A:44:E0:BC 200
```

**Aprovado quando:** o texto do segundo trabalho pode ser cortado sem atingir a
impressão, e a diferença entre os dois é visível.

| Resultado | |
|---|---|
| Margem de 20 pontos | ______ mm (aprox.) |
| Margem de 250 pontos | ______ mm (aprox.) |
| A diferença é visível? | ( ) sim ( ) não |
| Com 128 (padrão) o corte atinge o texto? | ( ) não ( ) sim |

> Se 128 ainda não bastar, informe o valor que funcionou: ele passa a ser o
> padrão do aplicativo.

---

## Teste 10 — Recuperação de desconexão

**Objetivo:** garantir que o app não trava quando a impressora cai no meio do
trabalho.

1. Inicie uma impressão longa:
   ```powershell
   miniprint-diag.exe print 18:57:2A:44:E0:BC "C:\caminho\foto.jpg" --confirmar
   ```
2. Durante o envio, **desligue a impressora**.
3. Observe o resultado do comando.

**O que observar:** o comando deve terminar com mensagem de erro clara
(`FALHOU: ...`), sem travar e sem exceção não tratada. No aplicativo gráfico,
o estado deve voltar para "Desconectado".

**Aprovado quando:** o erro é reportado e o app continua utilizável. Depois de
religar a impressora, conectar novamente deve funcionar.

| Resultado | |
|---|---|
| O comando terminou com erro claro? | ( ) sim ( ) não |
| O app travou? | ( ) não ( ) sim |
| Reconectou depois? | ( ) sim ( ) não |

---

## Tabela de resultados

| # | Teste | Resultado | Observações |
|---|---|---|---|
| 1 | Descoberta | | |
| 2 | Conexão | | |
| 3 | Serviços GATT | | |
| 4 | Comunicação | | |
| 5 | Texto | | |
| 6 | Imagem simples | | |
| 7 | Imagem complexa | | |
| 8 | Transmissão extensa | | |
| 9 | Alimentação | | |
| 10 | Recuperação | | |

---

## Se algo der errado

1. Rode novamente com `--verbose` e guarde a saída.
2. Gere o log pela tela **Diagnóstico → Exportar TXT**.
3. Se possível, guarde o `trafego.bin` do teste que falhou.
4. Envie: número do teste, saída do comando, o TXT e uma foto do papel.

Com esses três elementos é possível identificar se o problema é de enlace
(conexão), de protocolo (comandos) ou de imagem (raster).
