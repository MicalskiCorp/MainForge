# Chatbot para Criação de Fichas de Personagens de RPG

Aplicativo desktop em C#/.NET que usa a Claude API para entender livros de RPG em PDF e
conduzir a criação de personagens, terminando num PDF de ficha preenchido. Tudo roda
localmente na máquina do usuário; a única comunicação externa é com a Claude API.

## Decisões de arquitetura

- **Autenticação**: API key da Anthropic, via o SDK oficial `Anthropic` para .NET. Não existe
  hoje um Claude Agent SDK para C#, e não há mecanismo suportado para um app de terceiros
  autenticar usando a assinatura Claude Pro/Max de um usuário — por isso a licença aqui é
  billing por token com uma API key.
  A chave **não fica no código-fonte**: o usuário a informa dentro do aplicativo, e ela é
  guardada cifrada com DPAPI (`%APPDATA%\MainForge\chave-api.dat`), decifrável apenas pela
  mesma conta de usuário do mesmo Windows. A variável de ambiente `ANTHROPIC_API_KEY`
  continua funcionando e tem prioridade, para automação.
- **Ferramentas locais** (ler PDF, escrever Markdown, preencher ficha): expostas como
  *tools* (function calling) diretamente na Messages API, executadas em processo pelo
  próprio C#. Não há um servidor MCP — como tudo roda no mesmo processo local, o protocolo
  MCP não adiciona valor aqui (ele existe para expor ferramentas a clientes externos).
- **.NET 10** (LTS instalada na máquina), solução dividida em projetos por responsabilidade.

## Estrutura da solução

```
MainForge.sln
├── src/
│   ├── MainForge.Core     -> modelos de domínio, CaminhosDoProjeto (raiz de tudo que toca disco)
│   ├── MainForge.Claude   -> wrapper fino sobre o SDK oficial Anthropic
│   ├── MainForge.Tools    -> implementação das tools (leitura/escrita de arquivos, PDF)
│   ├── MainForge.Agents   -> DefinicaoDeAgente: prompt + allowlist de tools por agente
│   ├── MainForge.Cli      -> interface em console (a interface em uso hoje)
│   └── MainForge.App      -> aplicativo WPF (interface gráfica, ainda um shell vazio)
├── tests/MainForge.Tests
├── tools/ValidacaoPontaAPonta -> harness manual do fluxo completo (fora da solução)
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
dotnet build MainForge.sln
dotnet test MainForge.sln
dotnet run --project src/MainForge.Cli    # o aplicativo
```

Na primeira execução, use a opção **5) Configurar a chave da Claude API** para informar sua
API key — nada é exibido enquanto você digita, e a chave é gravada cifrada.

Menu do aplicativo:

1. **Ver sistemas** — o que já foi importado, o que já tem base de conhecimento e ficha.
2. **Importar um sistema de RPG** — você informa o nome do sistema, os PDFs dos livros e a
   ficha de personagem editável; o programa valida (livro legível, ficha com campos
   preenchíveis) e copia para `Systems/<Sistema>/` e `Templates/<Sistema>/`.
3. **Processar um sistema** (Agente Configurador) — lê os livros **e a ficha em branco** e
   gera `Knowledge/<Sistema>/*.md`. É a operação mais cara em tokens; pede confirmação.
4. **Criar um personagem** (Agente Dungeon Master) — conversa livre até a ficha em PDF sair
   em `Output/Personagens/`. `/sair` encerra a conversa.
5. **Configurar a chave da Claude API** — informar, substituir ou apagar.

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

> Se o build ou `dotnet sln add`/`dotnet restore` falhar de forma estranha nesta máquina,
> verifique a variável de ambiente `MSBuildSDKsPath` — se ela estiver fixada em um SDK antigo
> (ex.: `.../sdk/2.1.202/Sdks`), remova-a das variáveis de ambiente do Windows.

> PDFs precisam estar marcados como binários no Git (`.gitattributes`): com
> `core.autocrlf=true`, a conversão de fim de linha corrompe os offsets internos do arquivo.

## Status

Funcionando: as 8 ferramentas locais, o loop de tool-use (`SessaoDeAgente`), o allowlist de
ferramentas por agente, a importação de sistemas, o armazenamento cifrado da API key e a
interface em console (`MainForge.Cli`), com 44 testes automatizados.

Falta: validar o fluxo completo contra a API de verdade (harness em
`tools/ValidacaoPontaAPonta`), testar com um livro de RPG real e construir a interface
gráfica em WPF.
