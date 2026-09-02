---
name: estrutura-do-projeto
description: Mapa obrigatório da estrutura do MainForge — o que existe em cada pasta, quais dependências entre projetos são permitidas e onde cada tipo de arquivo novo deve nascer. Use SEMPRE antes de ler, navegar, criar, mover ou renomear qualquer coisa na estrutura do projeto: criar arquivo, classe, pasta, projeto .csproj, teste, ferramenta MCP, fluxo da CLI, agente ou sistema de RPG; decidir "onde isso mora?"; ou explicar a organização do repositório. Também vale para leitura: consulte antes de sair procurando arquivo por arquivo.
---

# Estrutura do MainForge

Aplicativo desktop em C#/.NET 10 que lê livros de RPG em PDF e conduz a criação de fichas de
personagem. Os agentes rodam no **Claude Code** instalado na máquina do usuário — não há chave
de API nem credencial no aplicativo.

Esta skill é o mapa. Consulte-a **antes** de criar ou procurar qualquer coisa: o projeto tem
dois eixos de organização que se cruzam (código em `src/`, dados em pastas de topo) e colocar
um arquivo do lado errado quebra guardrail de segurança, não só a arrumação.

## Regra de ouro

Nada de código fora de `src/`, `tests/` e `tools/`. Nada de dado de usuário dentro de `src/`.
As pastas de topo com inicial maiúscula (`Agents/`, `Input/`, `Templates/`, `Sistemas/`,
`Personagens/`, `Output/`) são **dados**, resolvidos exclusivamente por
[CaminhosDoProjeto](src/MainForge.Core/CaminhosDoProjeto.cs) — nunca monte esses caminhos na mão.

O nome da pasta e o da propriedade que a resolve não coincidem em dois casos, e é de propósito:
`Input/` é `CaminhosDoProjeto.Entrada` (os livros que o usuário fornece) e `Sistemas/` é
`CaminhosDoProjeto.Conhecimento` (o que o Configurador destilou deles). Essa correspondência é
escrita uma vez só, no construtor de `CaminhosDoProjeto`.

## Mapa da raiz

| Caminho | O que é | Quem escreve | Quem lê |
| --- | --- | --- | --- |
| `src/` | os seis projetos C# | pessoas | pessoas |
| `tests/MainForge.Tests` | testes xunit de tudo em `src/` | pessoas | `dotnet test` |
| `tools/ValidacaoPontaAPonta` | harness manual do fluxo completo, **fora da .sln** de propósito (gasta cota real) | pessoas | `dotnet run` manual |
| `tools/gerar-icone.ps1` | o desenho do ícone do executável (pixel art, 16x16) e a gravação do `.ico` | pessoas | `./tools/gerar-icone.ps1` |
| `src/MainForge.Cli/mainforge.ico` | resultado do script acima, apontado por `<ApplicationIcon>` — **derivado, não fonte** | `gerar-icone.ps1` | o Windows |
| `Agents/` | prompt de sistema de cada agente, em Markdown (`Configurador.md`, `DungeonMaster.md`) | pessoas | `DefinicaoDeAgente.CarregarPromptDeSistema` |
| `Input/<Sistema>/<fonte>/` | livros oficiais em PDF, um subdiretório por sistema e, dentro, um por fonte | `ImportadorDeSistema` | agente Configurador (`Read`) |
| `Input/<Sistema>/<fonte>/_texto/` | os mesmos livros em Markdown — é o que o Configurador lê de verdade | `ConversorDeLivros` | Configurador (`Read`, `procurar_no_texto_dos_livros`) |
| `Templates/<Sistema>/` | ficha de personagem em PDF editável (AcroForm) | `ImportadorDeSistema` | Configurador e `PreenchedorDeFicha` |
| `Sistemas/<Sistema>/<fonte>/` | base de conhecimento em Markdown + `index.md` por nível + `_estado-do-processamento.json` | só o MCP (`EscritorDeConhecimento`) | agente Dungeon Master |
| `Output/Personagens/<Id>-Magias.pdf` | a folha extra com as magias por extenso, quando a ficha do sistema não as comporta | `FolhaDeMagias` | o usuário |
| `Personagens/<Sistema>/<Id>/` | dossiê do personagem: `personagem.json` (situação, fontes da mesa, sessão) + `ficha.md` (estado dele em texto) | `RepositorioDePersonagens` — o C# grava o JSON, o agente grava o `.md` pelo MCP | a CLI e o Dungeon Master |
| `Personagens/<Sistema>/<Id>/Fichas/` | histórico: uma cópia do PDF por nível concluído (`nivel-03.pdf`) | `FichasDoPersonagem` | o usuário, pela CLI |
| `Output/Personagens/` | **uma** ficha por personagem — a atual, em `<Id>.pdf` | `PreenchedorDeFicha`, e por ele `GeradorDeFichaEmPdf` (que a refaz quando ela sumiu) e `ImportadorDePersonagem` | o usuário |
| `Output/Pacotes/` | sistemas exportados (`.mainforge.zip`) e personagens exportados com o histórico de níveis (`.mainforge-personagem.zip`) | `PacoteDeSistema`, `PacoteDePersonagem` | o usuário |
| `_preferencias.json` | escolhas de gasto de cota desta instalação (perfil de execução, teto em dólares) | `PreferenciasDoUsuario`, pelo menu Ambiente | `ContextoDoAplicativo` na abertura |
| `.claude/` | configuração do Claude Code **de quem desenvolve o projeto** | pessoas | esta sessão |
| `.github/workflows/` | fluxo que publica o binário como release | pessoas | GitHub Actions |
| `publicar.ps1` | empacota o aplicativo (executável único, self-contained) em `publicado/` | pessoas | `./publicar.ps1` |

