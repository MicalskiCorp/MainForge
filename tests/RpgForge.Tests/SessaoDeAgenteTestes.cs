using System.Text.Json;
using Anthropic.Core;
using Anthropic.Models.Beta.Messages;
using Anthropic.Services.Beta;
using Anthropic.Services.Beta.Messages;
using RpgForge.Agents;
using RpgForge.Claude;
using RpgForge.Core;

namespace RpgForge.Tests;

public class SessaoDeAgenteTestes : IDisposable
{
    private readonly string _raiz;
    private readonly CaminhosDoProjeto _caminhos;

    public SessaoDeAgenteTestes()
    {
        _raiz = Path.Combine(Path.GetTempPath(), "rpgforge-tests-" + Guid.NewGuid());
        _caminhos = new CaminhosDoProjeto(_raiz);

        Directory.CreateDirectory(_caminhos.Agentes);
        File.WriteAllText(Path.Combine(_caminhos.Agentes, "Configurador.md"), "Prompt de teste do Configurador.");
        File.WriteAllText(Path.Combine(_caminhos.Agentes, "DungeonMaster.md"), "Prompt de teste do Dungeon Master.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    /// <summary>
    /// Dublê de <see cref="IMessageService"/> que devolve respostas roteirizadas em vez de
    /// chamar a API de verdade — permite testar o loop do BetaToolRunner (SessaoDeAgente)
    /// de ponta a ponta, incluindo a execução real das nossas ferramentas contra o disco.
    /// </summary>
    private sealed class ServicoDeMensagensFalso(Queue<BetaMessage> respostas) : IMessageService
    {
        public List<MessageCreateParams> Chamadas { get; } = [];

        public Task<BetaMessage> Create(MessageCreateParams parameters, CancellationToken cancellationToken)
        {
            Chamadas.Add(parameters);
            return Task.FromResult(respostas.Dequeue());
        }

        public IMessageServiceWithRawResponse WithRawResponse => throw new NotSupportedException();

        public IMessageService WithOptions(Func<ClientOptions, ClientOptions> modifier) => throw new NotSupportedException();

        public IBatchService Batches => throw new NotSupportedException();

        public IAsyncEnumerable<BetaRawMessageStreamEvent> CreateStreaming(MessageCreateParams parameters, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BetaMessageTokensCount> CountTokens(MessageCountTokensParams parameters, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Constrói um <see cref="BetaMessage"/> a partir do JSON exatamente como a API devolve,
    /// em vez de um inicializador de objeto — a maioria das propriedades de BetaMessage é
    /// "required" (inclusive as que só fazem sentido nulas), então desserializar o payload
    /// real é bem mais simples do que preencher cada uma manualmente.
    /// </summary>
    private static BetaMessage MensagemDe(string json) => JsonSerializer.Deserialize<BetaMessage>(json)!;

    [Fact]
    public async Task EnviarAsync_ExecutaChamadaDeFerramentaEDevolveRespostaFinalDoTexto()
    {
        Directory.CreateDirectory(Path.Combine(_caminhos.Sistemas, "Aventura&Cia"));

        var chamadaDeFerramenta = MensagemDe("""
            {
              "id": "msg_1",
              "type": "message",
              "role": "assistant",
              "model": "claude-opus-5",
              "content": [{ "type": "tool_use", "id": "tu_1", "name": "listar_sistemas", "input": {} }],
              "stop_reason": "tool_use",
              "stop_sequence": null,
              "usage": { "input_tokens": 10, "output_tokens": 5 }
            }
            """);

        var respostaFinal = MensagemDe("""
            {
              "id": "msg_2",
              "type": "message",
              "role": "assistant",
              "model": "claude-opus-5",
              "content": [{ "type": "text", "text": "Encontrei 1 sistema." }],
              "stop_reason": "end_turn",
              "stop_sequence": null,
              "usage": { "input_tokens": 20, "output_tokens": 8 }
            }
            """);

        var servico = new ServicoDeMensagensFalso(new Queue<BetaMessage>([chamadaDeFerramenta, respostaFinal]));
        var opcoes = new OpcoesClienteClaude { ChaveApi = "chave-de-teste" };
        var sessao = new SessaoDeAgente(servico, DefinicaoDeAgente.Configurador, _caminhos, opcoes);

        var resposta = await sessao.EnviarAsync("Quais sistemas existem?");

        Assert.Equal("Encontrei 1 sistema.", resposta);
        Assert.Equal(2, servico.Chamadas.Count);
    }

    [Fact]
    public async Task EnviarAsync_AcumulaHistoricoEntreChamadas()
    {
        var respostaSimples = MensagemDe("""
            {
              "id": "msg_1",
              "type": "message",
              "role": "assistant",
              "model": "claude-opus-5",
              "content": [{ "type": "text", "text": "Oi!" }],
              "stop_reason": "end_turn",
              "stop_sequence": null,
              "usage": { "input_tokens": 10, "output_tokens": 5 }
            }
            """);

        var servico = new ServicoDeMensagensFalso(new Queue<BetaMessage>([respostaSimples]));
        var opcoes = new OpcoesClienteClaude { ChaveApi = "chave-de-teste" };
        var sessao = new SessaoDeAgente(servico, DefinicaoDeAgente.DungeonMaster, _caminhos, opcoes);

        await sessao.EnviarAsync("Olá");

        Assert.Equal(2, sessao.Historico.Count);
        Assert.Equal("user", (string)servico.Chamadas[0].Messages[0].Role);
    }
}
