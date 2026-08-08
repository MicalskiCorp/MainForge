using MainForge.Agents;
using MainForge.ClaudeCode;
using MainForge.Core;

namespace MainForge.Tests;

/// <summary>
/// Qual modelo cada agente usa em cada perfil. É a decisão de maior impacto na cota do usuário,
/// e a que mais fácil se desfaz sem querer — um <c>with</c> esquecido em
/// <see cref="DefinicaoDeAgente"/> devolve o Configurador ao modelo mais caro sem quebrar nada
/// visível.
/// </summary>
public sealed class AjusteDeExecucaoTestes
{
    /// <summary>
    /// O ponto inteiro da mudança: no perfil padrão, ler os livros não usa o modelo mais caro, e
    /// a conversa que valida regras usa.
    /// </summary>
    [Fact]
    public void NoPerfilPadrao_ExtracaoNaoUsaOModeloMaisCaro()
    {
        var extracao = AjusteDeExecucao.Resolver(PerfilDeExecucao.Equilibrado, NaturezaDoTrabalho.Extracao);
        var conversa = AjusteDeExecucao.Resolver(PerfilDeExecucao.Equilibrado, NaturezaDoTrabalho.Conversa);

        Assert.NotEqual(AjusteDeExecucao.Opus, extracao.Modelo);
        Assert.Equal(AjusteDeExecucao.Opus, conversa.Modelo);
    }

    /// <summary>O perfil de qualidade é o comportamento antigo: Opus em tudo.</summary>
    [Fact]
    public void NoPerfilDeQualidade_TudoRodaNoOpus()
    {
        foreach (var natureza in new[] { NaturezaDoTrabalho.Extracao, NaturezaDoTrabalho.Conversa })
        {
            Assert.Equal(
                AjusteDeExecucao.Opus,
                AjusteDeExecucao.Resolver(PerfilDeExecucao.Qualidade, natureza).Modelo);
        }
    }

    [Fact]
    public void TodoPerfilResolveModeloEEsforcoValidos()
    {
        string[] esforcosDoClaudeCode = ["low", "medium", "high", "xhigh", "max"];

        foreach (var perfil in Enum.GetValues<PerfilDeExecucao>())
        {
            foreach (var natureza in Enum.GetValues<NaturezaDoTrabalho>())
            {
                var ajuste = AjusteDeExecucao.Resolver(perfil, natureza);

                Assert.NotEmpty(ajuste.Modelo);
                Assert.Contains(ajuste.Esforco, esforcosDoClaudeCode);
            }
        }
    }

    /// <summary>
    /// O Configurador precisa estar declarado como extração: é essa marca, e não o nome dele,
    /// que decide em que modelo o livro inteiro vai ser lido.
    /// </summary>
    [Fact]
    public void ConfiguradorEExtracao_DungeonMasterEConversa()
    {
        Assert.Equal(NaturezaDoTrabalho.Extracao, DefinicaoDeAgente.Configurador.Natureza);
        Assert.Equal(NaturezaDoTrabalho.Conversa, DefinicaoDeAgente.DungeonMaster.Natureza);
    }

    /// <summary>Derivar o agente (mesa limitada, máquina sem PDF) não pode trocar o modelo dele.</summary>
    [Fact]
    public void AgenteDerivado_MantemANatureza()
    {
        var semPdf = DefinicaoDeAgente.ConfiguradorSemAbrirPdf(DefinicaoDeAgente.Configurador);

        var limitado = DefinicaoDeAgente.DungeonMasterLimitadoA(
            new SistemaRpg("Aventura&Cia"),
            [FonteDoSistema.Base],
            []);

        Assert.Equal(NaturezaDoTrabalho.Extracao, semPdf.Natureza);
        Assert.Equal(NaturezaDoTrabalho.Conversa, limitado.Natureza);
    }

    [Fact]
    public void ModeloForcado_VenceOPerfilSemMexerNoEsforco()
    {
        var opcoes = new OpcoesDoClaudeCode
        {
            CaminhoExecutavel = "claude",
            Perfil = PerfilDeExecucao.Economico,
            ModeloForcado = "claude-opus-5",
        };

        var ajuste = opcoes.AjusteDe(NaturezaDoTrabalho.Extracao);

        Assert.Equal("claude-opus-5", ajuste.Modelo);
        Assert.Equal(
            AjusteDeExecucao.Resolver(PerfilDeExecucao.Economico, NaturezaDoTrabalho.Extracao).Esforco,
            ajuste.Esforco);
    }

    [Fact]
    public void SemPreferenciaNenhuma_OPadraoEEquilibrado()
    {
        Assert.Equal(
            PerfilDeExecucao.Equilibrado,
            new OpcoesDoClaudeCode { CaminhoExecutavel = "claude" }.Perfil);
    }
}
