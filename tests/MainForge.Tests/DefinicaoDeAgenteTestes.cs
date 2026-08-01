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
    /// Estas quatro são as saídas de emergência do guardrail: com qualquer uma delas
    /// concedida, um agente contorna todas as outras restrições. Bash rodaria qualquer
    /// comando, Write/Edit escreveriam fora do confinamento em C#, e Task abriria um subagente
    /// sem nenhuma dessas regras.
    /// </summary>
    [Theory]
    [InlineData("Bash")]
    [InlineData("Write")]
    [InlineData("Edit")]
    [InlineData("Task")]
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
    /// para isso que o Configurador destilou o conhecimento em Knowledge/.
    /// </summary>
    [Fact]
    public void DungeonMaster_NaoPodeLerOsPdfsOriginais()
    {
        var negadas = DefinicaoDeAgente.DungeonMaster.FerramentasNegadas();

        Assert.Contains("Read(Systems/**)", negadas);
        Assert.Contains("Read(Templates/**)", negadas);
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
