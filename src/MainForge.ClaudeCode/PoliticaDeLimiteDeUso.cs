namespace MainForge.ClaudeCode;

/// <summary>
/// Quanto o aplicativo se dispõe a esperar quando a cota da assinatura acaba no meio de um
/// turno.
///
/// <para>Os valores padrão vêm da forma como a assinatura do Claude Code é medida: a janela
/// curta é de 5 horas, então esperar até 6 cobre uma janela inteira com folga. Já uma janela
/// semanal esgotada estoura esse teto de propósito — o aplicativo avisa e devolve o controle
/// em vez de ficar dormindo por dias.</para>
/// </summary>
public sealed record PoliticaDeLimiteDeUso
{
    public static readonly PoliticaDeLimiteDeUso Padrao = new();

    /// <summary>Quantas vezes aceitar esperar e tentar de novo no mesmo turno.</summary>
    public int MaximoDeEsperas { get; init; } = 3;

    /// <summary>Acima disto, não espera: avisa o usuário e desiste do turno.</summary>
    public TimeSpan EsperaMaxima { get; init; } = TimeSpan.FromHours(6);

    /// <summary>Quanto esperar quando o Claude Code não informa a hora da liberação.</summary>
    public TimeSpan EsperaPadrao { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Folga somada ao instante informado, para não bater na virada da janela.</summary>
    public TimeSpan Margem { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// De quanto em quanto tempo a espera dá sinal de vida. É também a granularidade com que o
    /// cancelamento (Ctrl+C) é percebido, por isso não deve ser grande.
    /// </summary>
    public TimeSpan IntervaloDeAviso { get; init; } = TimeSpan.FromSeconds(30);
}
