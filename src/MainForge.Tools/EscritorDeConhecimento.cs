using MainForge.Core;

namespace MainForge.Tools;

/// <summary>
/// Grava os arquivos Markdown da base de conhecimento em Knowledge/&lt;sistema&gt;/.
///
/// Existe como ferramenta MCP, e não como a ferramenta <c>Write</c> embutida do Claude Code,
/// justamente para o confinamento ser aplicado em C#: a regra "o Configurador só escreve
/// dentro de Knowledge/" é imposta por <see cref="CaminhosDoProjeto.ResolverDentroDe"/> aqui,
/// em vez de depender de uma lista de negação por caminho que precisaria antecipar todo
/// diretório que o agente poderia inventar.
/// </summary>
public static class EscritorDeConhecimento
{
    /// <summary>
    /// Cria ou sobrescreve um .md dentro de Knowledge/&lt;sistema&gt;/, criando os diretórios
    /// intermediários. Devolve o caminho gravado, relativo à raiz do projeto.
    /// </summary>
    public static async Task<string> EscreverAsync(
        CaminhosDoProjeto caminhos,
        string sistema,
        string caminhoRelativo,
        string conteudo,
        CancellationToken cancelamento = default)
    {
        if (!caminhoRelativo.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            throw new ErroDeFerramenta($"'{caminhoRelativo}' precisa terminar em .md.");
        }

        var diretorioSistema = CaminhosDoProjeto.ResolverDentroDe(caminhos.Conhecimento, sistema);
        var caminhoArquivo = CaminhosDoProjeto.ResolverDentroDe(diretorioSistema, caminhoRelativo);

        Directory.CreateDirectory(Path.GetDirectoryName(caminhoArquivo)!);
        await File.WriteAllTextAsync(caminhoArquivo, conteudo, cancelamento);

        return Path.GetRelativePath(caminhos.Raiz, caminhoArquivo);
    }
}
