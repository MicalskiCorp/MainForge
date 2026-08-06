using System.Text;
using MainForge.ClaudeCode;
using MainForge.Core;

namespace MainForge.Agents;

/// <summary>Falha de um turno do agente, com a mensagem já pronta para mostrar ao usuário.</summary>
public sealed class FalhaDoAgente(string mensagem) : Exception(mensagem);

/// <summary>
/// Uma conversa em andamento com um agente. Cada turno é uma execução do Claude Code em modo
/// headless; a continuidade entre turnos vem de <c>--session-id</c> no primeiro e
/// <c>--resume</c> nos seguintes — quem guarda o histórico é o Claude Code, não este objeto.
///
/// Isso é uma diferença real em relação à versão que falava com a Claude API direto, onde o
/// histórico morava em memória: agora a conversa sobrevive ao processo do aplicativo, e o
/// <see cref="IdDaSessao"/> é o que permite retomá-la.
///
/// <para><b>Cota esgotada não é erro.</b> Quando o Claude Code responde que a assinatura
/// estourou o limite da janela, o turno não falha: ele fica parado até a janela virar e então
/// retoma a mesma conversa, com todo o contexto que ela já tinha. É o comportamento certo
/// porque o consumo sai de uma cota que se renova sozinha — não há o que fazer além de
/// esperar, e desistir jogaria fora o livro que o agente já leu.</para>
/// </summary>
public sealed class SessaoDeAgente : IDisposable
{
    private readonly IExecutorDeTurno _executor;
    private readonly DefinicaoDeAgente _agente;
    private readonly CaminhosDoProjeto _caminhos;
    private readonly ConfiguracaoDoServidorMcp _configuracaoMcp;
    private readonly PoliticaDeLimiteDeUso _politica;
    private readonly Func<TimeSpan, CancellationToken, Task> _dormir;
    private readonly Func<DateTimeOffset> _agora;

    private string? _idDaSessao;
    private bool _jaIniciada;

    /// <param name="retomarSessao">
    /// Id de uma conversa anterior a continuar, em vez de começar do zero. É o que permite a um
    /// personagem interrompido voltar com todo o contexto já pago — a base que o agente leu, as
    /// escolhas já feitas. Se o Claude Code não tiver mais essa conversa, a sessão recomeça
    /// sozinha (veja <see cref="PareceSessaoPerdida"/>).
    /// </param>
    public SessaoDeAgente(
        OpcoesDoClaudeCode opcoes,
        DefinicaoDeAgente agente,
        CaminhosDoProjeto caminhos,
        PoliticaDeLimiteDeUso? politica = null,
        string? retomarSessao = null)
        : this(new ProcessoDoClaudeCode(opcoes), agente, caminhos, politica, retomarSessao: retomarSessao)
    {
    }

    /// <param name="dormir">Como esperar. Trocável para os testes não dormirem de verdade.</param>
    /// <param name="agora">Que horas são. Trocável pelo mesmo motivo.</param>
    /// <param name="retomarSessao">Id de uma conversa anterior a continuar.</param>
    public SessaoDeAgente(
        IExecutorDeTurno executor,
        DefinicaoDeAgente agente,
        CaminhosDoProjeto caminhos,
        PoliticaDeLimiteDeUso? politica = null,
        Func<TimeSpan, CancellationToken, Task>? dormir = null,
        Func<DateTimeOffset>? agora = null,
        string? retomarSessao = null)
    {
        _executor = executor;
        _agente = agente;
        _caminhos = caminhos;
        _configuracaoMcp = ConfiguracaoDoServidorMcp.Criar(caminhos, agente.FontesDaMesa);
        _politica = politica ?? PoliticaDeLimiteDeUso.Padrao;
        _dormir = dormir ?? Task.Delay;
        _agora = agora ?? (() => DateTimeOffset.Now);
        _idDaSessao = retomarSessao ?? Guid.NewGuid().ToString();

        // Conversa que já existe do outro lado se retoma com --resume; a nova se cria com
        // --session-id. Errar isto no primeiro turno é a diferença entre continuar de onde parou
        // e pagar de novo por tudo que o agente já tinha lido.
        _jaIniciada = retomarSessao is not null;
    }

