namespace MainForge.Core;

/// <summary>
/// Resolve os diretórios de dados de topo do projeto (Agents, Input, Templates, Sistemas,
/// Output) a partir de uma única raiz. Toda ferramenta e agente que acessa o sistema de
/// arquivos deve passar por aqui em vez de montar caminhos na mão, para que a regra
/// "não ler/escrever fora do projeto" tenha um único ponto de aplicação.
///
/// <para><b>Os nomes das propriedades e os das pastas não são os mesmos, de propósito.</b>
/// <see cref="Entrada"/> é a pasta <c>Input/</c>, com os livros que o usuário fornece;
/// <see cref="Conhecimento"/> é a pasta <c>Sistemas/</c>, com o que o Configurador destilou
/// deles. Este é o único lugar do código onde essa correspondência é escrita — mudar o nome de
/// uma pasta é mudar uma linha aqui.</para>
/// </summary>
public sealed class CaminhosDoProjeto
{
    public string Raiz { get; }
    public string Agentes { get; }

    /// <summary>Os livros em PDF fornecidos pelo usuário: a pasta <c>Input/</c>.</summary>
    public string Entrada { get; }

    public string Modelos { get; }

    /// <summary>As bases de conhecimento por sistema de RPG: a pasta <c>Sistemas/</c>.</summary>
    public string Conhecimento { get; }

    /// <summary>
    /// O dossiê de cada personagem — o que ele é, em que pé está a criação e qual conversa a
    /// produziu: a pasta <c>Personagens/</c>.
    ///
    /// <para><b>Por que não fica em <c>Output/</c>.</b> Ali estão as fichas em PDF, que são
    /// entrega para o usuário e de onde o código não tira decisão nenhuma. O dossiê é o
    /// contrário: é dado de trabalho, o aplicativo lê dele para saber o que continuar, e o
    /// Dungeon Master precisa alcançá-lo enquanto <c>Output/</c> continua fora do alcance de
    /// todo agente.</para>
    /// </summary>
    public string Personagens { get; }

    public string Saida { get; }
    public string SaidaPersonagens { get; }

    /// <summary>Os pacotes de sistema exportados, prontos para levar a outra máquina.</summary>
    public string SaidaPacotes { get; }

    public CaminhosDoProjeto(string raiz)
    {
        Raiz = Path.GetFullPath(raiz);
        Agentes = Path.Combine(Raiz, "Agents");
        Entrada = Path.Combine(Raiz, "Input");
        Modelos = Path.Combine(Raiz, "Templates");
        Conhecimento = Path.Combine(Raiz, "Sistemas");
        Personagens = Path.Combine(Raiz, "Personagens");
        Saida = Path.Combine(Raiz, "Output");
        SaidaPersonagens = Path.Combine(Saida, "Personagens");
        SaidaPacotes = Path.Combine(Saida, "Pacotes");
    }

    /// <summary>
    /// Variável de ambiente para rodar sobre outra raiz — útil para manter os dados fora da
    /// pasta do aplicativo (num disco maior, numa pasta sincronizada) sem mover o binário.
    /// </summary>
    public const string VariavelDeAmbiente = "MAINFORGE_RAIZ";

    /// <summary>
    /// Descobre a raiz dos dados, em três tentativas: a variável de ambiente, um diretório
    /// ancestral do executável que já tenha os prompts dos agentes, e o próprio diretório do
    /// executável.
    ///
    /// <para><b>Por que basta a pasta Agents.</b> Antes era preciso ter também <c>Input/</c>, o
    /// que só é verdade num repositório já usado: quem baixa o aplicativo pronto tem o executável
    /// e os prompts, e nada mais — as outras pastas nascem vazias no primeiro uso
    /// (<see cref="GarantirEstrutura"/>). Exigir <c>Input/</c> fazia essa instalação cair no
    /// diretório atual, que é de onde o programa foi chamado e não tem relação nenhuma com onde
    /// ele está.</para>
    /// </summary>
    public static CaminhosDoProjeto Descobrir(string? diretorioInicial = null)
    {
        var doAmbiente = Environment.GetEnvironmentVariable(VariavelDeAmbiente);

        if (!string.IsNullOrWhiteSpace(doAmbiente))
        {
            return new CaminhosDoProjeto(doAmbiente);
        }

        var inicio = diretorioInicial ?? AppContext.BaseDirectory;
        var diretorio = new DirectoryInfo(inicio);

        while (diretorio is not null)
        {
            if (TemPromptsDeAgente(diretorio.FullName))
            {
                return new CaminhosDoProjeto(diretorio.FullName);
            }

            diretorio = diretorio.Parent;
        }

        return new CaminhosDoProjeto(inicio);
    }

    private static bool TemPromptsDeAgente(string raiz)
    {
        var agentes = Path.Combine(raiz, "Agents");

        return Directory.Exists(agentes) && Directory.EnumerateFiles(agentes, "*.md").Any();
    }

    /// <summary>
    /// Cria as pastas de dados que ainda não existem. Roda na abertura do aplicativo: numa
    /// instalação recém-baixada não há <c>Input/</c> nem <c>Output/</c>, e a alternativa a
    /// criá-las seria cada fluxo tratar a ausência por conta própria — ou, pior, o usuário ver
    /// "diretório não encontrado" antes de ter feito nada.
    /// </summary>
    /// <returns>As pastas que precisaram ser criadas, relativas à raiz.</returns>
    public IReadOnlyList<string> GarantirEstrutura()
    {
        var criadas = new List<string>();

        foreach (var diretorio in new[] { Entrada, Modelos, Conhecimento, Personagens, SaidaPersonagens })
        {
            if (Directory.Exists(diretorio))
            {
                continue;
            }

            Directory.CreateDirectory(diretorio);
            criadas.Add(Path.GetRelativePath(Raiz, diretorio).Replace('\\', '/'));
        }

        return criadas;
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
