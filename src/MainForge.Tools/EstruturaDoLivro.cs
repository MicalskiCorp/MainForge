using System.Text;
using MainForge.Core;

namespace MainForge.Tools;

/// <summary>Um título encontrado no texto de um livro.</summary>
/// <param name="Nivel">Profundidade do título: 1 para <c>#</c>, 2 para <c>##</c>, e assim por diante.</param>
/// <param name="Titulo">O texto do título, sem os cerquilhas.</param>
/// <param name="Linha">Em que linha ele está — é o <c>offset</c> do <c>Read</c>.</param>
public sealed record TituloDoLivro(int Nivel, string Titulo, int Linha);

/// <summary>
/// O sumário de um livro convertido: os títulos do Markdown com o número da linha de cada um.
///
/// <para><b>Por que existe.</b> O aplicativo resolveu esse problema de um lado e não do outro. A
/// base de conhecimento tem um <c>index.md</c> por pasta justamente para o agente decidir o que
/// abrir sem abrir tudo; os livros em <c>Input/</c>, que são muito maiores, não tinham nada. O
/// único mapa de um manual de 30 mil linhas era adivinhar um termo de busca e torcer.</para>
///
/// <para>Um sumário custa algumas centenas de tokens e substitui as leituras exploratórias — as
/// caras, aquelas em que o agente abre um trecho para descobrir que o assunto está em outro
/// lugar. Com ele, "procure 'carga'" vira "abra Equipamentos, linha 8420".</para>
///
/// <para>Funciona com os dois conversores: o markitdown produz os títulos do próprio livro e o
/// <see cref="ExtratorDeTextoDePdf"/> produz um <c>## Página N</c> por página. No segundo caso o
/// sumário é o mapa de páginas, que é menos, mas ainda é a ponte para o PDF original.</para>
/// </summary>
public static class EstruturaDoLivro
{
    /// <summary>
    /// Acima disto o sumário deixa de ser um resumo. Um livro com milhares de títulos quase
    /// sempre foi convertido pelo extrator interno (um por página); mostrar todos custaria o que
    /// o sumário existe para poupar.
    /// </summary>
    public const int MaximoPadraoDeTitulos = 300;

    /// <param name="livro">
    /// Nome do PDF (ou do .md) a que restringir. Sem ele, devolve o sumário de todos os livros
    /// do sistema.
    /// </param>
    public static IReadOnlyList<(string Livro, IReadOnlyList<TituloDoLivro> Titulos)> Levantar(
        CaminhosDoProjeto caminhos,
        string sistema,
        string? livro = null,
        int nivelMaximo = 6,
        int maximo = MaximoPadraoDeTitulos)
    {
        var diretorio = CaminhosDoProjeto.ResolverDentroDe(caminhos.Entrada, sistema);

        if (!Directory.Exists(diretorio))
        {
            throw new ErroDeFerramenta($"O sistema '{sistema}' não tem livros importados.");
        }

        var textos = BuscaNosLivros.TextosDoSistema(diretorio, livro);

        if (textos.Count == 0)
        {
            throw new ErroDeFerramenta(
                $"Nenhum texto convertido em Input/{sistema}/**/{ConversorDeLivros.NomeDaPastaDeTexto}/" +
                (livro is null ? "" : $" para o livro '{livro}'") +
                ". Sem o texto não há sumário: leia o PDF com Read, ou peça ao usuário para reprocessar o sistema.");
        }

        return
        [
            .. textos.Select(texto => (
                Livro: Path.GetRelativePath(caminhos.Raiz, texto).Replace('\\', '/'),
                Titulos: TitulosDe(texto, nivelMaximo, maximo))),
        ];
    }

    /// <summary>O sumário formatado para o modelo ler, indentado por nível.</summary>
    public static string Descrever(
        IReadOnlyList<(string Livro, IReadOnlyList<TituloDoLivro> Titulos)> livros,
        int maximo)
    {
        var texto = new StringBuilder();

        foreach (var (livro, titulos) in livros)
        {
            texto.AppendLine(livro);

            if (titulos.Count == 0)
            {
                texto.AppendLine("  (sem títulos: a conversão não preservou a estrutura deste livro — " +
                                 "use procurar_no_texto_dos_livros para achar o assunto)");
                texto.AppendLine();
                continue;
            }

            foreach (var titulo in titulos)
            {
                texto.AppendLine($"{new string(' ', 2 * titulo.Nivel)}linha {titulo.Linha}: {titulo.Titulo}");
            }

            if (titulos.Count >= maximo)
            {
                texto.AppendLine($"  (parou em {maximo} títulos — peça um nivelMaximo menor para ver só os principais)");
            }

            texto.AppendLine();
        }

        texto.AppendLine(
            "Abra o trecho que interessa com Read no arquivo, usando offset na linha do título. " +
            "Não leia o arquivo inteiro.");

        return texto.ToString();
    }

    private static IReadOnlyList<TituloDoLivro> TitulosDe(string caminho, int nivelMaximo, int maximo)
    {
        var titulos = new List<TituloDoLivro>();
        var numero = 0;
        var dentroDeBlocoDeCodigo = false;

        try
        {
            foreach (var linha in File.ReadLines(caminho))
            {
                numero++;

                // Cerquilha dentro de bloco de código é comentário de exemplo, não título — e
                // livro de RPG convertido tem bloco de código onde havia quadro ou tabela.
                if (linha.StartsWith("```", StringComparison.Ordinal))
                {
                    dentroDeBlocoDeCodigo = !dentroDeBlocoDeCodigo;
                    continue;
                }

                if (dentroDeBlocoDeCodigo || !linha.StartsWith('#'))
                {
                    continue;
                }

                var nivel = linha.TakeWhile(caractere => caractere == '#').Count();
                var texto = linha.TrimStart('#', ' ').Trim();

                if (nivel > nivelMaximo || texto.Length == 0)
                {
                    continue;
                }

                titulos.Add(new TituloDoLivro(nivel, texto, numero));

                if (titulos.Count >= maximo)
                {
                    break;
                }
            }
        }
        catch (IOException)
        {
            return [];
        }

        return titulos;
    }
}
