# Design system do MiniPrint Desktop

Referência visual e técnica da interface (versão 1.2.0). Tudo vive em
`src/MiniPrint.Desktop/Themes` e é carregado por `App.xaml`.

## Direção visual

Utilitário profissional, neutro e calmo:

- Barra lateral em grafite, área de trabalho clara e fria.
- **Um único acento** (verde-azulado `#0F766E`) para a ação principal e o item
  ativo. Cores semânticas (sucesso, alerta, erro, informação) só indicam estado.
- Cartões com borda de 1 px e raio 10; sem sombras decorativas (sombra só em
  papel de pré-visualização, abas segmentadas e popups).
- Tipografia Segoe UI Variable Text (fallback Segoe UI); código em Cascadia
  Mono (fallback Consolas); ícones Segoe Fluent Icons (fallback MDL2).

## Camadas

| Arquivo | Conteúdo |
|---|---|
| `Tokens.xaml` | Cores, brushes, fontes, escala de espaçamento (4 px), raios, sombras, glifos de ícone, duração de movimento. **Única fonte de valores.** |
| `Controls.xaml` | Estilo implícito de todos os controles nativos: Button (Primary/Secondary/Ghost/Danger/DangerGhost/Icon/Toolbar), TextBox (com texto de apoio via `Tag`), ComboBox, CheckBox, Slider, ScrollBar, ToolTip, ContextMenu/MenuItem, ListBox, DataGrid, ProgressBar, DatePicker. Tipografia: `PageTitle`, `PageSubtitle`, `SectionHeader`, `Label`, `Value`, `Muted`, `Mono`, `Icon`. |
| `Components.xaml` | `Card`, `InsetPanel`, `PreviewWell`, `Paper`, `ActionBar`, `Divider`, selos (`Badge*` + `BadgeText*`), navegação lateral (`NavTabControl`/`NavTabItem`), abas segmentadas (`InnerTabControl`/`InnerTabItem`). |
| `Chrome.cs` | Propriedades anexadas `SidebarHeader`, `SidebarFooter`, `Icon`, `IsCompact` usadas pelo estilo da navegação. |

## Regras

1. **Nenhuma cor, raio ou espaçamento solto nas telas.** Use os tokens
   (`{StaticResource AccentBrush}`, `{StaticResource PagePadding}`,
   `{StaticResource RadiusCard}`...). Exceção: cores de *conteúdo do
   documento* no editor visual (papel branco, tinta preta).
2. **Uma ação primária por contexto** (`PrimaryButton`); as demais são
   `SecondaryButton` ou `GhostButton`. Ações destrutivas: `DangerButton`
   (isolada) ou `DangerGhostButton` (dentro de linhas de lista).
3. **Estados vazios** têm ícone, título e próxima ação (ver Modelos, Histórico,
   Impressora).
4. **Teclado e acessibilidade**: todos os botões, caixas e listas exibem anel
   de foco (`FocusRing`); campos de ação sem texto visível definem
   `AutomationProperties.Name`; texto secundário mantém contraste ≥ 4,5:1.
5. **Responsividade**: abaixo de 1180 px de largura a barra lateral recolhe
   para ícones (dica com o nome, estado da impressora como ponto colorido).
6. Mensagens ao usuário usam `Windows/MessageDialog` (mesma assinatura do
   `MessageBox`), nunca a caixa nativa do sistema.

## Paleta

| Token | Valor | Uso |
|---|---|---|
| `AppBackground` | `#F4F5F7` | Fundo da janela |
| `Surface` / `SurfaceAlt` / `SurfaceSunken` | `#FFFFFF` / `#F0F2F5` / `#E7EAEE` | Cartões / painéis recuados / trilhas |
| `Border` / `BorderStrong` | `#E2E5EA` / `#C9CED6` | Divisores / bordas de campos |
| `TextPrimary` / `TextSecondary` / `TextTertiary` | `#111827` / `#586172` / `#8A93A1` | Texto / apoio / placeholder |
| `Accent` (+Hover/Pressed/Soft) | `#0F766E` | Ação principal, item ativo |
| `Sidebar` / `SidebarRaised` | `#111827` / `#1B2436` | Navegação |
| `Success` / `Warning` / `Danger` / `Info` | `#15803D` / `#B45309` / `#B91C1C` / `#0369A1` | Estado (cada um com variante *Soft*) |
