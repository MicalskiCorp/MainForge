namespace MainForge.Core;

/// <summary>
/// De onde vem um pedaço de um sistema de RPG: o <b>jogo base</b> ou uma <b>expansão</b>
/// (compêndio, suplemento). É o nome de uma subpasta dentro de <c>Input/&lt;Sistema&gt;/</c> e
/// de <c>Sistemas/&lt;Sistema&gt;/</c> — os livros de uma fonte e o conhecimento destilado
/// deles ficam sempre com o mesmo nome nos dois lados.
///
/// <para><b>Por que separar.</b> Numa mesa de RPG nem toda expansão está em jogo: o grupo
/// decide se vai usar um compêndio ou não. Com tudo misturado numa pasta só, o Dungeon Master
/// não teria como oferecer só o que vale para aquele personagem — e "por favor ignore o
/// conteúdo do compêndio X" é instrução que depende do modelo obedecer. Separado por pasta, a
/// escolha do usuário vira negação de leitura: o que não foi escolhido o agente não alcança.
/// </para>
///
/// <para>A ficha do sistema é do sistema inteiro, não de uma fonte: <c>Ficha-Mapeamento.md</c> e
/// <c>Ficha-ModeloEmTexto.md</c> ficam na raiz de <c>Sistemas/&lt;Sistema&gt;/</c>, fora das
/// pastas de fonte, porque o Dungeon Master precisa deles com qualquer expansão selecionada.</para>
/// </summary>
public sealed record FonteDoSistema(string Id)
{
    /// <summary>
    /// Nome da pasta do jogo base. Fixo de propósito: é o único nome que o aplicativo pode
    /// assumir sem perguntar, e é o que garante que todo sistema tenha uma fonte obrigatória.
    /// </summary>
    public const string IdDaBase = "base";

    public static readonly FonteDoSistema Base = new(IdDaBase);

    public bool EhBase => Id.Equals(IdDaBase, StringComparison.OrdinalIgnoreCase);

    /// <summary>Como a fonte é chamada em texto de UI e em prompt de agente.</summary>
    public string Rotulo => EhBase ? "jogo base" : $"expansão '{Id}'";

    /// <summary>Valida um nome digitado pelo usuário e devolve a fonte correspondente.</summary>
    public static FonteDoSistema Criar(string nome) =>
        new(NomeDePasta.Validar(nome, "expansão", nameof(nome)));

    /// <summary>Base primeiro, expansões em ordem alfabética — a ordem em que fazem sentido ler.</summary>
    public static IReadOnlyList<FonteDoSistema> Ordenar(IEnumerable<FonteDoSistema> fontes) =>
    [
        .. fontes
            .OrderByDescending(fonte => fonte.EhBase)
            .ThenBy(fonte => fonte.Id, StringComparer.OrdinalIgnoreCase),
    ];
}
