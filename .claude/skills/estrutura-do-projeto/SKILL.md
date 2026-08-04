---
name: estrutura-do-projeto
description: Mapa obrigatório da estrutura do MainForge (MainForge) — o que existe em cada pasta, quais dependências entre projetos são permitidas e onde cada tipo de arquivo novo deve nascer. Use SEMPRE antes de ler, navegar, criar, mover ou renomear qualquer coisa na estrutura do projeto: criar arquivo, classe, pasta, projeto .csproj, teste, ferramenta MCP, fluxo da CLI, agente ou sistema de RPG; decidir "onde isso mora?"; ou explicar a organização do repositório. Também vale para leitura: consulte antes de sair procurando arquivo por arquivo.
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
As pastas de topo com nome em inglês e inicial maiúscula (`Agents/`, `Systems/`, `Templates/`,
`Knowledge/`, `Output/`) são **dados**, resolvidos exclusivamente por
[CaminhosDoProjeto](src/MainForge.Core/CaminhosDoProjeto.cs) — nunca monte esses caminhos na mão.

## Mapa da raiz

| Caminho | O que é | Quem escreve | Quem lê |
| --- | --- | --- | --- |
| `src/` | os sete projetos C# | pessoas | pessoas |
| `tests/MainForge.Tests` | testes xunit de tudo em `src/` | pessoas | `dotnet test` |
| `tools/ValidacaoPontaAPonta` | harness manual do fluxo completo, **fora da .sln** de propósito (gasta cota real) | pessoas | `dotnet run` manual |
| `Agents/` | prompt de sistema de cada agente, em Markdown (`Configurador.md`, `DungeonMaster.md`) | pessoas | `DefinicaoDeAgente.CarregarPromptDeSistema` |
| `Systems/<Sistema>/` | livros oficiais em PDF, um subdiretório por sistema | `ImportadorDeSistema` | agente Configurador (`Read`) |
| `Templates/<Sistema>/` | ficha de personagem em PDF editável (AcroForm) | `ImportadorDeSistema` | Configurador e `PreenchedorDeFicha` |
| `Knowledge/<Sistema>/` | base de conhecimento em Markdown + `index.md` por nível + `_estado-do-processamento.json` | só o MCP (`EscritorDeConhecimento`) | agente Dungeon Master |
| `Output/Personagens/` | fichas finais preenchidas | `PreenchedorDeFicha` | o usuário |
| `.claude/` | configuração do Claude Code **de quem desenvolve o projeto** | pessoas | esta sessão |

`Systems/`, `Templates/`, `Knowledge/` e `Output/` têm um `README.md` explicando a convenção
daquela pasta — se você criar uma pasta de topo nova, ela também precisa de um.

## Os projetos em `src/`

Dependência só desce nesta lista; inverter é sinal de que a classe está no projeto errado.

```
MainForge.Core        modelos de domínio (SistemaRpg) e CaminhosDoProjeto. Não referencia ninguém.
  ├─ MainForge.ClaudeCode   localiza e executa o `claude` headless, traduz o stream-json em
  │                         EventoDeAgente, reconhece cota esgotada (LimiteDeUso) e a política
  │                         de espera (PoliticaDeLimiteDeUso). Nada de RPG aqui dentro.
  ├─ MainForge.Tools        o que só o C# faz: AcroForm com PdfSharp, escrita em Knowledge/,
  │                         IndiceDeConhecimento, EstadoDoProcessamento, ImportadorDeSistema.
  │                         Sem dependência de agente nem de interface.
  │    └─ MainForge.Mcp     servidor MCP stdio (executável próprio) que expõe MainForge.Tools
  │                         ao Claude Code. Só adapta: a regra mora em Tools.
  └─ MainForge.Agents       DefinicaoDeAgente (prompt + permissões) e SessaoDeAgente (a conversa).
       ├─ MainForge.Cli     interface em console — a que está em uso.
       └─ MainForge.App     WPF, ainda um shell vazio.
```

`MainForge.Cli` e `MainForge.App` referenciam `MainForge.Mcp` **só para o executável dele cair
na mesma pasta de saída** — `ConfiguracaoDoServidorMcp` o procura lá. Não chame classes de
`MainForge.Mcp` a partir da interface.

TFMs: `net10.0` na maioria; `net10.0-windows` em `Cli`, `Tests` e `ValidacaoPontaAPonta` (o
resolvedor de fontes do PdfSharp lê `C:\Windows\Fonts`); `net10.0-windows10.0.19041.0` no WPF.

## Onde nasce cada coisa nova

