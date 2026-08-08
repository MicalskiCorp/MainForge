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
    /// <see cref="FerramentasEmbutidas"/>, para as embutidas, e
    /// <see cref="FerramentasNegadas"/>, para tudo o mais.
    /// </summary>
    public IReadOnlyList<string> FerramentasPermitidas { get; init; } = [];

    /// <summary>
    /// O conjunto exato de ferramentas embutidas que existem nesta sessão (<c>--tools</c>).
    /// <c>null</c> deixa o padrão do Claude Code; uma lista vazia desliga todas.
    ///
    /// <para><b>Por que isto e não só a lista de negação.</b> Negar por nome exige adivinhar
    /// tudo que existe do outro lado, e o comentário de <c>NegacoesComuns</c> conta o preço de
    /// errar: um agente recebeu <c>PowerShell</c> porque só <c>Bash</c> estava negado. Aqui a
    /// direção se inverte — o que não está na lista não existe na sessão, e ferramenta nova do
    /// Claude Code não entra sozinha.</para>
    ///
    /// <para>Sai mais barato também: definição de ferramenta é token pago em toda requisição de
    /// todo turno, e uma sessão que só usa <c>Read</c> e <c>Glob</c> estava carregando o esquema
    /// de mais de uma dúzia de ferramentas que ela nunca poderia chamar.</para>
    /// </summary>
    public IReadOnlyList<string>? FerramentasEmbutidas { get; init; }

    /// <summary>
    /// Se as skills e os comandos de barra do usuário ficam de fora desta sessão
    /// (<c>--disable-slash-commands</c>).
    ///
    /// <para>Eles vêm de <c>.claude/</c>, que aqui é a configuração de quem desenvolve o
    /// aplicativo e fala do código dele — assunto nenhum de um agente de RPG. Negar a ferramenta
    /// <c>Skill</c> pelo nome já ajudava; desligar o mecanismo evita depender de acertar o
    /// nome.</para>
    /// </summary>
    public bool SemSkills { get; init; } = true;

    /// <summary>
    /// Regras de negação (<c>--disallowedTools</c>). Negação vence concessão, e é o único
    /// mecanismo que de fato bloqueia uma ferramenta embutida — inclusive por caminho, como
    /// em <c>Read(Input/**)</c>.
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

    /// <summary>
    /// Modelo e esforço de raciocínio deste agente. Vem do perfil escolhido pelo usuário cruzado
    /// com o tipo de trabalho do agente: ler livro e conversar sobre regras não precisam do mesmo
    /// modelo, e tratá-los igual era o maior desperdício de cota do aplicativo.
    /// </summary>
    public required AjusteDeExecucao Ajuste { get; init; }

    /// <summary>Teto de gasto deste turno, em dólares. <c>null</c> significa sem teto.</summary>
    public decimal? TetoDeGastoUsd { get; init; }
}
