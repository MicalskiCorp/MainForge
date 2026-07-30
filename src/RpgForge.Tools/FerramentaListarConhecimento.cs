using System.Text.Json;
using Anthropic.Helpers.Beta;
using Anthropic.Models.Beta.Messages;
using RpgForge.Core;

namespace RpgForge.Tools;

/// <summary>
/// Lista os arquivos Markdown já existentes em Knowledge/&lt;sistema&gt;/, opcionalmente
/// restrito a uma subpasta. Usada tanto pelo Configurador (para saber o que já gerou) quanto
/// pelo Dungeon Master (para descobrir a estrutura de conhecimento antes de ler arquivos).
/// </summary>
public sealed class FerramentaListarConhecimento(CaminhosDoProjeto caminhos) : IBetaRunnableTool
{
    public string Name => "listar_conhecimento";

    public BetaToolUnion Definition => new BetaTool
    {
        Name = Name,
        Description = "Lista os arquivos Markdown existentes em Knowledge/<sistema>/, opcionalmente dentro de uma subpasta.",
        InputSchema = EsquemaFerramenta.CriarAPartirDe("""
            {
              "type": "object",
              "properties": {
                "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Knowledge/)." },
                "subpasta": { "type": "string", "description": "Subpasta opcional dentro de Knowledge/<sistema>/ para restringir a listagem." }
              },
              "required": ["sistema"]
            }
            """),
    };

    public Task<BetaToolResultBlockParamContent> ExecuteAsync(BetaToolUseBlock chamada, CancellationToken cancelamento)
    {
        var sistema = EntradaFerramenta.Obrigatorio(chamada.Input, "sistema");
        var subpasta = EntradaFerramenta.Opcional(chamada.Input, "subpasta");

        var diretorioSistema = CaminhosDoProjeto.ResolverDentroDe(caminhos.Conhecimento, sistema);
        var diretorioRaiz = subpasta is null
            ? diretorioSistema
            : CaminhosDoProjeto.ResolverDentroDe(diretorioSistema, subpasta);

        if (!Directory.Exists(diretorioRaiz))
        {
            throw new BetaToolError($"Não há base de conhecimento em '{diretorioRaiz}'.");
        }

        var arquivos = Directory.EnumerateFiles(diretorioRaiz, "*.md", SearchOption.AllDirectories)
            .Select(caminho => Path.GetRelativePath(diretorioSistema, caminho).Replace('\\', '/'))
            .OrderBy(caminho => caminho, StringComparer.OrdinalIgnoreCase)
            .ToList();

        BetaToolResultBlockParamContent resultado = JsonSerializer.Serialize(arquivos);
        return Task.FromResult(resultado);
    }
}
