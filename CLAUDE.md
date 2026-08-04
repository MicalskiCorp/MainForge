# MainForge — instruções para o Claude Code

Aplicativo desktop em C#/.NET 10 que lê livros de RPG em PDF e conduz a criação de fichas de
personagem, executando o Claude Code já instalado na máquina do usuário. Visão geral em
[README.md](README.md).

## Antes de mexer na estrutura

**Invoque a skill `estrutura-do-projeto` sempre que for ler, navegar, criar, mover ou renomear
qualquer coisa na estrutura do projeto** — arquivo, classe, pasta, projeto `.csproj`, teste,
ferramenta MCP, fluxo da CLI, agente ou sistema de RPG. Vale também para leitura: consulte a
skill antes de sair procurando arquivo por arquivo, e antes de responder "onde isso mora?".

A skill é o mapa de qual pasta é para quê, quais dependências entre projetos são permitidas e
onde cada tipo de arquivo novo deve nascer. Colocar um arquivo no lugar errado aqui quebra
guardrail de segurança, não só a arrumação — por isso a consulta é obrigatória, e não "quando
parecer útil".

Se a estrutura mudar de verdade, atualize a skill e o `README.md` na mesma alteração.

## Convenções

- **Idioma**: solução, projetos e namespaces em inglês; classes, métodos, variáveis,
  comentários e textos de UI em português (pt-BR).
- **Comentários** explicam *por que*, não *o que*. Cada classe pública tem um `<summary>` com a
  razão de existir.
- **Caminhos** sempre por `CaminhosDoProjeto`; nunca montados na mão a partir da raiz.

## Build e testes

```
dotnet build MainForge.sln
dotnet test MainForge.sln
dotnet run --project src/MainForge.Cli
```

`tools/ValidacaoPontaAPonta` está fora da solução de propósito: ele executa agentes de verdade
e consome a cota da assinatura. Não o inclua em build nem em teste automático.
