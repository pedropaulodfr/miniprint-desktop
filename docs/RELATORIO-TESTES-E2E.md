# Relatório — testes E2E da aba Modelos

Data: 2026-11-05 · Versão testada: **1.1.3** · Ambiente: Windows 11, app WPF nativo

## 1. Ambiente e ferramentas

| Item | Situação |
|---|---|
| Interface | WPF nativo (.NET 8, `net8.0-windows10.0.19041.0`), sem WebView/WebView2 — Playwright não se aplica |
| Automação | **UI Automation** (`System.Windows.Automation`, UIA) + entrada de mouse/teclado do Windows |
| Harness | PowerShell 5.1 em `_e2e/` (`lib.ps1` + roteiros), evidências em `_e2e/evidencias/` |
| FlaUI | não instalado e o NuGet está bloqueado no ambiente (sem acesso a `api.nuget.org`), então não foi adicionado como dependência |
| Testes unitários | xUnit, 255 testes |
| Isolamento de dados | `%LOCALAPPDATA%\MiniPrintDesktop` copiado para `_e2e/data-backup` no início de cada roteiro e **restaurado no `finally`** — os modelos reais do usuário não foram alterados |

Build e testes precederam a campanha: `dotnet build` (0 erros/0 avisos) e `dotnet test` (255/255).

## 2. Números

| Métrica | Valor |
|---|---|
| Verificações executadas | **280** (25 E2E + 255 unitárias) |
| Aprovadas | **280** após as correções |
| Reprovadas na primeira execução | 6 (5 do produto, 1 do roteiro) |
| Não executadas | 8 (lista na seção 6) |
| Bugs encontrados e corrigidos | 5 |
| Evidências | 19 capturas de tela + 3 CSVs de resultado |

## 3. Cobertura executada

### 3.1 Navegação (5 verificações)
Lista com as 6 seções · abertura da aba Modelos · criação de documento novo · paleta e painel de campos visíveis · retorno à lista.

### 3.2 Editor visual (14 verificações)
Inserção pela paleta (texto, linha, retângulo) · leitura de X/Y no painel · arraste real no canvas (mouse, 12 passos) · zoom "Ajustar" e "100%" **sem alterar coordenadas** · Ctrl+A com seleção múltipla · undo · redo · nome do documento digitado no cabeçalho · salvar · modelo na lista · reabrir · conteúdo preservado.

### 3.3 Campos e preenchimento (6 verificações)
Diálogo de campo abre · campos de entrada presentes · campo criado aparece na lista · modelo salvo · tela de preenchimento · prévia gerada (`384 pontos × 75 linhas`).

A validação também foi exercitada de forma não intencional: ao preencher um campo **monetário** com texto, a prévia foi bloqueada com "Valor inválido. Use, por exemplo, 150,00." — comportamento correto e comprovado por captura.

### 3.4 Regressão
`dotnet test`: **255/255** (inclui impressão/protocolo, BLE, histórico, importação/exportação, editor de blocos, renderização e o editor visual). Nenhuma impressão física foi disparada.

## 4. Bugs encontrados e corrigidos nesta rodada

| # | Severidade | Problema | Causa | Correção |
|---|---|---|---|---|
| 1 | **Crítica** | O conteúdo das abas não existia na árvore de acessibilidade: leitores de tela e ferramentas de automação viam apenas o menu lateral e o cabeçalho | O template do `TabControl` usava `<ContentPresenter ContentSource="SelectedContent"/>` **sem** `x:Name="PART_SelectedContentHost"` — o `TabControl` não reconhece o host de conteúdo e o peer de automação não publica os filhos | Nomeado no `NavTabControl`; a árvore passou a expor todo o conteúdo (comprovado pelo dump antes/depois) |
| 2 | **Alta** | Ícones apareciam como quadradinhos | O estilo implícito de `TextBlock` fixa `Segoe UI`, vencendo a herança de fonte dos glifos | Todo ícone virou `TextBlock` com estilo `GlyphText` (fonte MDL2 explícita); `IconButton` voltou a usar `ContentPresenter` |
| 3 | **Média** | Após Ctrl+A o painel continuava dizendo "1 elemento selecionado" | `SelectionCountText`/`HasMultipleSelection` não eram notificados ao mudar a seleção | `RaiseSelectionSummary()` chamado no setter de `SelectedElement` e em `SelectMany` |
| 4 | **Média** | Lista de camadas vazia ao abrir o editor | `RefreshLayers()` não era chamado na inicialização | Chamada incluída em `InitializeModernEditor` |
| 5 | **Baixa** | "Alinhar à base" com um único elemento usava a largura útil como altura | `Layout.MarginTop + Layout.ContentWidth` | Passou a usar `EffectiveHeight - MarginBottom` |

## 5. Evidências

`_e2e/evidencias/` (capturas reais da janela do aplicativo):

- `01-lista-modelos.png`, `01-editor-novo.png`, `01-volta-lista.png`
- `02-editor-inicial.png`, `02-tres-elementos.png`, `02-apos-arrastar.png`, `02-multiselecao.png`, `02-undo-redo.png`, `02-nome-digitado.png`, `02-lista-com-modelo.png`, `02-reaberto.png`
- `03-dialogo-campo.png`, `03-campo-criado.png`, `03-lista-modelo-campo.png`, `03-preenchimento.png`, `03-previa-documento.png`

Resultados por verificação em `_e2e/resultado-01.csv`, `resultado-02.csv`, `resultado-03.csv`.

## 6. Não executado (limitações e alternativas)