`Input/`, `Templates/`, `Sistemas/`, `Personagens/` e `Output/` têm um `README.md` explicando a
convenção daquela pasta — se você criar uma pasta de topo nova, ela também precisa de um.

`Personagens/` e `Output/Personagens/` são coisas diferentes, e a diferença é o item 7 das
regras: `Output/` é entrega, e o código não tira decisão de lá; `Personagens/` é dado de
trabalho — a CLI lê dali o que continuar, e é a única pasta de resultado que o Dungeon Master
alcança.

## O segundo nível: fonte

Dentro de `Input/<Sistema>/` e de `Sistemas/<Sistema>/` há **uma pasta por fonte**: `base/`
é o jogo base (nome fixo, em `FonteDoSistema.IdDaBase`) e cada outra é uma expansão, com o nome
que o usuário deu. Os dois lados usam os mesmos nomes: o conhecimento destilado dos livros de
uma fonte mora na pasta de mesmo nome.

Isto é guardrail, não arrumação. Na criação de personagem o usuário escolhe quais expansões a
mesa usa, e `DefinicaoDeAgente.DungeonMasterLimitadoA` transforma as recusadas em
`Read(Sistemas/<Sistema>/<fonte>/**)` negado. Conteúdo na pasta errada vira regra que vale numa
mesa que não a escolheu.

O `_texto/` de cada fonte segue a mesma regra, e por isso fica **dentro** da fonte: é o livro
daquela fonte, só que em Markdown. O underscore marca derivado (como o
`_estado-do-processamento.json`) e mantém a pasta fora de qualquer `Glob(*.pdf)`. Quem escreve
ali é o `ConversorDeLivros`, nunca o agente; apagar um `.md` de lá manda convertê-lo de novo no
próximo processamento.

Três exceções ficam na **raiz** de `Sistemas/<Sistema>/`: `Ficha-Mapeamento.md` e
`Ficha-ModeloEmTexto.md` (listados em `SistemaRpg.ArquivosDaFicha`) e `Ficha-Validacao.json`
(`SistemaRpg.NomeDaValidacaoDaFicha`). A ficha em PDF é do sistema inteiro e precisa valer com
qualquer expansão selecionada.

O terceiro está **fora** de `ArquivosDaFicha` de propósito: aquela lista é a dos arquivos
obrigatórios, e é ela que `PacoteDeSistema.Exportar` exige. A validação é desejável e não
indispensável — somá-la à lista tornaria inexportável toda base mapeada antes de ela existir. Ela
também é o único arquivo da raiz que não é Markdown: os outros dois são para o agente ler, e este
é para o C# executar (`RegrasDaFicha` -> `ValidacaoDePersonagem`).

