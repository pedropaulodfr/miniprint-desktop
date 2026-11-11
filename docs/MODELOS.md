# Modelos de documentos

A aba **Modelos** permite montar um documento uma única vez e reutilizá-lo
quantas vezes quiser, preenchendo apenas os dados que mudam.

> Este guia descreve **o que fazer**. Para entender **como o editor funciona por
> dentro** (modelo de dados, undo, validação, renderização, persistência), veja
> [`EDITOR-DE-MODELOS.md`](EDITOR-DE-MODELOS.md).

Fluxo: **criar o modelo → definir os campos → salvar → escolher o modelo →
preencher → visualizar → imprimir.**

---

## 1. Listagem

Em **Modelos** você vê todos os modelos salvos, com nome, descrição, quantidade
de campos, data de criação e data da última atualização.

| Ação | O que faz |
|---|---|
| **Novo modelo** | abre o editor com um documento em branco |
| **Utilizar** | abre o formulário de preenchimento |
| **Editar** | abre o editor com o modelo atual |
| **Duplicar** | cria uma cópia com o sufixo "(2)" |
| **Exportar** | grava um arquivo `.json` do modelo |
| **Excluir** | remove o modelo, com confirmação |
| **Importar** | lê um `.json` exportado, validando antes de salvar |
| **Criar modelo de exemplo** | adiciona o "Comprovante de Pagamento" pronto |

Acima da lista há **pesquisa por nome** (também busca na descrição) e
**ordenação** por nome, por atualização ou por criação.

Os modelos ficam em `%LOCALAPPDATA%\MiniPrintDesktop\templates.json` e
sobrevivem ao fechamento do aplicativo.

---

## 2. Editor do documento

O editor tem três colunas: **blocos e campos**, **propriedades** e
**pré-visualização** (que usa o mesmo renderizador da impressão real).

### 2.1 Blocos

| Bloco | Uso |
|---|---|
| **Texto** | parágrafo fixo, com ou sem campos; aceita quebra de linha |
| **Divisor** | linha de `-`, `=`, `_` ou uma regra sólida |
| **Espaço** | espaço em branco de N pontos (1 ponto = 0,125 mm) |

Cada bloco pode ter fonte, negrito, itálico, alinhamento e espaçamento antes e
depois. A ordem é alterada com **↑ / ↓**; **Duplicar** reaproveita a formatação.

### 2.2 Inserir um campo no texto

1. Selecione o bloco de texto.
2. Escolha o campo na lista ao lado do botão **Inserir campo**.
3. Clique em **Inserir campo** — o campo entra na posição do cursor.

O texto mostra o campo como `{{chave}}`, mas você **nunca precisa digitar as
chaves**: a inserção é feita pela interface. Na lista de blocos o campo aparece
como «Rótulo».

### 2.3 Documento

Nome, descrição, largura (384 pontos por padrão), fonte, entrelinhas,
alinhamento e as quatro margens. A largura máxima física da impressora é 384
pontos.

---

## 3. Campos dinâmicos

| Tipo | Comportamento |
|---|---|
| Texto curto | uma linha livre |
| Texto longo | várias linhas, com quebra automática no papel |
| Número | valida e normaliza (aceita vírgula) |
| Valor monetário | aceita `150`, `150,00`, `1.234,56`, `R$ 150,00`; imprime `1.234,56` |
| Data | seletor de data; imprime `dd/mm/aaaa` |
| CPF | máscara `000.000.000-00` e validação dos dígitos verificadores |
| CNPJ | máscara `00.000.000/0000-00` e validação dos dígitos |
| Telefone | máscara `(00) 00000-0000` (10 ou 11 dígitos) |
| Lista de opções | caixa de seleção com as opções cadastradas |

Cada campo tem **nome interno** (chave usada no texto), **rótulo** (o que o
usuário vê), **texto de orientação**, **obrigatório** e **valor padrão**.

O nome interno é gerado a partir do rótulo (`Nome do pagador` →
`nome_do_pagador`) e pode ser ajustado. A ordem dos campos no formulário é a
ordem da lista — use **↑ / ↓**.

> **Valor monetário não inclui "R$".** Escreva o símbolo no texto do modelo:
> `A importância de: R$ {{valor}}`.

---

## 4. Preenchimento e impressão

Ao clicar em **Utilizar**, o formulário é gerado automaticamente com os campos
do modelo.

