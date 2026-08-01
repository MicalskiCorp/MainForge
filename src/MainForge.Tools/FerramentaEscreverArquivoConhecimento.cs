using Anthropic.Helpers.Beta;
using Anthropic.Models.Beta.Messages;
using MainForge.Core;

namespace MainForge.Tools;

/// <summary>
/// Cria ou sobrescreve um arquivo Markdown dentro de Knowledge/&lt;sistema&gt;/. Cria os
/// diretórios intermediários conforme necessário — é assim que o Configurador monta a árvore
/// de conhecimento de um sistema novo. Ferramenta exclusiva do Agente Configurador.
/// </summary>
public sealed class FerramentaEscreverArquivoConhecimento(CaminhosDoProjeto caminhos) : IBetaRunnableTool
{
    public string Name => "escrever_arquivo_conhecimento";

    public BetaToolUnion Definition => new BetaTool
    {
        Name = Name,
        Description = "Cria ou sobrescreve um arquivo Markdown dentro de Knowledge/<sistema>/ com o conteúdo informado.",
        InputSchema = EsquemaFerramenta.CriarAPartirDe("""
            {
              "type": "object",
              "properties": {
                "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Knowledge/)." },
                "caminho": { "type": "string", "description": "Caminho do arquivo .md relativo a Knowledge/<sistema>/, ex.: \"Classes/Guerreiro.md\"." },
                "conteudo": { "type": "string", "description": "Conteúdo Markdown completo a gravar no arquivo." }
              },
              "required": ["sistema", "caminho", "conteudo"]
            }
            """),
    };

    public async Task<BetaToolResultBlockParamContent> ExecuteAsync(BetaToolUseBlock chamada, CancellationToken cancelamento)
    {
        var sistema = EntradaFerramenta.Obrigatorio(chamada.Input, "sistema");
        var caminho = EntradaFerramenta.Obrigatorio(chamada.Input, "caminho");
        var conteudo = EntradaFerramenta.Obrigatorio(chamada.Input, "conteudo");

        if (!caminho.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            throw new BetaToolError($"'{caminho}' precisa terminar em .md.");
        }

        var diretorioSistema = CaminhosDoProjeto.ResolverDentroDe(caminhos.Conhecimento, sistema);
        var caminhoArquivo = CaminhosDoProjeto.ResolverDentroDe(diretorioSistema, caminho);

        Directory.CreateDirectory(Path.GetDirectoryName(caminhoArquivo)!);
        await File.WriteAllTextAsync(caminhoArquivo, conteudo, cancelamento);

        BetaToolResultBlockParamContent resultado = $"Arquivo '{caminho}' gravado em Knowledge/{sistema}/.";
        return resultado;
    }
}
