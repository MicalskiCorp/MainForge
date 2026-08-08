using System.Text;

namespace MainForge.Tools;

/// <summary>Quanto sobrou depois da limpeza, para o console dizer se ela valeu.</summary>
/// <param name="LinhasRemovidas">Linhas de cabeçalho, rodapé ou número de página que saíram.</param>
/// <param name="CaracteresAntes">Tamanho do texto convertido, antes.</param>
/// <param name="CaracteresDepois">Tamanho depois.</param>
public sealed record ResultadoDaLimpeza(int LinhasRemovidas, int CaracteresAntes, int CaracteresDepois)
{
    public double ProporcaoRemovida =>
        CaracteresAntes == 0 ? 0 : 1 - ((double)CaracteresDepois / CaracteresAntes);
}

/// <summary>
/// Enxuga o Markdown recém-extraído de um livro, antes de ele virar a coisa que o agente lê
/// dezenas de vezes.
///
/// <para><b>Por que vale a pena.</b> Um livro de RPG de 300 páginas repete o título da obra no
/// alto de cada página, o nome do capítulo no rodapé e o número da página solto numa linha — e a
/// extração traz tudo isso, página por página. É conteúdo que não diz nada e que o agente relê a
/// cada trecho aberto, durante o processamento inteiro. A conversão hifeniza palavras no fim da
/// linha, e a busca por "resistência" não acha "resis-\ntência".</para>
///
/// <para><b>Por que aqui e não no prompt.</b> Isto roda na máquina do usuário, uma vez por livro,
/// de graça. Pedir ao modelo para ignorar cabeçalho e rodapé custaria cota em toda leitura para
/// resolver o mesmo problema pior.</para>
///
/// <para><b>Conservadora de propósito.</b> Duas condições, e as duas precisam valer: a linha se
/// repete em pelo menos um quinto das páginas <em>e</em> aparece sempre na borda delas — nas
/// primeiras ou nas últimas linhas do bloco. A frequência sozinha não basta, e o teste que provou
/// isso está escrito: uma regra que aparecesse repetida em muitas páginas seria apagada. Cabeçalho
/// e rodapé, por definição, moram na borda; regra mora no meio.</para>
///
/// <para><b>Sem marcação de página, só o seguro.</b> Reconhecer a borda depende de saber onde a
/// página começa, e isso só existe quando o texto veio do <see cref="ExtratorDeTextoDePdf"/>, que
/// escreve <c>## Página N</c>. No texto do markitdown, que não marca página, a remoção de
/// cabeçalho não roda — sobram a rejunção de hifenização e o colapso de linhas em branco, que não
/// dependem de posição nenhuma. Limpar menos é a resposta certa: perder uma linha de regra é pior
/// do que carregar um rodapé.</para>
/// </summary>
public static class LimpezaDoTextoDoLivro
{
    /// <summary>
    /// Abaixo disto não há repetição que se possa distinguir de coincidência, e a limpeza não
    /// roda: um livro de 5 páginas não tem cabeçalho detectável por frequência.
    /// </summary>
    private const int MinimoDePaginasParaDetectar = 8;

    /// <summary>
    /// Em que fração das páginas uma linha precisa aparecer para contar como cabeçalho ou rodapé.
    /// Um quinto é folgado: cobre o livro que alterna o cabeçalho entre página par e ímpar, e
    /// ainda fica muito acima de qualquer repetição legítima de regra.
    /// </summary>
    private const double FrequenciaDeCabecalho = 0.2;

    /// <summary>Linha maior que isto é texto, não cabeçalho — nenhum rodapé tem 90 caracteres.</summary>
    private const int TamanhoMaximoDeCabecalho = 90;

    /// <summary>
    /// Quantas linhas, de cada ponta da página, contam como borda. Duas cobrem o caso comum
    /// (título da obra e nome do capítulo no alto, capítulo e número embaixo) sem alcançar o
    /// primeiro parágrafo de conteúdo.
    /// </summary>
    private const int LinhasDeBorda = 2;

    /// <summary>Como o extrator interno marca o começo de cada página.</summary>
    private const string MarcaDePagina = "## Página ";

