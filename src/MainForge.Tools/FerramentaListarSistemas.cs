using System.Text.Json;
using Anthropic.Helpers.Beta;
using Anthropic.Models.Beta.Messages;
using MainForge.Core;

namespace MainForge.Tools;

/// <summary>
/// Lista os identificadores dos sistemas de RPG com livros já importados em Systems/,
/// prontos ou não para uso. Ferramenta do Agente Configurador.
/// </summary>
public sealed class FerramentaListarSistemas(CaminhosDoProjeto caminhos) : IBetaRunnableTool
{
    public string Name => "listar_sistemas";

    public BetaToolUnion Definition => new BetaTool
    {
        Name = Name,
        Description = "Lista os identificadores dos sistemas de RPG com livros já importados em Systems/, um por subpasta.",
        InputSchema = EsquemaFerramenta.CriarAPartirDe("""{"type":"object","properties":{}}"""),
    };

    public Task<BetaToolResultBlockParamContent> ExecuteAsync(BetaToolUseBlock chamada, CancellationToken cancelamento)
    {
        var sistemas = SistemaRpg.DescobrirImportados(caminhos).Select(s => s.Id).ToList();

        BetaToolResultBlockParamContent resultado = JsonSerializer.Serialize(sistemas);
        return Task.FromResult(resultado);
    }
}
