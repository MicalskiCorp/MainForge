using System.Text.Json;
using System.Text.Json.Serialization;

namespace MainForge.Core;

/// <summary>
/// As fontes que valem numa sessão: o sistema em jogo e as pastas de <c>Sistemas/</c> que o
/// agente pode alcançar naquela mesa.
///
/// <para><b>Por que isto precisa chegar ao servidor MCP.</b> A escolha das expansões é aplicada
/// como negação de <c>Read</c> por caminho, e isso basta enquanto o agente só lê arquivo. Uma
/// ferramenta MCP que <em>procure</em> dentro de <c>Sistemas/</c> corre por fora dessa negação:
/// ela roda em outro processo, com o confinamento aplicado em C#, e devolveria trecho de um
/// compêndio que a mesa recusou. Por isso a restrição viaja junto com o servidor
/// (<see cref="VariavelDeAmbiente"/>) e é conferida lá dentro, em vez de a busca existir sem
/// ela.</para>
///
/// <para>Ausência de restrição significa "sem limite de fonte" — é o caso do Configurador, que
/// escreve a base inteira.</para>
/// </summary>
/// <param name="Sistema">Sistema a que a restrição se aplica; qualquer outro fica todo fora.</param>
/// <param name="Fontes">Ids das pastas de fonte liberadas (sempre inclui o jogo base).</param>
public sealed record RestricaoDeFontes(string Sistema, IReadOnlyList<string> Fontes)
{
    /// <summary>
    /// Como a restrição chega ao processo do servidor MCP. Vai pelo ambiente, e não pela linha
    /// de comando, porque o servidor é lançado pelo Claude Code a partir de um arquivo de
    /// configuração — o bloco <c>env</c> dele é o único canal que não depende de o
    /// executável ser lançado de uma forma específica (irmão, <c>--mcp</c> ou via <c>dotnet</c>).
    /// </summary>
    public const string VariavelDeAmbiente = "MAINFORGE_FONTES_DA_MESA";

    private static readonly JsonSerializerOptions Formato = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Esta fonte deste sistema está liberada? Sistema diferente do restrito nunca está: a
    /// mesa é de um sistema só, e alcançar outro seria alcançar regras que ninguém escolheu.
    /// </summary>
    public bool Permite(string sistema, string fonte) =>
        Sistema.Equals(sistema, StringComparison.OrdinalIgnoreCase) &&
        Fontes.Contains(fonte, StringComparer.OrdinalIgnoreCase);

    public string Serializar() =>
        JsonSerializer.Serialize(new Transporte(Sistema, [.. Fontes]), Formato);

    /// <summary>
    /// Lê a restrição do texto transportado. Texto vazio, ausente ou corrompido devolve
    /// <c>null</c> — o que vale "sem restrição", e por isso quem usa isso para <em>conceder</em>
    /// acesso precisa tratar o <c>null</c> de propósito, e não por descuido.
    /// </summary>
    public static RestricaoDeFontes? Ler(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        try
        {
            var lido = JsonSerializer.Deserialize<Transporte>(texto, Formato);

            return lido is { Sistema.Length: > 0, Fontes.Count: > 0 }
                ? new RestricaoDeFontes(lido.Sistema, lido.Fontes)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Transporte(
        [property: JsonPropertyName("sistema")] string Sistema,
        [property: JsonPropertyName("fontes")] List<string> Fontes);
}
