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

    /// <summary>
    /// Para operações longas e não interativas — hoje, o processamento de um sistema pelo
    /// Configurador. Aqui não existe "desistir de esperar": o usuário mandou processar, o
    /// trabalho leva o tempo que levar e não há nada a decidir quando a cota acaba, porque
    /// esperar é a única saída (o consumo sai da assinatura, não de créditos).
    ///
    /// <para>Desistir seria pior do que parece: o Claude Code guarda o livro já lido no
    /// contexto da conversa, e devolver o controle ao usuário significa que esse contexto se
    /// perde e a leitura é refeita — cobrada de novo — na próxima execução.</para>
    ///
    /// <para>O teto de 8 dias não é para desistir de uma janela: a semanal libera em no máximo
    /// 7 dias, então ele só barra um instante absurdo vindo de uma mensagem mal interpretada.</para>
    /// </summary>
    public static readonly PoliticaDeLimiteDeUso ProcessamentoLongo = new()
    {
        MaximoDeEsperas = int.MaxValue,
        EsperaMaxima = TimeSpan.FromDays(8),
    };

    /// <summary>Quantas vezes aceitar esperar e tentar de novo no mesmo turno.</summary>
    public int MaximoDeEsperas { get; init; } = 3;

    /// <summary>Acima disto, não espera: avisa o usuário e desiste do turno.</summary>
    public TimeSpan EsperaMaxima { get; init; } = TimeSpan.FromHours(6);

    /// <summary>Quanto esperar quando o Claude Code não informa a hora da liberação.</summary>
    public TimeSpan EsperaPadrao { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Teto da espera às cegas. Sem hora informada, cada tentativa frustrada dobra o intervalo
    /// da seguinte — senão uma política que nunca desiste ficaria batendo no Claude Code a cada
    /// 15 minutos por dias a fio, gastando cota só para ouvir de novo que não há cota.
    /// </summary>
    public TimeSpan EsperaPadraoMaxima { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Folga somada ao instante informado, para não bater na virada da janela.</summary>
    public TimeSpan Margem { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// De quanto em quanto tempo a espera dá sinal de vida. É também a granularidade com que o
    /// cancelamento (Ctrl+C) é percebido, por isso não deve ser grande.
    /// </summary>
    public TimeSpan IntervaloDeAviso { get; init; } = TimeSpan.FromSeconds(30);
}
