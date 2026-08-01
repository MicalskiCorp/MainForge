using System.Text.Json;
using Anthropic.Models.Beta.Messages;

namespace MainForge.Tools;

/// <summary>
/// Converte um schema JSON (formato "properties"/"required" de JSON Schema) no
/// <see cref="InputSchema"/> exigido pelo SDK Anthropic para definir uma ferramenta. Escrever
/// o schema como um literal JSON é bem mais legível do que montar o dicionário de
/// propriedades manualmente.
/// </summary>
internal static class EsquemaFerramenta
{
    public static InputSchema CriarAPartirDe(string json)
    {
        using var documento = JsonDocument.Parse(json);
        var raiz = documento.RootElement;

        var propriedades = raiz.TryGetProperty("properties", out var propriedadesEl)
            ? propriedadesEl.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())
            : new Dictionary<string, JsonElement>();

        var obrigatorios = raiz.TryGetProperty("required", out var obrigatoriosEl)
            ? obrigatoriosEl.EnumerateArray().Select(e => e.GetString()!).ToList()
            : [];

        return new InputSchema
        {
            Properties = propriedades,
            Required = obrigatorios,
        };
    }
}
