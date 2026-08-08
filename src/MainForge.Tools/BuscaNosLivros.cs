using MainForge.Core;

namespace MainForge.Tools;

/// <summary>
/// Procura um termo no texto que <see cref="ConversorDeLivros"/> extraiu dos livros de um
/// sistema.
///
/// <para><b>Por que é uma ferramenta nossa e não a <c>Grep</c> do Claude Code.</b> Buscar é
/// exatamente o que falta ao Configurador depois que o livro vira Markdown: um manual de 300
/// páginas dá dezenas de milhares de linhas, e sem busca a única forma de achar a tabela de
/// magias é ler o arquivo inteiro — que é o custo que a conversão existia para eliminar. As
/// ferramentas de busca embutidas (<c>Grep</c>, <c>Bash</c>, <c>PowerShell</c>) continuam negadas
/// a todos os agentes, porque cada uma delas alcança qualquer arquivo da máquina; esta alcança
/// só <c>Input/&lt;sistema&gt;/**/_texto/</c>, e o confinamento é aplicado em C#.</para>
///
/// <para><b>O resultado pode vir com contexto.</b> Antes ele era só arquivo, linha e a linha
/// achada, e o agente precisava de um <c>Read</c> em seguida: duas chamadas para uma pergunta. Com
/// as linhas ao redor no próprio resultado, o caso comum vira uma chamada — e um turno a menos
/// vale mais que o texto que ele economizaria, porque cada turno reenvia a conversa inteira.</para>
/// </summary>
public static class BuscaNosLivros
{
    public const int MaximoPadraoDeOcorrencias = 30;

    /// <param name="livro">
    /// Nome do PDF (ou do .md) a que restringir a busca. Sem ele, procura em todos os livros do
    /// sistema.
    /// </param>
    /// <param name="linhasDeContexto">
    /// Quantas linhas ao redor de cada ocorrência devolver. Zero devolve só a linha achada.
    /// </param>
    public static IReadOnlyList<Ocorrencia> Procurar(
        CaminhosDoProjeto caminhos,
        string sistema,
        string termo,
        string? livro = null,
        int maximo = MaximoPadraoDeOcorrencias,
        int linhasDeContexto = BuscaEmTexto.ContextoPadrao)
    {
        if (string.IsNullOrWhiteSpace(termo))
        {
            throw new ErroDeFerramenta("Informe o termo a procurar.");
        }

        var diretorio = CaminhosDoProjeto.ResolverDentroDe(caminhos.Entrada, sistema);

        if (!Directory.Exists(diretorio))
        {
            throw new ErroDeFerramenta($"O sistema '{sistema}' não tem livros importados.");
        }

        var textos = TextosDoSistema(diretorio, livro);

        if (textos.Count == 0)
        {
            throw new ErroDeFerramenta(
                $"Nenhum texto convertido em Input/{sistema}/**/{ConversorDeLivros.NomeDaPastaDeTexto}/" +
                (livro is null ? "" : $" para o livro '{livro}'") +
                ". Leia o PDF com Read, ou peça ao usuário para reprocessar o sistema com o markitdown instalado.");
        }

        return BuscaEmTexto.Procurar(
            textos,
            arquivo => Path.GetRelativePath(caminhos.Raiz, arquivo).Replace('\\', '/'),
            termo,
            maximo,
            linhasDeContexto);
    }

    /// <summary>O mesmo resultado já formatado para o modelo ler.</summary>
    public static string Descrever(IReadOnlyList<Ocorrencia> ocorrencias, string termo, int maximo) =>
        BuscaEmTexto.Descrever(
            ocorrencias,
            termo,
            maximo,
            "no texto dos livros",
            ocorrencias.Any(ocorrencia => ocorrencia.Contexto is { Length: > 0 })
                ? "O trecho de cada ocorrência está acima. Só abra o arquivo com Read se precisar de mais do que isso."
                : "Abra o trecho com Read no arquivo acima, usando offset próximo da linha indicada — " +
                  "ou repita a busca com 'contexto' para receber as linhas em volta sem outra chamada.");

    public static IReadOnlyList<string> TextosDoSistema(string diretorioDoSistema, string? livro)
    {
        var alvo = livro is null ? null : Path.GetFileNameWithoutExtension(livro.Trim());

        return
        [
            .. Directory
                .EnumerateDirectories(diretorioDoSistema, ConversorDeLivros.NomeDaPastaDeTexto, SearchOption.AllDirectories)
                .SelectMany(pasta => Directory.EnumerateFiles(pasta, "*.md"))
                .Where(caminho => alvo is null ||
                                  Path.GetFileNameWithoutExtension(caminho).Equals(alvo, StringComparison.OrdinalIgnoreCase))
                .Order(),
        ];
    }
}
