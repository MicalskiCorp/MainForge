using MainForge.Core;

namespace MainForge.ClaudeCode;

/// <summary>
/// Configuração para executar o Claude Code. Repare no que <em>não</em> tem aqui: nenhuma
/// credencial. A autenticação é da instalação do Claude Code (a assinatura do usuário), e o
/// aplicativo nunca a lê, guarda ou transporta.
/// </summary>
public sealed record OpcoesDoClaudeCode
{
    public required string CaminhoExecutavel { get; init; }

    /// <summary>
    /// Quanto gastar por quanta qualidade. Quem traduz isto em modelo e esforço é
    /// <see cref="AjusteDeExecucao.Resolver"/>, que também leva em conta o tipo de trabalho do
    /// agente — o mesmo perfil não deve rodar a leitura dos livros e a conversa com o usuário no
    /// mesmo modelo.
    /// </summary>
    public PerfilDeExecucao Perfil { get; init; } = PerfilDeExecucao.Equilibrado;

    /// <summary>
    /// Um modelo específico, quando o usuário quer mandar nisso. <c>null</c> — o normal — deixa o
    /// perfil decidir por agente.
    /// </summary>
    public string? ModeloForcado { get; init; }

    /// <summary>
    /// Teto de gasto por execução de agente, em dólares (<c>--max-budget-usd</c>). <c>null</c>
    /// significa sem teto.
    ///
    /// <para><b>Por que existe.</b> Um agente que entra em laço — relê o mesmo trecho, insiste
    /// numa ferramenta negada — drena a janela da assinatura sem nada que o interrompa, e o
    /// usuário só descobre quando a cota acaba. O teto transforma isso num turno que termina.</para>
    /// </summary>
    public decimal? TetoDeGastoUsd { get; init; }

    /// <summary>
    /// Localiza o Claude Code e monta as opções, ou devolve <c>null</c> se não houver
    /// instalação encontrável.
    /// </summary>
    public static OpcoesDoClaudeCode? Resolver()
    {
        var executavel = LocalizadorDoClaudeCode.Localizar();

        return executavel is null
            ? null
            : new OpcoesDoClaudeCode { CaminhoExecutavel = executavel };
    }

    /// <summary>O modelo e o esforço com que um agente daquela natureza vai rodar.</summary>
    public AjusteDeExecucao AjusteDe(NaturezaDoTrabalho natureza)
    {
        var ajuste = AjusteDeExecucao.Resolver(Perfil, natureza);

        return ModeloForcado is { Length: > 0 } forcado ? ajuste with { Modelo = forcado } : ajuste;
    }
}
