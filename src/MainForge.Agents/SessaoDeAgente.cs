using System.Text.Json;
using Anthropic.Helpers.Beta;
using Anthropic.Models.Beta.Messages;
using Anthropic.Services.Beta;
using MainForge.Claude;
using MainForge.Core;

namespace MainForge.Agents;

/// <summary>
/// Uma chamada de ferramenta que o agente acabou de fazer, publicada enquanto o turno ainda
/// está em andamento. Serve para a interface mostrar progresso ("lendo o PDF...",
/// "escrevendo Classes.md...") em vez de ficar parada até a resposta final chegar.
/// </summary>
/// <param name="Nome">Nome da ferramenta, ex.: "escrever_arquivo_conhecimento".</param>
/// <param name="Entrada">Argumentos da chamada, já achatados em "campo=valor" para exibição.</param>
public sealed record UsoDeFerramenta(string Nome, string Entrada);

/// <summary>
/// Uma conversa em andamento com um agente: liga o prompt de sistema e as ferramentas
/// resolvidas de <see cref="DefinicaoDeAgente"/> ao loop de tool-use automático do SDK
/// Anthropic (<see cref="BetaToolRunner"/>) e mantém o histórico entre turnos. Recebe o
/// <see cref="IMessageService"/> beta em vez do <c>AnthropicClient</c> inteiro (via
/// <c>cliente.Beta.Messages</c>), para poder ser testada com um serviço falso sem chamar a
/// API de verdade.
/// </summary>
public sealed class SessaoDeAgente
{
    private readonly IMessageService _servicoDeMensagens;
    private readonly DefinicaoDeAgente _agente;
    private readonly CaminhosDoProjeto _caminhos;
    private readonly OpcoesClienteClaude _opcoes;
    private readonly IReadOnlyList<IBetaRunnableTool> _ferramentas;

    private List<BetaMessageParam> _historico = [];

    public SessaoDeAgente(
        IMessageService servicoDeMensagens,
        DefinicaoDeAgente agente,
        CaminhosDoProjeto caminhos,
        OpcoesClienteClaude opcoes)
    {
        _servicoDeMensagens = servicoDeMensagens;
        _agente = agente;
        _caminhos = caminhos;
        _opcoes = opcoes;
        _ferramentas = agente.ResolverFerramentas(caminhos);
    }

    public IReadOnlyList<BetaMessageParam> Historico => _historico;

    /// <summary>
    /// Envia uma mensagem do usuário, deixa o BetaToolRunner resolver todas as chamadas de
    /// ferramenta necessárias em um ou mais turnos, e devolve o texto da resposta final do
    /// agente. O histórico completo (incluindo tool_use/tool_result intermediários) fica
    /// acumulado para a próxima chamada — é assim que o agente lembra da conversa.
    /// </summary>
    /// <param name="aoUsarFerramenta">
    /// Chamado assim que o modelo pede cada ferramenta, antes de ela ser executada. Opcional;
    /// existe para a interface poder mostrar progresso durante turnos longos.
    /// </param>
    public async Task<string> EnviarAsync(
        string mensagemDoUsuario,
        Action<UsoDeFerramenta>? aoUsarFerramenta = null,
        CancellationToken cancelamento = default)
    {
        var mensagens = new List<BetaMessageParam>(_historico)
        {
            new() { Role = "user", Content = mensagemDoUsuario },
        };

        var parametros = new MessageCreateParams
        {
            Model = _opcoes.Modelo,
            MaxTokens = 8192,
            System = _agente.CarregarPromptDeSistema(_caminhos),
            Messages = mensagens,
        };

        var executor = _servicoDeMensagens.ToolRunner(parametros, _ferramentas, maxIterations: 50);

        // Equivale a RunUntilDoneAsync (que é só "itera e devolve a última mensagem"), mas
        // percorrendo o loop à mão para poder publicar cada tool_use enquanto ele acontece.
        BetaMessage? ultimaResposta = null;

        await foreach (var resposta in executor.WithCancellation(cancelamento))
        {
            ultimaResposta = resposta;

            if (aoUsarFerramenta is null)
            {
                continue;
            }

            foreach (var bloco in resposta.Content)
            {
                if (bloco.TryPickToolUse(out var usoDeFerramenta))
                {
                    aoUsarFerramenta(new UsoDeFerramenta(usoDeFerramenta.Name, DescreverEntrada(usoDeFerramenta)));
                }
            }
        }

        var respostaFinal = ultimaResposta
            ?? throw new InvalidOperationException(
                $"O agente '{_agente.Nome}' não devolveu nenhuma resposta.");

        // O BetaToolRunner só acrescenta um turno a Params.Messages quando prepara a
        // PRÓXIMA chamada — o turno final (sem mais tool_use) nunca entra sozinho, então
        // precisa ser adicionado manualmente aqui para o histórico ficar completo.
        var turnoFinal = new BetaMessageParam
        {
            Role = "assistant",
            Content = respostaFinal.Content.Select(ConverterParaBlocoDeParametro).ToList(),
        };

        _historico = [.. executor.Params.Messages, turnoFinal];

        return ExtrairTexto(respostaFinal);
    }

    /// <summary>
    /// Achata os argumentos de uma chamada de ferramenta em "campo=valor, campo=valor",
    /// truncando valores longos — o conteúdo inteiro de um arquivo Markdown não cabe (nem faz
    /// sentido) numa linha de progresso no console.
    /// </summary>
    private static string DescreverEntrada(BetaToolUseBlock usoDeFerramenta) =>
        string.Join(", ", usoDeFerramenta.Input.Select(par => $"{par.Key}={ResumirValor(par.Value)}"));

    private static string ResumirValor(JsonElement valor)
    {
        const int limite = 60;

        var texto = (valor.ValueKind == JsonValueKind.String ? valor.GetString() ?? "" : valor.GetRawText())
            .ReplaceLineEndings(" ")
            .Trim();

        return texto.Length <= limite ? texto : string.Concat(texto.AsSpan(0, limite), "...");
    }

    private static BetaContentBlockParam ConverterParaBlocoDeParametro(BetaContentBlock bloco)
    {
        if (bloco.TryPickText(out var texto))
        {
            return new BetaTextBlockParam(texto.Text);
        }

        if (bloco.TryPickToolUse(out var usoDeFerramenta))
        {
            return new BetaToolUseBlockParam
            {
                ID = usoDeFerramenta.ID,
                Name = usoDeFerramenta.Name,
                Input = usoDeFerramenta.Input,
            };
        }

        throw new NotSupportedException(
            $"Bloco de conteúdo do tipo '{bloco.Json}' não é suportado ao reconstruir o histórico da conversa.");
    }

    private static string ExtrairTexto(BetaMessage mensagem)
    {
        var partes = new List<string>();

        foreach (var bloco in mensagem.Content)
        {
            if (bloco.TryPickText(out var texto))
            {
                partes.Add(texto.Text);
            }
        }

        return string.Join("\n\n", partes);
    }
}
