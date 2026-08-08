using System.Diagnostics;
using System.Reflection;
using MainForge.ClaudeCode;
using MainForge.Core;

namespace MainForge.Tests;

/// <summary>
/// A linha de comando montada para o Claude Code.
///
/// <para><b>Por que testar isto.</b> Metade das decisões de custo e de segurança deste aplicativo
/// vira um argumento nessa linha — o modelo, o esforço de raciocínio, o conjunto de ferramentas
/// que existe na sessão, o isolamento das configurações do desenvolvedor. Nenhuma delas quebra o
/// build ao sumir: o agente simplesmente volta a rodar caro, ou com ferramenta demais, e ninguém
/// percebe até a fatura.</para>
/// </summary>
public sealed class LinhaDeComandoDoClaudeCodeTestes
{
    private static readonly OpcoesDoClaudeCode Opcoes = new() { CaminhoExecutavel = "claude" };

    /// <summary>
    /// A montagem é privada de propósito — ninguém fora da classe deveria montar essa linha. O
    /// teste alcança por reflexão em vez de abrir o método: expor um detalhe interno só para
    /// testá-lo convidaria alguém a usá-lo.
    /// </summary>
    private static List<string> Argumentos(PedidoDeTurno pedido)
    {
        var montar = typeof(ProcessoDoClaudeCode).GetMethod(
            "MontarInicio",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        var inicio = (ProcessStartInfo)montar.Invoke(new ProcessoDoClaudeCode(Opcoes), [pedido])!;

        return [.. inicio.ArgumentList];
    }

    private static string? ValorDe(List<string> argumentos, string flag)
    {
        var indice = argumentos.IndexOf(flag);

        return indice >= 0 && indice + 1 < argumentos.Count ? argumentos[indice + 1] : null;
    }

    private static PedidoDeTurno Pedido(AjusteDeExecucao? ajuste = null, decimal? teto = null) => new()
    {
        Mensagem = "processe",
        DiretorioDeTrabalho = ".",
        CaminhoPromptDeSistema = "Agents/Configurador.md",
        FerramentasPermitidas = ["Read", "Glob", "mcp__mainforge__consultar_progresso"],
        FerramentasNegadas = ["Bash", "Read(Output/**)"],
        FerramentasEmbutidas = ["Read", "Glob"],
        Ajuste = ajuste ?? AjusteDeExecucao.Resolver(PerfilDeExecucao.Equilibrado, NaturezaDoTrabalho.Extracao),
        TetoDeGastoUsd = teto,
    };

    [Fact]
    public void OModeloEOEsforcoVemDoAjusteDoAgente()
    {
        var argumentos = Argumentos(Pedido(new AjusteDeExecucao("claude-sonnet-5", "medium")));

        Assert.Equal("claude-sonnet-5", ValorDe(argumentos, "--model"));
        Assert.Equal("medium", ValorDe(argumentos, "--effort"));
    }

    /// <summary>
    /// O conjunto fechado de ferramentas embutidas. É a camada que não depende de adivinhar
    /// nomes: o que não está aqui não existe na sessão, nem como esquema enviado ao modelo.
    /// </summary>
    [Fact]
    public void AsFerramentasEmbutidasVaoEmTools()
    {
        Assert.Equal("Read,Glob", ValorDe(Argumentos(Pedido()), "--tools"));
    }

    [Fact]
    public void SemFerramentaNenhuma_ToolsVaiVazio()
    {
        var argumentos = Argumentos(Pedido() with { FerramentasEmbutidas = [] });

        Assert.Contains("--tools", argumentos);
        Assert.Equal("", ValorDe(argumentos, "--tools"));
    }

    /// <summary>
    /// Skills e comandos de barra vêm de <c>.claude/</c>, que aqui é a configuração de quem
    /// desenvolve o aplicativo. Desligar o mecanismo é mais firme que negar a ferramenta pelo nome.
    /// </summary>
    [Fact]
    public void AsSkillsDoProjetoFicamDeFora()
    {
        Assert.Contains("--disable-slash-commands", Argumentos(Pedido()));
    }

    /// <summary>
    /// Nenhuma configuração de fora entra na sessão do agente — nem a do usuário, nem a do
    /// projeto. Um hook definido em qualquer uma delas rodaria dentro da conversa de RPG.
    /// </summary>
    [Fact]
    public void NenhumaConfiguracaoDeForaEntraNaSessao()
    {
        Assert.Equal("", ValorDe(Argumentos(Pedido()), "--setting-sources"));
    }

    [Fact]
    public void OTetoDeGastoSoApareceQuandoHaUm()
    {
        Assert.DoesNotContain("--max-budget-usd", Argumentos(Pedido()));

        // Sempre com ponto decimal: o CLI não lê "1,5", e a máquina do usuário é pt-BR.
        Assert.Equal("1.5", ValorDe(Argumentos(Pedido(teto: 1.5m)), "--max-budget-usd"));
    }

    /// <summary>
    /// <c>bypassPermissions</c> desligaria as negações por caminho — que é como a escolha de
    /// expansões da mesa é aplicada.
    /// </summary>
    [Fact]
    public void AsPermissoesNuncaSaoIgnoradas()
    {
        var argumentos = Argumentos(Pedido());

        Assert.Equal("default", ValorDe(argumentos, "--permission-mode"));
        Assert.DoesNotContain("--dangerously-skip-permissions", argumentos);
        Assert.DoesNotContain("--allow-dangerously-skip-permissions", argumentos);
    }

    [Fact]
    public void AsNegacoesEAsConcessoesVaoJuntas()
    {
        var argumentos = Argumentos(Pedido());

        Assert.Contains("Bash", ValorDe(argumentos, "--disallowedTools"));
        Assert.Contains("Read(Output/**)", ValorDe(argumentos, "--disallowedTools")!);
        Assert.Contains("mcp__mainforge__consultar_progresso", ValorDe(argumentos, "--allowedTools")!);
    }
}
