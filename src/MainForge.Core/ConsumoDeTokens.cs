using System.Globalization;

namespace MainForge.Core;

/// <summary>
/// O que uma execução de agente custou: tokens de entrada, de saída e de cache, mais o valor em
/// dólares quando o Claude Code o informa.
///
/// <para><b>Por que existe.</b> Todo o desenho deste aplicativo é sobre gastar menos cota — a
/// conversão dos livros para texto, o índice da base, a retomada em vez do recomeço, a busca em
/// vez da leitura inteira. Nenhuma dessas decisões tinha número: o custo vinha no fluxo do
/// Claude Code e era descartado na hora de interpretá-lo. Sem medida, "ficou mais barato" é
/// palpite, e a próxima otimização é escolhida por intuição.</para>
///
/// <para><b>Por que mora no Core.</b> É contagem, não execução: quem produz o dado é o projeto
/// que fala com o Claude Code, e quem o guarda é o registro de processamento, em
/// <c>MainForge.Tools</c>. Os dois só se encontram aqui.</para>
///
/// <para><b>Cache não é desconto garantido.</b> <see cref="CacheLido"/> é cobrado a uma fração do
/// preço de entrada e <see cref="CacheCriado"/>, a um prêmio sobre ele. Guardamos os dois
/// separados justamente porque a diferença entre eles é o que diz se o desenho da conversa está
/// aproveitando o cache ou reescrevendo o prefixo a cada turno.</para>
/// </summary>
public sealed record ConsumoDeTokens(
    long Entrada = 0,
    long Saida = 0,
    long CacheCriado = 0,
    long CacheLido = 0,
    decimal? CustoUsd = null,
    int Turnos = 0)
{
    public static readonly ConsumoDeTokens Zero = new();

    /// <summary>Tudo que entrou no modelo, venha do cache ou não.</summary>
    public long TotalDeEntrada => Entrada + CacheCriado + CacheLido;

    /// <summary>Não houve turno nenhum, ou o Claude Code não informou contagem alguma.</summary>
    public bool Vazio => Turnos == 0 && TotalDeEntrada == 0 && Saida == 0;

    /// <summary>
    /// Quanto da entrada veio do cache. É o indicador que interessa numa conversa longa: perto de
    /// 100% significa que o prefixo está sendo reaproveitado; baixo significa que cada turno está
    /// pagando o contexto inteiro a preço cheio.
    /// </summary>
    public double ProporcaoEmCache =>
        TotalDeEntrada == 0 ? 0 : (double)CacheLido / TotalDeEntrada;

    public static ConsumoDeTokens operator +(ConsumoDeTokens um, ConsumoDeTokens outro) => new(
        um.Entrada + outro.Entrada,
        um.Saida + outro.Saida,
        um.CacheCriado + outro.CacheCriado,
        um.CacheLido + outro.CacheLido,
        Somar(um.CustoUsd, outro.CustoUsd),
        um.Turnos + outro.Turnos);

    /// <summary>
    /// A diferença entre dois acumulados — o que foi gasto entre um e outro.
    ///
    /// <para>Existe para quem soma turno a turno: com o total da conversa em mãos e o total que
    /// já tinha sido contado, isto é o gasto do último turno. Sem ele, cada volta do laço
    /// registraria de novo tudo que veio antes.</para>
    /// </summary>
    public static ConsumoDeTokens operator -(ConsumoDeTokens um, ConsumoDeTokens outro) => new(
        Math.Max(0, um.Entrada - outro.Entrada),
        Math.Max(0, um.Saida - outro.Saida),
        Math.Max(0, um.CacheCriado - outro.CacheCriado),
        Math.Max(0, um.CacheLido - outro.CacheLido),
        Subtrair(um.CustoUsd, outro.CustoUsd),
        Math.Max(0, um.Turnos - outro.Turnos));

    private static decimal? Somar(decimal? um, decimal? outro) =>
        um is null && outro is null ? null : (um ?? 0m) + (outro ?? 0m);

    private static decimal? Subtrair(decimal? um, decimal? outro) =>
        um is null ? null : Math.Max(0m, um.Value - (outro ?? 0m));

    /// <summary>
    /// Uma linha para o console. Números grandes vão abreviados porque o que interessa é a ordem
    /// de grandeza — ninguém decide nada com a diferença entre 412.383 e 412.400 tokens.
    /// </summary>
    public string Descrever()
    {
        if (Vazio)
        {
            return "consumo não informado pelo Claude Code";
        }

        var partes = new List<string>
        {
            $"{Abreviar(TotalDeEntrada)} de entrada",
        };

        if (CacheLido > 0)
        {
            partes.Add(string.Create(Formato, $"{ProporcaoEmCache:P0} em cache"));
        }

        partes.Add($"{Abreviar(Saida)} de saída");

        if (Turnos > 0)
        {
            partes.Add($"{Turnos} turno(s)");
        }

        if (CustoUsd is { } custo)
        {
            partes.Add(string.Create(Formato, $"US$ {custo:0.00}"));
        }

        return string.Join(", ", partes);
    }

    /// <summary>
    /// A interface do aplicativo é em português, e o número precisa sair igual em qualquer
    /// máquina — a mesma linha aparece no console de quem usa e na asserção de um teste que roda
    /// no CI, onde a cultura da máquina é outra.
    /// </summary>
    private static readonly CultureInfo Formato = CultureInfo.GetCultureInfo("pt-BR");

    private static string Abreviar(long tokens) => tokens switch
    {
        >= 1_000_000 => string.Create(Formato, $"{tokens / 1_000_000.0:0.0}M tokens"),
        >= 1_000 => string.Create(Formato, $"{tokens / 1_000.0:0.#}k tokens"),
        _ => string.Create(Formato, $"{tokens} tokens"),
    };
}
