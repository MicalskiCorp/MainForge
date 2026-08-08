using MainForge.Core;

namespace MainForge.Tools;

/// <summary>
/// Procura um termo dentro de <c>Sistemas/&lt;sistema&gt;/</c>, respeitando as fontes que a mesa
/// escolheu.
///
/// <para><b>Por que existe.</b> O Dungeon Master navega por índices: lê o <c>index.md</c> da
/// fonte, desce até a pasta certa, abre o arquivo. Isso funciona quando a pergunta cai na
/// estrutura ("que classes existem?") e desperdiça vários turnos quando ela é transversal
/// ("onde está a regra de carga?", "que magias curam?") — cada nível é uma leitura paga, e no
/// fim o agente ainda abre o arquivo errado uma vez ou outra. Uma busca direta troca essa
/// escada por uma chamada.</para>
///
/// <para><b>Por que ela precisa conhecer a mesa.</b> A escolha das expansões é aplicada como
/// negação de <c>Read</c> por caminho — e essa negação não alcança uma ferramenta MCP, que roda
/// em outro processo. Sem o <see cref="EscopoDaSessao"/>, esta busca devolveria trecho de um
/// compêndio que o usuário deixou de fora, ou seja, seria exatamente o contorno que o guardrail
/// existe para fechar. Por isso a restrição é conferida aqui, em C#, e não é opcional para quem
/// tem uma.</para>
///
/// <para>O resultado pode vir com as linhas ao redor de cada ocorrência: no meio de uma conversa
/// já longa, evitar a leitura seguinte vale mais que o texto que ela traria.</para>
/// </summary>
public static class BuscaNoConhecimento
{
    public const int MaximoPadraoDeOcorrencias = 30;

    /// <param name="escopo">
    /// As fontes desta mesa. <c>null</c> significa sem limite — é o caso de quem escreve a base,
    /// não de quem cria personagem.
    /// </param>
    /// <param name="linhasDeContexto">
    /// Quantas linhas ao redor de cada ocorrência devolver. Zero devolve só a linha achada.
    /// </param>
    public static IReadOnlyList<Ocorrencia> Procurar(
        CaminhosDoProjeto caminhos,
        string sistema,
        string termo,
        EscopoDaSessao? escopo = null,
        int maximo = MaximoPadraoDeOcorrencias,
        int linhasDeContexto = BuscaEmTexto.ContextoPadrao)
    {
        if (string.IsNullOrWhiteSpace(termo))
        {
            throw new ErroDeFerramenta("Informe o termo a procurar.");
        }

        var diretorio = CaminhosDoProjeto.ResolverDentroDe(caminhos.Conhecimento, sistema);

        if (!Directory.Exists(diretorio))
        {
            throw new ErroDeFerramenta($"O sistema '{sistema}' não tem base de conhecimento em Sistemas/.");
        }

        if (escopo is not null && !escopo.EhOSistema(sistema))
        {
            throw new ErroDeFerramenta(
                $"Esta mesa é do sistema '{escopo.Sistema}' — não há como procurar em '{sistema}'.");
        }

        return BuscaEmTexto.Procurar(
            ArquivosVisiveis(diretorio, escopo),
            arquivo => Path.GetRelativePath(caminhos.Raiz, arquivo).Replace('\\', '/'),
            termo,
            maximo,
            linhasDeContexto);
    }

    /// <summary>O mesmo resultado já formatado para o modelo ler.</summary>
    public static string Descrever(IReadOnlyList<Ocorrencia> ocorrencias, string termo, int maximo)
    {
        if (ocorrencias.Count == 0)
        {
            return $"Nenhuma ocorrência de '{termo}' na base deste sistema, dentro das fontes desta mesa. " +
                   "Se a regra deveria existir, diga isso ao usuário — o sistema pode precisar ser reprocessado.";
        }

        return BuscaEmTexto.Descrever(
            ocorrencias,
            termo,
            maximo,
            "na base deste sistema",
            ocorrencias.Any(ocorrencia => ocorrencia.Contexto is { Length: > 0 })
                ? "Se o trecho acima já responde, não abra o arquivo. Abra com Read quando precisar da regra inteira."
                : "Abra o arquivo com Read para ler a regra inteira antes de afirmar qualquer coisa ao usuário — " +
                  "ou repita a busca com 'contexto' para receber as linhas em volta sem outra chamada.");
    }

    /// <summary>
    /// Os arquivos que esta mesa enxerga: os das fontes escolhidas, mais os que ficam na raiz do
    /// sistema (os dois da ficha), que valem com qualquer expansão. O <c>index.md</c> fica de
    /// fora — ele é derivado, e uma linha de tabela dele não é a regra que se procura.
    /// </summary>
    private static IEnumerable<string> ArquivosVisiveis(string diretorioDoSistema, EscopoDaSessao? escopo)
    {
        var arquivos = Directory
            .EnumerateFiles(diretorioDoSistema, "*.md", SearchOption.AllDirectories)
            .Where(arquivo => !Path.GetFileName(arquivo).Equals(IndiceDeConhecimento.NomeDoArquivo, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase);

        if (escopo is null)
        {
            return arquivos;
        }

        return arquivos.Where(arquivo => EstaLiberado(diretorioDoSistema, arquivo, escopo));
    }

    private static bool EstaLiberado(string diretorioDoSistema, string arquivo, EscopoDaSessao escopo)
    {
        var relativo = Path.GetRelativePath(diretorioDoSistema, arquivo).Replace('\\', '/');
        var barra = relativo.IndexOf('/');

        // Arquivo solto na raiz do sistema é do sistema inteiro (Ficha-Mapeamento, Ficha-ModeloEmTexto)
        // ou resto de uma base do layout antigo, de antes da separação por fonte: nos dois casos ele
        // não pertence a expansão nenhuma e não é o que a escolha da mesa exclui.
        return barra <= 0 || escopo.Permite(escopo.Sistema, relativo[..barra]);
    }
}