`MigracaoDeFontes` leva um sistema do layout antigo (tudo solto na pasta do sistema) para este,
movendo arquivo e reapontando o registro de progresso — nunca reprocessando.

## Os projetos em `src/`

Dependência só desce nesta lista; inverter é sinal de que a classe está no projeto errado.

```
MainForge.Core        modelos de domínio (SistemaRpg, EscopoDaSessao, ConsumoDeTokens,
  │                   PerfilDeExecucao) e CaminhosDoProjeto. Não referencia ninguém.
  ├─ MainForge.ClaudeCode   localiza e executa o `claude` headless, traduz o stream-json em
  │                         EventoDeAgente, reconhece cota esgotada (LimiteDeUso) e a política
  │                         de espera (PoliticaDeLimiteDeUso), e resolve modelo + esforço por
  │                         agente (AjusteDeExecucao). Nada de RPG aqui dentro.
  ├─ MainForge.Tools        o que só o C# faz: AcroForm com PdfSharp nos dois sentidos
  │                         (PreenchedorDeFicha escreve, LeitorDeFichaPreenchida lê uma ficha
  │                         já preenchida e ImportadorDePersonagem a vira dossiê), a ficha do
  │                         dossiê refeita sem agente (GeradorDeFichaEmPdf no PDF, FichaEmTexto
  │                         no desenho do Ficha-ModeloEmTexto.md), a validação do personagem
  │                         contra as regras do sistema (RegrasDaFicha guarda o
  │                         Ficha-Validacao.json, ValidacaoDePersonagem o executa), a regra do
  │                         nome das magias (NomesDeMagia) e a folha extra delas (FolhaDeMagias),
  │                         escrita em Sistemas/, IndiceDeConhecimento, EstadoDoProcessamento,
  │                         ImportadorDeSistema, RepositorioDePersonagens, FichasDoPersonagem,
  │                         PacoteDeSistema, PacoteDePersonagem, PreferenciasDoUsuario,
  │                         as buscas (BuscaEmTexto e as duas que a usam) e a conversão dos
  │                         livros para texto (ConversorDeLivros -> markitdown,
  │                         ExtratorDeTextoDePdf -> PdfPig, LimpezaDoTextoDoLivro).
  │                         Sem dependência de agente nem de interface.
  │    └─ MainForge.Mcp     servidor MCP stdio (executável próprio) que expõe MainForge.Tools
  │                         ao Claude Code. Só adapta: a regra mora em Tools.
  └─ MainForge.Agents       DefinicaoDeAgente (prompt + permissões) e SessaoDeAgente (a conversa).
       └─ MainForge.Cli     interface em console — a interface do produto, por escolha.
```

São seis projetos, e o console é a única interface: **não haverá versão gráfica**. Havia um
`MainForge.App` em WPF, vazio, esperando esse dia; ele foi removido da solução. Interface nova
não nasce aqui sem essa decisão ser tomada de novo.

`MainForge.Cli` referencia `MainForge.Mcp` por dois motivos: o executável dele
cai na mesma pasta de saída (`ConfiguracaoDoServidorMcp` o procura lá) e o próprio aplicativo
sabe ser o servidor, quando lançado com `--mcp <raiz>` — é o que permite distribuir tudo num
executável só. Essa passagem mora inteira em [ModoServidorMcp.cs](src/MainForge.Cli/ModoServidorMcp.cs);
fora dela, não chame classes de `MainForge.Mcp` a partir da interface.

`Agents/*.md` são copiados para a pasta de saída de `MainForge.Cli` (item `Content` no `.csproj`):
sem eles ao lado do executável, o aplicativo baixado não tem prompt para mandar ao agente. O
arquivo de verdade continua sendo o de `Agents/` — nada de prompt embutido em C#.

TFMs: `net10.0` na maioria; `net10.0-windows` em `Cli`, `Tests` e `ValidacaoPontaAPonta` (o
resolvedor de fontes do PdfSharp lê `C:\Windows\Fonts`).

## Onde nasce cada coisa nova

