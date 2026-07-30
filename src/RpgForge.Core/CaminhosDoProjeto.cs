namespace RpgForge.Core;

/// <summary>
/// Resolve os diretórios de dados de topo do projeto (Agents, Systems, Templates,
/// Knowledge, Output) a partir de uma única raiz. Toda ferramenta e agente que acessa o
/// sistema de arquivos deve passar por aqui em vez de montar caminhos na mão, para que a
/// regra "não ler/escrever fora do projeto" tenha um único ponto de aplicação.
/// </summary>
public sealed class CaminhosDoProjeto
{
    public string Raiz { get; }
    public string Agentes { get; }
    public string Sistemas { get; }
    public string Modelos { get; }
    public string Conhecimento { get; }
    public string Saida { get; }
    public string SaidaPersonagens { get; }

    public CaminhosDoProjeto(string raiz)
    {
        Raiz = Path.GetFullPath(raiz);
        Agentes = Path.Combine(Raiz, "Agents");
        Sistemas = Path.Combine(Raiz, "Systems");
        Modelos = Path.Combine(Raiz, "Templates");
        Conhecimento = Path.Combine(Raiz, "Knowledge");
        Saida = Path.Combine(Raiz, "Output");
        SaidaPersonagens = Path.Combine(Saida, "Personagens");
    }

    /// <summary>
    /// Sobe a partir do diretório do executável em busca da raiz do repositório,
    /// identificada pela presença das pastas de topo Agents/Systems. Se nenhuma for
    /// encontrada (ex.: primeira execução antes de as pastas existirem), cai para o
    /// diretório atual.
    /// </summary>
    public static CaminhosDoProjeto Descobrir(string? diretorioInicial = null)
    {
        var diretorio = new DirectoryInfo(diretorioInicial ?? AppContext.BaseDirectory);

        while (diretorio is not null)
        {
            if (Directory.Exists(Path.Combine(diretorio.FullName, "Agents")) &&
                Directory.Exists(Path.Combine(diretorio.FullName, "Systems")))
            {
                return new CaminhosDoProjeto(diretorio.FullName);
            }

            diretorio = diretorio.Parent;
        }

        return new CaminhosDoProjeto(Directory.GetCurrentDirectory());
    }

    /// <summary>
    /// Resolve um caminho relativo fornecido pelo modelo ou por um arquivo de configuração,
    /// e lança exceção se ele escapar do diretório base informado (path traversal, caminhos
    /// absolutos fora da raiz, escape via symlink). Toda ferramenta que recebe um caminho
    /// relativo vindo do modelo deve chamar isto antes de tocar no sistema de arquivos.
    /// </summary>
    public static string ResolverDentroDe(string diretorioBase, string caminhoRelativo)
    {
        var caminhoBase = Path.GetFullPath(diretorioBase);
        var candidato = Path.GetFullPath(Path.Combine(caminhoBase, caminhoRelativo));

        var baseComSeparador = caminhoBase.EndsWith(Path.DirectorySeparatorChar)
            ? caminhoBase
            : caminhoBase + Path.DirectorySeparatorChar;

        if (!candidato.Equals(caminhoBase, StringComparison.OrdinalIgnoreCase) &&
            !candidato.StartsWith(baseComSeparador, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"Caminho '{caminhoRelativo}' resolve para fora do diretório permitido '{diretorioBase}'.");
        }

        return candidato;
    }
}
