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
| MCP | `escrever_arquivo_conhecimento`, `listar_campos_da_ficha` | `preencher_ficha_personagem` |
| Negações próprias | `Read(Output/**)` | `Read(Systems/**)`, `Read(Templates/**)` |

Negado para os dois, sempre: `Bash`, `Write`, `Edit`, `NotebookEdit`, `Task`, `WebFetch`,
`WebSearch`, `Grep`, e a leitura do código do próprio aplicativo. Cada um desses é uma saída
de emergência pela qual um agente contornaria todas as outras restrições.

## Estrutura da solução

```
MainForge.sln
├── src/
│   ├── MainForge.Core       -> modelos de domínio, CaminhosDoProjeto (raiz de tudo que toca disco)
│   ├── MainForge.ClaudeCode -> localiza e executa o Claude Code; traduz o stream-json em eventos
│   ├── MainForge.Tools      -> o que só o C# faz: AcroForm (PdfSharp) e escrita em Knowledge/
│   ├── MainForge.Mcp        -> servidor MCP stdio que expõe MainForge.Tools ao agente
│   ├── MainForge.Agents     -> DefinicaoDeAgente (prompt + permissões) e SessaoDeAgente
│   ├── MainForge.Cli        -> interface em console (a interface em uso hoje)
│   └── MainForge.App        -> aplicativo WPF (interface gráfica, ainda um shell vazio)
├── tests/MainForge.Tests
├── tools/ValidacaoPontaAPonta -> harness manual do fluxo completo (fora da solução)
├── Agents/               -> prompts dos agentes (Configurador.md, DungeonMaster.md)
├── Systems/              -> livros oficiais em PDF, um subdiretório por sistema
├── Templates/            -> fichas em PDF editável, um subdiretório por sistema
├── Knowledge/            -> base de conhecimento em Markdown, gerada pelo Configurador
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

1. **Ver sistemas** — o que já foi importado, o que já tem base de conhecimento e ficha.
2. **Importar um sistema de RPG** — você informa o nome do sistema, os PDFs dos livros e a
   ficha de personagem editável; o programa valida (livro legível, ficha com campos
   preenchíveis) e copia para `Systems/<Sistema>/` e `Templates/<Sistema>/`.
3. **Processar um sistema** (Agente Configurador) — lê os livros **e a ficha em branco** e
   gera `Knowledge/<Sistema>/*.md`. É a operação mais cara em tokens; pede confirmação.
4. **Criar um personagem** (Agente Dungeon Master) — conversa livre até a ficha em PDF sair
   em `Output/Personagens/`. `/sair` encerra a conversa.
5. **Verificar o Claude Code** — mostra o executável, o modelo e as permissões de cada
   agente, e faz um turno de teste para confirmar que a assinatura está ativa.

Adicionar um sistema de RPG novo não exige mexer em código: basta a opção 2 seguida da 3
(ou copiar as pastas na mão para `Systems/` e `Templates/`).

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
  como "pagar mais" para continuar — só esperar.
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
