using System.Text;

namespace MainForge.Cli;

/// <summary>
/// Abertura do aplicativo: o portão do castelo, com uma tocha acesa sobre cada torre, em
/// arte de texto. Só ASCII de propósito — caracteres de desenho de caixa e emoji saem
/// quebrados em consoles antigos do Windows com fonte raster, e a tela inicial é justamente
/// a primeira impressão do programa.
///
/// O desenho é montado por composição (torre esquerda + vão central + torre direita) em vez
/// de ser um bloco de texto solto: assim o alinhamento das colunas é garantido pela
/// construção, e mexer numa peça não desalinha o resto.
/// </summary>
internal static class TelaInicial
{
    private const int MargemEsquerda = 4;
    private const int LarguraDaTorre = 15;
    private const int LarguraDoVao = 36;
    private const int LarguraDoPortao = 18;

    // Peças da torre (todas com exatamente LarguraDaTorre caracteres).
    private const string AmeiasDaTorre = "|_|_|_|_|_|_|_|";
    private const string ParedeDaTorre = "|             |";
    private const string SeteiraDaTorre = "|  []     []  |";
    private const string BaseDaTorre = "|_____________|";

    // Peças do portão (todas com exatamente LarguraDoPortao caracteres).
    private const string ArcoDoPortao = " ________________ ";
    private const string OmbreirasDoPortao = "/                \\";
    private const string BarraDaGrade = " |--+--+--+--+--| ";
    private const string VaoDaGrade = " |  |  |  |  |  | ";
    private const string BaseDaGrade = "_|__|__|__|__|__|_";

    public static void Desenhar()
    {
        Console.WriteLine();

        foreach (var (linha, cor) in MontarPortao())
        {
            ConsoleUi.EscreverColorido(linha, cor);
        }

        ConsoleUi.EscreverColorido("\n                    M A I N   F O R G E\n", ConsoleColor.Yellow);
        ConsoleUi.Detalhe("           criação de fichas de RPG com a Claude API\n");
    }

    private static IEnumerable<(string Linha, ConsoleColor Cor)> MontarPortao()
    {
        const ConsoleColor fogo = ConsoleColor.Yellow;
        const ConsoleColor pedra = ConsoleColor.DarkGray;

        // As tochas ficam centralizadas sobre cada torre.
        yield return (ParDeTochas("(@)"), fogo);
        yield return (ParDeTochas("(@@@)"), fogo);
        yield return (ParDeTochas("\\|/"), fogo);
        yield return (ParDeTochas("|"), pedra);
        yield return (ParDeTochas("_|_"), pedra);

        yield return (Linha(AmeiasDaTorre, new string(' ', LarguraDoVao)), pedra);
        yield return (Linha(SeteiraDaTorre, new string(' ', LarguraDoVao)), pedra);

        // A muralha entre as torres, e sob ela o portão propriamente dito.
        yield return (Linha(ParedeDaTorre, new string('_', LarguraDoVao)), pedra);
        yield return (Linha(SeteiraDaTorre, CentralizarNoVao("")), pedra);
        yield return (Linha(ParedeDaTorre, CentralizarNoVao(ArcoDoPortao)), pedra);
        yield return (Linha(SeteiraDaTorre, CentralizarNoVao(OmbreirasDoPortao)), pedra);
        yield return (Linha(ParedeDaTorre, CentralizarNoVao(BarraDaGrade)), pedra);
        yield return (Linha(SeteiraDaTorre, CentralizarNoVao(VaoDaGrade)), pedra);
        yield return (Linha(ParedeDaTorre, CentralizarNoVao(BarraDaGrade)), pedra);
        yield return (Linha(SeteiraDaTorre, CentralizarNoVao(VaoDaGrade)), pedra);
        yield return (Linha(BaseDaTorre, CentralizarNoVao(BaseDaGrade, preencimento: '_')), pedra);

        yield return ($"   /{new string('=', LarguraDaTorre * 2 + LarguraDoVao)}\\", pedra);
    }

    /// <summary>Uma linha completa: margem + torre + vão + torre.</summary>
    private static string Linha(string torre, string vao) =>
        new StringBuilder()
            .Append(' ', MargemEsquerda)
            .Append(torre)
            .Append(vao)
            .Append(torre)
            .ToString();

    /// <summary>Centraliza uma peça do portão dentro do vão entre as torres.</summary>
    private static string CentralizarNoVao(string peca, char preencimento = ' ')
    {
        var lateral = new string(preencimento, (LarguraDoVao - LarguraDoPortao) / 2);
        var conteudo = peca.Length == 0 ? new string(preencimento, LarguraDoPortao) : peca;

        return lateral + conteudo + lateral;
    }

    /// <summary>A mesma peça de tocha sobre o centro de cada uma das duas torres.</summary>
    private static string ParDeTochas(string peca)
    {
        var centroDaTorreEsquerda = MargemEsquerda + LarguraDaTorre / 2;
        var centroDaTorreDireita = MargemEsquerda + LarguraDaTorre + LarguraDoVao + LarguraDaTorre / 2;
        var inicio = peca.Length / 2;

        var linha = new StringBuilder(new string(' ', centroDaTorreDireita + LarguraDaTorre));

        linha.Remove(centroDaTorreEsquerda - inicio, peca.Length).Insert(centroDaTorreEsquerda - inicio, peca);
        linha.Remove(centroDaTorreDireita - inicio, peca.Length).Insert(centroDaTorreDireita - inicio, peca);

        return linha.ToString().TrimEnd();
    }
}
