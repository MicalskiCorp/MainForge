using Anthropic.Helpers.Beta;
using Anthropic.Models.Beta.Messages;
using MainBuild.Core;

namespace MainBuild.Tools;

/// <summary>
/// Lê um PDF de livro de regras em Systems/&lt;sistema&gt;/ e devolve seu conteúdo integral
/// como bloco de documento no tool_result, para que o próprio Claude leia o PDF nativamente
/// — este projeto não faz parsing de PDF. Ferramenta exclusiva do Agente Configurador.
/// </summary>
public sealed class FerramentaLerPdfDoSistema(CaminhosDoProjeto caminhos) : IBetaRunnableTool
{
    public string Name => "ler_pdf_do_sistema";

    public BetaToolUnion Definition => new BetaTool
    {
        Name = Name,
        Description = "Lê um PDF de livro de regras em Systems/<sistema>/ e devolve seu conteúdo integral para leitura nativa do modelo.",
        InputSchema = EsquemaFerramenta.CriarAPartirDe("""
            {
              "type": "object",
              "properties": {
                "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Systems/)." },
                "arquivo": { "type": "string", "description": "Caminho do PDF relativo a Systems/<sistema>/, ex.: \"Livro Basico.pdf\"." }
              },
              "required": ["sistema", "arquivo"]
            }
            """),
    };

    public async Task<BetaToolResultBlockParamContent> ExecuteAsync(BetaToolUseBlock chamada, CancellationToken cancelamento)
    {
        var sistema = EntradaFerramenta.Obrigatorio(chamada.Input, "sistema");
        var arquivo = EntradaFerramenta.Obrigatorio(chamada.Input, "arquivo");

        var diretorioSistema = CaminhosDoProjeto.ResolverDentroDe(caminhos.Sistemas, sistema);
        var caminhoArquivo = CaminhosDoProjeto.ResolverDentroDe(diretorioSistema, arquivo);

        if (!string.Equals(Path.GetExtension(caminhoArquivo), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new BetaToolError($"'{arquivo}' não é um PDF.");
        }

        if (!File.Exists(caminhoArquivo))
        {
            throw new BetaToolError($"Arquivo '{arquivo}' não encontrado em Systems/{sistema}/.");
        }

        var bytes = await File.ReadAllBytesAsync(caminhoArquivo, cancelamento);
        var base64 = Convert.ToBase64String(bytes);

        List<Block> conteudo =
        [
            new BetaRequestDocumentBlock(new BetaBase64PdfSource(base64))
            {
                Title = Path.GetFileName(caminhoArquivo),
            },
        ];

        return conteudo;
    }
}
