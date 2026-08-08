using System.Text;

namespace MainForge.Tools;

/// <summary>Uma ocorrência do termo procurado dentro de um arquivo de texto.</summary>
/// <param name="Arquivo">Caminho do .md, relativo à raiz do projeto — é o que vai no <c>Read</c>.</param>
/// <param name="Linha">Número da linha, para abrir o trecho com um <c>offset</c>.</param>
/// <param name="Secao">O título Markdown mais próximo acima da linha.</param>
/// <param name="Trecho">A linha encontrada, encurtada.</param>
/// <param name="Contexto">
/// As linhas ao redor, quando quem chamou pediu. É o que dispensa a leitura seguinte.
/// </param>
public sealed record Ocorrencia(
    string Arquivo,
    int Linha,
    string Secao,
    string Trecho,
    string? Contexto = null);

/// <summary>
/// O motor das duas buscas do aplicativo — a que procura nos livros convertidos e a que procura
/// na base de conhecimento.
///
/// <para><b>Por que existe.</b> As duas eram o mesmo laço escrito duas vezes: percorrer linhas,
/// acompanhar o título mais recente, comparar sem acento, parar no máximo. Duplicado, qualquer
/// melhoria numa delas — como o contexto — precisava ser lembrada na outra.</para>
///
/// <para><b>Por que o contexto passou a existir.</b> O resultado era só a linha achada, e o
/// agente tinha que fazer um <c>Read</c> com <c>offset</c> em seguida: duas chamadas para uma
/// pergunta. A conta que justificava isso estava invertida. Cada chamada de ferramenta é um turno,
/// e um turno reenvia a conversa inteira ao modelo — numa sessão que já acumulou dezenas de
/// milhares de tokens, o round-trip evitado vale muito mais que as poucas dezenas de linhas de
/// contexto que o evitam. Devolver o trecho junto é mais barato que mandar buscá-lo.</para>
/// </summary>
public static class BuscaEmTexto
{
    /// <summary>Quantas linhas ao redor devolver quando quem chama não escolhe.</summary>
    public const int ContextoPadrao = 0;

    /// <summary>
    /// Teto de linhas de contexto por ocorrência. Existe para o contexto não desfazer a economia
    /// que ele veio fazer: com 30 ocorrências, um pedido de 200 linhas cada devolveria mais texto
    /// do que o arquivo inteiro que se estava tentando não ler.
    /// </summary>
    public const int ContextoMaximo = 40;

    public static IReadOnlyList<Ocorrencia> Procurar(
        IEnumerable<string> arquivos,
        Func<string, string> rotular,
        string termo,
        int maximo,
        int linhasDeContexto = ContextoPadrao)
    {
        var procurado = TextoNormalizado.SemAcento(termo.Trim());
        var contexto = Math.Clamp(linhasDeContexto, 0, ContextoMaximo);
        var achados = new List<Ocorrencia>();

        foreach (var arquivo in arquivos)
        {
            string[] linhas;

            try
            {
                linhas = File.ReadAllLines(arquivo);
            }
            catch (IOException)
            {
                // Um arquivo preso por outro processo não derruba a busca nos outros.
                continue;
            }

            var secao = "";

            for (var indice = 0; indice < linhas.Length; indice++)
            {
                var linha = linhas[indice];

                if (linha.StartsWith('#'))
                {
                    secao = linha.TrimStart('#', ' ').Trim();
                }

                if (!TextoNormalizado.SemAcento(linha).Contains(procurado, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                achados.Add(new Ocorrencia(
                    rotular(arquivo),
                    indice + 1,
                    secao,
                    Encurtar(linha.Trim()),
                    contexto == 0 ? null : Redondezas(linhas, indice, contexto)));

                if (achados.Count >= maximo)
                {
                    return achados;
                }
            }
        }

        return achados;
    }

    /// <summary>
    /// As linhas em volta da ocorrência, com a metade do orçamento para cada lado. Linhas em
    /// branco nas pontas saem: elas gastariam parte do contexto sem dizer nada.
    /// </summary>
    private static string Redondezas(string[] linhas, int indice, int total)
    {
        var antes = total / 2;
        var inicio = Math.Max(0, indice - antes);
        var fim = Math.Min(linhas.Length - 1, inicio + total);

        while (inicio < indice && linhas[inicio].Trim().Length == 0)
        {
            inicio++;
        }

        while (fim > indice && linhas[fim].Trim().Length == 0)
        {
            fim--;
        }

        var texto = new StringBuilder();

        for (var atual = inicio; atual <= fim; atual++)
        {
            texto.AppendLine(linhas[atual]);
        }

        return texto.ToString().TrimEnd();
    }

    /// <summary>
    /// O resultado formatado para o modelo ler, agrupado por arquivo.
    /// </summary>
    /// <param name="ondeProcurou">Como descrever o que foi vasculhado, ex.: "no texto dos livros".</param>
    /// <param name="comoSeguir">A linha final dizendo o que fazer com o resultado.</param>
    public static string Descrever(
        IReadOnlyList<Ocorrencia> ocorrencias,
        string termo,
        int maximo,
        string ondeProcurou,
        string comoSeguir)
    {
        if (ocorrencias.Count == 0)
        {
            return $"Nenhuma ocorrência de '{termo}' {ondeProcurou}.";
        }

        var texto = new StringBuilder();

        texto.AppendLine(ocorrencias.Count >= maximo
            ? $"As primeiras {ocorrencias.Count} ocorrências de '{termo}' (pode haver mais — refine o termo):"
            : $"{ocorrencias.Count} ocorrência(s) de '{termo}':");

        foreach (var grupo in ocorrencias.GroupBy(ocorrencia => ocorrencia.Arquivo))
        {
            texto.AppendLine();
            texto.AppendLine(grupo.Key);

            foreach (var ocorrencia in grupo)
            {
                var secao = ocorrencia.Secao.Length > 0 ? $" [{ocorrencia.Secao}]" : "";

                if (ocorrencia.Contexto is not { Length: > 0 } redondezas)
                {
                    texto.AppendLine($"  linha {ocorrencia.Linha}{secao}: {ocorrencia.Trecho}");
                    continue;
                }

                texto.AppendLine($"  linha {ocorrencia.Linha}{secao}:");
                texto.AppendLine("  ```");

                foreach (var linha in redondezas.ReplaceLineEndings("\n").Split('\n'))
                {
                    texto.AppendLine($"  {linha}");
                }

                texto.AppendLine("  ```");
            }
        }

        texto.AppendLine();
        texto.AppendLine(comoSeguir);

        return texto.ToString();
    }

    private static string Encurtar(string linha) =>
        linha.Length <= 160 ? linha : string.Concat(linha.AsSpan(0, 160), "...");
}
