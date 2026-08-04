using System.Runtime.CompilerServices;
using MainForge.Agents;
using MainForge.ClaudeCode;
using MainForge.Core;

namespace MainForge.Tests;

/// <summary>
/// A política de "cota esgotada não é erro": esperar a janela virar e retomar a mesma conversa.
/// Ela só se manifesta em condições que ninguém consegue reproduzir à mão na hora do teste, por
/// isso o Claude Code é substituído por um executor de mentira e o relógio, por um contador.
/// </summary>
public sealed class SessaoDeAgenteTestes : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "mainforge-sessao-" + Guid.NewGuid());
    private readonly CaminhosDoProjeto _caminhos;

    private DateTimeOffset _relogio = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private TimeSpan _dormido = TimeSpan.Zero;

    public SessaoDeAgenteTestes()
    {
        _caminhos = new CaminhosDoProjeto(_raiz);
        Directory.CreateDirectory(_raiz);
    }

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    /// <summary>Devolve, em ordem, os fins de turno combinados; o último se repete.</summary>
    private sealed class ExecutorFalso(params TurnoConcluido[] conclusoes) : IExecutorDeTurno
    {
        private int _chamadas;

        public List<PedidoDeTurno> Pedidos { get; } = [];

        public async IAsyncEnumerable<EventoDeAgente> ExecutarAsync(
            PedidoDeTurno pedido,
            [EnumeratorCancellation] CancellationToken cancelamento = default)
        {
            await Task.CompletedTask;
            Pedidos.Add(pedido);

            yield return conclusoes[Math.Min(_chamadas++, conclusoes.Length - 1)];
        }
    }

    private static TurnoConcluido Sucesso(string resposta) =>
        new(resposta, Guid.NewGuid().ToString(), Falhou: false, null, null);

    private static TurnoConcluido Falha(string motivo) =>
        new("", Guid.NewGuid().ToString(), Falhou: true, motivo, null);

    private static TurnoConcluido CotaEsgotada(DateTimeOffset? liberacao) =>
        new("", Guid.NewGuid().ToString(), Falhou: true, "usage limit reached", null,
            new LimiteDeUso("Claude AI usage limit reached", liberacao));

    private SessaoDeAgente Criar(IExecutorDeTurno executor, PoliticaDeLimiteDeUso? politica = null) =>
        new(executor,
            DefinicaoDeAgente.Configurador,
            _caminhos,
            politica,
            dormir: (tempo, _) =>
            {
                _dormido += tempo;
                _relogio += tempo;
                return Task.CompletedTask;
            },
            agora: () => _relogio);

    [Fact]
    public async Task CotaEsgotada_EsperaAJanelaVirarERetomaAMesmaConversa()
    {
        var executor = new ExecutorFalso(
            CotaEsgotada(_relogio.AddHours(2)),
            Sucesso("base gerada"));

        using var sessao = Criar(executor);

        var resposta = await sessao.EnviarAsync("processe o sistema");

        Assert.Equal("base gerada", resposta);
        Assert.Equal(2, executor.Pedidos.Count);
        Assert.True(_dormido >= TimeSpan.FromHours(2));

        // Retomar é o que preserva o livro que o agente já leu: sem isso, a espera teria sido
        // inútil porque a leitura seria refeita do zero.
        Assert.False(executor.Pedidos[0].Retomar);
        Assert.True(executor.Pedidos[1].Retomar);
    }

    [Fact]
    public async Task CotaEsgotadaSemHoraInformada_EsperaOIntervaloPadrao()
    {
        var executor = new ExecutorFalso(CotaEsgotada(null), Sucesso("pronto"));
        var politica = PoliticaDeLimiteDeUso.Padrao with { EsperaPadrao = TimeSpan.FromMinutes(10) };

        using var sessao = Criar(executor, politica);
        await sessao.EnviarAsync("processe");

        Assert.Equal(TimeSpan.FromMinutes(10), _dormido);
    }

    /// <summary>
    /// Uma cota semanal esgotada libera daqui a dias. Dormir esse tempo dentro do aplicativo
    /// seria pior que devolver o controle: o progresso já está salvo e o usuário decide quando
    /// voltar.
    /// </summary>
    [Fact]
    public async Task CotaQueSoVoltaDepoisDoTetoDeEspera_DesisteSemDormir()
    {
        var executor = new ExecutorFalso(CotaEsgotada(_relogio.AddDays(3)));

        using var sessao = Criar(executor);

        var excecao = await Assert.ThrowsAsync<FalhaDoAgente>(() => sessao.EnviarAsync("processe"));

        Assert.Equal(TimeSpan.Zero, _dormido);
        Assert.Single(executor.Pedidos);
        Assert.Contains("continua de onde parou", excecao.Message);
    }

    /// <summary>
    /// A mesma cota semanal, sob a política do processamento de sistema: aí esperar é o
    /// comportamento certo. Devolver o controle ao usuário custaria o contexto da conversa — o
    /// livro que o agente já leu — e a leitura seria cobrada de novo na próxima execução.
    /// </summary>
    [Fact]
    public async Task CotaSemanal_NoProcessamentoLongo_EsperaOsDiasERetomaAConversa()
    {
        var executor = new ExecutorFalso(
            CotaEsgotada(_relogio.AddDays(3)),
            Sucesso("base gerada"));

        using var sessao = Criar(executor, PoliticaDeLimiteDeUso.ProcessamentoLongo);

        var resposta = await sessao.EnviarAsync("processe");

        Assert.Equal("base gerada", resposta);
        Assert.True(_dormido >= TimeSpan.FromDays(3));
        Assert.True(executor.Pedidos[1].Retomar);
    }

    /// <summary>
    /// Três esperas seguidas passariam do teto da política padrão. No processamento de sistema
    /// não há teto: o usuário mandou processar e a única saída é esperar quantas janelas forem
    /// precisas.
    /// </summary>
    [Fact]
    public async Task VariasJanelasEsgotadasSeguidas_NoProcessamentoLongo_ContinuaEsperando()
    {
        var executor = new ExecutorFalso(
            CotaEsgotada(_relogio.AddHours(5)),
            CotaEsgotada(_relogio.AddHours(10)),
            CotaEsgotada(_relogio.AddHours(15)),
            CotaEsgotada(_relogio.AddHours(20)),
            Sucesso("base gerada"));

        using var sessao = Criar(executor, PoliticaDeLimiteDeUso.ProcessamentoLongo);

        Assert.Equal("base gerada", await sessao.EnviarAsync("processe"));
        Assert.Equal(5, executor.Pedidos.Count);
    }

    /// <summary>
    /// Sem hora informada, uma política que nunca desiste tentaria a cada 15 minutos por dias —
    /// e cada tentativa é um Claude Code lançado à toa. O intervalo dobra até o teto.
    /// </summary>
    [Fact]
    public async Task CotaEsgotadaSemHoraInformada_DobraAEsperaACadaTentativaFrustrada()
    {
        var executor = new ExecutorFalso(
            CotaEsgotada(null),
            CotaEsgotada(null),
            CotaEsgotada(null),
            Sucesso("pronto"));

        var politica = PoliticaDeLimiteDeUso.ProcessamentoLongo with
        {
            EsperaPadrao = TimeSpan.FromMinutes(10),
            EsperaPadraoMaxima = TimeSpan.FromMinutes(30),
        };

        using var sessao = Criar(executor, politica);
        await sessao.EnviarAsync("processe");

        // 10min + 20min + 30min (o teto barra o terceiro, que dobrado seria 40min).
        Assert.Equal(TimeSpan.FromMinutes(60), _dormido);
    }

    /// <summary>
    /// Uma hora de liberação que já passou chega quando a mensagem é de uma janela anterior.
    /// Sem tratar isso como espera às cegas, a política que nunca desiste viraria um laço
    /// quente: alvo no passado, espera zero, tentativa imediata, para sempre.
    /// </summary>
    [Fact]
    public async Task HoraDeLiberacaoNoPassado_EsperaEmVezDeTentarNaHora()
    {
        var executor = new ExecutorFalso(CotaEsgotada(_relogio.AddHours(-2)), Sucesso("pronto"));

        var politica = PoliticaDeLimiteDeUso.ProcessamentoLongo with
        {
            EsperaPadrao = TimeSpan.FromMinutes(10),
        };

        using var sessao = Criar(executor, politica);
        await sessao.EnviarAsync("processe");

        Assert.Equal(TimeSpan.FromMinutes(10), _dormido);
    }

    [Fact]
    public async Task CotaEsgotadaSempre_DesisteDepoisDoMaximoDeEsperas()
    {
        var executor = new ExecutorFalso(CotaEsgotada(_relogio.AddMinutes(30)));
        var politica = PoliticaDeLimiteDeUso.Padrao with { MaximoDeEsperas = 2 };

        using var sessao = Criar(executor, politica);

        await Assert.ThrowsAsync<FalhaDoAgente>(() => sessao.EnviarAsync("processe"));

        Assert.Equal(3, executor.Pedidos.Count); // a primeira tentativa mais as duas esperas
    }

    [Fact]
    public async Task FalhaQueNaoECota_NaoEsperaNada()
    {
        var executor = new ExecutorFalso(Falha("tool 'Bash' is not allowed"));

        using var sessao = Criar(executor);

        await Assert.ThrowsAsync<FalhaDoAgente>(() => sessao.EnviarAsync("processe"));

        Assert.Equal(TimeSpan.Zero, _dormido);
        Assert.Single(executor.Pedidos);
    }

    /// <summary>
    /// Depois de horas esperando, o Claude Code pode não ter mais a sessão. Recomeçar a conversa
    /// custa reler o livro, mas ainda é melhor que devolver um erro para o usuário no fim da
    /// espera.
    /// </summary>
    [Fact]
    public async Task SessaoPerdidaNoClaudeCode_RecomecaAConversaUmaVez()
    {
        var executor = new ExecutorFalso(
            Sucesso("primeiro turno"),
            Falha("No conversation found with session ID: abc"),
            Sucesso("segundo turno"));

        using var sessao = Criar(executor);

        await sessao.EnviarAsync("oi");
        var resposta = await sessao.EnviarAsync("continua");

        Assert.Equal("segundo turno", resposta);
        Assert.Equal(3, executor.Pedidos.Count);
        Assert.True(executor.Pedidos[1].Retomar);
        Assert.False(executor.Pedidos[2].Retomar);
        Assert.NotEqual(executor.Pedidos[1].IdDaSessao, executor.Pedidos[2].IdDaSessao);
    }

    [Fact]
    public async Task EsperaPelaCota_AvisaAInterfaceEnquantoDorme()
    {
        var executor = new ExecutorFalso(CotaEsgotada(_relogio.AddMinutes(5)), Sucesso("pronto"));
        var avisos = new List<AguardandoLimiteDeUso>();

        using var sessao = Criar(executor);

        await sessao.EnviarAsync("processe", evento =>
        {
            if (evento is AguardandoLimiteDeUso espera)
            {
                avisos.Add(espera);
            }
        });

        Assert.NotEmpty(avisos);
        Assert.All(avisos, aviso => Assert.Contains("usage limit", aviso.Mensagem));

        // Os avisos precisam ir diminuindo: é o que a interface mostra como contagem regressiva.
        Assert.True(avisos[0].Restante > avisos[^1].Restante);
    }
}
