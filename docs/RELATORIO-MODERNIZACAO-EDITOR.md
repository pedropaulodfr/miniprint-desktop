# Relatório — modernização do editor visual de modelos

Versão **1.1.0** · 2026-11-05

## 1. Resumo

O editor visual foi reconstruído na camada de interface e de interação, mantendo
o modelo de dados (`DocumentTemplate`, `DocumentLayout`, `LayoutElement`), o
formato de arquivo (envelope versão 2) e o renderizador da impressão. Nenhum
modelo existente precisa de migração e o pipeline BLE não foi alterado.

## 2. O que foi entregue

### Interface (spec 3)

| Item | Situação |
|---|---|
| Barra superior com identidade, edição, visualização e finalização | feito |
| Nome do documento editável no topo + indicador de alterações e status | feito |
| Painéis laterais redimensionáveis (`GridSplitter`) | feito |
| Recolher/expandir painéis, com largura e estado persistidos | feito |
| "Restaurar disposição padrão" | feito (`ResetLayoutCommand`) |
| Canvas como protagonista (papel branco, sombra, fundo neutro) | feito |

### Paleta (spec 4)

- Categorias recolhíveis: Texto, Campos, Elementos gráficos, Estruturas prontas.
- Pesquisa instantânea por nome, dica e palavras-chave.
- Inserção por clique (na seleção ou no primeiro espaço livre) e por
  **arrastar-e-soltar** no ponto exato do papel.
- Elemento inserido é selecionado automaticamente.
- Campos do documento aparecem na paleta e reaproveitam a chave existente.
- Estado vazio com atalhos para Texto, Título, Linha e Campo.

### Canvas (spec 5)

- Régua horizontal em pontos, acompanhando o zoom.
- Grade configurável; margens e guias com visibilidade controlada.
- Encaixe inteligente em grade, margens, centro do papel e bordas/centros dos
  outros elementos, com tolerância configurável e **guias visuais temporárias**.
- Zoom por botões, controle deslizante, "Ajustar" (Ctrl+0), "100%" e
  Ctrl + roda com preservação da região visível. O zoom não altera coordenadas.
- Rolagem vertical/horizontal para documentos altos.

### Seleção e manipulação (spec 6)

- Shift + clique (adiciona), Ctrl + clique (alterna), seleção por área
  arrastando sobre o papel, seleção múltipla no painel de camadas.
- Oito alças de redimensionamento (cantos e lados), Shift preserva a proporção,
  medidas exibidas no rodapé e limites mínimos/máximos respeitados.
- Esc cancela a interação em andamento.
- Duplo clique edita o texto direto no canvas; em campos abre a configuração.
- Menu de contexto com apenas os comandos aplicáveis.
- Setas movem 1 ponto, Shift + setas 8 pontos; Ctrl+B/Ctrl+I alternam negrito e
  itálico.

### Propriedades (spec 7)

- Painel contextual: documento sem seleção; texto, imagem, linha/retângulo,
  campo e multi-seleção com as propriedades comuns.
- Edição em tempo real (sem botão "Aplicar").
- Grupos visuais: posição/tamanho, conteúdo, fonte, parágrafo, dimensões.

### Campos dinâmicos (spec 8)

- Janela curta de criação/edição: rótulo, tipo, obrigatório, orientação e valor
  padrão; chave interna gerada automaticamente e exibida só como referência.
- Gerenciamento centralizado no painel direito: lista, inserir no documento,
  editar e excluir com contagem de uso e confirmação.
- Inserção por arrastar da paleta e por clique; sem duplicar o campo.

### Camadas (spec 9)

- Ícone por tipo, nome, tipo textual, visibilidade e bloqueio.
- Pesquisa de camadas.
- Reordenação por arrastar-e-soltar.
- Seleção múltipla e sincronização com o canvas.

### Desfazer/refazer (spec 10)

- Snapshot agora cobre **documento, campos e layout**.
- Coalescência por natureza da operação (digitar, mexer em campo) dentro de
  900 ms — uma operação lógica, um passo.
- Limite de 120 estados; IDs, grupos e vínculos de campo preservados.

### Produtividade (spec 11)

| Item | Situação |
|---|---|
| Copiar/aplicar estilo | feito (fonte, alinhamento, espessura) |
| Alinhar (6 posições) e distribuir (horizontal/vertical) com espaçamento uniforme | feito |
| Duplicação com deslocamento e seleção da cópia | feito |
| Localizar e substituir nos textos fixos | feito (Ctrl+F) |
| Blocos prontos como elementos reais | já existiam; agora na paleta com categorias |
| Painel de histórico visual | **não implementado** (ver seção 4) |

### Prévia e renderização (spec 12)

- Modo prévia (Ctrl+P) dedicado, com dimensões finais, botão de imprimir teste e
  retorno ao editor.
- Mesmo renderizador da impressão (`VisualTemplateRenderer`); nenhum segundo
  caminho de renderização foi criado.
- Ajustes de layout: margens consistentes, correção do "alinhar à base" com um
  único elemento (usava a largura útil como altura) e medição de altura
  automática mantida.

