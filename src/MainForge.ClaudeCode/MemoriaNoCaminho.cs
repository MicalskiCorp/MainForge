namespace MainForge.ClaudeCode;

/// <summary>
/// Detecta arquivos de memória do Claude Code (<c>CLAUDE.md</c>) na raiz de dados do MainForge.
///
/// <para><b>Por que isto importa.</b> Os agentes rodam com a raiz do projeto como diretório de
/// trabalho, e o Claude Code carrega sozinho o <c>CLAUDE.md</c> que encontrar ali — em toda
/// requisição de todo turno. Num repositório de desenvolvimento isso significa mandar ao agente
/// de RPG instruções sobre <c>dotnet build</c>, sobre a estrutura de pastas do código e, no caso
/// deste projeto, uma ordem de invocar uma skill que está negada. São tokens pagos para entregar
/// uma instrução impossível de obedecer.</para>
///
/// <para><b>Por que só avisar.</b> As duas formas de desligar a leitura automática de memória
/// cobram um preço que este produto não pode pagar: <c>--bare</c> passa a exigir
/// <c>ANTHROPIC_API_KEY</c> e nunca lê a sessão autenticada — e o aplicativo inteiro existe para
/// usar a assinatura do usuário —, e <c>--safe-mode</c> desliga os servidores MCP, que são as
/// ferramentas do agente. Não há flag que tire a memória e deixe o resto.</para>
///
/// <para>O que sobra é escolher a raiz: numa instalação baixada não existe <c>CLAUDE.md</c>
/// nenhum, e quem desenvolve pode apontar <see cref="Core.CaminhosDoProjeto.VariavelDeAmbiente"/>
/// para uma pasta de dados fora do repositório. É isto que o aviso diz.</para>
/// </summary>
public static class MemoriaNoCaminho
{
    /// <summary>O arquivo que o Claude Code carrega sozinho a partir do diretório de trabalho.</summary>
    public const string NomeDoArquivo = "CLAUDE.md";

    /// <summary>Existe um CLAUDE.md na raiz de dados? Se existir, ele entra em toda conversa.</summary>
    public static bool ExisteEm(string raiz) => File.Exists(Path.Combine(raiz, NomeDoArquivo));

    /// <summary>
    /// Quanto ele custa por turno, em tokens aproximados. A conta grosseira (quatro caracteres
    /// por token) basta: o número existe para dar ordem de grandeza a quem for decidir se vale a
    /// pena mexer nisso.
    /// </summary>
    public static int TokensAproximados(string raiz)
    {
        try
        {
            var arquivo = new FileInfo(Path.Combine(raiz, NomeDoArquivo));

            return arquivo.Exists ? (int)(arquivo.Length / 4) : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    /// <summary>O aviso pronto, ou <c>null</c> quando não há o que avisar.</summary>
    public static string? Aviso(string raiz)
    {
        if (!ExisteEm(raiz))
        {
            return null;
        }

        return
            $"Há um {NomeDoArquivo} em {raiz}. O Claude Code o carrega sozinho em toda conversa de " +
            $"agente (~{TokensAproximados(raiz)} tokens por turno), e o conteúdo dele não é assunto " +
            "de um agente de RPG. Isso acontece quando o aplicativo roda sobre o próprio " +
            $"repositório; para separar, aponte {Core.CaminhosDoProjeto.VariavelDeAmbiente} para uma " +
            "pasta de dados fora dele.";
    }
}
