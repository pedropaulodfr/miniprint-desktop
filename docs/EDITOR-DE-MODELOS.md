# Como funciona o editor de modelos

Este documento descreve o funcionamento da aba **Modelos** do MiniPrint Desktop:
as telas, o modelo de dados, as interações do editor visual, a validação, a
renderização e a persistência. O roteiro de uso (o que clicar) está em
`docs/MODELOS.md`; aqui o foco é **como cada parte funciona por dentro**.

---

## 1. Visão geral

A aba Modelos é composta por quatro telas que compartilham o mesmo objeto
`DocumentTemplate`:

| Tela | ViewModel | Para que serve |
|---|---|---|
| Lista | `TemplateListViewModel` | pesquisar, ordenar, importar/exportar, abrir |
| Editor de blocos | `TemplateEditorViewModel` | documentos em fluxo de texto (formato antigo) |
| Editor visual | `VisualEditorViewModel` | documentos com posicionamento livre (formato atual) |
| Preenchimento | `TemplateFillViewModel` | preencher campos, visualizar e imprimir |

A ligação entre elas é feita por uma interface única, `ITemplateHost`, que
concentra as operações de navegação e persistência (`ShowTemplateList`,
`EditTemplate`, `UseTemplate`, `SaveTemplate`, ...). Nenhuma tela conhece as
outras: todas conversam com o host.

Em `TemplatesView.xaml` um `ContentControl` com `DataTemplate` por tipo de
ViewModel decide qual tela aparece. A propriedade `Current` do host é a única
fonte da verdade sobre "onde estou".

```
Lista ──Novo/Editar──▶ Editor (blocos ou visual) ──Salvar──▶ Lista
  └──Utilizar──────────▶ Preenchimento ──Imprimir──▶ Impressora
```

O modelo escolhe o editor: `DocumentTemplate.IsVisual` (isto é,
`Layout != null`) abre o editor visual; caso contrário, abre o editor de blocos.

---

## 2. O modelo de dados

`DocumentTemplate` (em `MiniPrint.Core/Templates/DocumentTemplate.cs`) reúne:

| Grupo | Campos |
|---|---|
| Identificação | `Id`, `Name`, `Description`, `SchemaVersion`, `CreatedAt`, `UpdatedAt` |
| Papel | `PaperWidth` (384 pontos por padrão), `MarginTop/Bottom/Left/Right` |
| Estilo do fluxo | `FontSize`, `Bold`, `Alignment`, `LineSpacing` |
| Conteúdo | `Blocks` (fluxo de texto) e/ou `Layout` (posicionamento absoluto) |
| Dados | `Fields` (campos dinâmicos) |

Os dois modos de conteúdo coexistem no arquivo, mas só um é usado:

- **Blocos** — lista de parágrafos, divisores e espaços; o texto pode conter
  `{{chave}}`.
- **Layout** — `DocumentLayout` com `Elements` posicionados em coordenadas
  absolutas.

### 2.1 Elementos do layout

`LayoutElement` tem um `Kind` que muda tudo o que o editor e o renderizador
fazem com ele:

| Kind | Desenho | Propriedades específicas |
|---|---|---|
| `Texto` | texto livre, aceita `{{chave}}` | fonte, tamanho, negrito, itálico, alinhamento, entrelinhas, transbordo |
| `Campo` | `«Rótulo»` no editor; valor formatado na impressão | `FieldKey` |
| `Imagem` | imagem embutida em base64 | brilho, contraste, meio-tom, rotação, recorte, proporção |
| `Linha` | linha horizontal | espessura |
| `Retangulo` | contorno ou preenchido | espessura, preenchido |

Todo elemento tem ainda `Id` (Guid), `Name` (nome na camada), `X`, `Y`,
`Width`, `Height`, `Visible`, `Locked`, `AutoHeight` e `GroupId`.

### 2.2 O documento visual

`DocumentLayout` define o papel:

- `Width` entre 64 e 576 pontos (384 é a largura física do cabeçote);
- `Height` só é usado quando `AutoHeight` está desligado;
- `EffectiveHeight` = altura automática (maior `Y + Height` visível + margem
  inferior) ou a altura fixa;
