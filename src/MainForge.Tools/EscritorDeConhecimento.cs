using MainForge.Core;

namespace MainForge.Tools;

/// <summary>
/// Grava os arquivos Markdown da base de conhecimento em Sistemas/&lt;sistema&gt;/.
///
/// Existe como ferramenta MCP, e não como a ferramenta <c>Write</c> embutida do Claude Code,
/// justamente para o confinamento ser aplicado em C#: a regra "o Configurador só escreve
/// dentro de Sistemas/" é imposta por <see cref="CaminhosDoProjeto.ResolverDentroDe"/> aqui,
/// em vez de depender de uma lista de negação por caminho que precisaria antecipar todo
/// diretório que o agente poderia inventar.
///
/// <para>Passar por um único ponto tem uma segunda vantagem: é aqui que o
/// <see cref="IndiceDeConhecimento">índice</see> e o
/// <see cref="EstadoDoProcessamento">registro de progresso</see> são atualizados. O agente não
/// precisa lembrar de manter nenhum dos dois — e por isso eles não ficam desatualizados quando
/// ele esquece.</para>
/// </summary>
public static class EscritorDeConhecimento
{
    /// <summary>
    /// Cria ou sobrescreve um .md dentro de Sistemas/&lt;sistema&gt;/, criando os diretórios
    /// intermediários. Devolve o caminho gravado, relativo à raiz do projeto.
    /// </summary>
    /// <param name="resumo">
    /// Uma linha dizendo o que há no arquivo. Vira a descrição dele no <c>index.md</c> da
    /// pasta, que é o que outro agente lê para decidir se precisa abrir este arquivo.
    /// </param>
    /// <param name="livro">Livro de origem do conteúdo, quando vem de um compêndio/expansão.</param>
    public static async Task<string> EscreverAsync(
        CaminhosDoProjeto caminhos,
        string sistema,
        string caminhoRelativo,
        string conteudo,
        string? resumo = null,
        string? livro = null,
        CancellationToken cancelamento = default)
    {
        if (!caminhoRelativo.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            throw new ErroDeFerramenta($"'{caminhoRelativo}' precisa terminar em .md.");
        }

        if (Path.GetFileName(caminhoRelativo).Equals(IndiceDeConhecimento.NomeDoArquivo, StringComparison.OrdinalIgnoreCase))
        {
            throw new ErroDeFerramenta(
                $"'{IndiceDeConhecimento.NomeDoArquivo}' é gerado automaticamente a partir do conteúdo da pasta — " +
                "não grave um você mesmo. Para descrever uma pasta, use descrever_pasta_de_conhecimento; " +
                "para descrever um arquivo, informe o 'resumo' ao gravá-lo.");
        }

        var diretorioSistema = CaminhosDoProjeto.ResolverDentroDe(caminhos.Conhecimento, sistema);
        var caminhoArquivo = CaminhosDoProjeto.ResolverDentroDe(diretorioSistema, caminhoRelativo);

        Directory.CreateDirectory(Path.GetDirectoryName(caminhoArquivo)!);
        await File.WriteAllTextAsync(caminhoArquivo, conteudo, cancelamento);

        var relativoAoSistema = Path.GetRelativePath(diretorioSistema, caminhoArquivo).Replace('\\', '/');

        IndiceDeConhecimento.Reconstruir(
            caminhos,
            sistema,
            resumo is { Length: > 0 } ? new Dictionary<string, string> { [relativoAoSistema] = resumo } : null);

        var estado = EstadoDoProcessamento.Carregar(caminhos, sistema);
        estado.MarcarArquivo(relativoAoSistema, resumo, livro);
        estado.Salvar();

        return Path.GetRelativePath(caminhos.Raiz, caminhoArquivo);
    }

    /// <summary>
    /// Registra a descrição de uma pasta no <c>index.md</c> dela. É o texto que diz "aqui
    /// dentro estão as classes jogáveis" antes da lista de arquivos.
    /// </summary>
    public static IReadOnlyList<string> DescreverPasta(
        CaminhosDoProjeto caminhos,
        string sistema,
        string pastaRelativa,
        string descricao)
    {
        var diretorioSistema = CaminhosDoProjeto.ResolverDentroDe(caminhos.Conhecimento, sistema);
        var diretorio = CaminhosDoProjeto.ResolverDentroDe(diretorioSistema, pastaRelativa);

        if (!Directory.Exists(diretorio))
        {
            throw new ErroDeFerramenta(
                $"A pasta '{pastaRelativa}' ainda não existe em Sistemas/{sistema}/ — " +
                "grave um arquivo dentro dela antes de descrevê-la.");
        }

        var relativoAoSistema = Path.GetRelativePath(diretorioSistema, diretorio).Replace('\\', '/');

        return IndiceDeConhecimento.Reconstruir(
            caminhos,
            sistema,
            new Dictionary<string, string> { [relativoAoSistema == "." ? "" : relativoAoSistema] = descricao });
    }
}
