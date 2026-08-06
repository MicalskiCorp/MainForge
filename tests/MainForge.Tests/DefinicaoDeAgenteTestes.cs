using MainForge.Agents;
using MainForge.Core;

namespace MainForge.Tests;

/// <summary>
/// O allowlist por agente é o guardrail central do produto. Sob o Claude Code ele virou
/// configuração de permissões passada na linha de comando, então estes testes travam
/// justamente as propriedades que um ajuste distraído nessa configuração quebraria.
/// </summary>
public class DefinicaoDeAgenteTestes
{
    private static readonly DefinicaoDeAgente[] TodosOsAgentes =
        [DefinicaoDeAgente.Configurador, DefinicaoDeAgente.DungeonMaster];

    [Fact]
    public void Configurador_CarregaSeuPromptDeSistemaDoDisco()
    {
        var prompt = DefinicaoDeAgente.Configurador.CarregarPromptDeSistema(CaminhosDoProjeto.Descobrir());

        Assert.Contains("Agente: Configurador", prompt);
    }

    [Fact]
    public void DungeonMaster_CarregaSeuPromptDeSistemaDoDisco()
    {
        var prompt = DefinicaoDeAgente.DungeonMaster.CarregarPromptDeSistema(CaminhosDoProjeto.Descobrir());

        Assert.Contains("Agente: Dungeon Master", prompt);
    }

    [Fact]
    public void Configurador_NaoRecebeAFerramentaDePreencherFicha()
    {
        Assert.DoesNotContain(
            DefinicaoDeAgente.Configurador.FerramentasPermitidas(),
            ferramenta => ferramenta.Contains("preencher_ficha_personagem"));
    }

    [Fact]
    public void DungeonMaster_NaoRecebeAFerramentaDeEscreverConhecimento()
    {
        Assert.DoesNotContain(
            DefinicaoDeAgente.DungeonMaster.FerramentasPermitidas(),
            ferramenta => ferramenta.Contains("escrever_arquivo_conhecimento"));
    }

    [Fact]
    public void FerramentasMcp_SaoPrefixadasComoOClaudeCodeAsNomeia()
    {
        var permitidas = DefinicaoDeAgente.DungeonMaster.FerramentasPermitidas();

        Assert.Contains("mcp__mainforge__preencher_ficha_personagem", permitidas);
    }

    /// <summary>
    /// Estas são as saídas de emergência do guardrail: com qualquer uma delas concedida, um
    /// agente contorna todas as outras restrições. Bash e PowerShell rodariam qualquer comando,
    /// Write/Edit escreveriam fora do confinamento em C#, Task abriria um subagente sem nenhuma
    /// dessas regras, e Skill carregaria instruções de <c>.claude/skills/</c> — que são do
    /// desenvolvimento do aplicativo e falam do código dele.
    ///
    /// <para><c>PowerShell</c> está na lista por ter acontecido: o Configurador rodando no
    /// Windows recebeu essa ferramenta e usou <c>Get-ChildItem</c> e <c>Select-String</c> para
    /// fazer o que a negação de <c>Bash</c> e de <c>Grep</c> proibia. Negar um shell só não nega
    /// shell nenhum.</para>
    /// </summary>
    [Theory]
    [InlineData("Bash")]
    [InlineData("PowerShell")]
    [InlineData("BashOutput")]
    [InlineData("KillShell")]
    [InlineData("Write")]
    [InlineData("Edit")]
    [InlineData("Task")]
    [InlineData("Skill")]
    [InlineData("SlashCommand")]
    [InlineData("Grep")]
    public void NenhumAgentePodeUsarFerramentaQueContornaOGuardrail(string ferramenta)
    {
        foreach (var agente in TodosOsAgentes)
        {
            Assert.Contains(ferramenta, agente.FerramentasNegadas());
            Assert.DoesNotContain(ferramenta, agente.FerramentasPermitidas());
        }
    }

    /// <summary>
    /// O Dungeon Master nunca lê os PDFs originais: eles são caros em tokens e é justamente
    /// para isso que o Configurador destilou o conhecimento em Sistemas/.
    /// </summary>
    [Fact]
    public void DungeonMaster_NaoPodeLerOsPdfsOriginais()
    {
        var negadas = DefinicaoDeAgente.DungeonMaster.FerramentasNegadas();

        Assert.Contains("Read(Input/**)", negadas);
        Assert.Contains("Read(Templates/**)", negadas);
    }

    /// <summary>
    /// Os agentes rodam com a raiz do projeto como diretório de trabalho, então a configuração
    /// do Claude Code de quem desenvolve o aplicativo fica no caminho deles. Nada ali é assunto
    /// de um agente de RPG — e `.claude/skills/` inclusive descreve o código-fonte, que eles já
    /// não podem ler.
    /// </summary>
    [Fact]
    public void NenhumAgenteLeAConfiguracaoDoClaudeCodeDoProjeto()
    {
        foreach (var agente in TodosOsAgentes)
        {
            Assert.Contains("Read(.claude/**)", agente.FerramentasNegadas());
        }
    }

