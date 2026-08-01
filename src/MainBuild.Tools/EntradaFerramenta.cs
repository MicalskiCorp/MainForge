using System.Text.Json;
using Anthropic.Helpers.Beta;

namespace MainBuild.Tools;

/// <summary>
/// Leitura validada dos campos de entrada de uma chamada de ferramenta
/// (<see cref="Anthropic.Models.Beta.Messages.BetaToolUseBlock.Input"/>). Centraliza a
/// mensagem de erro devolvida ao modelo quando um campo obrigatório falta ou tem o tipo
/// errado, em vez de cada ferramenta reinventar essa checagem.
/// </summary>
internal static class EntradaFerramenta
{
    public static string Obrigatorio(IReadOnlyDictionary<string, JsonElement> entrada, string campo)
    {
        if (!entrada.TryGetValue(campo, out var valor) ||
            valor.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(valor.GetString()))
        {
            throw new BetaToolError($"Campo obrigatório '{campo}' ausente ou inválido.");
        }

        return valor.GetString()!;
    }

    public static string? Opcional(IReadOnlyDictionary<string, JsonElement> entrada, string campo)
    {
        return entrada.TryGetValue(campo, out var valor) && valor.ValueKind == JsonValueKind.String
            ? valor.GetString()
            : null;
    }

    public static IReadOnlyDictionary<string, string> ObjetoDeTexto(IReadOnlyDictionary<string, JsonElement> entrada, string campo)
    {
        if (!entrada.TryGetValue(campo, out var valor) || valor.ValueKind != JsonValueKind.Object)
        {
            throw new BetaToolError($"Campo obrigatório '{campo}' ausente ou inválido (esperado um objeto).");
        }

        return valor.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
    }
}