| Vou criar | Vai em | E também |
| --- | --- | --- |
| regra de negócio que toca disco | `src/MainForge.Tools/` | um `*Testes.cs` em `tests/MainForge.Tests/` |
| ferramenta nova para o agente | a lógica em `MainForge.Tools`, o schema em [CatalogoDeFerramentas.cs](src/MainForge.Mcp/CatalogoDeFerramentas.cs), o despacho em [ServidorMcp.cs](src/MainForge.Mcp/ServidorMcp.cs) | conceder em `DefinicaoDeAgente.FerramentasMcpPermitidas` **e** citar no `Agents/<Agente>.md` — ferramenta não anunciada no prompt não é usada |
| opção nova de menu | um `FluxoDeXxx.cs` em `src/MainForge.Cli/`, ligado no `switch` de [Program.cs](src/MainForge.Cli/Program.cs) | texto de UI sempre por `ConsoleUi`; o estado da janela do console (maximizar, tamanho) fica em `JanelaDoConsole`, o único ponto com P/Invoke |
| prompt/instrução de agente | `Agents/<Agente>.md` (nunca embutido em C#) | se for um agente novo, um `static readonly DefinicaoDeAgente` em [DefinicaoDeAgente.cs](src/MainForge.Agents/DefinicaoDeAgente.cs) |
| conceito de domínio puro | `src/MainForge.Core/` | só se não depender de PDF, de agente nem de interface |
| algo sobre executar o Claude Code | `src/MainForge.ClaudeCode/` | mantenha o projeto ignorante de RPG |
| sistema de RPG novo | `Systems/<Sistema>/` + `Templates/<Sistema>/` pela opção 2 do menu | **não exige mexer em código** |
| arquivo de conhecimento | `Knowledge/<Sistema>/` pelo agente | nunca escreva ali na mão |

Projeto `.csproj` novo só quando a responsabilidade não couber em nenhum dos sete — e aí ele
entra em `MainForge.sln` (exceto harness manual, que fica em `tools/` fora da solução).

## Regras que a estrutura impõe

1. **Idioma.** Solução, projetos e namespaces em inglês (padrão .NET). Todo o resto — classes,
   métodos, variáveis, comentários, textos de UI — em **português (pt-BR)**, com acento.
2. **Caminho sempre por `CaminhosDoProjeto`.** É o único ponto onde "não sair da raiz do
   projeto" é aplicado; qualquer caminho vindo do modelo passa por `ResolverDentroDe`.
3. **Escrita do agente só pelo MCP.** As ferramentas `Write`/`Edit`/`Bash` são negadas a todo
   agente em `DefinicaoDeAgente.NegacoesComuns`. Mexer nessa lista é mexer no guardrail —
   `DefinicaoDeAgenteTestes` trava as negações críticas.
4. **`index.md` é derivado, nunca escrito à mão** (nem pelo agente, nem por você): quem o gera
   é `IndiceDeConhecimento`, a cada gravação.
5. **`.claude/` não é para os agentes do aplicativo.** Eles rodam com a raiz do projeto como
   diretório de trabalho, então `Read(.claude/**)` e `Skill` estão entre as negações comuns:
   esta skill descreve o código do aplicativo, que não interessa a um agente de RPG.
6. **PDF é binário no Git** (`.gitattributes`): com `core.autocrlf=true` a conversão de fim de
   linha corrompe os offsets internos do arquivo.
7. **`Output/` é do usuário.** Nada do código lê de lá para tomar decisão, e o Configurador tem
   `Read(Output/**)` negado.

## Como navegar (em vez de varrer)

- "Onde fica a regra X?" → `README.md` da raiz tem a visão geral; cada classe tem um
  `<summary>` que explica **por que** ela existe, não só o que faz. Leia o summary antes do corpo.
- "O que o agente pode fazer?" → `Agents/<Agente>.md` (o que ele sabe) e `DefinicaoDeAgente`
  (o que ele consegue). Os dois precisam concordar.
- "Como o conhecimento está organizado?" → `Knowledge/index.md` e o `index.md` de cada pasta.
  A estrutura interna de `Knowledge/<Sistema>/` é decidida pelo agente conforme o sistema de
  RPG — **não há esqueleto fixo** e o código não deve assumir um.
- "Isso já tem teste?" → `tests/MainForge.Tests/<Classe>Testes.cs`, mesmo nome da classe.

## Antes de criar um arquivo, confira

- [ ] É código? Então está sob `src/`, `tests/` ou `tools/` — e no projeto certo da cadeia.
- [ ] Alguém acima na cadeia de dependências vai precisar dele? Se sim, ele está baixo demais.
- [ ] Toca disco? Usa `CaminhosDoProjeto`, não `Path.Combine` a partir da raiz.
- [ ] Nome e conteúdo em pt-BR (menos namespace/projeto).
- [ ] Se mudou permissão de agente, o `Agents/*.md` correspondente foi atualizado junto.
- [ ] Se mudou a estrutura de verdade, o `README.md` da raiz e **esta skill** foram atualizados.