- Campos obrigatórios são validados antes de gerar o documento.
- CPF, CNPJ, telefone e valores são formatados ao sair do campo.
- **Limpar campos** volta aos valores padrão.
- **Visualizar documento** gera a imagem exata que será impressa.
- **Imprimir** envia pelo mesmo pipeline das outras telas (Bluetooth BLE,
  384 pontos, controle de fluxo e margem de corte).

Se a impressora não estiver conectada, o aplicativo avisa em vez de falhar em
silêncio.

---

## 5. Histórico e privacidade

Cada impressão de modelo entra no histórico como tipo **Modelo**.

Por padrão o histórico guarda **apenas o nome do modelo**. A opção
**"Guardar os dados preenchidos no histórico"**, na tela de preenchimento,
inclui um resumo dos valores (até três campos) no registro. Ela vem desligada
para não armazenar dados pessoais sem necessidade.

---

## 6. Exportar e importar

**Exportar** grava um JSON com este envelope:

```json
{
  "Format": "miniprint.template",
  "Version": 1,
  "Template": {
    "Id": "…",
    "Name": "Comprovante de Pagamento",
    "Description": "…",
    "PaperWidth": 384,
    "FontSize": 11,
    "Blocks": [ { "Kind": "Texto", "Text": "Recebemos de: {{nome_pagador}}" } ],
    "Fields": [ { "Key": "nome_pagador", "Label": "Nome do pagador", "Type": "TextoCurto" } ]
  }
}
```

Na importação o arquivo é **validado antes de salvar**:

- o campo `Format` precisa ser `miniprint.template`;
- a versão precisa ser suportada;
- o modelo precisa ter nome e ao menos um bloco;
- as chaves dos campos precisam ser válidas e não repetidas.

Se algo estiver errado, nada é importado e a lista de erros é exibida. Modelos
importados sempre recebem um **novo Id**, então nunca sobrescrevem um existente.

### 6.1 Modelos de exemplo prontos para importar

Os modelos de exemplo estão disponíveis como arquivos em `docs/exemplos/`:

| Arquivo | Conteúdo |
|---|---|
| `comprovante-de-pagamento.json` | comprovante (editor de blocos) |
| `recibo-de-servico.json` | recibo (editor de blocos) |
| `comprovante-de-pagamento-visual.json` | comprovante (editor visual) |
| `recibo-de-servico-visual.json` | recibo (editor visual) |
| `etiqueta-de-produto-visual.json` | etiqueta com nome, código, preço e descrição |
| `ordem-de-servico-visual.json` | ordem de serviço com número, cliente, telefone e valor |