- margens, `GridSize` (padrão 8) e `SnapToGrid` (padrão ligado) — **grade e
  encaixe não afetam a impressão**, são só do editor;
- `ContentWidth`/`ContentLeft` delimitam a área útil entre as margens.

### 2.3 Campos dinâmicos

`TemplateField` guarda `Key` (nome interno, usado em `{{chave}}`), `Label`
(rótulo mostrado no preenchimento), `Type`, `Required`, `Placeholder`,
`DefaultValue` e `Options`.

Tipos disponíveis (`TemplateFieldType`): `TextoCurto`, `TextoLongo`, `Numero`,
`Monetario`, `Data`, `Cpf`, `Cnpj`, `Telefone`, `Lista`.

A chave é sugerida a partir do rótulo por `TemplateFieldFormatter.SuggestKey`
("Nome do pagador" → `nome_do_pagador`), sem acentos e com `_` no lugar dos
espaços; a unicidade é garantida com sufixo `_2`, `_3`...

O **rótulo** é a identidade visível do campo: ele aparece no painel de camadas,
dentro de `«»` no canvas e como nome do campo no formulário de preenchimento.

---

## 3. Lista de modelos

`TemplateListViewModel` mantém uma cópia filtrável e ordenável de
`host.Templates`:

- **Pesquisa** por nome ou descrição, sem diferenciar maiúsculas.
- **Ordenação** por nome, por atualização mais recente (padrão) ou por criação.
- **Ações**: novo documento (editor visual), novo modelo em blocos, utilizar,
  editar, duplicar, exportar, excluir, importar e criar modelo de exemplo.

Importar e exportar usam o envelope JSON descrito em 5.1. A importação **nunca
sobrescreve** nada: o modelo recebe um novo `Id` e, se o nome já existir, ganha
o sufixo "(2)". O arquivo é validado por inteiro antes de qualquer gravação —
um JSON inválido não deixa modelo pela metade.

---

## 4. Editor visual

### 4.1 Como a tela é montada

Três colunas (`VisualEditorView.xaml`):

| Coluna | Conteúdo |
|---|---|
| Esquerda (240 px) | paleta: textos prontos, campos dinâmicos, elementos gráficos e estruturas |
| Centro | o papel, com zoom, grade, encaixe e modo impressão |
| Direita (320 px) | cartão **Documento**, **Propriedades** do elemento e **Camadas** |

O cartão **Documento** (nome, descrição, largura, margens, altura automática)
está sempre habilitado. O bloco **Propriedades** só fica ativo quando há
seleção. O título da janela é o próprio nome do documento: digitar no campo
atualiza o cabeçalho na hora.

### 4.2 Coordenadas e zoom

As coordenadas dos elementos são **pontos de impressão** — a mesma unidade do
raster (1 ponto = 1 pixel de 1 bit = 0,125 mm). O canvas não converte nada:

- o papel é um `Border` com `Width = PaperWidth` e `Height = CanvasHeight`;
- cada elemento vira um `ContentPresenter` num `Canvas` interno, posicionado por
  `Canvas.Left/Top` (via `ItemContainerStyle`) e dimensionado por `Width/Height`;
- o zoom é um `LayoutTransform` (`ScaleTransform`) no papel, então muda apenas o
  tamanho na tela, nunca o documento.

Zoom vai de 40% a 300%; **Ajustar** calcula `largura disponível / largura do
papel` e limita à mesma faixa.

### 4.3 Arrastar e redimensionar

A interação fica em `VisualEditorView.xaml.cs` e trabalha com três estados:
`_dragElement` (arrastando), `_resizeElement` + `_resizeHandle` (redimensionando,
alça `NW`/`NE`/`SW`/`SE`) e `_interactionActive` (já houve alteração real).

- **Arrastar**: o botão é pressionado no elemento (`OnElementMouseDown`), o
  deslocamento é medido em relação ao papel e só começa depois de 2 pontos de
  movimento (evita "arrastar sem querer" ao clicar). O destino passa por
  `Snap` e por `MoveElementTo`, que respeita a área útil e move o grupo inteiro
  quando o elemento pertence a um.