    /// <summary>
    /// Limpa o arquivo no lugar. Devolve o que saiu, ou <c>null</c> se não houve nada a fazer.
    /// </summary>
    public static ResultadoDaLimpeza? Limpar(string caminhoDoTexto)
    {
        string original;

        try
        {
            original = File.ReadAllText(caminhoDoTexto);
        }
        catch (IOException)
        {
            return null;
        }

        var limpo = Aplicar(original);

        if (limpo.Texto == original)
        {
            return null;
        }

        try
        {
            File.WriteAllText(caminhoDoTexto, limpo.Texto, new UTF8Encoding(false));
        }
        catch (IOException)
        {
            return null;
        }

        return new ResultadoDaLimpeza(limpo.LinhasRemovidas, original.Length, limpo.Texto.Length);
    }

    /// <summary>A limpeza como transformação pura — é por aqui que os testes entram.</summary>
    public static (string Texto, int LinhasRemovidas) Aplicar(string texto)
    {
        var linhas = texto.ReplaceLineEndings("\n").Split('\n');
        var aRemover = CabecalhosERodapes(linhas);

        var resultado = new StringBuilder();
        var removidas = 0;
        var emBrancoSeguidas = 0;
        var dentroDeBlocoDeCodigo = false;

        for (var indice = 0; indice < linhas.Length; indice++)
        {
            var linha = linhas[indice];
            var conteudo = linha.Trim();

            if (conteudo.StartsWith("```", StringComparison.Ordinal))
            {
                dentroDeBlocoDeCodigo = !dentroDeBlocoDeCodigo;
            }

            if (aRemover.Contains(indice))
            {
                removidas++;
                continue;
            }

            if (conteudo.Length == 0)
            {
                // Mais de uma linha em branco seguida não separa nada que uma já não separe.
                if (++emBrancoSeguidas > 1)
                {
                    continue;
                }

                resultado.Append('\n');
                continue;
            }

            emBrancoSeguidas = 0;

            // Palavra partida no fim da linha: junta com a seguinte, senão a busca por
            // "resistência" nunca acha "resis-" seguido de "tência".
            if (!dentroDeBlocoDeCodigo &&
                TerminaEmHifenDePalavra(conteudo) &&
                indice + 1 < linhas.Length &&
                ComecaComLetraMinuscula(linhas[indice + 1].TrimStart()))
            {
                var seguinte = linhas[indice + 1].TrimStart();
                var corte = seguinte.IndexOf(' ');

                resultado.Append(conteudo[..^1]);
                resultado.Append(corte < 0 ? seguinte : seguinte[..corte]);
                resultado.Append('\n');

                linhas[indice + 1] = corte < 0 ? "" : seguinte[(corte + 1)..];
                continue;
            }

            resultado.Append(linha.TrimEnd());
            resultado.Append('\n');
        }

        return (resultado.ToString().Trim() + "\n", removidas);
    }

    /// <summary>
    /// Quais linhas, por posição no arquivo, são cabeçalho ou rodapé.
    ///
    /// <para>Duas condições precisam valer ao mesmo tempo: a linha está na <b>borda</b> de uma
    /// página (nas primeiras ou nas últimas linhas do bloco) e o mesmo texto aparece na borda de
    /// muitas outras. Um número de página solto conta pela posição, sem precisar se repetir —
    /// ele muda a cada página, por definição.</para>
    ///
    /// <para>A frequência sozinha não serve, e isso não é teoria: uma regra que se repetisse em
    /// várias páginas — uma nota de rodapé de tabela, um aviso recorrente — seria apagada por
    /// ela. A borda é o que separa o que emoldura a página do que está escrito nela.</para>
    ///
    /// <para>Sem <c>## Página N</c> não há borda que reconhecer, e nada é removido.</para>
    /// </summary>
    private static HashSet<int> CabecalhosERodapes(string[] linhas)
    {
        var paginas = Paginas(linhas);

        if (paginas.Count < MinimoDePaginasParaDetectar)
        {
            return [];
        }

        // Candidata: uma linha de borda, curta e que não seja título nem tabela. Guardadas por
        // texto para a contagem, e por posição para a remoção.
        var porTexto = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var numerosDePagina = new HashSet<int>();

        foreach (var (inicio, fim) in paginas)
        {
            var uteis = Uteis(linhas, inicio, fim);

            foreach (var indice in Bordas(uteis))
            {
                var conteudo = linhas[indice].Trim();

                if (EhSoNumeroDePagina(conteudo))
                {
                    numerosDePagina.Add(indice);
                    continue;
                }

                if (conteudo.Length > TamanhoMaximoDeCabecalho)
                {
                    continue;
                }

                if (!porTexto.TryGetValue(conteudo, out var ocorrencias))
                {
                    porTexto[conteudo] = ocorrencias = [];
                }

                ocorrencias.Add(indice);
            }
        }

        var limite = Math.Max(3, (int)(paginas.Count * FrequenciaDeCabecalho));

        return
        [
            .. numerosDePagina,
            .. porTexto.Values.Where(ocorrencias => ocorrencias.Count >= limite).SelectMany(ocorrencias => ocorrencias),
        ];
    }

