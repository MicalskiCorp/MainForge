using System.Text.Json;
using Anthropic.Helpers.Beta;
using Anthropic.Models.Beta.Messages;
using RpgForge.Core;

namespace RpgForge.Tools;

/// <summary>
/// Lista os identificadores dos sistemas de RPG já processados pelo Configurador, com base de
/// conhecimento pronta em Knowledge/. Ferramenta do Agente Dungeon Master.
/// </summary>
public sealed class FerramentaListarSistemasProntos(CaminhosDoProjeto caminhos) : IBetaRunnableTool
{
    public string Name => "listar_sistemas_prontos";

    public BetaToolUnion Definition => new BetaTool
    {
        Name = Name,
        Description = "Lista os identificadores dos sistemas de RPG com base de conhecimento pronta em Knowledge/.",
        InputSchema = EsquemaFerramenta.CriarAPartirDe("""{"type":"object","properties":{}}"""),
    };

    public Task<BetaToolResultBlockParamContent> ExecuteAsync(BetaToolUseBlock chamada, CancellationToken cancelamento)
    {
        var sistemas = SistemaRpg.DescobrirProntos(caminhos).Select(s => s.Id).ToList();

        BetaToolResultBlockParamContent resultado = JsonSerializer.Serialize(sistemas);
        return Task.FromResult(resultado);
    }
}