| Área | Motivo | Alternativa |
|---|---|---|
| Diálogos nativos de Exportar/Importar | `SaveFileDialog`/`OpenFileDialog` do Windows exigem automação de janelas nativas; o roteiro pode clicar, mas a seleção de pasta é instável | Validar por teste de unidade (`TemplateSerializer` + compatibilidade) e um roteiro dedicado com caminho fixo digitado no diálogo |
| Arraste das alças de redimensionamento | As alças são `Rectangle` sem `AutomationPeer`; a posição depende do zoom e do papel | Redimensionamento foi validado por propriedades (Larg./Alt.) na UI; o arraste da alça pode ser automatizado por coordenadas calculadas a partir do retângulo do elemento |
| Estresse de 50 elementos / 100 undo-redo pela UI | Cada operação de UI custa ~1 s; 150 operações passariam de 2 min por execução | Complementado por teste unitário que insere 50 elementos e executa 100 pares de undo/redo no ViewModel (mesma lógica usada pela UI) |
| Imagens (inserção, brilho, recorte) | Exigem `OpenFileDialog` | Teste com imagem embutida em base64 via modelo JSON, sem diálogo |
| Impressão física / BLE | Exigiria acionar o hardware sem autorização | Regressão dos testes de protocolo e do transporte simulado |
| Resoluções menores / DPI diferente | A sessão automatizada usa uma resolução fixa | Captura em janela 1260×820; o layout usa `WrapPanel` e painéis recolhíveis para telas menores |
| Painel de histórico visual, régua vertical | Funcionalidades não implementadas (ver relatório da modernização) | — |

## 7. Classificação geral

| Severidade | Encontrados | Corrigidos | Pendentes |
|---|---|---|---|
| Crítica | 1 | 1 | 0 |
| Alta | 1 | 1 | 0 |
| Média | 2 | 2 | 0 |
| Baixa | 1 | 1 | 0 |

Nenhum defeito conhecido permanece aberto nas áreas testadas. O que falta é **cobertura**, não correção: exportar/importar por diálogo, alças de redimensionamento e estresse de UI dedicado.

## 8. Conclusão

A aba Modelos e o editor visual estão operacionais nas áreas exercitadas por automação real: navegação, inserção, movimento com o mouse, seleção múltipla, zoom sem perda de coordenadas, undo/redo, persistência (salvar, reabrir), campos dinâmicos (criação por diálogo, validação, formatação) e pré-visualização em 384 pontos.

O teste também revelou um problema de acessibilidade que afetava qualquer leitor de tela — o conteúdo das abas não era publicado. A correção melhora a acessibilidade do produto e habilita a automação das demais telas.

**Não considero o editor "aprovado por completo"** porque as áreas da seção 6 não foram exercitadas ponta a ponta; elas são o próximo incremento da suíte.

## 9. Correção 1.1.4 — campos CPF/CNPJ independentes

**Reportado pelo usuário:** dois elementos «CPF/CNPJ», um para o pagador e outro para o receptor; ao renomear o do receptor, os dois mudavam.

**Causa:** `QuickAddField` reaproveitava um campo existente quando tipo e rótulo coincidiam. Inserir "CPF / CNPJ" duas vezes pela paleta criava **um único** `TemplateField` referenciado pelos dois elementos — renomear um renomeava o campo compartilhado.

**Correção:**

- a paleta agora **sempre cria um campo novo** (quem quiser reutilizar um campo usa a lista "Campos do documento", que já insería o elemento ligado à chave existente);
- rótulos repetidos ganham sufixo automático ("CPF/CNPJ", "CPF/CNPJ 2"...), mantendo a chave interna única;
- editar um campo continua renomeando todos os elementos **daquele** campo — comportamento correto, agora sem fusões involuntárias.

**Evidência automatizada:** dois testes novos em `EditorModernTests`
(`InsertingTheSameFieldTwiceCreatesTwoIndependentFields` e
`RenamingAFieldElementKeepsOtherFieldsUntouched`) — suíte com **257/257**.

**Observação para modelos já salvos:** elementos que foram fundidos continuam apontando para o mesmo campo. Para separar, selecione o elemento e escolha outro campo em "Campo associado" (ou exclua o campo e recrie). A correção vale para novas inserções.

**Re-execução na UI:** o roteiro dedicado (`_e2e/04-cpf.ps1`) foi escrito, mas nesta sessão o harness passou a falhar ao trocar de aba (clique sintético sem efeito, variando entre execuções) — instabilidade do ambiente de automação, não do aplicativo. A verificação ficou coberta pelos testes de ViewModel, que exercitam o mesmo caminho usado pela interface.
## 10. Correção 1.1.5 — campos órfãos após excluir elementos

**Reportado pelo usuário:** ao excluir o elemento "Lista de opções", o campo continuava em "Campos do documento" e aparecia no formulário de preenchimento.

**Causa:** `DeleteSelection` removia o elemento do layout, mas não tocava em `template.Fields`. O campo ficava órfão (nenhum elemento o usava) e o preenchimento listava **todos** os campos do modelo, usados ou não.

**Correção:**

- ao excluir elementos, os campos que ficaram sem uso são identificados e o editor pergunta se devem ser removidos — dentro do **mesmo passo** de histórico (um Ctrl+Z devolve elemento e campo);
- a lista "Campos do documento" ganhou **lixeira por campo** e o botão **Limpar sem uso**, com contador "N sem uso";
- o formulário de preenchimento passou a montar os campos apenas para `template.IsFieldUsed(chave)`, que considera elementos `Campo`, textos com `{{chave}}` e blocos de texto;
- o botão "+" de inserir campo (que havia perdido comando e dica na conversão dos ícones) foi restaurado.

**Evidência automatizada:** quatro testes novos (`DeletingTheLastFieldElementRemovesTheField`, `DeletingOneFieldElementKeepsTheOtherFields`, `UnusedFieldIsNotReportedAsUsed`, `RemovingAFieldByCommandUpdatesTheList`) — suíte com **261/261**.