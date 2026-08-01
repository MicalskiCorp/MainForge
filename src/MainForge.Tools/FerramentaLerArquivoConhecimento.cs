using Anthropic.Helpers.Beta;
using Anthropic.Models.Beta.Messages;
using MainForge.Core;

namespace MainForge.Tools;

/// <summary>
/// Lê o conteúdo de um arquivo Markdown já existente em Knowledge/&lt;sistema&gt;/. Ferramenta
/// do Agente Dungeon Master — nunca lê os PDFs originais em Systems/.
/// </summary>
public sealed class FerramentaLerArquivoConhecimento(CaminhosDoProjeto caminhos) : IBetaRunnableTool
{
    public string Name => "ler_arquivo_conhecimento";

    public BetaToolUnion Definition => new BetaTool
    {
        Name = Name,
        Description = "Lê o conteúdo de um arquivo Markdown já existente em Knowledge/<sistema>/.",
        InputSchema = EsquemaFerramenta.CriarAPartirDe("""
            {
              "type": "object",
              "properties": {
                "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Knowledge/)." },
                "caminho": { "type": "string", "description": "Caminho do arquivo .md relativo a Knowledge/<sistema>/." }
              },
              "required": ["sistema", "caminho"]
            }
            """),
    };

    public async Task<BetaToolResultBlockParamContent> ExecuteAsync(BetaToolUseBlock chamada, CancellationToken cancelamento)
    {
        var sistema = EntradaFerramenta.Obrigatorio(chamada.Input, "sistema");
        var caminho = EntradaFerramenta.Obrigatorio(chamada.Input, "caminho");

        var diretorioSistema = CaminhosDoProjeto.ResolverDentroDe(caminhos.Conhecimento, sistema);
        var caminhoArquivo = CaminhosDoProjeto.ResolverDentroDe(diretorioSistema, caminho);

        if (!File.Exists(caminhoArquivo))
        {
            throw new BetaToolError($"Arquivo '{caminho}' não encontrado em Knowledge/{sistema}/.");
        }

        var texto = await File.ReadAllTextAsync(caminhoArquivo, cancelamento);

        BetaToolResultBlockParamContent resultado = texto;
        return resultado;
    }
}
