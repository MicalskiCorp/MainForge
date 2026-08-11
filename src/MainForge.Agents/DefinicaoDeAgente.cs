using MainForge.ClaudeCode;
using MainForge.Core;

namespace MainForge.Agents;

/// <summary>
/// Um agente nomeado: seu prompt de sistema (carregado de Agents/*.md) mais o conjunto exato
/// de ferramentas que ele pode usar. É aqui que os guardrails da especificação do produto são
/// aplicados.
///
/// <para><b>Como o guardrail funciona sob o Claude Code.</b> São três camadas, e é preciso
/// entender por que nenhuma delas sozinha basta:</para>
///
/// <list type="number">
///   <item><b>Diretório de trabalho.</b> O processo roda com a raiz do projeto como diretório
///   de trabalho, e o Claude Code não acessa arquivos fora dele.</item>
///   <item><b>O conjunto de ferramentas embutidas que existe na sessão</b>
///   (<see cref="FerramentasNativasPermitidas"/>, passadas em <c>--tools</c>). O que não está
///   nessa lista não é oferecido ao modelo — nem como esquema, nem como possibilidade. É a
///   camada que não depende de adivinhar nomes, e por isso ferramenta nova do Claude Code não
///   entra sozinha numa sessão de RPG.</item>
///   <item><b>Negações por ferramenta e por caminho</b> (<see cref="FerramentasNegadas"/>).
///   É o que restringe <em>por caminho</em> — a escolha de expansões da mesa vira
///   <c>Read(Sistemas/&lt;fonte&gt;/**)</c> negado — e o que continua barrando por nome, como
///   segunda linha. <c>--allowedTools</c> apenas <em>concede</em>: ferramentas de leitura já
///   são aprovadas por padrão, então listar <c>Read(Sistemas/**)</c> não impede leituras fora
///   de Sistemas/ — só uma negação explícita faz isso.</item>
///   <item><b>Confinamento em código.</b> Toda <em>escrita</em> passa pelo servidor MCP em
///   C#, onde <see cref="CaminhosDoProjeto.ResolverDentroDe"/> rejeita qualquer caminho que
///   escape do diretório permitido. É a única camada que não depende de acertar uma lista de
///   negação, e por isso é onde mora a regra que realmente importa.</item>
/// </list>
/// </summary>
/// <param name="Nome">Nome do agente, usado em mensagens ao usuário.</param>
/// <param name="NomeArquivoPrompt">Arquivo em Agents/ com o prompt de sistema.</param>
/// <param name="FerramentasNativasPermitidas">Ferramentas embutidas do Claude Code concedidas.</param>
/// <param name="FerramentasMcpPermitidas">Ferramentas do servidor MCP do MainForge concedidas, sem prefixo.</param>
/// <param name="NegacoesEspecificas">Negações próprias deste agente, somadas às comuns a todos.</param>
public sealed record DefinicaoDeAgente(
    string Nome,
    string NomeArquivoPrompt,
    IReadOnlyList<string> FerramentasNativasPermitidas,
    IReadOnlyList<string> FerramentasMcpPermitidas,
    IReadOnlyList<string> NegacoesEspecificas)
{
    /// <summary>
    /// Nome do servidor MCP no arquivo de configuração. O Claude Code expõe as ferramentas
    /// dele como <c>mcp__mainforge__&lt;ferramenta&gt;</c>.
    /// </summary>
    public const string NomeDoServidorMcp = "mainforge";

    /// <summary>
    /// Que tipo de trabalho este agente faz. É o que, cruzado com o perfil escolhido pelo
    /// usuário, decide em que modelo e com quanto esforço de raciocínio ele roda.
    ///
    /// <para>Não é detalhe de configuração: rodar a extração dos livros no mesmo modelo da
    /// conversa era o maior desperdício de cota do aplicativo, e a diferença entre os dois
    /// trabalhos é uma propriedade do agente, não uma preferência.</para>
    /// </summary>
    public NaturezaDoTrabalho Natureza { get; init; } = NaturezaDoTrabalho.Conversa;

    /// <summary>
    /// O que esta sessão pode tocar — as fontes da mesa e, quando há uma, o personagem dela.
    /// <c>null</c> significa sem limite: é o caso do Configurador, que escreve a base inteira.
    ///
    /// <para>Existe além das negações de <c>Read</c> porque elas param no agente: uma ferramenta
    /// MCP que leia <c>Sistemas/</c> ou grave um dossiê roda em outro processo, onde a lista de
    /// negação do Claude Code não chega. Este escopo é o que viaja até lá.</para>
    /// </summary>
    public EscopoDaSessao? Escopo { get; init; }

    /// <summary>
    /// Negado para todos os agentes, sem exceção. Cada item fecha um caminho pelo qual um
    /// agente contornaria o próprio allowlist:
    /// <list type="bullet">
    ///   <item><c>Bash</c> e <c>PowerShell</c> fariam qualquer coisa que as outras negações
    ///   proíbem. São dois nomes de ferramenta diferentes, e negar só um não fecha nada: o
    ///   Configurador rodando no Windows recebeu <c>PowerShell</c> e listou pastas e procurou
    ///   texto em arquivo com <c>Get-ChildItem</c> e <c>Select-String</c> — exatamente o que a
    ///   negação de <c>Bash</c> e <c>Grep</c> existia para impedir. <c>BashOutput</c> e
    ///   <c>KillShell</c> vão junto por serem a continuação de um shell já aberto;</item>
    ///   <item><c>Write</c>/<c>Edit</c>/<c>NotebookEdit</c> escreveriam fora do MCP, sem o
    ///   confinamento de diretório em C#;</item>
    ///   <item><c>Task</c> abriria um subagente sem estas restrições;</item>
    ///   <item><c>WebFetch</c>/<c>WebSearch</c> trariam regras da internet — os agentes devem
    ///   responder a partir dos livros importados, não de outra fonte;</item>
    ///   <item><c>Grep</c> leria conteúdo de arquivo por um caminho que não confirmei
    ///   respeitar as negações por diretório (o <c>Read</c> respeita);</item>
    ///   <item><c>Skill</c> e <c>SlashCommand</c> carregariam instruções de <c>.claude/</c>, que
    ///   existem para quem desenvolve o aplicativo e falam do código dele — o agente roda com a
    ///   raiz do projeto como diretório de trabalho, então enxergaria isso sem esta negação;</item>
    ///   <item>o código do próprio aplicativo não interessa a nenhum agente de RPG.</item>
    /// </list>
    ///
    /// <para><b>Negar por nome é uma lista, e lista se esquece.</b> Por isso ela deixou de ser a
    /// primeira linha: hoje o conjunto de ferramentas embutidas é declarado em
    /// <c>--tools</c> (veja <see cref="FerramentasNativasPermitidas"/>), e o que não está lá não
    /// existe na sessão — inclusive o que a Anthropic acrescentar depois desta lista ter sido
    /// escrita. A camada que realmente contém a <em>escrita</em> continua sendo o servidor MCP
    /// em C#.</para>
    ///
    /// <para>A lista fica porque as duas outras coisas que ela faz não têm substituto: negar por
    /// caminho (é assim que a escolha de expansões da mesa é aplicada) e barrar de novo, por
    /// nome, o que já não deveria existir. Custa nada e cobre o caso de <c>--tools</c> um dia
    /// mudar de semântica.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> NegacoesComuns =
    [
        "Bash",
        "PowerShell",
        "BashOutput",
        "KillShell",
        "Write",
        "Edit",
        "NotebookEdit",
        "Task",
        "Agent",
        "Skill",
        "SlashCommand",
        "WebFetch",
        "WebSearch",
        "Grep",
        "Read(src/**)",
        "Read(tests/**)",
        "Read(tools/**)",
        "Read(.git/**)",
        "Read(.claude/**)",
    ];

    public string CarregarPromptDeSistema(CaminhosDoProjeto caminhos)
    {
        var caminho = CaminhoDoPrompt(caminhos);

        if (!File.Exists(caminho))
        {
            throw new FileNotFoundException(
                $"Prompt do agente '{Nome}' não encontrado em '{caminho}'.", caminho);
        }

        return File.ReadAllText(caminho);
    }

    public string CaminhoDoPrompt(CaminhosDoProjeto caminhos) =>
        Path.Combine(caminhos.Agentes, NomeArquivoPrompt);

    /// <summary>
    /// Lista completa de ferramentas concedidas, com as do MCP já prefixadas como o Claude
    /// Code as nomeia.
    /// </summary>
    public IReadOnlyList<string> FerramentasPermitidas() =>
    [
        .. FerramentasNativasPermitidas,
        .. FerramentasMcpPermitidas.Select(nome => $"mcp__{NomeDoServidorMcp}__{nome}"),
    ];

    /// <summary>Negações comuns mais as específicas do agente.</summary>
    public IReadOnlyList<string> FerramentasNegadas() => [.. NegacoesComuns, .. NegacoesEspecificas];

    /// <summary>
    /// Monta o pedido de execução deste agente para o Claude Code.
    /// </summary>
    public PedidoDeTurno MontarPedido(
        CaminhosDoProjeto caminhos,
        string mensagem,
        string? caminhoConfigMcp,
        string? idDaSessao,
        bool retomar,
        AjusteDeExecucao ajuste,
        decimal? tetoDeGastoUsd = null) => new()
        {
            Mensagem = mensagem,
            DiretorioDeTrabalho = caminhos.Raiz,
            CaminhoPromptDeSistema = CaminhoDoPrompt(caminhos),
            FerramentasPermitidas = FerramentasPermitidas(),
            FerramentasNegadas = FerramentasNegadas(),
            FerramentasEmbutidas = FerramentasNativasPermitidas,
            CaminhoConfigMcp = caminhoConfigMcp,
            IdDaSessao = idDaSessao,
            Retomar = retomar,
            Ajuste = ajuste,
            TetoDeGastoUsd = tetoDeGastoUsd,
        };

    /// <summary>
    /// Lê os livros em Input/ e a ficha em branco em Templates/, e escreve a base de
    /// conhecimento em Sistemas/. Nunca conversa com o usuário final e nunca olha as fichas
    /// de personagens já criadas em Output/.
    /// </summary>
    public static readonly DefinicaoDeAgente Configurador = new(
        Nome: "Configurador",
        NomeArquivoPrompt: "Configurador.md",
        FerramentasNativasPermitidas: ["Read", "Glob"],
        // Ler o livro e transpor a regra para Markdown é transcrição estruturada, não julgamento:
        // é o trabalho que não justifica o modelo mais caro, e é justamente o que lê mais.
        FerramentasMcpPermitidas:
        [
            "escrever_arquivo_conhecimento",
            "descrever_pasta_de_conhecimento",
            "registrar_plano_de_conhecimento",
            "consultar_progresso",
            "procurar_no_texto_dos_livros",
            "estrutura_do_livro",
            "listar_campos_da_ficha",
            "conferir_ficha_do_sistema",
        ],
        // Output/ são as fichas em PDF já entregues e Personagens/ são os dossiês delas: os dois
        // são resultado do Dungeon Master, e nada do que o Configurador faz depende de olhar
        // personagem nenhum.
        NegacoesEspecificas: ["Read(Output/**)", "Read(Personagens/**)"])
    {
        Natureza = NaturezaDoTrabalho.Extracao,
    };

    /// <summary>
    /// O Configurador numa máquina em que abrir PDF não funciona: a leitura dos livros em
    /// <c>Input/</c> fica negada, e sobra o texto já convertido em <c>_texto/</c>.
    ///
    /// <para><b>Por que negar em vez de só pedir.</b> O <c>Read</c> do Claude Code rasteriza as
    /// páginas do PDF com o <c>pdftoppm</c>; sem o poppler instalado, toda tentativa termina em
    /// <c>pdftoppm is not installed</c>. O prompt já manda ler o Markdown, mas "manda" é um
    /// pedido: o agente esbarra numa tabela que a conversão embaralhou, tenta conferir no PDF
    /// original — que é o que ele deveria fazer se aquilo funcionasse — e queima um turno para
    /// descobrir o que o aplicativo já sabia. Negar o caminho quebrado transforma isso numa
    /// recusa imediata, com o agente seguindo pelo texto.</para>
    ///
    /// <para>A negação vale só para <c>Input/</c>. A ficha em branco de <c>Templates/</c>
    /// continua sendo lida como PDF: ali o que interessa é o leiaute, não há versão em texto, e
    /// se essa leitura também falhar o usuário precisa ver a falha — é sinal de que falta o
    /// poppler para gerar os arquivos da ficha.</para>
    /// </summary>
    public static DefinicaoDeAgente ConfiguradorSemAbrirPdf(DefinicaoDeAgente configurador) =>
        configurador with
        {
            NegacoesEspecificas = [.. configurador.NegacoesEspecificas, "Read(Input/**/*.pdf)"],
        };

    /// <summary>
    /// Só lê Sistemas/ e Personagens/, e só escreve pelas ferramentas MCP — o dossiê do
    /// personagem e a ficha em PDF. Nunca lê os PDFs originais em Input/ (caros em tokens, e é
    /// justamente para isso que o Configurador destilou o conhecimento) nem o template em
    /// Templates/ — para ver a ficha ele usa o Ficha-ModeloEmTexto.md, e para saber os nomes dos
    /// campos, o Ficha-Mapeamento.md.
    ///
    /// <para><c>Output/</c> continua negado mesmo agora que ele produz PDF ali: escrever é pela
    /// ferramenta, e ler as fichas já entregues não ajuda em nada — o estado do personagem mora
    /// em <c>Personagens/</c>, em texto, que é o que ele consegue de fato usar.</para>
    /// </summary>
    public static readonly DefinicaoDeAgente DungeonMaster = new(
        Nome: "DungeonMaster",
        NomeArquivoPrompt: "DungeonMaster.md",
        FerramentasNativasPermitidas: ["Read", "Glob"],
        FerramentasMcpPermitidas:
        [
            "preencher_ficha_personagem",
            "registrar_personagem",
            "procurar_no_conhecimento",
        ],
        NegacoesEspecificas: ["Read(Input/**)", "Read(Templates/**)", "Read(Output/**)"]);

    /// <summary>
    /// O Dungeon Master de uma mesa que não usa todas as expansões: as fontes que o usuário não
    /// escolheu viram negação de leitura por caminho, e as que ele escolheu viajam até o servidor
    /// MCP em <see cref="Escopo"/>.
    ///
    /// <para><b>Por que negar em vez de pedir.</b> "Não use o compêndio X" no prompt é um pedido
    /// — o modelo esbarra no arquivo enquanto navega pelo índice e o conteúdo entra na conversa
    /// de qualquer jeito. Negar a pasta faz a escolha do usuário valer mesmo: o personagem não
    /// pode ganhar uma subclasse de um livro que a mesa não usa se o agente não alcança o
    /// arquivo onde ela está.</para>
    ///
    /// <para><b>Por que as duas listas.</b> A negação por caminho vale para o <c>Read</c> do
    /// agente e para mais nada. A busca na base é uma ferramenta MCP, que roda noutro processo:
    /// ela precisa saber quais fontes valem, ou seria a porta lateral para o conteúdo que a
    /// negação acabou de fechar.</para>
    /// </summary>
    /// <param name="sistema">Sistema em que o personagem está sendo criado.</param>
    /// <param name="fontesDaMesa">Fontes que valem — o jogo base mais as expansões marcadas.</param>
    /// <param name="fontesRecusadas">Fontes que ficam de fora — em geral, as expansões não marcadas.</param>
    /// <param name="personagem">
    /// O único personagem que esta conversa pode gravar. Sem isto, um identificador trocado pelo
    /// modelo — o que acontece de verdade numa evolução, em que dois personagens do mesmo sistema
    /// estão em jogo — sobrescreveria o dossiê de outro personagem por inteiro.
    /// </param>
    public static DefinicaoDeAgente DungeonMasterLimitadoA(
        SistemaRpg sistema,
        IReadOnlyList<FonteDoSistema> fontesDaMesa,
        IReadOnlyList<FonteDoSistema> fontesRecusadas,
        string? personagem = null) =>
        DungeonMaster with
        {
            NegacoesEspecificas =
            [
                .. DungeonMaster.NegacoesEspecificas,
                .. fontesRecusadas.Select(fonte => $"Read(Sistemas/{sistema.Id}/{fonte.Id}/**)"),
            ],
            Escopo = new EscopoDaSessao(
                sistema.Id,
                [.. fontesDaMesa.Select(fonte => fonte.Id).DefaultIfEmpty(FonteDoSistema.IdDaBase)])
            {
                Personagem = personagem,
            },
        };
}
