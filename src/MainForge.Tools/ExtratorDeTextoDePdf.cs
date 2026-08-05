using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace MainForge.Tools;

/// <summary>
/// Extrai o texto de um PDF em C#, sem programa externo nenhum.
///
/// <para><b>Por que existe, se já há o markitdown.</b> Porque sem ele o aplicativo simplesmente
/// não funcionava. O <c>Read</c> do Claude Code rasteriza as páginas do PDF, e para isso depende
/// do <c>pdftoppm</c> (poppler); numa máquina sem poppler, ler um livro grande falha com
/// "pdftoppm is not installed". Sem poppler e sem markitdown — o caso de um Windows recém
/// instalado, sem Python —, o agente ficava sem <em>nenhum</em> caminho até o livro: o PDF não
/// abria e o texto não existia.</para>
///
/// <para>Uma dependência que o usuário precisa instalar não pode ser o único caminho para a
/// operação central do produto. O markitdown continua sendo o preferido (lida melhor com tabelas
/// e estrutura); este extrator é o piso, e vem junto com o aplicativo.</para>
///
/// <para>A saída marca cada página com um título (<c>## Página 42</c>). Não é enfeite: é o que
/// dá à busca uma seção para mostrar, e o que permite ao agente citar de onde tirou a regra —
/// além de ser a ponte para o PDF original quando alguém precisar conferir.</para>
/// </summary>
public static class ExtratorDeTextoDePdf
{
    /// <summary>
    /// Abaixo disto o "texto" não é texto: é um livro digitalizado (páginas que são só imagem),
    /// em que a extração devolve meia dúzia de caracteres soltos por página. Gravar isso seria
    /// pior que não gravar nada — o agente leria um livro em branco achando que ele não diz nada,
    /// em vez de cair no PDF.
    /// </summary>
    private const int MinimoDeCaracteresPorPagina = 40;

    /// <summary>
    /// Lê <paramref name="caminhoDoPdf"/> e grava o texto em <paramref name="destino"/>.
    /// Devolve o número de páginas aproveitadas.
    /// </summary>
    /// <exception cref="ErroDeFerramenta">
    /// O PDF não abre (protegido, corrompido) ou não tem texto extraível.
    /// </exception>
    public static int ExtrairPara(string caminhoDoPdf, string destino, CancellationToken cancelamento = default)
    {
        var texto = new StringBuilder();
        var paginasComTexto = 0;
        var caracteres = 0;
        int totalDePaginas;

        try
        {
            using var documento = PdfDocument.Open(caminhoDoPdf);

            totalDePaginas = documento.NumberOfPages;

            texto.AppendLine($"# {Path.GetFileNameWithoutExtension(caminhoDoPdf)}");
            texto.AppendLine();
            texto.AppendLine(
                $"> Texto extraído de {Path.GetFileName(caminhoDoPdf)} ({totalDePaginas} páginas) pelo " +
                "extrator interno do MainForge. Os títulos de página abaixo correspondem às páginas do PDF.");
            texto.AppendLine();

            for (var numero = 1; numero <= totalDePaginas; numero++)
            {
                cancelamento.ThrowIfCancellationRequested();

                var conteudo = TextoDaPagina(documento, numero);

                if (conteudo.Length == 0)
                {
                    continue;
                }

                paginasComTexto++;
                caracteres += conteudo.Length;

                texto.AppendLine($"## Página {numero}");
                texto.AppendLine();
                texto.AppendLine(conteudo);
                texto.AppendLine();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception excecao) when (excecao is not ErroDeFerramenta)
        {
            throw new ErroDeFerramenta($"não foi possível abrir o PDF: {excecao.Message}", excecao);
        }

        if (totalDePaginas == 0 || caracteres < MinimoDeCaracteresPorPagina * Math.Max(1, totalDePaginas / 2))
        {
            throw new ErroDeFerramenta(
                "o PDF quase não tem texto extraível — provavelmente é digitalizado (páginas que são só " +
                "imagem). Um livro assim precisa de OCR, que este aplicativo não faz.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        File.WriteAllText(destino, texto.ToString(), new UTF8Encoding(false));

        return paginasComTexto;
    }

    /// <summary>
    /// Uma página que não se deixa ler não derruba o livro inteiro: ela some do texto e as outras
    /// 300 continuam valendo. Perder uma página é um buraco no conteúdo; perder o livro é o
    /// processamento inteiro.
    /// </summary>
    private static string TextoDaPagina(PdfDocument documento, int numero)
    {
        try
        {
            return ContentOrderTextExtractor.GetText(documento.GetPage(numero))?.Trim() ?? "";
        }
        catch (Exception excecao) when (excecao is not OutOfMemoryException)
        {
            return "";
        }
    }
}
