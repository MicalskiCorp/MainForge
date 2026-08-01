namespace MainForge.ClaudeCode;

/// <summary>
/// Configuração para executar o Claude Code. Repare no que <em>não</em> tem aqui: nenhuma
/// credencial. A autenticação é da instalação do Claude Code (a assinatura do usuário), e o
/// aplicativo nunca a lê, guarda ou transporta.
/// </summary>
public sealed record OpcoesDoClaudeCode
{
    /// <summary>
    /// Modelo usado por todo agente. "Melhor modelo disponível", segundo a especificação do
    /// produto, hoje significa Claude Opus 5.
    /// </summary>
    public const string ModeloPadrao = "claude-opus-5";

    public required string CaminhoExecutavel { get; init; }

    public string Modelo { get; init; } = ModeloPadrao;

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
}
