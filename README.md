# Chatbot para Criação de Fichas de Personagens de RPG

[![build](https://github.com/MicalskiCorp/MainForge/actions/workflows/build.yml/badge.svg)](https://github.com/MicalskiCorp/MainForge/actions/workflows/build.yml)
[![licença MIT](https://img.shields.io/badge/licen%C3%A7a-MIT-blue.svg)](LICENSE)

Aplicativo desktop em C#/.NET que entende livros de RPG em PDF e conduz a criação de
personagens, terminando num PDF de ficha preenchido. Tudo roda localmente na máquina do
usuário.

## Baixar e usar

**[Baixar o MainForge 1.1.0](https://github.com/MicalskiCorp/MainForge/releases/download/v1.1.0/MainForge-1.1.0-win-x64.zip)**
(Windows x64, 33 MB) — descompacte o `.zip` onde quiser e rode `MainForge.Cli.exe`.
As demais versões ficam em [releases](https://github.com/MicalskiCorp/MainForge/releases/latest).

O download é **um executável só**, com o runtime .NET e as bibliotecas de PDF dentro dele: não
há instalador, não escreve no registro e não precisa de administrador. Ao lado dele vão os
prompts dos agentes (`Agents/`) e as pastas de dados, que o aplicativo cria vazias no primeiro
uso. Para desinstalar, apague a pasta.

A única coisa que você precisa instalar é o
**[Claude Code](https://claude.com/product/claude-code)**, autenticado na sua conta — é ele que
roda os agentes, com a **sua** assinatura:

```
winget install --id Anthropic.ClaudeCode
```

Não precisa decorar: o menu **Ambiente** confere todas as dependências e oferece rodar esse
comando por você, e abre a janela do Claude Code para o `/login`. O primeiro processamento
também passa por essa conferência antes de gastar qualquer cota.

| | Precisa instalar? | Para quê |
| --- | --- | --- |
| .NET 10 | não — vai embutido | rodar o aplicativo |
| PdfPig, PDFsharp | não — compiladas junto | extrair texto dos livros e preencher a ficha |
| **Claude Code** | **sim** | rodar os agentes com a sua assinatura |
| poppler (`pdftoppm`) | recomendado | o agente abrir PDF — em especial a ficha em branco |
| markitdown | opcional | conversão dos livros com mais qualidade em tabelas |

**Requisitos e limites, sem letra miúda:** o pacote é **Windows x64** (em Windows ARM roda por
emulação; não há build para macOS nem Linux, porque o aplicativo usa a API de console do Windows
e as fontes de `C:\Windows\Fonts` para desenhar a ficha).

### O Windows vai avisar na primeira execução

Vai aparecer *"O Windows protegeu o seu PC"*. Clique em **Mais informações** › **Executar assim
mesmo**.

O aviso não diz que o programa é malicioso — diz que o **publicador é desconhecido**. O
SmartScreen acumula confiança por certificado de assinatura de código, e certificado custa
algumas centenas de dólares por ano, com a chave obrigatoriamente em hardware. Este é um projeto
livre e gratuito: preferimos manter assim e ser francos sobre o aviso a repassar esse custo de
alguma forma.

O caminho para fazer o aviso desaparecer **sem custo** é a
[SignPath Foundation](https://signpath.org/), que fornece certificado e infraestrutura de
assinatura a projetos de código aberto. O passo já está no
[workflow de release](.github/workflows/release.yml), desligado enquanto não houver os segredos
da conta — quando houver, os pacotes passam a sair assinados sem mais nenhuma mudança. Como o
certificado é OV, a confiança se acumula com os downloads em vez de existir desde o primeiro; o
que remove o aviso de imediato é o certificado EV, e esse é pago.

No lugar da assinatura, você tem como **verificar por conta própria** que o arquivo é exatamente
o que a compilação pública gerou. Todo release traz o `.sha256` ao lado do `.zip`:

```powershell
Get-FileHash .\MainForge-1.1.0-win-x64.zip -Algorithm SHA256
```

O valor precisa bater com o do arquivo `.sha256` e com o que está nas notas do release. Se bater,
o arquivo não foi adulterado entre o servidor do GitHub e o seu disco — que é justamente contra o
que a assinatura protegeria.

Se quiser desconfiar de tudo, o caminho mais forte é não baixar nada: **compile do código-fonte**
(veja [Rodando](#rodando)). O binário do release sai do mesmo `publicar.ps1` que você roda na sua
máquina, por um
[workflow público](.github/workflows/release.yml) — não há passo manual entre o código e o `.zip`.

Quem for compilar do código-fonte encontra as instruções em [Rodando](#rodando), e
`./publicar.ps1` gera o mesmo pacote do release.

## Decisões de arquitetura

- **Autenticação**: nenhuma. O aplicativo não tem, não pede e não guarda credencial —
  ele executa o **Claude Code** já instalado e autenticado na máquina, e quem paga a conta
  é a assinatura do usuário. Não existe Claude Agent SDK para C#, então o que o
  `MainForge.ClaudeCode` faz é o que os SDKs de TypeScript e Python fazem por baixo:
  lançar o `claude` em modo headless (`-p --output-format stream-json`), mandar a mensagem
  pela stdin e traduzir o fluxo de eventos da stdout.
- **Ferramentas**: híbridas, por necessidade técnica.
  - Ler PDF é do `Read` embutido do Claude Code, que lê PDF nativamente. **Não dá para
    fazer isso por MCP**: uma ferramenta MCP que devolva o PDF faz o Claude Code gravar o
    binário em disco e passar só o caminho ao modelo, que não consegue lê-lo. Isso foi
    verificado, não deduzido. Mesmo assim, o caminho normal deixou de ser o PDF: veja
    [Os livros viram texto antes](#os-livros-viram-texto-antes).
  - Manipular AcroForm (listar e preencher os campos da ficha) e escrever em `Sistemas/`
    ficam em C#, expostos por um servidor MCP local (`MainForge.Mcp`, stdio, lançado pelo
    próprio Claude Code).
- **.NET 10** (LTS instalada na máquina), solução dividida em projetos por responsabilidade.

### Sobre o MCP

Uma versão anterior deste README dizia que MCP não agregava valor "porque tudo roda no mesmo
processo". Isso deixou de ser verdade: os agentes rodam agora em outro processo (o Claude
Code), e o MCP é justamente a ponte entre os dois.

## O guardrail de ferramentas

"Cada agente só pode usar certas ferramentas" continua valendo, em três camadas — nenhuma
delas suficiente sozinha:

1. **Diretório de trabalho.** O processo roda com a raiz do projeto como diretório de
   trabalho, e o Claude Code não acessa arquivos fora dele.
2. **Negações por ferramenta e por caminho** (`--disallowedTools`, em
   `DefinicaoDeAgente.FerramentasNegadas`). ⚠️ **`--allowedTools` apenas concede, não
   restringe.** Ferramentas de leitura já são aprovadas por padrão, então listar
   `Read(Sistemas/**)` *não* impede leituras fora de `Sistemas/` — só uma negação
   explícita bloqueia. Negação vence concessão.
3. **Confinamento em código.** Toda **escrita** passa pelo servidor MCP, onde
   `CaminhosDoProjeto.ResolverDentroDe` rejeita qualquer caminho que escape do diretório
   permitido. É a única camada que não depende de acertar uma lista de negação — e por isso
   é onde mora a regra que realmente importa.

| | Configurador | Dungeon Master |
| --- | --- | --- |
| Embutidas | `Read`, `Glob` | `Read`, `Glob` |
| MCP | `escrever_arquivo_conhecimento`, `descrever_pasta_de_conhecimento`, `registrar_plano_de_conhecimento`, `consultar_progresso`, `procurar_no_texto_dos_livros`, `listar_campos_da_ficha` | `preencher_ficha_personagem`, `registrar_personagem`, `procurar_no_conhecimento` |
| Negações próprias | `Read(Output/**)`, `Read(Personagens/**)` | `Read(Input/**)`, `Read(Templates/**)`, `Read(Output/**)` |

Negado para os dois, sempre: `Bash`, `PowerShell`, `BashOutput`, `KillShell`, `Write`, `Edit`,
`NotebookEdit`, `Task`, `Agent`, `Skill`, `SlashCommand`, `WebFetch`, `WebSearch`, `Grep`, e a
leitura do código do próprio aplicativo e de `.claude/`. Cada um desses é uma saída de
emergência pela qual um agente contornaria todas as outras restrições. `Skill`, `SlashCommand` e
`.claude/` entram na lista porque o agente roda com a raiz do projeto como diretório de
trabalho: sem a negação, ele enxergaria as skills de quem desenvolve o aplicativo, que descrevem
justamente o código-fonte que ele não pode ler.

> `PowerShell` entrou na lista depois de acontecer: no Windows, o Configurador recebeu essa
> ferramenta e usou `Get-ChildItem` e `Select-String` para listar pastas e procurar texto —
> exatamente o que as negações de `Bash` e `Grep` existiam para impedir. **Negar um shell não
> nega shell nenhum**: negação por nome é uma lista, e lista se esquece. É por isso que a camada
> que realmente contém a escrita é o servidor MCP em C#, e não esta tabela.

Buscar continua sendo necessário, e é por isso que existem `procurar_no_texto_dos_livros` (para
o Configurador, alcançando só `Input/<Sistema>/**/_texto/`) e `procurar_no_conhecimento` (para o
Dungeon Master, alcançando só `Sistemas/<Sistema>/`). As duas fazem o que a `Grep` faria, com o
confinamento aplicado em C#.

Duas negações são **da execução**, não do agente, e existem para não oferecer caminho que não
leva a lugar nenhum: as fontes que a mesa não usa (`DungeonMasterLimitadoA`) e, quando falta o
poppler, os PDFs dos livros (`ConfiguradorSemAbrirPdf`).

> **A negação por caminho para no agente.** `procurar_no_conhecimento` percorre `Sistemas/` por
> conta própria, dentro do **processo do servidor MCP** — onde a lista de `--disallowedTools` do
> Claude Code não chega. Uma busca assim sem mais nada devolveria trecho do compêndio que o
> usuário acabou de deixar de fora: seria a porta lateral exata que a negação existia para
> fechar. Por isso as fontes escolhidas viajam com a sessão (`RestricaoDeFontes`, pelo bloco
> `env` da configuração do servidor) e são conferidas lá dentro, em C#. Ferramenta MCP nova que
> leia `Sistemas/` precisa conferi-la também.

## Estrutura da solução

```
MainForge.sln
├── src/
│   ├── MainForge.Core       -> modelos de domínio, CaminhosDoProjeto (raiz de tudo que toca disco)
│   ├── MainForge.ClaudeCode -> localiza e executa o Claude Code; traduz o stream-json em eventos;
│   │                           reconhece cota esgotada (LimiteDeUso) para poder esperar a janela
│   ├── MainForge.Tools      -> o que só o C# faz: AcroForm (PdfSharp), escrita em Sistemas/,
│   │                           os índices (IndiceDeConhecimento) e o progresso (EstadoDoProcessamento)
│   ├── MainForge.Mcp        -> servidor MCP stdio que expõe MainForge.Tools ao agente
│   ├── MainForge.Agents     -> DefinicaoDeAgente (prompt + permissões) e SessaoDeAgente
│   ├── MainForge.Cli        -> interface em console (a interface em uso hoje)
│   └── MainForge.App        -> aplicativo WPF (interface gráfica, ainda um shell vazio)
├── tests/MainForge.Tests
├── tools/ValidacaoPontaAPonta -> harness manual do fluxo completo (fora da solução), sobre o
│                                 sistema fictício "SistemaTeste"
├── .claude/skills/       -> skills do Claude Code de quem desenvolve o projeto (não dos agentes
│                            do aplicativo, que têm `Skill` negada). `estrutura-do-projeto` é o
│                            mapa de onde cada coisa mora e é obrigatória antes de mexer nele
├── CLAUDE.md             -> instruções de projeto para o Claude Code de desenvolvimento
├── Agents/               -> prompts dos agentes (Configurador.md, DungeonMaster.md)
├── Input/              -> livros oficiais em PDF, um subdiretório por sistema e, dentro
│                            dele, um por fonte: base/ e uma pasta por expansão. Cada fonte
│                            ganha um _texto/ com a versão Markdown dos PDFs dela
├── Templates/            -> fichas em PDF editável, um subdiretório por sistema
├── Sistemas/            -> base de conhecimento em Markdown, gerada pelo Configurador,
│                            com a mesma divisão por fonte, um index.md por nível e o
│                            registro de progresso do sistema
├── Personagens/          -> o dossiê de cada personagem (situação, fontes da mesa, sessão e
│                            o estado dele em texto). É o que torna a criação retomável
└── Output/
    ├── Personagens/      -> fichas finais preenchidas
    └── Pacotes/          -> sistemas exportados para levar a outra máquina
```

**Convenção de idioma:** nomes de solução, projetos e namespaces ficam em inglês (padrão do
ecossistema .NET), mas todo o resto — classes, métodos, variáveis, comentários e textos de
UI — é em português (pt-BR).

## Pré-requisito

O [Claude Code](https://claude.com/product/claude-code) instalado e autenticado: rode
`claude` uma vez num terminal e faça login. O aplicativo o procura no PATH e nos diretórios
padrão de instalação; se estiver em outro lugar, aponte a variável de ambiente
`MAINFORGE_CLAUDE_CODE` para o executável.

## Rodando

```
dotnet build MainForge.sln
dotnet test MainForge.sln
dotnet run --project src/MainForge.Cli    # o aplicativo
./publicar.ps1 -Versao 1.1.0              # gera o .zip do release (win-x64, executável único)
```

Os mesmos comandos rodam no CI a cada push e pull request
([build.yml](.github/workflows/build.yml)), em Windows — que é onde o aplicativo vive. Além de
compilar e testar, o CI **empacota e executa o pacote**: confere que os prompts dos agentes e a
licença estão dentro dele e que o binário responde no modo servidor MCP. Os dois últimos existem
porque já quebraram: a cópia dos prompts acontece só na publicação, e um pacote que não abre
passaria em todos os testes de unidade.

O `publicar.ps1` roda o mesmo publish do fluxo de release
([`.github/workflows/release.yml`](.github/workflows/release.yml)), que empacota e anexa o `.zip`
quando uma tag `v*` é empurrada. Um detalhe da distribuição vale saber: **o servidor MCP é o
próprio executável**, lançado com `--mcp <raiz>`. Enquanto ele era um segundo programa, um
pacote self-contained levaria dois runtimes .NET inteiros e duas versões que podiam divergir.

O aplicativo abre maximizado, no console clássico e no Windows Terminal. Não é capricho: a tela
inicial tem cerca de 145 colunas, o desenho da ficha em texto tem 78, e o progresso do agente
imprime chamadas de ferramenta longas — numa janela de 80x25 tudo isso quebra linha.

Achar a janela é o problema todo, e `JanelaDoConsole` resolve de dois jeitos porque são dois
mundos. No conhost, `GetConsoleWindow` já devolve a janela de verdade. No Windows Terminal (e
em qualquer host via ConPTY) ele devolve uma `PseudoConsoleWindow`, que é um objeto interno do
próprio processo, sem pixel na tela — mexer nela não maximiza nada e chega a travar o console.
O terminal também não é processo ancestral nosso, então nem pela árvore de processos se chega
até ele. O que liga os dois é o **título**: o terminal espelha no título da janela o título do
console da aba ativa, então o aplicativo escreve um título único, acha a janela que passou a
exibi-lo e maximiza aquela.

Um pedido só não basta: o terminal ainda está se montando quando o aplicativo começa, e o
tamanho de inicialização que ele aplica em seguida desfazia o nosso — daí a janela abrir
maximizada e encolher logo depois, de forma intermitente. Por isso o pedido é reafirmado por
cinco segundos, numa linha de execução em segundo plano (o menu aparece na hora). Passado esse
prazo, o aplicativo não mexe mais na janela: quem quiser restaurá-la na mão manda.

Duas consequências: rodando dentro de um terminal que já estava aberto com outras abas, é
aquela janela inteira que é maximizada — a janela não é nossa, nós só pedimos; e
`MAINFORGE_SEM_MAXIMIZAR=1` desliga tudo isso.

O menu principal tem três portas, na ordem em que o trabalho acontece: primeiro o sistema de RPG
existe, depois os personagens nascem dele, e o ambiente só interessa quando algo não funciona.
Cada porta abre um submenu com as ações daquele assunto.

### 1. Sistemas

A tela abre com o panorama — o que foi importado, o que já tem base e ficha, e o que ficou pela
metade — e abaixo dele as ações:

- **Novo sistema** — você informa o nome, os PDFs dos livros do jogo base e a ficha de
  personagem editável; o programa valida (livro legível, ficha com campos preenchíveis) e copia
  para `Input/<Sistema>/base/` e `Templates/<Sistema>/`.
- **Adicionar livros** — pergunta se o livro é do jogo base ou de uma expansão (e, se for de
  uma expansão nova, o nome dela). O livro entra em `Input/<Sistema>/<fonte>/` e o Configurador
  lê **só ele**, somando o conteúdo à base que já está pronta.
- **Processar** (Agente Configurador) — converte os livros para texto (veja
  [Os livros viram texto antes](#os-livros-viram-texto-antes)), lê os livros **e a ficha em
  branco** e gera `Sistemas/<Sistema>/<fonte>/*.md`, uma pasta por fonte. É a operação mais cara
  em tokens; pede confirmação. **Os sistemas com processamento incompleto vêm no topo da lista,
  marcados** — continuar lê só o que falta, e essa é a única situação em que processar de novo é
  barato. Se não houver nada pendente, o aplicativo recusa e explica por quê.
- **Exportar** e **importar pacote** — veja
  [Levar um sistema para outra máquina](#levar-um-sistema-para-outra-máquina).

Adicionar um sistema de RPG novo não exige mexer em código: novo sistema seguido de processar
(ou copiar as pastas na mão para `Input/` e `Templates/`).

### 2. Personagens

A tela lista os personagens com a situação de cada um e as ações:

- **Criar** (Agente Dungeon Master) — escolhe o sistema, pergunta quais expansões aquela mesa
  usa e daí é conversa livre, até a ficha em PDF sair em `Output/Personagens/`.
- **Continuar** um que ficou em desenvolvimento, **evoluir** um pronto (subir de nível, mexer no
  inventário, corrigir dados) e **descontinuar ou reativar**. Veja
  [Personagem é um objeto do aplicativo](#personagem-é-um-objeto-do-aplicativo).

`/sair` encerra qualquer conversa; o que já foi decidido fica salvo.

### 3. Ambiente

- **Claude Code** — mostra o executável, o modelo e as permissões de cada agente, abre a janela
  de login da sua conta Claude e faz um turno de teste para confirmar que a assinatura está
  ativa.
- **Dependências** — o que esta máquina tem, o que falta, para que serve cada coisa e o comando
  exato para instalar o que faltar. O processamento de sistema também passa por aqui antes de
  começar: descobrir que falta o Claude Code depois de confirmar a operação mais cara do
  aplicativo é o pior momento possível para essa notícia.

O sistema `SistemaTeste` não aparece em nenhuma dessas telas: ele existe só para o harness de
validação, que o alcança pelo nome. Esconder é da interface, não do disco — quem abre o
aplicativo veria um sistema fictício ao lado dos de verdade sem ter como saber que não é para
usar.

## Jogo base e expansões

Um sistema é dividido por **fonte**: `base/` é o jogo base e cada outra pasta é uma expansão
(compêndio, suplemento), com o nome que o usuário deu a ela. A divisão vale nos dois lados —
`Input/<Sistema>/<fonte>/` guarda os PDFs e `Sistemas/<Sistema>/<fonte>/` guarda o
conhecimento destilado deles.

Isso existe por causa de uma pergunta na criação de personagem: **quais expansões esta mesa
usa?** Cada grupo combina quais compêndios estão em jogo, e um personagem com uma subclasse de
um livro que o grupo não usa é um personagem inválido.

A resposta não vira um pedido no prompt — vira **negação de leitura**: as pastas das expansões
não marcadas são bloqueadas para o Dungeon Master naquela sessão (`DungeonMasterLimitadoA`).
Pedir "não use o compêndio X" não bastaria: o agente esbarraria no arquivo enquanto navega pelo
índice e o conteúdo entraria na conversa de qualquer jeito.

Duas consequências para o Configurador: o conteúdo de um livro vai **sempre** para a pasta da
fonte dele, e conteúdo de expansão que altera uma regra do jogo base vira arquivo novo dentro
da expansão, citando o original em vez de reescrevê-lo — quem não usa aquele livro precisa
continuar vendo a regra intacta. As únicas exceções são `Ficha-Mapeamento.md` e
`Ficha-ModeloEmTexto.md`, que ficam na raiz do sistema porque a ficha em PDF é do sistema
inteiro e não muda com a expansão em uso.

Um sistema importado por uma versão anterior do aplicativo tem tudo solto na pasta do sistema.
O panorama do menu Sistemas oferece movê-lo para `base/` (e o processamento exige isso antes de
começar): é só mover arquivo, sem reler livro nenhum.

## Personagem é um objeto do aplicativo

Um personagem não é só o PDF que sai no fim. Ele tem um **dossiê** em
`Personagens/<Sistema>/<Personagem>/` e uma **situação**:

| Situação | O que significa | O que dá para fazer |
| --- | --- | --- |
| **desenvolvendo** | a criação começou e não terminou | continuar de onde parou |
| **concluído** | a ficha em PDF foi gerada | evoluir: nível, inventário, correções |
| **descontinuado** | abandonado de propósito | reativar; nada é apagado do disco |

São dois arquivos, com donos diferentes. O `personagem.json` é do aplicativo: situação, fontes
que aquela mesa usa, id da conversa no Claude Code, valores da última ficha gerada e o
histórico. O `ficha.md` é do agente, gravado pela ferramenta `registrar_personagem` a cada bloco
de decisões fechado — atributos definidos, classe escolhida, equipamento comprado.

**Por que os dois.** Retomar pela conversa é o que sai barato: o `--resume` do Claude Code traz
de volta a base que o agente já leu, que é o gasto mais caro e o único que não dá para refazer
de graça. Mas conversa expira, e é apagada. Quando ela não existe mais, o `ficha.md` é o que
reconstrói o personagem sem reler a base inteira — por isso ele é escrito como estado completo,
para quem não acompanhou a conversa, e não como um diário do que mudou.

Quem fecha a criação é o PDF, não o modelo dizendo que terminou: `preencher_ficha_personagem`
recebe o identificador do personagem e é ela que marca o dossiê como concluído. Evoluir um
personagem gera a ficha de novo, a partir do template em branco, com os valores atualizados.

As fontes da mesa ficam gravadas no dossiê e valem para sempre: subir de nível com uma expansão
que a mesa não usava produziria um personagem que ninguém pode jogar. Uma expansão processada
depois entra na lista de recusadas daquele personagem, em vez de aparecer liberada só por não
ter sido negada.

## Levar um sistema para outra máquina

Mapear um sistema é a única operação que custa a cota da assinatura — e o resultado é o mesmo
para todo mundo que tem aqueles livros. O menu Sistemas exporta um sistema pronto para um
arquivo `.mainforge.zip` e o importa do outro lado.

O pacote leva a **base de conhecimento em Markdown** (com os índices) e a **ficha em PDF**. Não
leva os livros: são dezenas de MB e, sendo obra comercial, não são de quem os importou para
redistribuir. A base destilada e a ficha em branco bastam para criar personagem, que é o
objetivo.

Três consequências:

- A exportação **recusa** um sistema cujo mapeamento da ficha esteja incompleto
  (`Ficha-Mapeamento.md` e `Ficha-ModeloEmTexto.md`). Sem eles o destinatário recebe uma base que
  não preenche PDF nenhum, e descobrir isso do outro lado é tarde demais.
- O sistema importado chega **completo e sem pendência** — não há livro por ler. Quem quiser
  acrescentar conteúdo depois traz os PDFs por conta própria e processa: só os livros novos são
  lidos.
- Importar sobre um sistema de mesmo nome **pede confirmação explícita**, ou um nome alternativo.
  A base que está lá pode ter custado horas de cota.

O manifesto do pacote é lido antes de qualquer arquivo ser extraído, e todo caminho de dentro
dele passa pelo mesmo confinamento que vale para caminho vindo do modelo: um `.zip` vem de fora
e pode carregar `../../` (o "zip slip"), e o que escapa da pasta de destino é recusado.

## Os livros viram texto antes

Ler PDF é a operação mais cara do aplicativo, e é cara duas vezes: o Claude Code **rasteriza**
as páginas pedidas e manda imagens ao modelo. Um manual de 300 páginas esgota a janela da
assinatura antes de o agente ter visto metade dele — e, quando o `pdftoppm` (do poppler) não
está instalado, ler um intervalo de páginas nem funciona: falha com
`pdftoppm is not installed`.

Por isso, antes de o Configurador começar, o aplicativo converte cada PDF de `Input/` em
Markdown e grava o resultado em `Input/<Sistema>/<fonte>/_texto/<Livro>.md`. O agente passa a
ler o `.md`: é o mesmo conteúdo por uma fração dos tokens, e o livro inteiro cabe na janela.

Quem converte são dois, nesta ordem:

1. o [markitdown](https://github.com/microsoft/markitdown), da Microsoft, se estiver instalado —
   sai melhor em tabelas e listas;
2. o **extrator interno** ([ExtratorDeTextoDePdf](src/MainForge.Tools/ExtratorDeTextoDePdf.cs),
   sobre [PdfPig](https://github.com/UglyToad/PdfPig)), que vem junto com o aplicativo e cobre a
   maioria dos livros de regras. Ele também assume quando o markitdown falha num livro
   específico.

O segundo existe porque o primeiro não pode ser obrigatório: numa máquina sem Python **e** sem
poppler — um Windows recém-instalado —, o agente ficava sem nenhum caminho até o livro, com o
texto inexistente e o `Read` do PDF falhando. Uma dependência que o usuário precisa instalar não
pode ser o único caminho para a operação central do produto. Na prática, os três livros de Aventura&Cia
(19 MB de PDF) viraram 2,6 MB de texto em **6 segundos**, sem instalar nada.

- A conversão roda **na máquina do usuário**, não gasta cota nenhuma, e fica em cache: só é
  refeita quando o PDF muda (data e tamanho). Apagar o `.md` manda convertê-lo de novo.
- O texto de uma fonte fica **dentro daquela fonte**, pela mesma razão de todo o resto: o
  conteúdo de um compêndio não pode acabar valendo numa mesa que não o escolheu.
- Achar uma regra no meio de dezenas de milhares de linhas é com `procurar_no_texto_dos_livros`
  (arquivo, linha e seção), seguida de um `Read` com `offset` naquele ponto. Ler o arquivo de
  ponta a ponta seria trocar um desperdício por outro.
- O extrator interno marca cada página (`## Página 42`), o que dá à busca uma seção para mostrar
  e permite ao agente citar de onde tirou a regra.
- **Sem poppler, a leitura dos PDFs de `Input/` é negada ao agente naquela execução**
  (`Read(Input/**/*.pdf)`), e o prompt diz por quê. Não é capricho: enquanto ela ficava
  disponível, o agente tentava conferir no PDF a tabela que a conversão embaralhou — o que seria
  o certo se funcionasse — e gastava um turno por livro para receber
  `pdftoppm is not installed`. Negar o caminho quebrado é o que transforma isso numa recusa
  imediata. Com poppler instalado, a leitura continua liberada como recurso extra.
- Livro **digitalizado** (páginas que são só imagem) não tem texto para extrair; o aplicativo
  diz isso em vez de gravar um arquivo vazio, e aí o PDF volta a ser o caminho (com poppler).
- A ficha em branco de `Templates/` **não** é convertida: ali o que interessa é o leiaute, que é
  justamente o que a conversão perde.

**Instalar o markitdown é opcional**, e só melhora a qualidade da conversão:

```
pip install "markitdown[pdf]"        # precisa de Python 3.10+
uv tool install "markitdown[pdf]"    # alternativa, se você usa uv
```

O aplicativo procura, nesta ordem: `MAINFORGE_MARKITDOWN` (caminho do executável), `markitdown`
no PATH, `python -m markitdown` e, por último, `uvx markitdown`. Depois de instalar, apague a
pasta `_texto/` do sistema para os livros serem convertidos de novo — o cache não sabe que
apareceu um conversor melhor.

## A base de conhecimento é indexada — e buscável

Cada pasta de `Sistemas/` ganha um `index.md` com uma linha sobre cada arquivo e cada
subpasta dela, e `Sistemas/index.md` lista os sistemas. É por aí que o Dungeon Master navega:
lê o índice, decide o que interessa e abre só isso, em vez de varrer a base inteira para achar
uma regra.

O índice resolve a pergunta que cai na estrutura ("que classes existem?") e desperdiça turnos na
que é transversal ("onde está a regra de carga?"): cada nível é uma leitura paga, e no fim o
agente ainda abre o arquivo errado uma vez ou outra. Para essas, existe
`procurar_no_conhecimento` — arquivo, linha e seção numa chamada só, ignorando acento e
maiúscula, já limitada às fontes daquela mesa.

O Configurador recebe no prompt um **esqueleto sugerido** de pastas (criação de personagem,
atributos, raças, classes, antecedentes, perícias, magias, equipamentos, progressão) para não
replanejar a estrutura do zero em toda base — mas ele adapta ao sistema real, porque um jogo
organizado por clã ou por aspecto não tem "classes". O que não muda é a granularidade: **um
assunto por arquivo**, na unidade da escolha que o jogador faz. Um arquivo com todas as classes
obriga a carregar todas para responder sobre uma; um arquivo por habilidade multiplica as
leituras para montar um personagem só.

Os índices são derivados, não escritos pelo agente — são regravados a cada gravação, e a
ferramenta de escrita recusa um `index.md` vindo do modelo. O que o Configurador fornece é o
`resumo` de cada arquivo e a descrição de cada pasta; o formato é do C#. Bases geradas por
versões anteriores do aplicativo são indexadas na primeira vez que o sistema é processado, com
descrições derivadas do próprio conteúdo — sem custo de tokens.

## Processamento retomável

O Configurador registra o plano de arquivos antes de começar, e cada gravação atualiza
`Sistemas/<Sistema>/_estado-do-processamento.json`. Uma execução interrompida — cota
esgotada, Ctrl+C, máquina desligada — deixa esse registro coerente com o que existe em disco,
e a execução seguinte pergunta o que falta em vez de reler o livro inteiro.

Três consequências práticas:

- **Sistema sem nada pendente não é processado de novo.** Quando todo arquivo do plano existe e
  todo livro já foi incorporado, o menu deixa de oferecer "continuar de onde parou" — não há
  onde parar, e aceitar isso mandaria o agente reler os livros para reescrever a base que já
  estava pronta. Sobra o "recomeçar do zero", que é uma escolha explícita.
- **Livro só conta como incorporado se a fonte dele ganhou conteúdo naquela execução.** Sem essa
  condição, um compêndio que o agente não conseguiu nem abrir (PDF sem o poppler que o `Read` do
  Claude Code exige) era marcado como lido por não haver mais nada pendente no plano antigo — e o
  sistema ficava "pronto" com uma expansão que ninguém leu. Quando o registro já está nesse
  estado, a tela de processamento avisa quais fontes constam como lidas sem nada em `Sistemas/`
  e oferece relê-las. Ela **aponta** em vez de corrigir sozinha porque mover um livro de fonte
  produz exatamente a mesma imagem, e nesse caso reler seria desperdício.
- Um livro só é marcado como incorporado quando a execução **termina**. Se ela morrer no último
  passo (era o que acontecia com a cota esgotada não reconhecida), o livro fica registrado como
  pendente mesmo tendo sido lido inteiro. Nesse caso o menu oferece marcá-lo como incorporado
  sem chamar agente nenhum — a decisão é do usuário porque, do lado do disco, "livro por ler" e
  "livro lido cujo registro não fechou" são indistinguíveis.
- Apagar um `.md` na mão basta para mandá-lo ser regerado: o disco é a verdade final, e o
  registro é reconciliado com ele antes de cada execução.
- Trocar um PDF em `Input/<Sistema>/<fonte>/` (mesmo nome, conteúdo diferente) marca aquele
  livro como não lido de novo. Quem decide isso é o **conteúdo**, por SHA-256, e não a data do
  arquivo: copiar a pasta do projeto, restaurar um backup ou deixar uma sincronização de nuvem
  passar por cima muda a data sem trocar nada dentro do livro, e isso já marcou como "não lidos"
  os dois livros de uma base que estava inteira. O hash só é calculado quando data ou tamanho
  mudam — no caso comum, nenhum arquivo chega a ser aberto. **Mover** um livro de fonte também
  não obriga a relê-lo: o conteúdo é o mesmo, muda apenas o destino dele em `Sistemas/`.
- Uma base que já existia antes de tudo isso é **adotada**: o panorama do menu Sistemas oferece gerar
  índice e registro a partir do que está em disco (de graça, sem agente). Os `.md` presentes
  entram como prontos e os livros, como ainda não lidos — que é a leitura honesta de uma base
  gerada pela metade. A execução seguinte então oferece "ler só os livros ainda não
  incorporados", em vez de recomeçar o sistema inteiro.

## Quando a cota da assinatura acaba

Cota esgotada não é tratada como erro — desde que seja reconhecida. A detecção é por texto
porque é o que o CLI oferece (não há código de erro dedicado), e cada forma nova que ele
inventa precisa entrar em `DetectorDeLimiteDeUso.Sinais`. Foi o caso de
`You've hit your session limit · resets 4:10pm`, que passava batido: o processamento morria com
"Falha ao processar" no lugar da contagem regressiva, e o que o agente já tinha lido ia junto.
Deixar de reconhecer um limite custa um processamento inteiro; reconhecer um que não existe
custa uma espera que o Ctrl+C desfaz — por isso a lista é frouxa de propósito.

O aplicativo reconhece a mensagem do Claude Code,
descobre quando a janela vira (o CLI manda o instante junto da mensagem), mostra a contagem
regressiva e retoma a **mesma conversa** quando a hora chega — o livro já lido continua no
contexto. Ctrl+C cancela a espera; o que já foi gerado fica salvo de qualquer forma.

Quanto o aplicativo se dispõe a esperar está em `PoliticaDeLimiteDeUso`, e depende de haver
alguém esperando na frente do console:

- **Processar um sistema** usa a política `ProcessamentoLongo`: espera **quantas
  janelas forem necessárias, pelo tempo que for** — inclusive a semanal, que só libera dias
  depois — e retoma sozinho. Não há o que perguntar ao usuário: o consumo sai de uma cota que
  se renova sozinha, e devolver o controle jogaria fora o contexto da conversa, fazendo a
  próxima execução pagar de novo pela leitura do livro. É só deixar o aplicativo aberto.
- **Conversar sobre um personagem** (criar, continuar, evoluir) segue a política padrão — até 3 esperas de no máximo 6
  horas. Ali o usuário está na conversa, e prendê-lo por dias não faria sentido.

Quando o Claude Code não informa a hora da liberação, a espera é às cegas e vai dobrando a
cada tentativa frustrada (15min, 30min, 1h...) até o teto da política, para não ficar batendo
no CLI de 15 em 15 minutos só para ouvir de novo que não há cota.

## Os dois arquivos da ficha

Além dos arquivos de regras, o Configurador é obrigado a gerar dois arquivos de nome fixo,
que são a ponte entre a conversa e o PDF final:

- `Sistemas/<Sistema>/Ficha-Mapeamento.md` — tabela ligando cada campo preenchível do PDF ao
  dado do personagem que vai nele, com formato e fórmula de cálculo quando houver.
- `Sistemas/<Sistema>/Ficha-ModeloEmTexto.md` — a ficha redesenhada em arte de texto (ASCII,
  até 78 colunas), com um marcador `{{NomeDoCampo}}` em cada lugar preenchível.

Antes de gerar o PDF, o Dungeon Master preenche esse desenho com os dados do personagem e o
mostra na conversa, para o usuário conferir e confirmar. Assim o usuário vê a ficha como ela
vai ficar sem precisar abrir o PDF, e o mapeamento fica registrado em vez de ser redescoberto
a cada criação de personagem.

### O PDF preenchido pede para ser redesenhado

A ficha salva em `Output/Personagens/` sai com `/NeedAppearances` marcado, que manda o leitor de
PDF desenhar cada campo a partir do valor e do `/DA` do próprio formulário.

Sem isso, o que aparece é o desenho que o PdfSharp gera ao gravar o campo — e ele põe o valor
inteiro num único operador de texto, quebras de linha incluídas, que dentro de uma string de PDF
não quebram nada. Num campo de várias linhas (traços de personalidade, ideais, história) o
resultado é tudo espremido numa linha só, cortada na borda do quadro, na cor errada. Era por
isso que esses campos só ficavam certos depois de alguém clicar neles e editá-los: o clique faz
o leitor refazer o desenho — que é exatamente o que a marca pede que ele faça ao abrir o
arquivo.

## Privacidade e segurança

O aplicativo roda inteiro na máquina de quem o usa. Não há servidor do MainForge, não há conta
do MainForge e não há telemetria: as únicas conexões que saem daqui são as que o Claude Code faz
com a Anthropic, com a assinatura de quem está usando.

**Credenciais.** O MainForge não pede, não guarda, não lê e não copia credencial nenhuma. O login
é do Claude Code e fica onde ele o guarda, fora do projeto — por isso ele não viaja no `.zip` e
não vai junto se você levar a pasta para outra máquina. O menu Ambiente abre uma janela do
próprio Claude Code para o `/login`, em vez de mostrar um formulário de usuário e senha: pedir a
senha da sua conta a um programa que não tem por que vê-la é a forma de um golpe de phishing, não
de um login legítimo. Nenhuma versão do aplicativo guarda chave de API — a que existia foi
removida quando ele passou a usar o Claude Code.

**Seus arquivos.** Livros (`Input/`), fichas em branco (`Templates/`), o texto extraído
(`_texto/`), os dossiês dos personagens (`Personagens/`) e o que é gerado (`Output/`, inclusive
os pacotes exportados) ficam só no seu disco, e estão todos no `.gitignore` — não vão para o Git
nem por acidente. Os agentes rodam confinados à pasta do
projeto, e toda escrita passa pelo servidor MCP em C#, onde `ResolverDentroDe` rejeita qualquer
caminho que escape do diretório permitido.

**O que os agentes não alcançam.** `Bash`, `PowerShell`, `Write`, `Edit`, `WebFetch`, `WebSearch`
e a leitura de `.claude/` e do código-fonte estão negados a todos eles — cada um é uma saída pela
qual um agente contornaria as demais restrições. O Dungeon Master ainda perde `Input/` e
`Templates/`, e o Configurador perde `Output/`. Isso é verificado por testes automatizados, não
só escrito aqui.

**Instalação de programas.** O aplicativo nunca instala nada sozinho: ele mostra o comando exato,
pergunta, e só então roda — e não instala o Claude Code por você, porque isso significaria baixar
e executar um script da internet em seu nome.

**A base de conhecimento é sua e fica na sua máquina.** `Sistemas/` guarda as regras destiladas
dos livros que você importou; se esses livros são comerciais, o conteúdo é deles. Por isso o
`.gitignore` mantém as bases fora do Git — a única versionada é a do `SistemaTeste`, que é
fictício. Cada pessoa gera a sua a partir dos próprios livros, o que também é o motivo de o
aplicativo ser distribuído sem base nenhuma pronta.

## Licença

[MIT](LICENSE) — use, modifique e redistribua à vontade, inclusive comercialmente, mantendo o
aviso de copyright. É a mesma licença do PDFsharp, e compatível com a Apache-2.0 do PdfPig.

A licença cobre **o código deste repositório**. Ela não alcança os livros de RPG que você
importar nem as bases geradas a partir deles: esse conteúdo continua sendo de quem o publicou, e
é por isso que ele nunca entra no Git (veja [Privacidade e segurança](#privacidade-e-segurança)).

## Créditos

- [markitdown](https://github.com/microsoft/markitdown) — Microsoft, licença MIT. Converte os
  livros em PDF para Markdown antes do processamento. É uma dependência **externa e opcional**:
  o aplicativo o executa se ele estiver instalado na máquina, e não redistribui nem embute
  código dele.
- [PdfPig](https://github.com/UglyToad/PdfPig) — licença Apache-2.0. Extrai o texto dos PDFs
  quando o markitdown não está disponível.
- [PDFsharp](https://www.pdfsharp.net/) — licença MIT. Lê e preenche os campos AcroForm das
  fichas.
- [Claude Code](https://claude.com/product/claude-code) — Anthropic. Executa os agentes com a
  assinatura do usuário.

## Limitações desta abordagem

- **Cota, não dinheiro.** O consumo sai da assinatura do Claude Code (janelas de 5 horas e
  semanal), não de créditos de API. Processar um livro grande pode esgotar a janela e não há
  como "pagar mais" para continuar — só esperar, que é o que o aplicativo faz sozinho.
- **Não distribuível como está.** Cada pessoa que rodar o aplicativo precisa da própria
  instalação do Claude Code, autenticada com a própria assinatura.
- **Menos controle fino.** `max_tokens`, formato exato do prompt e política de retentativa
  são do Claude Code. Em troca vêm compactação automática de contexto e um loop de agente
  pronto.
- **Configuração do usuário vaza para a sessão.** `--strict-mcp-config` impede que servidores
  MCP globais entrem, mas hooks e settings pessoais do Claude Code ainda se aplicam.

## Status

Funcionando: a execução dos agentes pelo Claude Code, o servidor MCP, o guardrail de
permissões por agente (verificado com o Dungeon Master tendo `Input/` negado de fato), a
importação de sistemas, o gerenciamento de personagens (com retomada e evolução), os pacotes de
sistema e a interface em console, com 220 testes automatizados. O Configurador
foi validado ponta a ponta gerando `Sistemas/SistemaTeste/`.

Falta: rodar a validação ponta a ponta completa incluindo o Dungeon Master
(`dotnet run --project tools/ValidacaoPontaAPonta`), testar com um livro de RPG real e
construir a interface gráfica em WPF.

> Se o build ou `dotnet sln add`/`dotnet restore` falhar de forma estranha nesta máquina,
> verifique a variável de ambiente `MSBuildSDKsPath` — se ela estiver fixada em um SDK antigo
> (ex.: `.../sdk/2.1.202/Sdks`), remova-a das variáveis de ambiente do Windows.

> PDFs precisam estar marcados como binários no Git (`.gitattributes`): com
> `core.autocrlf=true`, a conversão de fim de linha corrompe os offsets internos do arquivo.