| Vou criar | Vai em | E também |
| --- | --- | --- |
| regra de negócio que toca disco | `src/MainForge.Tools/` | um `*Testes.cs` em `tests/MainForge.Tests/` |
| ferramenta nova para o agente | a lógica em `MainForge.Tools`, o schema em [CatalogoDeFerramentas.cs](src/MainForge.Mcp/CatalogoDeFerramentas.cs), o despacho em [ServidorMcp.cs](src/MainForge.Mcp/ServidorMcp.cs) | conceder em `DefinicaoDeAgente.FerramentasMcpPermitidas`, acrescentar à lista de `ServidorMcpTestes` **e** citar no `Agents/<Agente>.md` — ferramenta não anunciada no prompt não é usada. Se ela lê `Sistemas/` ou grava personagem, precisa conferir o `EscopoDaSessao` (veja a regra 9) |
| decisão sobre gasto de cota | modelo e esforço em [AjusteDeExecucao.cs](src/MainForge.ClaudeCode/AjusteDeExecucao.cs); flag nova do `claude` em `ProcessoDoClaudeCode.MontarInicio` | trave em `LinhaDeComandoDoClaudeCodeTestes` — nada ali quebra o build ao sumir, só volta a custar caro em silêncio |
| opção nova de menu | um `FluxoDeXxx.cs` em `src/MainForge.Cli/`, ligado ao `MenuDeXxx.cs` do assunto (Sistemas, Personagens, Ambiente) | o menu principal tem três portas e não ganha uma quarta sem motivo forte; texto de UI sempre por `ConsoleUi`; o estado da janela do console fica em `JanelaDoConsole`, o único ponto com P/Invoke |
| mudança no ícone do aplicativo | o mapa de pixels em [gerar-icone.ps1](tools/gerar-icone.ps1), nunca o `.ico` | rode o script (`-Amostra` grava um PNG para conferir no olho) e comite o `.ico` junto — editar o binário à mão faz o desenho e o resultado divergirem para sempre |
| prompt/instrução de agente | `Agents/<Agente>.md` (nunca embutido em C#) | se for um agente novo, um `static readonly DefinicaoDeAgente` em [DefinicaoDeAgente.cs](src/MainForge.Agents/DefinicaoDeAgente.cs) |
| conceito de domínio puro | `src/MainForge.Core/` | só se não depender de PDF, de agente nem de interface |
| algo sobre executar o Claude Code | `src/MainForge.ClaudeCode/` | mantenha o projeto ignorante de RPG |
| sistema de RPG novo | `Input/<Sistema>/base/` + `Templates/<Sistema>/` por Sistemas > Novo sistema | **não exige mexer em código** |
| expansão/compêndio | `Input/<Sistema>/<Expansao>/` por Sistemas > Adicionar livros | **não exige mexer em código** |
| arquivo de conhecimento | `Sistemas/<Sistema>/<fonte>/` pelo agente | nunca escreva ali na mão |
| dossiê de personagem | `Personagens/<Sistema>/<Id>/` por `RepositorioDePersonagens` | nunca escreva ali na mão: o JSON é do C#, o `ficha.md` é do agente pelo MCP |

Projeto `.csproj` novo só quando a responsabilidade não couber em nenhum dos seis — e aí ele
entra em `MainForge.sln` (exceto harness manual, que fica em `tools/` fora da solução).

## Regras que a estrutura impõe

1. **Idioma.** Solução, projetos e namespaces em inglês (padrão .NET). Todo o resto — classes,
   métodos, variáveis, comentários, textos de UI — em **português (pt-BR)**, com acento.
2. **Caminho sempre por `CaminhosDoProjeto`.** É o único ponto onde "não sair da raiz do
   projeto" é aplicado; qualquer caminho vindo do modelo passa por `ResolverDentroDe`. A raiz é
   descoberta pela presença de `Agents/` com prompts — no repositório e também numa instalação
   baixada, onde as outras pastas nascem vazias em `GarantirEstrutura`.
3. **Escrita do agente só pelo MCP.** O conjunto de ferramentas embutidas que existe na sessão é
   declarado em `--tools` a partir de `DefinicaoDeAgente.FerramentasNativasPermitidas` — hoje só
   `Read` e `Glob`. O que não está lá não é oferecido ao modelo, e é por isso que ferramenta nova
   do Claude Code não entra sozinha. `NegacoesComuns` continua negando por nome como segunda
   linha (negar `Bash` sem negar `PowerShell` já deixou um agente listar pastas à vontade) e é o
   único jeito de negar **por caminho**. `DefinicaoDeAgenteTestes` e
   `LinhaDeComandoDoClaudeCodeTestes` travam as duas camadas.
4. **`index.md` é derivado, nunca escrito à mão** (nem pelo agente, nem por você): quem o gera
   é `IndiceDeConhecimento`, a cada gravação.
5. **`.claude/` não é para os agentes do aplicativo.** Eles rodam com a raiz do projeto como
   diretório de trabalho, então `Read(.claude/**)` e `Skill` estão entre as negações comuns:
   esta skill descreve o código do aplicativo, que não interessa a um agente de RPG.
6. **PDF é binário no Git** (`.gitattributes`): com `core.autocrlf=true` a conversão de fim de
   linha corrompe os offsets internos do arquivo.
7. **`Output/` é do usuário.** Nada do código lê de lá para tomar decisão, e o Configurador tem
   `Read(Output/**)` negado. É **uma ficha por personagem**, com o nome decidido pelo aplicativo
   (`FichasDoPersonagem.NomeNaSaida`) e não pelo modelo: quem abre a pasta procura "a ficha do
   Thoradin", não escolhe entre quatro arquivos qual é o que vale hoje. O que a regeração
   substituiria é guardado antes em `Personagens/<Sistema>/<Id>/Fichas/`, um PDF por nível.

   **A exceção, e ela é uma só:** `GeradorDeFichaEmPdf` pergunta a `Output/` se o arquivo que o
   dossiê aponta ainda está lá. É um `File.Exists` — existência, nunca conteúdo. O dossiê continua
   sendo a verdade sobre o que a ficha tem dentro, e a resposta só decide entre refazer o arquivo
   e deixá-lo em paz. **E não se pergunta ao usuário**: "o PDF sumiu, quer que eu gere de novo?" é
   uma confirmação sem alternativa — ele veio ver a ficha, o arquivo que faltava é reconstituível
   de graça a partir do que já está gravado, e o que está lá nunca é sobrescrito. Ler valor de
   `Output/` seria outra coisa, e continua proibido: é cópia de saída, e o usuário pode ter editado
   o PDF à mão.
8. **Todo conteúdo mora numa fonte.** Em `Input/` e em `Sistemas/`, nada de conteúdo fica
   solto na raiz do sistema — a única exceção é `SistemaRpg.ArquivosDaFicha`. Código novo que
   monte caminho de sistema passa pela fonte (`DiretorioDaFonte`,
   `DiretorioConhecimentoDaFonte`), nunca por `Path.Combine(caminhos.Sistemas, sistema, ...)`.
9. **O que o agente pode tocar para no agente.** A escolha de expansões da mesa é aplicada como
   `Read(Sistemas/<Sistema>/<fonte>/**)` negado, e a identidade do personagem, só pelo prompt —
   nenhuma das duas alcança o servidor MCP, que roda em outro processo. Ferramenta MCP que
   percorra `Sistemas/` **ou grave um personagem** precisa conferir o `EscopoDaSessao` que chega
   pelo ambiente (`EscopoDaSessao.VariavelDeAmbiente`, posto no bloco `env` por
   `ConfiguracaoDoServidorMcp`) — sem isso ela é a porta lateral para o que a negação acabou de
   fechar. São quatro: `BuscaNoConhecimento` (fontes), `registrar_personagem`,
   `preencher_ficha_personagem` e `validar_personagem` (sistema e personagem — a primeira grava o
   estado *completo* do personagem, então o identificador errado apaga outro por inteiro).
   `BuscaNoConhecimentoTestes` e `EscopoDoPersonagemTestes` travam o comportamento.

   A mesma regra vale fora do MCP, para código que percorre `Sistemas/` a partir do dossiê:
   `FolhaDeMagias` procura a descrição de cada magia só nas fontes de `Personagem.Fontes`. Ele
   roda em C#, longe de qualquer negação do agente — e sem esse filtro entregaria ao jogador, na
   folha impressa, a magia do compêndio que a mesa dele dispensou.
10. **Caminho de dentro de um `.zip` é caminho vindo de fora.** A importação de pacote resolve
    cada entrada com `ResolverDentroDe` antes de extrair — um `.zip` pode carregar `../../` (o
    "zip slip") tanto quanto um caminho vindo do modelo. Vale para os dois pacotes
    (`PacoteDeSistema` e `PacoteDePersonagem`), e cada um tem teste disso.

    São **dois** pacotes de propósito: o de sistema é o resultado de ler os livros, igual para
    quem tem aqueles livros; o de personagem é de quem joga. Exportar um sistema não pode levar
    junto os personagens de quem o exportou. Caminho gravado dentro de um pacote de personagem é
    da instalação de origem — a importação reaponta o histórico para a pasta de destino, senão o
    personagem chega procurando as próprias fichas na máquina de onde saiu.

11. **Turno é mais caro que resultado.** Cada chamada de ferramenta é um turno, e todo turno
    reenvia a conversa inteira ao modelo. Numa sessão que já leu meio livro, evitar uma chamada
    vale mais que economizar no tamanho do que ela devolve — foi essa conta invertida que fazia
    as buscas devolverem só a linha achada e mandarem o agente ler o trecho depois. Ferramenta
    nova deve responder à pergunta inteira, não devolver o endereço da resposta.

    O corolário: o que o C# consegue ler de graça não deve virar chamada de ferramenta. O dossiê
    do personagem e o índice das fontes vão escritos na primeira mensagem por isso
    (`FluxoDePersonagem.PrimeiraMensagem`), e o texto dos livros é limpo antes de o agente pagar
    para lê-lo (`LimpezaDoTextoDoLivro`).

12. **Medir antes de otimizar.** `ConsumoDeTokens` acumula por sistema
    (`_estado-do-processamento.json`) e por personagem (`personagem.json`), e aparece em
    Ambiente > Consumo. Mudança que promete economizar cota precisa aparecer ali — o custo já foi
    descartado em silêncio uma vez, quando `total_cost_usd` era parseado e jogado fora.

13. **Caminho que uma pessoa digita entra; caminho que o modelo manda, não.** Os PDFs do usuário
    — os livros, a ficha em branco, a ficha preenchida que `ImportadorDePersonagem` importa —
    moram fora da raiz do projeto, e exigir `ResolverDentroDe` neles inviabilizaria a única forma
    de trazer conteúdo para cá. O que os torna seguros é a origem (uma pessoa no console) e o
    fato de serem **copiados para dentro** antes de qualquer uso; nenhum deles chega por
    ferramenta MCP, onde continua valendo a regra 2 sem exceção.

## Como navegar (em vez de varrer)

- "Onde fica a regra X?" → `README.md` da raiz tem a visão geral; cada classe tem um
  `<summary>` que explica **por que** ela existe, não só o que faz. Leia o summary antes do corpo.
- "O que o agente pode fazer?" → `Agents/<Agente>.md` (o que ele sabe) e `DefinicaoDeAgente`
  (o que ele consegue). Os dois precisam concordar.
- "Como o conhecimento está organizado?" → `Sistemas/index.md` e o `index.md` de cada pasta.
  A estrutura interna de **cada fonte** é decidida pelo agente conforme o sistema de RPG —
  **não há esqueleto fixo** e o código não deve assumir um. O nível da fonte, esse sim, é do
  código: `base/` e uma pasta por expansão, sempre.
- "Isso já tem teste?" → `tests/MainForge.Tests/<Classe>Testes.cs`, mesmo nome da classe.

## Antes de criar um arquivo, confira

- [ ] É código? Então está sob `src/`, `tests/` ou `tools/` — e no projeto certo da cadeia.
- [ ] Alguém acima na cadeia de dependências vai precisar dele? Se sim, ele está baixo demais.
- [ ] Toca disco? Usa `CaminhosDoProjeto`, não `Path.Combine` a partir da raiz.
- [ ] Nome e conteúdo em pt-BR (menos namespace/projeto).
- [ ] Se mudou permissão de agente, o `Agents/*.md` correspondente foi atualizado junto.
- [ ] Se mudou a estrutura de verdade, o `README.md` da raiz e **esta skill** foram atualizados.
