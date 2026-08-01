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
/// </summary>
public sealed class SessaoDeAgente : IDisposable
{
    private readonly ProcessoDoClaudeCode _processo;
    private readonly DefinicaoDeAgente _agente;
    private readonly CaminhosDoProjeto _caminhos;
    private readonly ConfiguracaoDoServidorMcp _configuracaoMcp;

    private string? _idDaSessao;
    private bool _jaIniciada;

    public SessaoDeAgente(OpcoesDoClaudeCode opcoes, DefinicaoDeAgente agente, CaminhosDoProjeto caminhos)
    {
        _processo = new ProcessoDoClaudeCode(opcoes);
        _agente = agente;
        _caminhos = caminhos;
        _configuracaoMcp = ConfiguracaoDoServidorMcp.Criar(caminhos);
        _idDaSessao = Guid.NewGuid().ToString();
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
    /// parcial). Opcional; existe para a interface poder mostrar progresso durante turnos
    /// longos.
    /// </param>
    public async Task<string> EnviarAsync(
        string mensagem,
        Action<EventoDeAgente>? aoAcontecer = null,
        CancellationToken cancelamento = default)
    {
        var pedido = _agente.MontarPedido(
            _caminhos,
            mensagem,
            _configuracaoMcp.Caminho,
            _idDaSessao,
            retomar: _jaIniciada);

        var textos = new StringBuilder();
        TurnoConcluido? conclusao = null;

        await foreach (var evento in _processo.ExecutarAsync(pedido, cancelamento))
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

        if (conclusao is null)
        {
            throw new FalhaDoAgente($"O agente '{_agente.Nome}' não devolveu nenhuma resposta.");
        }

        if (conclusao.IdDaSessao is not null)
        {
            _idDaSessao = conclusao.IdDaSessao;
        }

        if (conclusao.Falhou)
        {
            throw new FalhaDoAgente(
                $"O agente '{_agente.Nome}' falhou: {conclusao.MotivoDaFalha ?? "motivo não informado"}");
        }

        // A partir daqui a conversa existe no Claude Code e os próximos turnos a retomam.
        _jaIniciada = true;

        // A resposta final do 'result' é a fonte de verdade; o texto acumulado só entra se
        // ela vier vazia (acontece quando o turno termina logo após uma ferramenta).
        return conclusao.Resposta.Length > 0
            ? conclusao.Resposta
            : textos.ToString().TrimEnd();
    }

    public void Dispose() => _configuracaoMcp.Dispose();
}
