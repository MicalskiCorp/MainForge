namespace MainForge.ClaudeCode;

/// <summary>
/// Quem sabe rodar um turno de agente. Existe para que <c>SessaoDeAgente</c> — onde mora a
/// política de retomada e de espera por limite de uso — possa ser exercitada sem lançar o
/// Claude Code de verdade: essa política é justamente a que só se manifesta em condições
/// difíceis de reproduzir à mão (cota esgotada, sessão perdida).
/// </summary>
public interface IExecutorDeTurno
{
    IAsyncEnumerable<EventoDeAgente> ExecutarAsync(PedidoDeTurno pedido, CancellationToken cancelamento = default);
}
