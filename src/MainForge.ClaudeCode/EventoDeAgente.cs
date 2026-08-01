namespace MainForge.ClaudeCode;

/// <summary>
/// Um acontecimento durante um turno do agente, traduzido do fluxo <c>stream-json</c> do
/// Claude Code para algo que a interface entenda sem conhecer o formato do CLI.
/// </summary>
public abstract record EventoDeAgente;

/// <summary>Texto que o agente escreveu — pode vir em vários pedaços ao longo do turno.</summary>
public sealed record TextoDoAgente(string Texto) : EventoDeAgente;

/// <summary>
/// O agente acabou de pedir uma ferramenta, antes de ela executar. Serve para a interface
/// mostrar progresso ("lendo o PDF...", "escrevendo Classes.md...") em vez de ficar parada.
/// </summary>
/// <param name="Nome">Nome da ferramenta, ex.: <c>Read</c> ou <c>mcp__mainforge__preencher_ficha_personagem</c>.</param>
/// <param name="Entrada">Argumentos achatados em "campo=valor" para exibição.</param>
public sealed record UsoDeFerramenta(string Nome, string Entrada) : EventoDeAgente;

/// <summary>
/// Uma ferramenta devolveu erro. O caso mais interessante é a negação por permissão: é assim
/// que o guardrail aparece na interface quando um agente tenta algo fora do seu escopo.
/// </summary>
public sealed record FalhaDeFerramenta(string Detalhe) : EventoDeAgente;

/// <summary>Sinal de que o agente está raciocinando (o conteúdo do raciocínio não é exposto).</summary>
public sealed record AgentePensando : EventoDeAgente;

/// <summary>
/// Fim do turno. <paramref name="Resposta"/> é o texto final do agente.
/// <paramref name="IdDaSessao"/> precisa ser guardado para retomar a conversa no turno
/// seguinte.
/// </summary>
public sealed record TurnoConcluido(
    string Resposta,
    string? IdDaSessao,
    bool Falhou,
    string? MotivoDaFalha,
    decimal? CustoUsd) : EventoDeAgente;
