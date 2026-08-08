using MainForge.Core;

namespace MainForge.ClaudeCode;

/// <summary>
/// Que tipo de trabalho um agente faz. É o que decide se ele precisa do modelo caro.
/// </summary>
public enum NaturezaDoTrabalho
{
    /// <summary>
    /// Ler muito e reescrever fielmente: achar a seção, transpor para Markdown, gravar. É volume,
    /// não julgamento — e volume no modelo mais caro é onde a cota deste aplicativo some.
    /// </summary>
    Extracao,

    /// <summary>
    /// Conversar com uma pessoa validando regra contra regra, calculando e impedindo escolha
    /// inválida. Aqui o modelo melhor se paga: um erro de regra vira um personagem que a mesa não
    /// aceita.
    /// </summary>
    Conversa,
}

/// <summary>
/// O modelo e o esforço de raciocínio com que um agente vai rodar.
///
/// <para>Os dois andam juntos de propósito: trocar o modelo sem mexer no esforço deixa metade da
/// economia na mesa, porque o gasto de raciocínio é invisível no relatório de tokens de entrada e
/// aparece inteiro na conta.</para>
/// </summary>
/// <param name="Modelo">Identificador do modelo, como o <c>--model</c> do Claude Code o espera.</param>
/// <param name="Esforco">Nível de <c>--effort</c>: <c>low</c>, <c>medium</c>, <c>high</c>, <c>xhigh</c> ou <c>max</c>.</param>
public sealed record AjusteDeExecucao(string Modelo, string Esforco)
{
    public const string Opus = "claude-opus-5";
    public const string Sonnet = "claude-sonnet-5";
    public const string Haiku = "claude-haiku-4-5-20251001";

    /// <summary>
    /// A tabela de decisão inteira, em um lugar só.
    ///
    /// <para>A linha que mais importa é a do meio, porque é o padrão: extrair conhecimento de um
    /// livro sai no Sonnet, e a conversa que valida as regras continua no Opus. Era essa a mistura
    /// que não existia — o Configurador lia livros inteiros no modelo mais caro do catálogo para
    /// fazer um trabalho de transcrição estruturada.</para>
    /// </summary>
    public static AjusteDeExecucao Resolver(PerfilDeExecucao perfil, NaturezaDoTrabalho natureza) =>
        (perfil, natureza) switch
        {
            (PerfilDeExecucao.Economico, NaturezaDoTrabalho.Extracao) => new(Haiku, "low"),
            (PerfilDeExecucao.Economico, NaturezaDoTrabalho.Conversa) => new(Sonnet, "low"),

            (PerfilDeExecucao.Equilibrado, NaturezaDoTrabalho.Extracao) => new(Sonnet, "medium"),
            (PerfilDeExecucao.Equilibrado, NaturezaDoTrabalho.Conversa) => new(Opus, "medium"),

            _ => new(Opus, "high"),
        };

    /// <summary>Como o perfil aparece na interface, com o custo relativo dito em voz alta.</summary>
    public static string Descrever(PerfilDeExecucao perfil) => perfil switch
    {
        PerfilDeExecucao.Economico =>
            "Econômico — o mais barato que dá conta. Bom para experimentar um sistema novo antes " +
            "de gastar cota de verdade com ele.",
        PerfilDeExecucao.Equilibrado =>
            "Equilibrado (padrão) — mapeia os livros no Sonnet e conversa no Opus. A leitura dos " +
            "livros é a parte cara, e ela não precisa do modelo mais caro.",
        _ =>
            "Melhor qualidade — Opus em tudo, com raciocínio longo. Custa várias vezes mais para " +
            "processar um sistema.",
    };
}