- **Redimensionar**: as quatro alças aparecem sobre o elemento selecionado.
  `ApplyResize` calcula largura/altura a partir do canto oposto e impede que a
  caixa "vire" (mínimo de 8 × 6 pontos).
- **Captura do mouse**: a captura é feita no container do elemento (não na alça)
  e encerrada por `MouseUp`, por perda de captura (janela desativada) ou por
  movimento com o botão já solto. Um `PreviewMouseLeftButtonUp` no editor é a
  rede de segurança: nenhuma interação fica "presa" ao elemento.
- **Undo**: `BeginInteraction` guarda o estado antes da primeira alteração real;
  clicar sem arrastar não entra no histórico.

### 4.4 Seleção, grupos e camadas

- Clicar seleciona; clicar no papel vazio desmarca; **Ctrl+A** seleciona todos os
  elementos visíveis.
- Ao selecionar um elemento que tem `GroupId`, a seleção é expandida para todos
  os membros do grupo (`Selection`).
- **Agrupar** (Ctrl+G) atribui um `GroupId` novo; **Desagrupar** limpa.
- **Camadas** lista os elementos na ordem inversa de desenho (o de cima primeiro)
  com nome, tipo, botão de visibilidade e de bloqueio. A seleção da lista e a do
  canvas são a mesma propriedade.
- **Trazer/Enviar** altera a posição na lista `Elements`, que é a ordem de
  desenho: o primeiro é desenhado antes (fica atrás).

### 4.5 Alinhar e distribuir

Com um elemento, o alinhamento é em relação à área útil (margens). Com vários,
ao bloco da seleção (menor `X`, maior `Right`, etc.). Distribuir exige três ou
mais elementos e iguala os espaços entre eles, no eixo escolhido.

### 4.6 Undo e redo

O histórico é uma pilha de **snapshots JSON do `Layout`** (não do modelo
inteiro, portanto não cobre nome, campos ou tipo de campo):

- até **120** estados; um novo `PushUndo` limpa o redo;
- `Undo`/`Redo` restauram o layout e tentam manter a seleção pelo `Id`;
- alterações de propriedade usam um snapshot "preguiçoso"
  (`EnsurePropertyUndo`): ele é tirado na primeira edição depois de cada
  seleção, então digitar em vários campos do mesmo elemento gera um único passo
  de histórico.

### 4.7 Altura automática

- **Por elemento**: com `AutoHeight` ligado, `UpdateAutoHeight` mede o texto com
  o mesmo renderizador da impressão (`VisualTemplateRenderer.MeasureTextHeight`)
  e ajusta `Height`.
- **Do documento**: com altura automática, `EffectiveHeight` acompanha o
  elemento mais baixo visível, mais a margem inferior.

### 4.8 Imagens

O arquivo é lido e **embutido em base64** (`ImageData`), para o modelo exportado
funcionar em outro computador. O caminho original (`ImagePath`) fica só como
referência. Brilho, contraste, meio-tom, rotação (0/90/180/270), recorte e
"manter proporção" são aplicados no momento de renderizar.

### 4.9 Campos no editor visual

- A paleta lista todos os campos do modelo (`FieldChoices`) e insere um elemento
  `Campo` ligado à chave; **+ Novo campo** cria o campo e o elemento de uma vez,
  com rótulo "Campo 1", "Campo 2"...
- No canvas o campo aparece como `«Rótulo»` em azul; na impressão, o valor
  formatado.
- Em **Propriedades → Campo** (visível quando um `Campo` está selecionado) dá
  para editar **rótulo**, **tipo de dado** e **obrigatório**.
- O rótulo é compartilhado: editar "Nome na camada", o "Conteúdo" do elemento ou
  o rótulo em Propriedades atualiza os três e todos os elementos daquele campo.
- **Campo associado** troca a chave do elemento; o rótulo acompanha o novo campo.
- Modelos antigos que ficaram com o rótulo padrão "Novo campo" adotam o nome da
  camada ao abrir no editor (normalização em memória; vale ao salvar).

