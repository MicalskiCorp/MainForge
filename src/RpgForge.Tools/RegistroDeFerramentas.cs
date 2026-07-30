using Anthropic.Helpers.Beta;
using RpgForge.Core;

namespace RpgForge.Tools;

/// <summary>
/// Ponto único de construção de todas as ferramentas locais, indexadas pelo nome usado nas
/// chamadas de tool_use. A filtragem por agente (allowlist de
/// DefinicaoDeAgente.FerramentasPermitidas) acontece em RpgForge.Agents, que depende deste
/// projeto — aqui só existe o catálogo completo.
/// </summary>
public static class RegistroDeFerramentas
{
    public static IReadOnlyDictionary<string, IBetaRunnableTool> CriarTodas(CaminhosDoProjeto caminhos)
    {
        IBetaRunnableTool[] ferramentas =
        [
            new FerramentaListarSistemas(caminhos),
            new FerramentaListarSistemasProntos(caminhos),
            new FerramentaListarConhecimento(caminhos),
            new FerramentaLerPdfDoSistema(caminhos),
            new FerramentaEscreverArquivoConhecimento(caminhos),
            new FerramentaLerArquivoConhecimento(caminhos),
            new FerramentaPreencherFichaPersonagem(caminhos),
        ];

        return ferramentas.ToDictionary(f => f.Name);
    }
}