    /// <summary>Onde cada página começa e termina, pela marcação do extrator interno.</summary>
    private static List<(int Inicio, int Fim)> Paginas(string[] linhas)
    {
        var marcas = new List<int>();

        for (var indice = 0; indice < linhas.Length; indice++)
        {
            if (linhas[indice].TrimStart().StartsWith(MarcaDePagina, StringComparison.OrdinalIgnoreCase))
            {
                marcas.Add(indice);
            }
        }

        return
        [
            .. marcas.Select((inicio, ordem) => (
                Inicio: inicio + 1,
                Fim: ordem + 1 < marcas.Count ? marcas[ordem + 1] - 1 : linhas.Length - 1)),
        ];
    }

    /// <summary>
    /// As linhas de conteúdo de uma página, na ordem: sem as vazias, sem título e sem tabela.
    /// São elas que têm borda — linha em branco no alto da página não é cabeçalho, é espaço.
    /// </summary>
    private static List<int> Uteis(string[] linhas, int inicio, int fim)
    {
        var uteis = new List<int>();

        for (var indice = inicio; indice <= fim && indice < linhas.Length; indice++)
        {
            var conteudo = linhas[indice].Trim();

            if (conteudo.Length > 0 && !conteudo.StartsWith('#') && !conteudo.StartsWith('|'))
            {
                uteis.Add(indice);
            }
        }

        return uteis;
    }

    /// <summary>
    /// As primeiras e as últimas linhas de uma página. Numa página curta demais para ter borda e
    /// miolo, nada é candidato: remover ali apagaria a página inteira.
    /// </summary>
    private static IEnumerable<int> Bordas(List<int> uteis) =>
        uteis.Count <= LinhasDeBorda * 2
            ? []
            : uteis.Take(LinhasDeBorda).Concat(uteis.TakeLast(LinhasDeBorda));

    /// <summary>
    /// Uma linha que só tem o número da página. Aceita as formas comuns ("42", "- 42 -",
    /// "Pagina 42") e nada além disso: um valor numérico solto numa tabela de regras não vem
    /// sozinho na linha.
    /// </summary>
    private static bool EhSoNumeroDePagina(string conteudo)
    {
        var limpo = conteudo.Trim('-', '—', '–', '|', '*', '_', '.', ' ');

        if (limpo.Length == 0 || limpo.Length > 20)
        {
            return false;
        }

        foreach (var prefixo in new[] { "pagina ", "página ", "page ", "pag. ", "p. " })
        {
            if (limpo.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase))
            {
                limpo = limpo[prefixo.Length..].Trim();
                break;
            }
        }

        return limpo.Length > 0 && limpo.All(char.IsAsciiDigit);
    }

    private static bool TerminaEmHifenDePalavra(string conteudo) =>
        conteudo.Length >= 3 &&
        conteudo[^1] == '-' &&
        char.IsLetter(conteudo[^2]);

    private static bool ComecaComLetraMinuscula(string conteudo) =>
        conteudo.Length > 0 && char.IsLower(conteudo[0]);
}