### 4.10 Validação e pré-visualização

Toda alteração passa por `SchedulePreview` e `UpdateValidation`:

- a prévia é regerada 450 ms depois da última alteração (debounce) e roda em
  thread de fundo (`Task.Run`), com controle de geração para descartar resultados
  velhos;
- o texto de validação do rodapé mostra erros (bloqueiam o salvar) e avisos;
- **Modo impressão** troca o canvas pela imagem rasterizada final.

A prévia usa **valores de exemplo** (`BuildDemoValues`): valor padrão do campo,
primeira opção de uma lista, data de hoje, `150,00` para valores e `«Rótulo»`
para texto. É o mesmo renderizador que imprime, então o que aparece é o que sai.

### 4.11 Salvar

1. recalcula as alturas automáticas;
2. monta uma cópia de trabalho com nome, descrição, papel e layout atuais;
3. valida a estrutura — se houver erro, mostra a lista e **não salva**;
4. normaliza (nome e rótulos sem espaços nas pontas) e grava via
   `host.SaveTemplate`, que substitui o modelo na lista e persiste.

### 4.12 Atalhos

| Atalho | Ação |
|---|---|
| `Delete` / `Backspace` | excluir seleção |
| `Ctrl+Z` / `Ctrl+Y` | desfazer / refazer |
| `Ctrl+C` / `Ctrl+V` / `Ctrl+X` | copiar / colar / recortar |
| `Ctrl+D` | duplicar |
| `Ctrl+A` | selecionar todos |
| `Ctrl+S` | salvar |
| `Ctrl+G` | agrupar |
| setas / `Shift` + setas | mover 1 / 8 pontos |

Os atalhos são ignorados quando o foco está em um `TextBox`, `ComboBox` ou
`DatePicker`, para não roubar a digitação.

---

## 5. Editor de blocos

O editor antigo continua disponível para modelos sem `Layout`:

- **Blocos** de texto, divisor e espaço, em ordem, com formatação por bloco.
- **Campos** têm lista própria; **Inserir campo** insere `{{chave}}` na posição
  do cursor do bloco selecionado (`TemplateComposer.InsertField`).
- Prévia com debounce de 320 ms pelo mesmo renderizador.
- **Converter para o editor visual** (`TemplateLayoutConverter.ToVisual`)
  transforma os blocos em elementos posicionados, preservando conteúdo, campos e
  formatação; nada é gravado até salvar.

---

## 6. Preenchimento e impressão

`TemplateFillViewModel` monta um `TemplateFieldInput` por campo do modelo:

- o nome exibido é o `Label` do campo (ou a `Key`, se o rótulo estiver vazio);
- cada tipo escolhe o controle: `DatePicker` para data, `ComboBox` para lista,
  `TextBox` multilinha para texto longo e caixa com máscara para CPF, CNPJ,
  telefone e valores;
- ao sair do campo mascarado o valor é formatado (`ApplyMask`);
- **Visualizar documento** e **Imprimir** validam todos os campos antes de
  renderizar (`TemplateValidator.ValidateValues`), destacando os inválidos;
- a impressão usa `VisualTemplateRenderer` (modelos visuais) ou
  `TemplateRenderer` (blocos) e envia pelo mesmo caminho BLE das outras telas;
- cada impressão entra no histórico como tipo **Modelo**, com apenas o nome do
  modelo por padrão. A opção "Guardar os dados preenchidos no histórico" inclui
  um resumo de até três valores.

---

## 7. Renderização

`VisualTemplateRenderer` é o único caminho de saída — a prévia do editor, a
prévia do preenchimento e o papel saem do mesmo código:

1. bitmap de `width` × `EffectiveHeight` (largura limitada a 576, altura a
   24 000 pontos);
2. fundo branco, GDI+ com `AntiAliasGridFit`, sem suavização de formas;
3. desenho na ordem de `Elements`, ignorando `Visible = false`;
4. texto de campo resolvido por `ResolveElementText` → `TemplateFieldFormatter.Format`
   (CPF, CNPJ, telefone, `1.234,56`, `dd/MM/aaaa`), e textos livres com
   `{{chave}}` resolvidos por `TemplateComposer.Resolve`;
