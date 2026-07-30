# Chatbot para Criação de Fichas de Personagens de RPG

Aplicativo desktop em C#/.NET que usa a Claude API para entender livros de RPG em PDF e
conduzir a criação de personagens, terminando num PDF de ficha preenchido. Tudo roda
localmente na máquina do usuário; a única comunicação externa é com a Claude API.

## Decisões de arquitetura

- **Autenticação**: API key da Anthropic (variável de ambiente `ANTHROPIC_API_KEY`), via o
  SDK oficial `Anthropic` para .NET. Não existe hoje um Claude Agent SDK para C#, e não há
  mecanismo suportado para um app de terceiros autenticar usando a assinatura Claude
  Pro/Max de um usuário — por isso a licença aqui é billing por token com uma API key.
- **Ferramentas locais** (ler PDF, escrever Markdown, preencher ficha): expostas como
  *tools* (function calling) diretamente na Messages API, executadas em processo pelo
  próprio C#. Não há um servidor MCP — como tudo roda no mesmo processo local, o protocolo
  MCP não adiciona valor aqui (ele existe para expor ferramentas a clientes externos).
- **.NET 10** (LTS instalada na máquina), solução dividida em projetos por responsabilidade.

## Estrutura da solução

```
RpgForge.sln
├── src/
│   ├── RpgForge.Core     -> modelos de domínio, CaminhosDoProjeto (raiz de tudo que toca disco)
│   ├── RpgForge.Claude   -> wrapper fino sobre o SDK oficial Anthropic
│   ├── RpgForge.Tools    -> implementação das tools (leitura/escrita de arquivos, PDF)
│   ├── RpgForge.Agents   -> DefinicaoDeAgente: prompt + allowlist de tools por agente
│   └── RpgForge.App      -> aplicativo WPF (interface gráfica)
├── tests/RpgForge.Tests
├── Agents/               -> prompts dos agentes (Configurador.md, DungeonMaster.md)
├── Systems/              -> livros oficiais em PDF, um subdiretório por sistema
├── Templates/            -> fichas em PDF editável, um subdiretório por sistema
├── Knowledge/            -> base de conhecimento em Markdown, gerada pelo Configurador
└── Output/Personagens/   -> fichas finais preenchidas
```

O guardrail "cada agente só pode usar certas ferramentas" é reforçado em código em
`DefinicaoDeAgente.FerramentasPermitidas` — não depende do agente "se comportar" ao seguir o
prompt.

**Convenção de idioma:** nomes de solução, projetos e namespaces ficam em inglês (padrão do
ecossistema .NET), mas todo o resto — classes, métodos, variáveis, comentários e textos de
UI — é em português (pt-BR).

## Rodando

```
setx ANTHROPIC_API_KEY "sk-ant-..."   # uma vez, ou defina no ambiente da sessão
dotnet build RpgForge.sln
dotnet test RpgForge.sln
```

> Se o build ou `dotnet sln add`/`dotnet restore` falhar de forma estranha nesta máquina,
> verifique a variável de ambiente `MSBuildSDKsPath` — se ela estiver fixada em um SDK antigo
> (ex.: `.../sdk/2.1.202/Sdks`), remova-a das variáveis de ambiente do Windows.

## Status

Scaffold inicial: solução, projetos, referências, `CaminhosDoProjeto` com proteção contra
path traversal, carregamento dos prompts dos agentes, e o pacote `Anthropic` já
referenciado. Ainda faltam: implementação das tools (leitura de PDF, geração da estrutura de
Knowledge, preenchimento de PDF), o loop de tool-use com a API, e a interface WPF.
