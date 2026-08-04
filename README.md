# Chatbot para Criação de Fichas de Personagens de RPG

Aplicativo desktop em C#/.NET que entende livros de RPG em PDF e conduz a criação de
personagens, terminando num PDF de ficha preenchido. Tudo roda localmente na máquina do
usuário.

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
    verificado, não deduzido.
  - Manipular AcroForm (listar e preencher os campos da ficha) e escrever em `Knowledge/`
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
   `Read(Knowledge/**)` *não* impede leituras fora de `Knowledge/` — só uma negação
   explícita bloqueia. Negação vence concessão.
3. **Confinamento em código.** Toda **escrita** passa pelo servidor MCP, onde
   `CaminhosDoProjeto.ResolverDentroDe` rejeita qualquer caminho que escape do diretório
   permitido. É a única camada que não depende de acertar uma lista de negação — e por isso
   é onde mora a regra que realmente importa.

| | Configurador | Dungeon Master |
| --- | --- | --- |
| Embutidas | `Read`, `Glob` | `Read`, `Glob` |
| MCP | `escrever_arquivo_conhecimento`, `descrever_pasta_de_conhecimento`, `registrar_plano_de_conhecimento`, `consultar_progresso`, `listar_campos_da_ficha` | `preencher_ficha_personagem` |
| Negações próprias | `Read(Output/**)` | `Read(Systems/**)`, `Read(Templates/**)` |

Negado para os dois, sempre: `Bash`, `Write`, `Edit`, `NotebookEdit`, `Task`, `WebFetch`,
`WebSearch`, `Grep`, e a leitura do código do próprio aplicativo. Cada um desses é uma saída
de emergência pela qual um agente contornaria todas as outras restrições.

## Estrutura da solução

```
MainForge.sln
├── src/
│   ├── MainForge.Core       -> modelos de domínio, CaminhosDoProjeto (raiz de tudo que toca disco)
│   ├── MainForge.ClaudeCode -> localiza e executa o Claude Code; traduz o stream-json em eventos;
│   │                           reconhece cota esgotada (LimiteDeUso) para poder esperar a janela
│   ├── MainForge.Tools      -> o que só o C# faz: AcroForm (PdfSharp), escrita em Knowledge/,
│   │                           os índices (IndiceDeConhecimento) e o progresso (EstadoDoProcessamento)
│   ├── MainForge.Mcp        -> servidor MCP stdio que expõe MainForge.Tools ao agente
│   ├── MainForge.Agents     -> DefinicaoDeAgente (prompt + permissões) e SessaoDeAgente
│   ├── MainForge.Cli        -> interface em console (a interface em uso hoje)
│   └── MainForge.App        -> aplicativo WPF (interface gráfica, ainda um shell vazio)
├── tests/MainForge.Tests
├── tools/ValidacaoPontaAPonta -> harness manual do fluxo completo (fora da solução), sobre o
│                                 sistema fictício "SistemaTeste"
├── Agents/               -> prompts dos agentes (Configurador.md, DungeonMaster.md)
├── Systems/              -> livros oficiais em PDF, um subdiretório por sistema
├── Templates/            -> fichas em PDF editável, um subdiretório por sistema
├── Knowledge/            -> base de conhecimento em Markdown, gerada pelo Configurador,
│                            com um index.md por nível e o registro de progresso do sistema
└── Output/Personagens/   -> fichas finais preenchidas
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
```

Menu do aplicativo:

1. **Ver sistemas** — o que já foi importado, o que já tem base de conhecimento e ficha, e o
   que ficou pela metade.
2. **Importar um sistema de RPG** — você informa o nome do sistema, os PDFs dos livros e a
   ficha de personagem editável; o programa valida (livro legível, ficha com campos
   preenchíveis) e copia para `Systems/<Sistema>/` e `Templates/<Sistema>/`.
3. **Adicionar livro a um sistema** — compêndios, expansões e suplementos de um sistema que já
   existe. O livro entra em `Systems/<Sistema>/` e o Configurador lê **só ele**, somando o
   conteúdo à base que já está pronta.
4. **Processar um sistema** (Agente Configurador) — lê os livros **e a ficha em branco** e
   gera `Knowledge/<Sistema>/*.md`. É a operação mais cara em tokens; pede confirmação. Se já
   houver progresso, pergunta se é para continuar de onde parou ou recomeçar do zero.
5. **Criar um personagem** (Agente Dungeon Master) — conversa livre até a ficha em PDF sair
   em `Output/Personagens/`. `/sair` encerra a conversa.
6. **Verificar o Claude Code** — mostra o executável, o modelo e as permissões de cada
   agente, e faz um turno de teste para confirmar que a assinatura está ativa.

Adicionar um sistema de RPG novo não exige mexer em código: basta a opção 2 seguida da 4
(ou copiar as pastas na mão para `Systems/` e `Templates/`).