5. luminância → binarização por limiar **180** → empacotamento em 384 pontos.

---

## 8. Persistência

| Arquivo | Conteúdo |
|---|---|
| `%LOCALAPPDATA%\MiniPrintDesktop\templates.json` | todos os modelos |
| `%LOCALAPPDATA%\MiniPrintDesktop\history.json` | histórico de impressões |

### 8.1 Formato de exportação

Envelope `miniprint.template`, versão **2** (versão 1 = só blocos), enums como
texto:

```json
{
  "Format": "miniprint.template",
  "Version": 2,
  "Template": {
    "Name": "Recibo",
    "PaperWidth": 384,
    "Fields": [ { "Key": "nome", "Label": "Nome", "Type": "TextoCurto" } ],
    "Layout": {
      "Width": 384,
      "AutoHeight": true,
      "Elements": [
        { "Kind": "Campo", "FieldKey": "nome", "X": 16, "Y": 48, "Width": 300, "Height": 20 }
      ]
    }
  }
}
```

Arquivos versão 1 continuam abrindo: sem `Layout`, caem no editor de blocos.

---

## 9. Regras de validação

| Regra | Gravidade |
|---|---|
| modelo sem nome | erro |
| largura fora de 64–576 pontos | erro |
| documento visual sem elementos / de blocos sem blocos | erro |
| dois elementos com o mesmo `Id` | erro |
| elemento `Campo` sem chave ou apontando para campo inexistente | erro |
| chave de campo vazia, inválida ou repetida | erro |
| rótulo de campo vazio | aviso |
| lista de opções sem opções | aviso |
| campo não usado no documento | aviso |
| elemento passando da largura do papel | aviso (será cortado) |
| imagem sem arquivo embutido | aviso |

Erros bloqueiam o salvamento; avisos apenas aparecem no rodapé.

---

## 10. Como verificar

```powershell
# testes (inclui editor visual, campos, renderização e validação)
dotnet test tests\MiniPrint.Tests\MiniPrint.Tests.csproj

# autoverificação da interface (abre as seis abas e reporta erros de binding)
MiniPrint.exe --selftest
# relatório em %LOCALAPPDATA%\MiniPrintDesktop\selftest.txt

# modelo de exemplo e validação por linha de comando
miniprint-diag template --exemplo 1 --exportar comprovante.json
miniprint-diag template --validar comprovante.json
```

---

## 11. Limitações e decisões

- A largura é limitada a 384 pontos (cabeçote da impressora); o formato aceita
  até 576, mas o raster útil é 384.
- A impressão é monocromática, com binarização por limiar — não há tons de cinza.
- O editor visual trabalha com um elemento por caixa; dois campos na mesma linha
  exigem dois elementos posicionados lado a lado (ou um texto com `{{chave}}`).
- O undo cobre o layout, não os campos do modelo (criar, renomear ou trocar o
  tipo de um campo não é desfeito por Ctrl+Z).
- A seleção múltipla vem de Ctrl+A ou de agrupamento; não há Shift+clique.
- Textos não têm rotação; imagens têm (0, 90, 180, 270).
- A prévia do editor é uma aproximação em WPF enquanto se edita; o **Modo
  impressão** e a prévia do preenchimento mostram o resultado fiel.

---

## 12. O que mudou na versão 1.1 (editor modernizado)

A tela foi reconstruída mantendo o mesmo modelo de dados e o mesmo renderizador.
Nenhum arquivo de modelo mudou de formato.

### 12.1 Componentes novos

