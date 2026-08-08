using MainForge.Core;

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
/// A cota da assinatura acabou e o turno está parado esperando a próxima janela abrir. É
/// repetido de tempos em tempos durante a espera, para a interface poder mostrar quanto falta
/// em vez de parecer travada.
/// </summary>
/// <param name="Ate">Quando a janela abre, se o Claude Code informou.</param>
/// <param name="Restante">Quanto ainda falta esperar.</param>
/// <param name="Mensagem">A mensagem original do Claude Code, já resumida.</param>
public sealed record AguardandoLimiteDeUso(
    DateTimeOffset? Ate,
    TimeSpan Restante,
    string Mensagem) : EventoDeAgente;

/// <summary>
/// Fim do turno. <paramref name="Resposta"/> é o texto final do agente.
/// <paramref name="IdDaSessao"/> precisa ser guardado para retomar a conversa no turno
/// seguinte.
/// </summary>
/// <param name="Consumo">
/// O que este turno custou, quando o Claude Code informou. <c>null</c> num turno que morreu antes
/// de o CLI fechar a conta.
/// </param>
/// <param name="Limite">
/// Preenchido quando a falha foi cota da assinatura esgotada, e não erro. Quem chama decide se
/// espera a janela virar ou desiste.
/// </param>
public sealed record TurnoConcluido(
    string Resposta,
    string? IdDaSessao,
    bool Falhou,
    string? MotivoDaFalha,
    ConsumoDeTokens? Consumo,
    LimiteDeUso? Limite = null) : EventoDeAgente;