O sistema `SistemaTeste` não aparece em nenhuma dessas telas: ele existe só para o harness de
validação, que o alcança pelo nome. Esconder é da interface, não do disco — quem abre o
aplicativo veria um sistema fictício ao lado dos de verdade sem ter como saber que não é para
usar.

## A base de conhecimento é indexada

Cada pasta de `Knowledge/` ganha um `index.md` com uma linha sobre cada arquivo e cada
subpasta dela, e `Knowledge/index.md` lista os sistemas. É por aí que o Dungeon Master navega:
lê o índice, decide o que interessa e abre só isso, em vez de varrer a base inteira para achar
uma regra.

Os índices são derivados, não escritos pelo agente — são regravados a cada gravação, e a
ferramenta de escrita recusa um `index.md` vindo do modelo. O que o Configurador fornece é o
`resumo` de cada arquivo e a descrição de cada pasta; o formato é do C#. Bases geradas por
versões anteriores do aplicativo são indexadas na primeira vez que o sistema é processado, com
descrições derivadas do próprio conteúdo — sem custo de tokens.

## Processamento retomável

O Configurador registra o plano de arquivos antes de começar, e cada gravação atualiza
`Knowledge/<Sistema>/_estado-do-processamento.json`. Uma execução interrompida — cota
esgotada, Ctrl+C, máquina desligada — deixa esse registro coerente com o que existe em disco,
e a execução seguinte pergunta o que falta em vez de reler o livro inteiro.

Três consequências práticas:

- Apagar um `.md` na mão basta para mandá-lo ser regerado: o disco é a verdade final, e o
  registro é reconciliado com ele antes de cada execução.
- Trocar um PDF em `Systems/<Sistema>/` (mesmo nome, conteúdo diferente) marca aquele livro
  como não lido de novo, porque tamanho e data de modificação mudaram.
- Uma base que já existia antes de tudo isso é **adotada**: a opção 1 do menu oferece gerar
  índice e registro a partir do que está em disco (de graça, sem agente). Os `.md` presentes
  entram como prontos e os livros, como ainda não lidos — que é a leitura honesta de uma base
  gerada pela metade. A execução seguinte então oferece "ler só os livros ainda não
  incorporados", em vez de recomeçar o sistema inteiro.

## Quando a cota da assinatura acaba

Cota esgotada não é tratada como erro. O aplicativo reconhece a mensagem do Claude Code,
descobre quando a janela vira (o CLI manda o instante junto da mensagem), mostra a contagem
regressiva e retoma a **mesma conversa** quando a hora chega — o livro já lido continua no
contexto. Ctrl+C cancela a espera; o que já foi gerado fica salvo de qualquer forma.

Os limites dessa espera estão em `PoliticaDeLimiteDeUso`: por padrão, até 3 esperas por turno
e no máximo 6 horas cada uma (uma janela curta inteira, com folga). Uma cota semanal esgotada
estoura esse teto de propósito — aí o aplicativo avisa e devolve o controle em vez de dormir
por dias.

## Os dois arquivos da ficha

Além dos arquivos de regras, o Configurador é obrigado a gerar dois arquivos de nome fixo,
que são a ponte entre a conversa e o PDF final:

- `Knowledge/<Sistema>/Ficha-Mapeamento.md` — tabela ligando cada campo preenchível do PDF ao
  dado do personagem que vai nele, com formato e fórmula de cálculo quando houver.
- `Knowledge/<Sistema>/Ficha-ModeloEmTexto.md` — a ficha redesenhada em arte de texto (ASCII,
  até 78 colunas), com um marcador `{{NomeDoCampo}}` em cada lugar preenchível.

Antes de gerar o PDF, o Dungeon Master preenche esse desenho com os dados do personagem e o
mostra na conversa, para o usuário conferir e confirmar. Assim o usuário vê a ficha como ela
vai ficar sem precisar abrir o PDF, e o mapeamento fica registrado em vez de ser redescoberto
a cada criação de personagem.

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
permissões por agente (verificado com o Dungeon Master tendo `Systems/` negado de fato), a
importação de sistemas e a interface em console, com 50 testes automatizados. O Configurador
foi validado ponta a ponta gerando `Knowledge/SistemaTeste/`.

Falta: rodar a validação ponta a ponta completa incluindo o Dungeon Master
(`dotnet run --project tools/ValidacaoPontaAPonta`), testar com um livro de RPG real e
construir a interface gráfica em WPF.

> Se o build ou `dotnet sln add`/`dotnet restore` falhar de forma estranha nesta máquina,
> verifique a variável de ambiente `MSBuildSDKsPath` — se ela estiver fixada em um SDK antigo
> (ex.: `.../sdk/2.1.202/Sdks`), remova-a das variáveis de ambiente do Windows.

> PDFs precisam estar marcados como binários no Git (`.gitattributes`): com
> `core.autocrlf=true`, a conversão de fim de linha corrompe os offsets internos do arquivo.
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

