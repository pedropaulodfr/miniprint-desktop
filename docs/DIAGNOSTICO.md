# Diagnóstico de problemas

Guia para descobrir **onde** está o problema quando algo não funciona.

---

## 1. Antes de tudo: a impressora está disponível?

Uma impressora BLE aceita **uma conexão por vez**. Se o celular está conectado a
ela, o Windows vê o anúncio mas não consegue abrir os serviços.

Checklist:

1. Feche o app Fun Printer/iPrint no celular.
2. Desligue o Bluetooth do celular (ou esqueça o dispositivo).
3. Reinicie a impressora (desligar e ligar).
4. No Windows: **Configurações → Bluetooth e dispositivos** — o Bluetooth precisa
   estar ligado.
5. Aproxime a impressora (menos de 2 metros).

No aplicativo, a aba **Impressora** mostra a mensagem do estado do Bluetooth do
computador. Se estiver "O Bluetooth está desligado", nada de descoberta vai
funcionar.

---

## 2. Onde ficam os dados

| O quê | Caminho |
|---|---|
| Configurações | `%LOCALAPPDATA%\MiniPrintDesktop\settings.json` |
| Histórico | `%LOCALAPPDATA%\MiniPrintDesktop\history.json` |
| Log de diagnóstico | `%LOCALAPPDATA%\MiniPrintDesktop\diagnostico.log` |
| Exportações | `%LOCALAPPDATA%\MiniPrintDesktop\exports\` |
| Relatório de autoverificação | `%LOCALAPPDATA%\MiniPrintDesktop\selftest.txt` |

Na tela **Configurações** há o botão **Abrir pasta de dados**.

Para voltar ao estado inicial, feche o aplicativo e apague `settings.json`
(as configurações voltam ao padrão; o histórico é preservado).

---

## 3. Sintomas e causas

### 3.1 "Nenhuma impressora compatível encontrada"

| Verificação | Ação |
|---|---|
| Impressora ligada? | ligue e aguarde o LED estabilizar |
| Conectada ao celular? | desconecte o Bluetooth do telefone |
| Longe demais? | aproxime |
| Pesquisa curta? | aumente a duração para 20 s |
| Outro app usando? | feche Fun Printer, iPrint, apps de impressão |

Se ela aparecer na lista do Windows mas não na pesquisa do app, use o campo
**Endereço MAC (conexão manual)** com `18:57:2A:44:E0:BC`.

### 3.2 "Falha ao descobrir serviços GATT"

Significa que o Windows conectou mas não conseguiu ler os serviços. Causas:

- a impressora está conectada a outro dispositivo (mais comum);
- a impressora entrou em suspensão — desligue e ligue;
- o adaptador Bluetooth está sobrecarregado — desligue e ligue o Bluetooth do PC.

### 3.3 "A impressora pausou o fluxo (XOFF) e não retomou"

O equipamento pediu pausa e não liberou dentro do timeout.

- verifique se o papel acabou ou está preso;
- aumente **Timeout de comunicação** em Configurações (por exemplo 30000 ms);
- diminua **Bloco por escrita** para 100 e aumente o **Intervalo entre pacotes**
  para 30 ms — enlace mais lento, porém mais estável.

### 3.4 A impressão sai clara

Aumente a energia:

- **Energia para imagens**: de 16384 para 20000–24000;
- **Energia para texto**: de 24576 para 28000–32000.

Energia demais deixa a imagem "borrada" e pode manchar o papel.

### 3.5 A impressão sai borrada ou com falhas horizontais

- reduza a energia;
- aumente a **Velocidade** (o valor do comando `0xBD` é invertido: menor = mais
  rápido). Valores usuais: 24 a 40;
- confira a limpeza do cabeçote térmico.

### 3.6 A imagem sai espelhada ou de cabeça para baixo

Isso indica orientação de raster. Reúna:

1. o log exportado (Diagnóstico → Exportar TXT);
2. uma foto do papel;
3. o arquivo de imagem usado.

Com isso o ajuste é feito na conversão (ordem das linhas ou espelhamento de
bits), sem mexer no protocolo.

### 3.7 A impressão para no meio e não volta

- o enlace caiu: veja no log a linha "Desconexão inesperada";
- desconecte, religue a impressora e conecte novamente;
- se acontecer sempre no mesmo ponto, guarde o `--dump trafego.bin` do trabalho.

### 3.8 O aplicativo não abre

1. Execute `MiniPrint.exe --selftest` em um terminal e veja o código de saída.
2. Leia `%LOCALAPPDATA%\MiniPrintDesktop\diagnostico.log` — a última linha de
   erro indica a causa.
3. Confirme que o Windows é 10 build 19041 ou superior (`winver`).

---

## 4. Ferramenta de linha de comando

Melhor caminho para isolar problema de app versus problema de equipamento.

```powershell
# o Windows vê a impressora?
tools\miniprint-diag.exe scan 12

# os serviços existem?
tools\miniprint-diag.exe inspect 18:57:2A:44:E0:BC

# o aparelho responde?
tools\miniprint-diag.exe probe 18:57:2A:44:E0:BC --verbose

# o pipeline monta os pacotes corretamente? (sem hardware)
tools\miniprint-diag.exe simulate --texto "teste"

# converte uma imagem e mostra o resultado em PNG
tools\miniprint-diag.exe encode "foto.png"

# imprime de verdade (gasta papel)
tools\miniprint-diag.exe print 18:57:2A:44:E0:BC --teste --confirmar --verbose
```

Se `scan` e `probe` funcionam mas o aplicativo não imprime, o problema está na
interface. Se `probe` falha, o problema é de enlace/equipamento — e o app não
tem como resolver.

---

## 5. Como relatar um problema

Envie quatro coisas:

1. **número do teste** de `TESTES-FISICOS.md` (ou descrição do passo);
2. **saída do comando** com `--verbose` (copie o texto);
3. **log exportado** (Diagnóstico → Exportar TXT);
4. **foto do papel**, se houve impressão.

Se o problema for no meio de uma transmissão, inclua também o arquivo gerado com
`--dump`.

O log contém: dispositivos encontrados, serviços, UUIDs, MTU, tamanho dos
pacotes, respostas em hexadecimal, erros e tempos. Não contém dados pessoais.

---

## 6. Limpeza completa

Para remover tudo (aplicativo + dados):

1. Desinstale pelo **Painel de Controle → Programas** (ou Configurações → Apps).
2. Apague `%LOCALAPPDATA%\MiniPrintDesktop`.
3. Se usou a versão portátil, apague a pasta onde descompactou.