No aplicativo instalado eles ficam em `%LOCALAPPDATA%\Programs\MiniPrint Desktop\modelos\`.

Para importar: **Modelos → Importar** e escolha o arquivo `.json`.

Para gerar novamente ou conferir um arquivo antes de importar:

```powershell
miniprint-diag template --exemplo 1 --exportar comprovante.json
miniprint-diag template --validar comprovante.json
```

Se o modelo já existir com o mesmo nome, a importação cria uma cópia com o
sufixo "(2)" — o original não é alterado.

---

## 7. Onde ficam os arquivos

| Arquivo | Conteúdo |
|---|---|
| `%LOCALAPPDATA%\MiniPrintDesktop\templates.json` | modelos salvos |
| `%LOCALAPPDATA%\MiniPrintDesktop\history.json` | histórico de impressões |

Para fazer backup, copie o `templates.json` ou exporte cada modelo em JSON.

---

## 8. Editor visual

O editor visual monta o documento por posicionamento livre — mais próximo de um
editor gráfico do que de um editor de texto.

### 8.1 Como abrir

- **Modelos → Novo documento** cria um documento em branco no editor visual.
- **Editar** em um modelo existente abre no editor em que ele foi criado.
- Modelos antigos (blocos) podem ser convertidos no rodapé do editor de blocos
  (**Converter para o editor visual**).

### 8.2 A tela

| Área | Conteúdo |
|---|---|
| Barra superior | nome do documento, indicador de alterações, desfazer/refazer, área de transferência, copiar estilo, localizar/substituir, alinhamento e distribuição, prévia e **Salvar** |
| Paleta (esquerda) | elementos agrupados em **Texto**, **Campos**, **Elementos gráficos** e **Estruturas prontas**, com pesquisa |
| Canvas (centro) | barra de zoom (controle, Ajustar, 100%, grade e toggles de visualização/encaixe) e o papel, com régua, grade, margens e guias |
| Painel (direita) | propriedades contextuais, campos do documento e camadas |

Os painéis laterais são **redimensionáveis** (arraste a divisória) e **recolhíveis**
(botões nas extremidades da barra superior). A largura e o estado de cada painel
são lembrados entre sessões.

### 8.3 Paleta e inserção rápida

- **Pesquisar elementos**: digite "linha", "campo", "imagem"... e a paleta é
  filtrada na hora (procura também nas palavras-chave: "dinheiro" acha o campo
  monetário).
- **Clique** insere o elemento na posição do elemento selecionado ou no primeiro
  espaço livre.
- **Arraste** o elemento da paleta e solte no ponto exato do papel.
- Os campos do documento também aparecem na paleta; clicar cria o elemento já
  associado à chave correta (sem duplicar o campo).
- O elemento inserido é **selecionado automaticamente** e suas propriedades
  aparecem no painel da direita.
- Documento vazio mostra um convite com atalhos para Texto, Título, Linha e Campo.

### 8.4 Canvas

- **Régua** no topo em pontos (1 ponto = 0,125 mm), acompanhando o zoom.
- **Grade** e **encaixe** com tamanho configurável; o encaixe também alinha às
  bordas e aos centros dos outros elementos, às margens e ao centro do papel.
  Durante o arraste aparecem **guias coloridas** mostrando o alinhamento — elas
  são apenas visuais e nunca entram no documento.
- **Margens** (retângulo tracejado) e **guias** podem ser ligadas/desligadas na
  barra superior.
- **Zoom** de 40% a 300% pelo controle deslizante, pelos botões, por "Ajustar"
  (Ctrl+0) ou "100%". Ctrl + roda do mouse amplia mantendo a região visível.
  O zoom nunca altera as coordenadas dos elementos.
- **Prévia** (Ctrl+P) troca o editor pela imagem rasterizada final, com o mesmo
  renderizador usado na impressão.

### 8.5 Seleção e manipulação

- Clique seleciona; **Shift + clique** acrescenta; **Ctrl + clique** alterna.
- **Arrastar sobre o papel** seleciona por área.
- **Ctrl + A** seleciona todos os elementos visíveis.
- O painel de camadas também aceita seleção múltipla (Ctrl/Shift) e é mantido
  sincronizado com o canvas.
- **Arrastar** move o elemento (e todo o grupo ou a seleção múltipla).
- **Oito alças** (cantos e lados) redimensionam; **Shift** durante o
  redimensionamento preserva a proporção. Os limites do papel e os tamanhos
  mínimos são respeitados, e as medidas atuais aparecem no rodapé do canvas.
- **Duplo clique** em um texto edita o conteúdo direto no papel (Enter confirma,
  Esc cancela). Em um campo, abre a janela de configuração do campo.
- **Esc** cancela arraste, edição ou prévia.

### 8.6 Menu de contexto

Botão direito sobre um elemento mostra apenas os comandos aplicáveis: editar
texto/campo, recortar, copiar, colar, duplicar, excluir, copiar/aplicar estilo,
bloquear, ocultar, trazer/enviar, agrupar/desagrupar e alinhar.

### 8.7 Propriedades

O painel muda com a seleção:

- **Nada selecionado** → propriedades do documento (nome, descrição, largura,
  margens, altura automática, rascunho automático).
- **Texto** → conteúdo, fonte (família, tamanho, negrito, itálico), alinhamento,
  entrelinhas, transbordo e altura automática.
- **Campo** → vínculo com o campo dinâmico, rótulo, tipo de dado e
  obrigatoriedade.
- **Imagem** → brilho, contraste, meio-tom, rotação, recorte e proporção.
- **Linha/retângulo** → espessura e preenchimento.
- **Vários elementos** → posição/tamanho do primeiro, visibilidade, bloqueio,
  alinhamento e distribuição.

Tudo é aplicado em tempo real: não existe botão "Aplicar".

### 8.8 Campos dinâmicos

- **Novo campo** abre uma janela curta com rótulo, tipo, obrigatório, orientação
  e valor padrão; a chave interna é gerada sozinha (`Nome do cliente` →
  `nome_do_cliente`) e mostrada apenas como referência.
- **Campos do documento** (painel direito) lista todos os campos, permite inserir
  um no documento com um clique e abrir a edição.
- **Editar campo** altera rótulo, tipo ou obrigatoriedade; o rótulo acompanha
  todos os elementos do documento.
- **Excluir** (ícone de lixeira na lista de campos) mostra quantos elementos usam o
  campo e pede confirmação.
- **Ao excluir o último elemento** que usava um campo, o editor pergunta se o
  campo também deve ser removido — antes ele ficava órfão na lista e continuava
  aparecendo no preenchimento.
- **Campos sem uso** aparecem contados em "Campos do documento" e podem ser
  limpos de uma vez pelo botão **Limpar sem uso**.
- O formulário de preenchimento mostra **apenas campos usados** no documento.
- No canvas o campo aparece como «Rótulo»; na impressão, o valor formatado.

### 8.9 Camadas

- Lista na ordem de cima para baixo, com ícone por tipo, nome, visibilidade e
  bloqueio.
- **Pesquisa** por nome ou tipo para documentos grandes.
- **Arrastar** um item reorganiza a ordem de sobreposição.
- A seleção da lista e a do canvas são a mesma; itens bloqueados e ocultos são
  identificados visualmente.

### 8.10 Organização e produtividade

- **Alinhar** à esquerda, centro, direita, topo, meio e base — com um elemento, em
  relação à área útil; com vários, ao bloco da seleção.
- **Distribuir** horizontal/verticalmente com espaçamento uniforme.
- **Agrupar** (Ctrl+G) e **desagrupar**.
- **Copiar estilo** e **aplicar estilo**: leva fonte, alinhamento e espessura de
  um elemento para outros.
- **Duplicar** (Ctrl+D) cria a cópia deslocada e já selecionada.
- **Localizar e substituir** (Ctrl+F) atua nos textos fixos do documento, sem
  tocar nas chaves dos campos.

### 8.11 Salvamento e recuperação

- O topo mostra "Alterações não salvas" ou "Salvo" e o horário.
- **Salvar rascunho automaticamente** (ligado por padrão) grava um rascunho a
  cada ~20 s em `%LOCALAPPDATA%\MiniPrintDesktop\drafts\`. Se o aplicativo for
  fechado de forma inesperada, ao reabrir o modelo o rascunho é oferecido.
- Um rascunho nunca sobrescreve `templates.json`: se estiver corrompido, o modelo
  salvo continua íntegro.
- Salvar valida o documento; erros bloqueiam a gravação e aparecem no topo.

### 8.12 Atalhos

| Atalho | Ação |
|---|---|
| `Ctrl+Z` / `Ctrl+Y` | desfazer / refazer |
| `Ctrl+C` / `Ctrl+V` / `Ctrl+X` | copiar / colar / recortar |
| `Ctrl+D` | duplicar |
| `Ctrl+A` | selecionar todos |
| `Ctrl+S` | salvar |
| `Ctrl+G` | agrupar |
| `Ctrl+B` / `Ctrl+I` | negrito / itálico |
| `Ctrl+F` | localizar e substituir |
| `Ctrl+P` | prévia de impressão |
| `Ctrl+0` | ajustar à largura |
| `Ctrl` + roda | zoom na posição do cursor |
| setas / `Shift` + setas | mover 1 / 8 pontos |
| `Delete` / `Backspace` | excluir |
| `Esc` | cancela prévia, edição inline ou arraste |

Os atalhos são ignorados enquanto o foco está em um campo de texto.

### 8.13 Undo/Redo

O histórico guarda **documento (nome, papel, margens), campos e layout** — antes
cobria só o layout. Alterações seguidas do mesmo tipo (digitar um texto, ajustar
um campo) viram **um único passo**, e o limite continua em 120 operações.

### 8.14 Compatibilidade

O arquivo continua com a versão 2 do envelope:

- arquivos versão 1 (só blocos) continuam abrindo;
- arquivos versão 2 mantêm `Layout` com elementos posicionados;
- nenhuma propriedade foi removida; as preferências novas do editor ficam no
  arquivo de configuração, não nos modelos.


## 9. Limitações conhecidas

- A largura do documento é limitada a 384 pontos (limite físico do cabeçote).
- A impressão é monocromática; não há cores nem tons de cinza reais.
- Campos do tipo "lista" não permitem digitar um valor fora das opções.
- No editor visual cada elemento é uma caixa independente: para dois campos na
  mesma linha, use dois elementos lado a lado ou um texto com `{{chave}}`.
- A pré-visualização do editor usa valores de exemplo («Rótulo», 150,00, data de
  hoje) até o documento ser preenchido de verdade.
- No editor visual, o texto do canvas é uma aproximação do WPF; o **Modo
  impressão** (Ctrl+P) e a prévia do preenchimento mostram o resultado fiel.
- A seleção por área começa no papel vazio; Shift+clique e Ctrl+clique também
  estão disponíveis, assim como a seleção múltipla no painel de camadas.
- Textos não têm rotação; imagens têm (0, 90, 180, 270).
- A régua é horizontal; não há régua vertical.
- Gráficos vetoriais livres (caneta, formas curvas) não fazem parte do escopo.