    /// <summary>
    /// Numa máquina sem o poppler, o <c>Read</c> do Claude Code não abre PDF nenhum — ele
    /// rasteriza as páginas com o <c>pdftoppm</c>. Deixar a ferramenta disponível ali só faz o
    /// agente gastar um turno por livro para receber "pdftoppm is not installed"; o texto já
    /// convertido em <c>_texto/</c> é o caminho inteiro.
    /// </summary>
    [Fact]
    public void ConfiguradorSemAbrirPdf_NegaOsLivrosMasNaoAFichaEmBranco()
    {
        var agente = DefinicaoDeAgente.ConfiguradorSemAbrirPdf(DefinicaoDeAgente.Configurador);

        var negadas = agente.FerramentasNegadas();

        Assert.Contains("Read(Input/**/*.pdf)", negadas);

        // A ficha em Templates/ não tem versão em texto e é lida justamente pelo leiaute.
        Assert.DoesNotContain("Read(Templates/**)", negadas);

        // O texto convertido mora dentro de Input/, e continua legível.
        Assert.DoesNotContain("Read(Input/**)", negadas);

        // E as negações de sempre continuam valendo.
        Assert.Contains("Bash", negadas);
        Assert.Contains("Read(Output/**)", negadas);
    }

    [Fact]
    public void ConfiguradorSemAbrirPdf_NaoMexeNoAgentePadrao()
    {
        DefinicaoDeAgente.ConfiguradorSemAbrirPdf(DefinicaoDeAgente.Configurador);

        Assert.DoesNotContain("Read(Input/**/*.pdf)", DefinicaoDeAgente.Configurador.FerramentasNegadas());
    }

    /// <summary>
    /// A escolha de expansões feita pelo usuário antes da conversa vira negação de leitura. Se
    /// fosse só um pedido no prompt, o agente esbarraria no arquivo enquanto navega pelo índice
    /// e o conteúdo entraria na conversa de qualquer jeito — o personagem acabaria com uma
    /// opção de um livro que aquela mesa não usa.
    /// </summary>
    [Fact]
    public void DungeonMasterLimitadoA_NegaAsFontesQueAMesaNaoUsa()
    {
        var agente = DefinicaoDeAgente.DungeonMasterLimitadoA(
            new SistemaRpg("Aventura&Cia"),
            [FonteDoSistema.Base],
            [new FonteDoSistema("Compendio-Arcano"), new FonteDoSistema("Compendio-Sombrio")]);

        var negadas = agente.FerramentasNegadas();

        Assert.Contains("Read(Sistemas/Aventura&Cia/Compendio-Arcano/**)", negadas);
        Assert.Contains("Read(Sistemas/Aventura&Cia/Compendio-Sombrio/**)", negadas);

        // O que a mesa usa continua acessível, e as negações de sempre seguem valendo.
        Assert.DoesNotContain("Read(Sistemas/Aventura&Cia/base/**)", negadas);
        Assert.Contains("Read(Input/**)", negadas);
        Assert.Contains("Bash", negadas);
    }

    [Fact]
    public void DungeonMasterLimitadoA_SemExpansaoRecusada_NaoAcrescentaNegacao()
    {
        var agente = DefinicaoDeAgente.DungeonMasterLimitadoA(
            new SistemaRpg("Aventura&Cia"),
            [FonteDoSistema.Base],
            []);

        Assert.Equal(DefinicaoDeAgente.DungeonMaster.FerramentasNegadas(), agente.FerramentasNegadas());
    }

    /// <summary>
    /// A negação por caminho para no <c>Read</c> do agente: a ferramenta MCP que procura dentro
    /// de <c>Sistemas/</c> roda em outro processo, onde essa lista não chega. Se a restrição de
    /// fontes não viajar junto com o agente, a escolha de expansões do usuário deixa de valer
    /// justamente pela ferramenta que lê a base inteira.
    /// </summary>
    [Fact]
    public void DungeonMasterLimitadoA_LevaAsFontesDaMesaParaOServidorMcp()
    {
        var agente = DefinicaoDeAgente.DungeonMasterLimitadoA(
            new SistemaRpg("Aventura&Cia"),
            [FonteDoSistema.Base, new FonteDoSistema("Compendio-Arcano")],
            [new FonteDoSistema("Compendio-Sombrio")]);

        var mesa = Assert.IsType<RestricaoDeFontes>(agente.FontesDaMesa);

        Assert.True(mesa.Permite("Aventura&Cia", "base"));
        Assert.True(mesa.Permite("Aventura&Cia", "Compendio-Arcano"));
        Assert.False(mesa.Permite("Aventura&Cia", "Compendio-Sombrio"));

        // Outro sistema nunca entra: a mesa é de um sistema só.
        Assert.False(mesa.Permite("OutroSistema", "base"));
    }

    /// <summary>
    /// O Configurador escreve a base inteira e não pertence a mesa nenhuma — restrição de fonte
    /// ali seria um limite sem dono, que só apareceria como uma busca que não acha nada.
    /// </summary>
    [Fact]
    public void Configurador_NaoTemRestricaoDeFontes()
    {
        Assert.Null(DefinicaoDeAgente.Configurador.FontesDaMesa);
    }

    /// <summary>
    /// Uma concessão que também aparece na lista de negação seria uma contradição silenciosa:
    /// no Claude Code a negação vence, então a ferramenta simplesmente não funcionaria e o
    /// motivo não estaria escrito em lugar nenhum.
    /// </summary>
    [Fact]
    public void NenhumAgenteConcedeENegaAMesmaFerramenta()
    {
        foreach (var agente in TodosOsAgentes)
        {
            Assert.Empty(agente.FerramentasPermitidas().Intersect(agente.FerramentasNegadas()));
        }
    }
}