    /// <summary>
    /// Identificador da conversa no Claude Code. Útil para diagnóstico — dá para inspecionar a
    /// sessão depois com <c>claude --resume &lt;id&gt;</c>.
    /// </summary>
    public string? IdDaSessao => _idDaSessao;

    /// <summary>
    /// Envia uma mensagem do usuário, deixa o agente resolver todas as chamadas de ferramenta
    /// necessárias e devolve o texto da resposta final.
    /// </summary>
    /// <param name="aoAcontecer">
    /// Chamado a cada acontecimento do turno (uso de ferramenta, erro de ferramenta, texto
    /// parcial, espera por limite de uso). Opcional; existe para a interface poder mostrar
    /// progresso durante turnos longos.
    /// </param>
    public async Task<string> EnviarAsync(
        string mensagem,
        Action<EventoDeAgente>? aoAcontecer = null,
        CancellationToken cancelamento = default)
    {
        var esperas = 0;
        var esperasAsCegas = 0;
        var jaRecomecouSessao = false;

        while (true)
        {
            var (conclusao, textos) = await ExecutarTurnoAsync(mensagem, aoAcontecer, cancelamento);

            if (conclusao.IdDaSessao is not null)
            {
                _idDaSessao = conclusao.IdDaSessao;
            }

            if (!conclusao.Falhou)
            {
                // A partir daqui a conversa existe no Claude Code e os próximos turnos a retomam.
                _jaIniciada = true;

                // A resposta final do 'result' é a fonte de verdade; o texto acumulado só entra
                // se ela vier vazia (acontece quando o turno termina logo após uma ferramenta).
                return conclusao.Resposta.Length > 0 ? conclusao.Resposta : textos.TrimEnd();
            }

            if (conclusao.Limite is { } limite && esperas < _politica.MaximoDeEsperas)
            {
                esperas++;

                // O trabalho já feito está do lado do Claude Code: retomar a conversa é o que
                // evita reler o livro inteiro depois da espera.
                _jaIniciada = _idDaSessao is not null;

                var alvo = QuandoAJanelaVira(limite, ref esperasAsCegas);

                await EsperarPelaProximaJanelaAsync(limite, alvo, aoAcontecer, cancelamento);
                continue;
            }

            // Retomar uma sessão que o Claude Code não tem mais (expirada, apagada, ou nunca
            // criada porque a primeira tentativa morreu cedo) é recuperável: recomeça a
            // conversa. Uma vez só — se falhar de novo, é outro problema.
            if (!jaRecomecouSessao && _jaIniciada && PareceSessaoPerdida(conclusao.MotivoDaFalha))
            {
                jaRecomecouSessao = true;
                _idDaSessao = Guid.NewGuid().ToString();
                _jaIniciada = false;
                continue;
            }

            throw new FalhaDoAgente(
                $"O agente '{_agente.Nome}' falhou: {conclusao.MotivoDaFalha ?? "motivo não informado"}");
        }
    }

    private async Task<(TurnoConcluido Conclusao, string Textos)> ExecutarTurnoAsync(
        string mensagem,
        Action<EventoDeAgente>? aoAcontecer,
        CancellationToken cancelamento)
    {
        var pedido = _agente.MontarPedido(
            _caminhos,
            mensagem,
            _configuracaoMcp.Caminho,
            _idDaSessao,
            retomar: _jaIniciada);

        var textos = new StringBuilder();
        TurnoConcluido? conclusao = null;

        await foreach (var evento in _executor.ExecutarAsync(pedido, cancelamento))
        {
            if (evento is TurnoConcluido fim)
            {
                conclusao = fim;
                continue;
            }

            if (evento is TextoDoAgente texto)
            {
                textos.AppendLine(texto.Texto);
            }

            aoAcontecer?.Invoke(evento);
        }

        return conclusao is null
            ? throw new FalhaDoAgente($"O agente '{_agente.Nome}' não devolveu nenhuma resposta.")
            : (conclusao, textos.ToString());
    }

