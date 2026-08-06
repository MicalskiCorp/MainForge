using System.Text;
using MainForge.Core;

namespace MainForge.Tools;

/// <summary>Uma ocorrência do termo procurado dentro da base de conhecimento de um sistema.</summary>
/// <param name="Arquivo">Caminho do .md, relativo à raiz do projeto — é o que vai no <c>Read</c>.</param>
/// <param name="Linha">Número da linha, para abrir o trecho com um <c>offset</c>.</param>
/// <param name="Secao">O título Markdown mais próximo acima da linha.</param>
/// <param name="Trecho">A linha encontrada, encurtada.</param>
public sealed record OcorrenciaNoConhecimento(string Arquivo, int Linha, string Secao, string Trecho);

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
/// em outro processo. Sem a <see cref="RestricaoDeFontes"/>, esta busca devolveria trecho de um
/// compêndio que o usuário deixou de fora, ou seja, seria exatamente o contorno que o guardrail
/// existe para fechar. Por isso a restrição é conferida aqui, em C#, e não é opcional para quem
/// tem uma.</para>
///
/// <para>O resultado é magro de propósito — arquivo, linha, seção e a linha achada. Devolver o
/// contexto inteiro de cada ocorrência traria de volta o custo que se está cortando.</para>
/// </summary>
public static class BuscaNoConhecimento
{
    public const int MaximoPadraoDeOcorrencias = 30;

    /// <param name="restricao">
    /// As fontes desta mesa. <c>null</c> significa sem limite — é o caso de quem escreve a base,
    /// não de quem cria personagem.
    /// </param>
    public static IReadOnlyList<OcorrenciaNoConhecimento> Procurar(
        CaminhosDoProjeto caminhos,
        string sistema,
        string termo,
        RestricaoDeFontes? restricao = null,
        int maximo = MaximoPadraoDeOcorrencias)
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

        if (restricao is not null && !restricao.Sistema.Equals(sistema, StringComparison.OrdinalIgnoreCase))
        {
            throw new ErroDeFerramenta(
                $"Esta mesa é do sistema '{restricao.Sistema}' — não há como procurar em '{sistema}'.");
        }

        var procurado = TextoNormalizado.SemAcento(termo.Trim());
        var achados = new List<OcorrenciaNoConhecimento>();

        foreach (var arquivo in ArquivosVisiveis(diretorio, restricao))
        {
            var secao = "";
            var numero = 0;

            foreach (var linha in File.ReadLines(arquivo))
            {
                numero++;

                if (linha.StartsWith('#'))
                {
                    secao = linha.TrimStart('#', ' ').Trim();
                }

                if (!TextoNormalizado.SemAcento(linha).Contains(procurado, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                achados.Add(new OcorrenciaNoConhecimento(
                    Path.GetRelativePath(caminhos.Raiz, arquivo).Replace('\\', '/'),
                    numero,
                    secao,
                    Encurtar(linha.Trim())));

                if (achados.Count >= maximo)
                {
                    return achados;
                }
            }
        }

        return achados;
    }

    /// <summary>O mesmo resultado já formatado para o modelo ler.</summary>
    public static string Descrever(IReadOnlyList<OcorrenciaNoConhecimento> ocorrencias, string termo, int maximo)
    {
        if (ocorrencias.Count == 0)
        {
            return $"Nenhuma ocorrência de '{termo}' na base deste sistema, dentro das fontes desta mesa. " +
                   "Se a regra deveria existir, diga isso ao usuário — o sistema pode precisar ser reprocessado.";
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
                texto.AppendLine($"  linha {ocorrencia.Linha}{secao}: {ocorrencia.Trecho}");
            }
        }

        texto.AppendLine();
        texto.AppendLine("Abra o arquivo com Read para ler a regra inteira antes de afirmar qualquer coisa ao usuário.");

        return texto.ToString();
    }

    /// <summary>
    /// Os arquivos que esta mesa enxerga: os das fontes escolhidas, mais os que ficam na raiz do
    /// sistema (os dois da ficha), que valem com qualquer expansão. O <c>index.md</c> fica de
    /// fora — ele é derivado, e uma linha de tabela dele não é a regra que se procura.
    /// </summary>
    private static IEnumerable<string> ArquivosVisiveis(string diretorioDoSistema, RestricaoDeFontes? restricao)
    {
        var arquivos = Directory
            .EnumerateFiles(diretorioDoSistema, "*.md", SearchOption.AllDirectories)
            .Where(arquivo => !Path.GetFileName(arquivo).Equals(IndiceDeConhecimento.NomeDoArquivo, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase);

        if (restricao is null)
        {
            return arquivos;
        }

        return arquivos.Where(arquivo => EstaLiberado(diretorioDoSistema, arquivo, restricao));
    }

    private static bool EstaLiberado(string diretorioDoSistema, string arquivo, RestricaoDeFontes restricao)
    {
        var relativo = Path.GetRelativePath(diretorioDoSistema, arquivo).Replace('\\', '/');
        var barra = relativo.IndexOf('/');

        // Arquivo solto na raiz do sistema é do sistema inteiro (Ficha-Mapeamento, Ficha-ModeloEmTexto)
        // ou resto de uma base do layout antigo, de antes da separação por fonte: nos dois casos ele
        // não pertence a expansão nenhuma e não é o que a escolha da mesa exclui.
        return barra <= 0 || restricao.Permite(restricao.Sistema, relativo[..barra]);
    }

    private static string Encurtar(string linha) =>
        linha.Length <= 160 ? linha : string.Concat(linha.AsSpan(0, 160), "...");
}