### Salvamento (spec 13)

- Indicador de alterações e status de salvamento no topo.
- Rascunho automático a cada ~20 s (configurável ao ligar/desligar), em pasta
  separada, com recuperação oferecida ao reabrir o modelo.
- Falha de rascunho só vai para o log; nunca sobrescreve `templates.json`.
- Confirmação ao sair com alterações pendentes (comportamento existente mantido).

### Primeiro uso e acessibilidade (spec 14/15)

- Cartão de boas-vindas na primeira abertura, dispensável, com cinco dicas.
- Dicas de ferramenta em todos os botões de ícone.
- Mensagens de erro de validação no topo e no rodapé do canvas.
- Contraste e tamanho dos alvos mantidos na paleta de estilos existente.

## 3. Compatibilidade

- `templates.json` e os arquivos exportados continuam iguais (envelope versão 2;
  arquivos versão 1 continuam abrindo no editor de blocos).
- Campos, tipos, formatos e impressão inalterados.
- As preferências novas ficam em `settings.json` (`Editor`), com valores padrão
  aplicados quando o arquivo é antigo.

## 4. O que não foi implementado e por quê

| Item da spec | Motivo |
|---|---|
| Painel de histórico visual (11.6) | O histórico atual é uma pilha de snapshots JSON; expor uma lista legível exigiria rotular cada operação e manter metadados paralelos. Preferi manter o undo/redo sólido e não criar uma lista que não corresponda exatamente aos passos. É o próximo candidato natural. |
| Régua vertical | A régua horizontal cobre o uso real (largura é a dimensão crítica em 384 pontos). Uma régua vertical exigiria um segundo canvas sincronizado com a rolagem. |
| Renomear camada com duplo clique na lista | O nome já é editável em "Nome na camada" no painel de propriedades; duplicar o caminho exigiria um modo de edição por item e não acrescentava velocidade real. |
| Favoritos na paleta | O campo `FavoriteTools` já existe nas preferências, mas a interface de favoritos ficou de fora para não inflar a primeira versão; a pesquisa cobre o caso de uso. |
| Menus de alinhamento inteligente por distância (igualar espaçamentos automaticamente) | A distribuição uniforme foi entregue; o "espaçamento igual ao vizinho" depende de detecção de intenção e ficou para uma próxima rodada. |
| Rotação de texto | Não existe no modelo (`LayoutElement` não tem rotação de texto) e mudaria o renderizador; fora do escopo de UI. |

## 5. Verificação

```
dotnet build MiniPrint.sln -c Release   →  0 erros, 0 avisos
dotnet test  tests\MiniPrint.Tests      →  255 testes, todos passando
MiniPrint.exe --selftest                →  OK, 6 abas, 0 erros de binding
```

Os testes novos (`EditorModernTests.cs`) cobrem o motor de encaixe, a seleção
múltipla e por área, duplicação deslocada, copiar estilo, substituição de texto,
ordem de camadas e undo de criação de campo.

## 6. Arquivos

Novos:
`Editing/EditorSnapEngine.cs`, `Editing/CanvasInteractionController.cs`,
`Editing/EditorDraftStore.cs`, `ViewModels/VisualEditorViewModel.Modern.cs`,
`ViewModels/TemplateFieldTypeChoice.cs`, `Windows/FieldEditorWindow.xaml(.cs)`,
`Windows/FindReplaceWindow.xaml(.cs)`, `Core/Configuration/EditorSettings.cs`,
`tests/MiniPrint.Tests/EditorModernTests.cs`, `docs/EDITOR-DE-MODELOS.md`.

Alterados:
`Views/VisualEditorView.xaml(.cs)`, `ViewModels/VisualEditorViewModel.cs`,
`ViewModels/TemplatesViewModel.cs`, `Themes/Styles.xaml`, `App.xaml`,
`Converters/Converters.cs`, `Core/Templates/DocumentLayout.cs` (`KindGlyph`),
`Core/Configuration/AppSettings.cs`, `docs/MODELOS.md`.

## 7. Correção 1.1.2 — ícones do editor

Alguns botões apareciam como quadradinhos. Causa: o estilo global de `TextBlock`
define `FontFamily="Segoe UI"` e vence a herança de fonte. Os ícones que eram
apenas texto no `Content` do botão (ou `TextBlock` criados pelo `ContentPresenter`)
caíam nessa fonte e os caracteres da área privada do Unicode viravam "tofu".

Correção:

- todo ícone agora é um `TextBlock` explícito com o estilo `GlyphText`
  (`FontFamily="Segoe MDL2 Assets"`), inclusive o nome do documento e as alças de
  recolher painel;
- o `IconButton` voltou a usar `ContentPresenter` (aceita qualquer conteúdo) e não
  depende mais de herança de fonte;
- o ícone de régua (U+E7D8, inexistente na fonte) foi trocado por um glifo válido;
- a barra superior deixou de concentrar tudo: zoom, grade e toggles de visualização
  foram para a barra do canvas, que agora usa `WrapPanel` e não se sobrepõe em
  janelas estreitas.