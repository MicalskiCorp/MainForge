using Anthropic.Helpers.Beta;
using Anthropic.Models.Beta.Messages;
using MainForge.Core;

namespace MainForge.Tools;

/// <summary>
/// Devolve a ficha de personagem em branco de Templates/&lt;sistema&gt;/ em duas formas ao mesmo
/// tempo: o PDF inteiro, para o modelo VER o leiaute (rótulos, agrupamentos, posição de cada
/// caixa), e a lista exata dos nomes dos campos de formulário, que é o que a ferramenta de
/// preenchimento vai exigir depois. Ferramenta exclusiva do Agente Configurador — é com ela
/// que ele aprende a mapear "dado do personagem -> campo do PDF" e consegue redesenhar a
/// ficha em arte de texto.
/// </summary>
public sealed class FerramentaLerFichaModelo(CaminhosDoProjeto caminhos) : IBetaRunnableTool
{
    public string Name => "ler_ficha_modelo";

    public BetaToolUnion Definition => new BetaTool
    {
        Name = Name,
        Description =
            "Lê a ficha de personagem em branco em Templates/<sistema>/ e devolve o PDF (para ver o leiaute) " +
            "junto com a lista exata dos nomes dos campos preenchíveis do formulário.",
        InputSchema = EsquemaFerramenta.CriarAPartirDe("""
            {
              "type": "object",
              "properties": {
                "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Templates/)." },
                "arquivoModelo": { "type": "string", "description": "Nome do PDF dentro de Templates/<sistema>/. Só é obrigatório se houver mais de um PDF nessa pasta." }
              },
              "required": ["sistema"]
            }
            """),
    };

    public async Task<BetaToolResultBlockParamContent> ExecuteAsync(BetaToolUseBlock chamada, CancellationToken cancelamento)
    {
        var sistema = EntradaFerramenta.Obrigatorio(chamada.Input, "sistema");
        var arquivoModelo = EntradaFerramenta.Opcional(chamada.Input, "arquivoModelo");

        var diretorioModelo = CaminhosDoProjeto.ResolverDentroDe(caminhos.Modelos, sistema);
        var caminhoModelo = LocalizadorDeFichaModelo.Resolver(diretorioModelo, arquivoModelo);

        IReadOnlyList<string> campos;

        try
        {
            campos = ImportadorDeSistema.LerCamposDaFicha(caminhoModelo);
        }
        catch (InvalidOperationException excecao)
        {
            throw new BetaToolError(excecao.Message, excecao);
        }

        var bytes = await File.ReadAllBytesAsync(caminhoModelo, cancelamento);

        List<Block> conteudo =
        [
            new BetaRequestDocumentBlock(new BetaBase64PdfSource(Convert.ToBase64String(bytes)))
            {
                Title = Path.GetFileName(caminhoModelo),
            },
            new BetaTextBlockParam(
                $"Campos preenchíveis de '{Path.GetFileName(caminhoModelo)}' ({campos.Count}), " +
                $"exatamente como devem ser informados a preencher_ficha_personagem:\n" +
                string.Join("\n", campos.Select(campo => $"- {campo}"))),
        ];

        return conteudo;
    }
}