    /// <summary>
    /// Quando vale a pena tentar de novo.
    ///
    /// <para>Com a hora informada pelo Claude Code, é ela mais uma margem. Sem ela — ou com uma
    /// hora que já passou, o que acontece quando a mensagem anterior era de outra janela — a
    /// espera é às cegas, e aí cada tentativa frustrada dobra a próxima até o teto da política.
    /// Sem esse escalonamento, uma política que nunca desiste tentaria a cada 15 minutos por
    /// dias, e cada tentativa é um Claude Code lançado à toa.</para>
    /// </summary>
    private DateTimeOffset QuandoAJanelaVira(LimiteDeUso limite, ref int esperasAsCegas)
    {
        var agora = _agora();

        if (limite.Liberacao is { } liberacao && liberacao + _politica.Margem > agora)
        {
            esperasAsCegas = 0;

            return liberacao + _politica.Margem;
        }

        // Deslocar mais de 20 vezes estouraria o long; e muito antes disso o teto já venceu.
        var dobras = Math.Min(esperasAsCegas++, 20);

        var ticks = Math.Min(
            _politica.EsperaPadrao.Ticks * (1L << dobras),
            _politica.EsperaPadraoMaxima.Ticks);

        return agora + TimeSpan.FromTicks(Math.Max(ticks, _politica.EsperaPadrao.Ticks));
    }

    /// <summary>
    /// Dorme até a janela de uso virar, avisando de tempos em tempos. A espera é fatiada para
    /// que o Ctrl+C do usuário seja percebido em segundos, e não só no fim.
    /// </summary>
    private async Task EsperarPelaProximaJanelaAsync(
        LimiteDeUso limite,
        DateTimeOffset alvo,
        Action<EventoDeAgente>? aoAcontecer,
        CancellationToken cancelamento)
    {
        var total = alvo - _agora();

        if (total > _politica.EsperaMaxima)
        {
            throw new FalhaDoAgente(
                $"{limite.Mensagem} A cota só volta em {Descrever(total)} " +
                $"({alvo:dd/MM HH:mm}), acima do limite de espera de {Descrever(_politica.EsperaMaxima)}. " +
                "O que já foi gerado está salvo: rode o processamento de novo quando a cota voltar e " +
                "ele continua de onde parou.");
        }

        while (true)
        {
            var restante = alvo - _agora();

            if (restante <= TimeSpan.Zero)
            {
                return;
            }

            aoAcontecer?.Invoke(new AguardandoLimiteDeUso(limite.Liberacao, restante, limite.Mensagem));

            var fatia = restante < _politica.IntervaloDeAviso ? restante : _politica.IntervaloDeAviso;

            await _dormir(fatia, cancelamento);
        }
    }

    /// <summary>
    /// O Claude Code não tem código de erro para "essa sessão não existe" — a mensagem é o que
    /// há. Reconhecê-la errado só custa uma conversa recomeçada, então o critério é frouxo de
    /// propósito.
    /// </summary>
    private static bool PareceSessaoPerdida(string? motivo)
    {
        if (motivo is null)
        {
            return false;
        }

        var fala = motivo.Contains("session", StringComparison.OrdinalIgnoreCase) ||
                   motivo.Contains("conversation", StringComparison.OrdinalIgnoreCase) ||
                   motivo.Contains("sessão", StringComparison.OrdinalIgnoreCase);

        var problema = motivo.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                       motivo.Contains("no conversation", StringComparison.OrdinalIgnoreCase) ||
                       motivo.Contains("already in use", StringComparison.OrdinalIgnoreCase) ||
                       motivo.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                       motivo.Contains("não encontrada", StringComparison.OrdinalIgnoreCase);

        return fala && problema;
    }

    /// <summary>
    /// Tempo em português curto. O caso de dias existe porque uma cota semanal esgotada é
    /// esperada de verdade agora: "6d13h" se lê; "157h30", não.
    /// </summary>
    public static string Descrever(TimeSpan tempo) => tempo switch
    {
        { TotalMinutes: < 1 } => $"{Math.Max(1, (int)tempo.TotalSeconds)}s",
        { TotalHours: < 1 } => $"{(int)tempo.TotalMinutes}min",
        { TotalDays: < 1 } => $"{(int)tempo.TotalHours}h{tempo.Minutes:00}",
        _ => $"{(int)tempo.TotalDays}d{tempo.Hours:00}h",
    };

    public void Dispose() => _configuracaoMcp.Dispose();
}
