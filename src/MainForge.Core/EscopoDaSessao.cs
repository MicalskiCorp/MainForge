using System.Text.Json;
using System.Text.Json.Serialization;

namespace MainForge.Core;

/// <summary>
/// O que uma sessão de agente tem direito de tocar: o sistema em jogo, as pastas de
/// <c>Sistemas/</c> que aquela mesa usa e — numa conversa de personagem — qual personagem é o
/// dela.
///
/// <para><b>Por que isto precisa chegar ao servidor MCP.</b> A escolha das expansões é aplicada
/// como negação de <c>Read</c> por caminho, e isso basta enquanto o agente só lê arquivo. Uma
/// ferramenta MCP que <em>procure</em> dentro de <c>Sistemas/</c>, ou que <em>grave</em> um
/// dossiê, corre por fora dessa negação: ela roda em outro processo, com o confinamento aplicado
/// em C#. Por isso o escopo viaja junto com o servidor (<see cref="VariavelDeAmbiente"/>) e é
/// conferido lá dentro, em vez de as ferramentas existirem sem ele.</para>
///
/// <para><b>Por que o personagem entrou aqui.</b> <c>registrar_personagem</c> grava o estado
/// <em>completo</em> do personagem e <c>preencher_ficha_personagem</c> fecha a criação dele —
/// as duas recebiam o identificador como texto livre e obedeciam. O prompt pedia para não mexer
/// no dossiê alheio, mas pedido não é guardrail: um identificador trocado no meio de uma
/// evolução sobrescreve outro personagem inteiro, sem volta e sem aviso. É a mesma regra das
/// fontes, aplicada ao dado que é irrecuperável.</para>
///
/// <para>Ausência de escopo significa "sem limite" — é o caso do Configurador, que escreve a
/// base inteira e não pertence a mesa nenhuma.</para>
/// </summary>
/// <param name="Sistema">Sistema a que o escopo se aplica; qualquer outro fica todo fora.</param>
/// <param name="Fontes">Ids das pastas de fonte liberadas (sempre inclui o jogo base).</param>
public sealed record EscopoDaSessao(string Sistema, IReadOnlyList<string> Fontes)
{
    /// <summary>
    /// Como o escopo chega ao processo do servidor MCP. Vai pelo ambiente, e não pela linha
    /// de comando, porque o servidor é lançado pelo Claude Code a partir de um arquivo de
    /// configuração — o bloco <c>env</c> dele é o único canal que não depende de o
    /// executável ser lançado de uma forma específica (irmão, <c>--mcp</c> ou via <c>dotnet</c>).
    /// </summary>
    public const string VariavelDeAmbiente = "MAINFORGE_FONTES_DA_MESA";

    /// <summary>
    /// O único personagem que esta sessão pode gravar. <c>null</c> quando a sessão não é de um
    /// personagem — e aí as ferramentas de personagem não têm o que conferir.
    /// </summary>
    public string? Personagem { get; init; }

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
        EhOSistema(sistema) && Fontes.Contains(fonte, StringComparer.OrdinalIgnoreCase);

    public bool EhOSistema(string sistema) =>
        Sistema.Equals(sistema, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Este é o personagem desta conversa? Escopo sem personagem definido não limita nada — a
    /// sessão não é de personagem, e recusar tudo aqui quebraria quem nunca teve essa restrição.
    /// </summary>
    public bool PermitePersonagem(string personagem) =>
        Personagem is null || Personagem.Equals(personagem, StringComparison.OrdinalIgnoreCase);

    public string Serializar() =>
        JsonSerializer.Serialize(new Transporte(Sistema, [.. Fontes], Personagem), Formato);

    /// <summary>
    /// Lê o escopo do texto transportado. Texto vazio, ausente ou corrompido devolve
    /// <c>null</c> — o que vale "sem restrição", e por isso quem usa isso para <em>conceder</em>
    /// acesso precisa tratar o <c>null</c> de propósito, e não por descuido.
    /// </summary>
    public static EscopoDaSessao? Ler(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        try
        {
            var lido = JsonSerializer.Deserialize<Transporte>(texto, Formato);

            return lido is { Sistema.Length: > 0, Fontes.Count: > 0 }
                ? new EscopoDaSessao(lido.Sistema, lido.Fontes) { Personagem = lido.Personagem }
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Transporte(
        [property: JsonPropertyName("sistema")] string Sistema,
        [property: JsonPropertyName("fontes")] List<string> Fontes,
        [property: JsonPropertyName("personagem")] string? Personagem = null);
}
