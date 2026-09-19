using System.Reflection;
using MainForge.ClaudeCode;

namespace MainForge.Tests;

/// <summary>
/// A leitura do evento <c>result</c> do <c>stream-json</c>.
///
/// <para><b>Por que testar isto.</b> O motivo de uma falha é o que a sessão usa para decidir se
/// ela é recuperável — cota esgotada espera, sessão perdida recomeça. Se o motivo chega sem a
/// explicação, as duas recuperações deixam de acontecer em silêncio e o usuário só vê
/// "error_during_execution".</para>
/// </summary>
public sealed class ProcessoDoClaudeCodeTestes
{
    /// <summary>Alcança o interpretador privado pelo mesmo motivo de <see cref="LinhaDeComandoDoClaudeCodeTestes"/>.</summary>
    private static (bool Falhou, string? Motivo) Interpretar(string linha)
    {
        var interpretar = typeof(ProcessoDoClaudeCode).GetMethod(
            "Interpretar",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        object?[] argumentos = [linha, null, null, false, null, null];
        var eventos = (IEnumerable<EventoDeAgente>)interpretar.Invoke(null, argumentos)!;
        _ = eventos.ToList();

        return ((bool)argumentos[3]!, (string?)argumentos[4]);
    }

    [Fact]
    public void Resume_de_sessao_inexistente_traz_a_explicacao_do_array_errors()
    {
        // Saída real do Claude Code para "--resume" de um id que ele não conhece.
        const string linha =
            """{"type":"result","subtype":"error_during_execution","is_error":true,"num_turns":0,"session_id":"00000000-0000-4000-8000-000000000000","total_cost_usd":0,"errors":["No conversation found with session ID: 00000000-0000-4000-8000-000000000000"]}""";

        var (falhou, motivo) = Interpretar(linha);

        Assert.True(falhou);
        Assert.Equal(
            "error_during_execution: No conversation found with session ID: 00000000-0000-4000-8000-000000000000",
            motivo);
    }

    [Fact]
    public void Falha_sem_errors_continua_com_subtype_e_result()
    {
        const string linha =
            """{"type":"result","subtype":"error_during_execution","is_error":true,"result":"5-hour limit reached"}""";

        var (_, motivo) = Interpretar(linha);

        Assert.Equal("error_during_execution: 5-hour limit reached", motivo);
    }
}