| Componente | Papel |
|---|---|
| `Editing/EditorSnapEngine` | encaixe puro (grade, margens, centro do papel, bordas/centros de outros elementos). Devolve posição ajustada e as guias a desenhar |
| `Editing/CanvasInteractionController` | máquina de estados do mouse: mover, redimensionar (8 alças, proporção com Shift) e seleção por área |
| `Editing/EditorDraftStore` | rascunhos automáticos em `%LOCALAPPDATA%\MiniPrintDesktop\drafts\`, separados do `templates.json` |
| `ViewModels/VisualEditorViewModel.Modern` | parte parcial do ViewModel com painéis, paleta, campos, produtividade e autosave |
| `ViewModels/TemplateFieldTypeChoice` | tipos de campo com rótulo amigável |
| `Windows/FieldEditorWindow` | criação/edição de campo em uma janela curta (chave gerada sozinha) |
| `Windows/FindReplaceWindow` | localizar e substituir nos textos fixos |
| `Core.Configuration.EditorSettings` | preferências do editor (painéis, guias, encaixe, autosave) gravadas em `settings.json` |

O code-behind do `VisualEditorView` ficou restrito a: traduzir eventos de mouse e
teclado, posicionar sobreposições (seleção por área, editor inline), montar o
menu de contexto, desenhar a régua e sincronizar as colunas dos painéis.

### 12.2 Encaixe e guias

`EditorSnapEngine.SnapMove` recebe o retângulo em movimento, os demais elementos,
o papel e as margens, e devolve a posição corrigida com a lista de guias. O
algoritmo compara as três âncoras do elemento (início, centro e fim) com cada
candidato — bordas e centros dos outros elementos, margens, centro do papel e
múltiplos da grade — e escolhe a menor distância dentro da tolerância. As guias
são `SnapGuide` (coordenadas em pontos) e viram linhas no canvas por
`ItemsControl`; nunca entram no documento.

No redimensionamento só as bordas que o usuário está movendo são encaixadas
(`SnapResize` recebe as alças, por exemplo `"SE"`), evitando que a caixa "pule"
ao mexer no canto oposto.

### 12.3 Histórico

O snapshot passou a ser o **documento inteiro** (`BuildWorkingCopy`), não só o
layout:

- cobre nome, papel, margens, campos e layout;
- `PushUndo(mergeKey)` coalesce alterações da mesma natureza dentro de 900 ms,
  então digitar ou ajustar um campo gera um passo só;
- o limite continua em 120 estados;
- `Restore` recria elementos, campos e seleção (pelo `Id`), e reinicia o
  snapshot "preguiçoso" da seleção.

### 12.4 Preferências e layout

`AppSettings.Editor` (`EditorSettings`) guarda larguras dos painéis, estado
recolhido, grade/encaixe, tolerância, régua, margens, guias e autosave. A view
aplica as larguras nas colunas do `Grid` e grava de volta ao soltar o
`GridSplitter` (com debounce de 500 ms).

### 12.5 Salvamento automático e recuperação

O `VisualEditorViewModel` mantém um `DispatcherTimer` que grava o rascunho
quando `IsDirty` está ligado. O rascunho é um envelope próprio
(`miniprint.editor-draft`) com modelo, marca `isNew` e data. Ao abrir um modelo
existente, `TemplatesViewModel.EditTemplate` compara a data do rascunho com
`UpdatedAt` e pergunta se deve recuperá-lo; recusar apaga o rascunho. Salvar ou
descartar também apaga.

### 12.6 Seleção e edição inline

`SelectElement(elemento, aditivo, alternar)` e `SelectInRect` alimentam a mesma
coleção usada pelo canvas e pelo painel de camadas. O duplo clique abre o
`TextBox` de edição inline posicionado sobre o elemento (em coordenadas do
papel), com Enter para confirmar e Esc para cancelar. Em campos, o duplo clique
abre a janela de configuração.

### 12.7 Menu de contexto

Montado em código porque o menu precisa do estado da seleção: só entram comandos
aplicáveis (por exemplo "Aplicar estilo" exige um estilo copiado; "Agrupar" exige
dois elementos).

### 12.8 Testes

`tests/MiniPrint.Tests/EditorModernTests.cs` cobre o motor de encaixe, seleção
múltipla, seleção por área, duplicação deslocada, copiar estilo, substituição de
textos, ordem de camadas e o undo de criação de campo. A autoverificação da
interface (`--selftest`) continua sendo executada na geração do instalador.
