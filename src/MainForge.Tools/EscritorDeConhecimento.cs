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

        ExigirNomeDeMagiaEmIngles(caminhos, sistema, caminhoRelativo, conteudo);

        Directory.CreateDirectory(Path.GetDirectoryName(caminhoArquivo)!);
        await File.WriteAllTextAsync(caminhoArquivo, conteudo, cancelamento);

        var relativoAoSistema = Path.GetRelativePath(diretorioSistema, caminhoArquivo).Replace('\\', '/');

        // Só a cadeia de pastas até este arquivo: nenhum índice fora dela menciona o que acabou
        // de ser gravado. Reconstruir o sistema inteiro a cada arquivo fazia uma base de centenas
        // de arquivos gastar mais tempo reescrevendo índice do que gerando conteúdo.
        IndiceDeConhecimento.ReconstruirAte(
            caminhos,
            sistema,
            relativoAoSistema,
            resumo is { Length: > 0 } ? new Dictionary<string, string> { [relativoAoSistema] = resumo } : null);

        var estado = EstadoDoProcessamento.Carregar(caminhos, sistema);
        estado.MarcarArquivo(relativoAoSistema, resumo, livro);
        estado.Salvar();

        return Path.GetRelativePath(caminhos.Raiz, caminhoArquivo);
    }

    /// <summary>
    /// Recusa um arquivo de magias em que os nomes não estejam em inglês com a tradução ao lado.
    ///
    /// <para><b>Por que a recusa mora aqui, e não só no prompt.</b> É a mesma razão de a escrita
    /// inteira passar por esta classe: o prompt é um pedido, e um pedido esquecido no meio de um
    /// processamento de duas horas só aparece meses depois, quando alguém procura <c>Fireball</c>
    /// na base e não acha. Aqui a regra é conferida no ato, e a mensagem já diz como corrigir — o
    /// agente regrava o arquivo no turno seguinte, que é o momento mais barato possível para isso.</para>
    ///
    /// <para>Ela só opina onde tem certeza: dentro de uma pasta de magias
    /// (<see cref="NomesDeMagia.EhArquivoDeMagias"/>), num sistema que não é em português, e sobre
    /// títulos que nomeiam uma magia em vez de organizar a seção. Sistema sem
    /// <c>Ficha-Validacao.json</c> — mapeado por uma versão anterior — passa direto: sem saber o
    /// idioma do sistema, cobrar o nome em inglês seria chute.</para>
    /// </summary>
    private static void ExigirNomeDeMagiaEmIngles(
        CaminhosDoProjeto caminhos,
        string sistema,
        string caminhoRelativo,
        string conteudo)
    {
        if (!NomesDeMagia.EhArquivoDeMagias(caminhoRelativo)
            || RegrasDaFicha.Carregar(caminhos, sistema) is not { EmPortugues: false })
        {
            return;
        }

        var semIngles = NomesDeMagia.TitulosSemNomeEmIngles(conteudo);

        if (semIngles.Count == 0)
        {
            return;
        }

        var lista = string.Join("\n", semIngles.Take(15).Select(titulo => $"  - {titulo}"));
        var resto = semIngles.Count > 15 ? $"\n  ... e mais {semIngles.Count - 15}." : "";

        throw new ErroDeFerramenta(
            $"'{caminhoRelativo}' tem {semIngles.Count} magia(s) sem o nome em inglês:\n{lista}{resto}\n\n" +
            NomesDeMagia.ComoEscrever + " O arquivo NÃO foi gravado — corrija os títulos e grave de novo. " +
            "Se algum desses títulos não for o nome de uma magia, transforme-o numa linha de texto " +
            "em vez de um título.");
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
        var chave = relativoAoSistema == "." ? "" : relativoAoSistema;

        return IndiceDeConhecimento.ReconstruirAte(
            caminhos,
            sistema,
            chave,
            new Dictionary<string, string> { [chave] = descricao });
    }
}
