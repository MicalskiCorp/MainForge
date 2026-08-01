namespace MainForge.ClaudeCode;

/// <summary>
/// Tudo que define uma invocação do Claude Code: a mensagem do usuário, o prompt de sistema
/// do agente, o allowlist/denylist de ferramentas e a continuidade da conversa. É deliberado
/// que este tipo não saiba nada sobre agentes de RPG — quem traduz
/// <c>DefinicaoDeAgente</c> para isto é o projeto MainForge.Agents.
/// </summary>
public sealed record PedidoDeTurno
{
    /// <summary>A mensagem do usuário para este turno. Vai pela stdin, nunca pela linha de comando.</summary>
    public required string Mensagem { get; init; }

    /// <summary>
    /// Diretório de trabalho do processo. O Claude Code restringe o acesso a arquivos ao seu
    /// diretório de trabalho, então apontar isto para a raiz do projeto já é a primeira
    /// camada de confinamento — antes mesmo das regras por ferramenta.
    /// </summary>
    public required string DiretorioDeTrabalho { get; init; }

    /// <summary>
    /// Caminho do arquivo .md com o prompt de sistema do agente. Substitui integralmente o
    /// prompt padrão do Claude Code: aqui o agente é um Configurador ou um Dungeon Master,
    /// não um assistente de programação.
    /// </summary>
    public required string CaminhoPromptDeSistema { get; init; }

    /// <summary>
    /// Ferramentas explicitamente concedidas (<c>--allowedTools</c>). Atenção: isto só
    /// <em>concede</em> — não restringe. O que efetivamente bloqueia é
    /// <see cref="FerramentasNegadas"/>.
    /// </summary>
    public IReadOnlyList<string> FerramentasPermitidas { get; init; } = [];

    /// <summary>
    /// Regras de negação (<c>--disallowedTools</c>). Negação vence concessão, e é o único
    /// mecanismo que de fato bloqueia uma ferramenta embutida — inclusive por caminho, como
    /// em <c>Read(Systems/**)</c>.
    /// </summary>
    public IReadOnlyList<string> FerramentasNegadas { get; init; } = [];

    /// <summary>Caminho do JSON de configuração do servidor MCP local, quando o agente usa um.</summary>
    public string? CaminhoConfigMcp { get; init; }

    /// <summary>
    /// Identificador da conversa. Definido por nós no primeiro turno (<c>--session-id</c>) e
    /// reusado nos seguintes (<c>--resume</c>) — é assim que o Dungeon Master lembra da
    /// conversa entre uma mensagem e outra.
    /// </summary>
    public string? IdDaSessao { get; init; }

    /// <summary>Se este turno deve retomar a conversa de <see cref="IdDaSessao"/> em vez de começar uma nova.</summary>
    public bool Retomar { get; init; }
}
