using System.Globalization;
using System.Text;
using MainForge.Core;

namespace MainForge.Tools;

/// <summary>Uma ocorrência do termo procurado dentro do texto de um livro.</summary>
/// <param name="Livro">Caminho do .md convertido, relativo à raiz do projeto.</param>
/// <param name="Linha">Número da linha, para o agente abrir o trecho com <c>Read</c> e um offset.</param>
/// <param name="Secao">O título Markdown mais próximo acima da linha — diz em que parte do livro ela está.</param>
/// <param name="Trecho">A linha encontrada, encurtada.</param>
public sealed record OcorrenciaNoLivro(string Livro, int Linha, string Secao, string Trecho);

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
/// só <c>Systems/&lt;sistema&gt;/**/_texto/</c>, e o confinamento é aplicado em C#.</para>
///
/// <para>O resultado é deliberadamente magro — arquivo, linha, seção e a linha encontrada. Quem
/// decide o que abrir é o agente, com <c>Read</c> e um offset; devolver o contexto inteiro de
/// cada ocorrência traria de volta o custo que se está tentando cortar.</para>
/// </summary>
public static class BuscaNosLivros
{
    public const int MaximoPadraoDeOcorrencias = 30;

    /// <param name="livro">
    /// Nome do PDF (ou do .md) a que restringir a busca. Sem ele, procura em todos os livros do
    /// sistema.
    /// </param>
    public static IReadOnlyList<OcorrenciaNoLivro> Procurar(
        CaminhosDoProjeto caminhos,
        string sistema,
        string termo,
        string? livro = null,
        int maximo = MaximoPadraoDeOcorrencias)
    {
        if (string.IsNullOrWhiteSpace(termo))
        {
            throw new ErroDeFerramenta("Informe o termo a procurar.");
        }

        var diretorio = CaminhosDoProjeto.ResolverDentroDe(caminhos.Sistemas, sistema);

        if (!Directory.Exists(diretorio))
        {
            throw new ErroDeFerramenta($"O sistema '{sistema}' não tem livros importados.");
        }

        var textos = TextosDoSistema(diretorio, livro);

        if (textos.Count == 0)
        {
            throw new ErroDeFerramenta(
                $"Nenhum texto convertido em Systems/{sistema}/**/{ConversorDeLivros.NomeDaPastaDeTexto}/" +
                (livro is null ? "" : $" para o livro '{livro}'") +
                ". Leia o PDF com Read, ou peça ao usuário para reprocessar o sistema com o markitdown instalado.");
        }

        var procurado = SemAcento(termo.Trim());
        var achados = new List<OcorrenciaNoLivro>();

        foreach (var texto in textos)
        {
            var secao = "";
            var numero = 0;

            foreach (var linha in File.ReadLines(texto))
            {
                numero++;

                if (linha.StartsWith('#'))
                {
                    secao = linha.TrimStart('#', ' ').Trim();
                }

                if (!SemAcento(linha).Contains(procurado, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                achados.Add(new OcorrenciaNoLivro(
                    Path.GetRelativePath(caminhos.Raiz, texto).Replace('\\', '/'),
                    numero,
                    secao,
                    Encurtar(linha.Trim())));

                if (achados.Count >= maximo)
                {
                    return achados;
                }
            }
        }

        return achados;
    }

    /// <summary>O mesmo resultado já formatado para o modelo ler.</summary>
    public static string Descrever(IReadOnlyList<OcorrenciaNoLivro> ocorrencias, string termo, int maximo)
    {
        if (ocorrencias.Count == 0)
        {
            return $"Nenhuma ocorrência de '{termo}' no texto dos livros.";
        }

        var texto = new StringBuilder();

        texto.AppendLine(ocorrencias.Count >= maximo
            ? $"As primeiras {ocorrencias.Count} ocorrências de '{termo}' (pode haver mais — refine o termo):"
            : $"{ocorrencias.Count} ocorrência(s) de '{termo}':");

        foreach (var grupo in ocorrencias.GroupBy(ocorrencia => ocorrencia.Livro))
        {
            texto.AppendLine();
            texto.AppendLine(grupo.Key);

            foreach (var ocorrencia in grupo)
            {
                var secao = ocorrencia.Secao.Length > 0 ? $" [{ocorrencia.Secao}]" : "";
                texto.AppendLine($"  linha {ocorrencia.Linha}{secao}: {ocorrencia.Trecho}");
            }
        }

        texto.AppendLine();
        texto.AppendLine("Abra o trecho com Read no arquivo acima, usando offset próximo da linha indicada.");

        return texto.ToString();
    }

    private static IReadOnlyList<string> TextosDoSistema(string diretorioDoSistema, string? livro)
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

    private static string Encurtar(string linha) =>
        linha.Length <= 160 ? linha : string.Concat(linha.AsSpan(0, 160), "...");

    /// <summary>
    /// Compara ignorando acento: o agente escreve "Maos" e o livro traz "Mãos", e uma busca que
    /// falha por isso manda ele de volta a ler o PDF inteiro.
    /// </summary>
    private static string SemAcento(string texto)
    {
        var decomposto = texto.Normalize(NormalizationForm.FormD);
        var limpo = new StringBuilder(decomposto.Length);

        foreach (var caractere in decomposto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caractere) != UnicodeCategory.NonSpacingMark)
            {
                limpo.Append(caractere);
            }
        }

        return limpo.ToString().Normalize(NormalizationForm.FormC);
    }
}
