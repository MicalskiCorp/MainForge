using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MainForge.Core;

namespace MainForge.ClaudeCode;

/// <summary>
/// Executa um turno do agente lançando o Claude Code em modo headless e traduzindo o fluxo
/// <c>stream-json</c> da saída padrão em <see cref="EventoDeAgente"/>. É o substituto do
/// antigo cliente da Claude API: em vez de mandar mensagens para a API com uma chave, o
/// aplicativo dirige um Claude Code já autenticado com a assinatura do usuário.
/// </summary>
public sealed class ProcessoDoClaudeCode(OpcoesDoClaudeCode opcoes) : IExecutorDeTurno
{
    /// <summary>
    /// Roda um turno e devolve os acontecimentos conforme eles chegam. O último evento é
    /// sempre um <see cref="TurnoConcluido"/>, inclusive quando o processo falha — quem chama
    /// nunca precisa inspecionar código de saída.
    /// </summary>
    public async IAsyncEnumerable<EventoDeAgente> ExecutarAsync(
        PedidoDeTurno pedido,
        [EnumeratorCancellation] CancellationToken cancelamento = default)
    {
        using var processo = new Process { StartInfo = MontarInicio(pedido) };

        var erros = new StringBuilder();

        processo.ErrorDataReceived += (_, evento) =>
        {
            if (evento.Data is not null)
            {
                erros.AppendLine(evento.Data);
            }
        };

        if (!processo.Start())
        {
            yield return new TurnoConcluido(
                "", null, Falhou: true,
                $"Não foi possível iniciar '{opcoes.CaminhoExecutavel}'.", null, null);
            yield break;
        }

        processo.BeginErrorReadLine();

        // A mensagem vai pela stdin, não pela linha de comando: prompts longos, com quebras
        // de linha e aspas, quebrariam qualquer tentativa de escapar argumentos no Windows.
        await processo.StandardInput.WriteAsync(pedido.Mensagem);
        processo.StandardInput.Close();

        string? idDaSessao = null;
        string? respostaFinal = null;
        bool falhou = false;
        string? motivo = null;
        ConsumoDeTokens? consumo = null;

        try
        {
            while (await processo.StandardOutput.ReadLineAsync(cancelamento) is { } linha)
            {
                if (linha.Length == 0)
                {
                    continue;
                }

                foreach (var evento in Interpretar(linha, ref idDaSessao, ref respostaFinal, ref falhou, ref motivo, ref consumo))
                {
                    yield return evento;
                }
            }

            await processo.WaitForExitAsync(cancelamento);
        }
        finally
        {
            Encerrar(processo);
        }

        if (respostaFinal is null && !falhou)
        {
            falhou = true;
            motivo = erros.Length > 0
                ? erros.ToString().Trim()
                : $"O Claude Code terminou com código {processo.ExitCode} sem produzir resposta.";
        }

        // Cota esgotada é procurada só no caminho de falha, e sobre tudo que o CLI disse: a
        // mensagem tanto pode vir no corpo do 'result' quanto na saída de erro, dependendo de
        // onde o limite bateu.
        var limite = falhou
            ? DetectorDeLimiteDeUso.Detectar($"{motivo}\n{erros}")
            : null;

        yield return new TurnoConcluido(respostaFinal ?? "", idDaSessao, falhou, motivo, consumo, limite);
    }

    private ProcessStartInfo MontarInicio(PedidoDeTurno pedido)
    {
        var inicio = new ProcessStartInfo
        {
            FileName = opcoes.CaminhoExecutavel,
            WorkingDirectory = pedido.DiretorioDeTrabalho,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Sem UTF-8 explícito, acentos do português viram lixo nos dois sentidos.
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        var argumentos = inicio.ArgumentList;

        // -p sem valor posicional faz o Claude Code ler o prompt da stdin.
        argumentos.Add("-p");
        argumentos.Add("--output-format");
        argumentos.Add("stream-json");
        argumentos.Add("--verbose");            // exigido junto de stream-json em modo -p
        argumentos.Add("--model");
        argumentos.Add(pedido.Ajuste.Modelo);

        // O esforço de raciocínio é gasto que não aparece na contagem de entrada e aparece
        // inteiro na conta. Deixá-lo no padrão era pagar raciocínio de problema difícil para
        // transcrever a tabela de armas de um livro.
        argumentos.Add("--effort");
        argumentos.Add(pedido.Ajuste.Esforco);

        argumentos.Add("--system-prompt-file");
        argumentos.Add(pedido.CaminhoPromptDeSistema);
        argumentos.Add("--permission-mode");
        argumentos.Add("default");              // nunca bypassPermissions: as negações precisam valer

        if (pedido.TetoDeGastoUsd is { } teto)
        {
            argumentos.Add("--max-budget-usd");
            argumentos.Add(teto.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // --tools define o que existe; --allowedTools define o que roda sem perguntar. Os dois
        // são necessários: sem o primeiro, o modelo recebe o esquema de ferramentas que jamais
        // poderia usar; sem o segundo, cada chamada pararia pedindo permissão a um console que
        // não está esperando por isso.
        if (pedido.FerramentasEmbutidas is { } embutidas)
        {
            argumentos.Add("--tools");
            argumentos.Add(string.Join(",", embutidas));
        }

        if (pedido.SemSkills)
        {
            argumentos.Add("--disable-slash-commands");
        }

        // Nenhuma configuração de fora: nem a do usuário (~/.claude), nem a do projeto
        // (.claude/settings.json), nem a local. Elas são de quem desenvolve o aplicativo ou de
        // quem usa o Claude Code para outra coisa, e um hook definido lá rodaria dentro da sessão
        // do agente de RPG — que é a última coisa que alguém esperava ao configurá-lo.
        argumentos.Add("--setting-sources");
        argumentos.Add("");

        if (pedido.FerramentasPermitidas.Count > 0)
        {
            argumentos.Add("--allowedTools");
            argumentos.Add(string.Join(",", pedido.FerramentasPermitidas));
        }

        if (pedido.FerramentasNegadas.Count > 0)
        {
            argumentos.Add("--disallowedTools");
            argumentos.Add(string.Join(",", pedido.FerramentasNegadas));
        }

        if (pedido.CaminhoConfigMcp is not null)
        {
            argumentos.Add("--mcp-config");
            argumentos.Add(pedido.CaminhoConfigMcp);
            // Sem isto, os servidores MCP globais do usuário entrariam na sessão do agente.
            argumentos.Add("--strict-mcp-config");
        }

        if (pedido.IdDaSessao is not null)
        {
            argumentos.Add(pedido.Retomar ? "--resume" : "--session-id");
            argumentos.Add(pedido.IdDaSessao);
        }

        return inicio;
    }

    private static IEnumerable<EventoDeAgente> Interpretar(
        string linha,
        ref string? idDaSessao,
        ref string? respostaFinal,
        ref bool falhou,
        ref string? motivo,
        ref ConsumoDeTokens? consumo)
    {
        JsonDocument documento;

        try
        {
            documento = JsonDocument.Parse(linha);
        }
        catch (JsonException)
        {
            // O CLI pode escrever avisos soltos na stdout; ignorar é melhor que derrubar o turno.
            return [];
        }

        using (documento)
        {
            var raiz = documento.RootElement;

            if (raiz.ValueKind != JsonValueKind.Object ||
                !raiz.TryGetProperty("type", out var tipoEl) ||
                tipoEl.ValueKind != JsonValueKind.String)
            {
                return [];
            }

            if (raiz.TryGetProperty("session_id", out var sessaoEl) &&
                sessaoEl.ValueKind == JsonValueKind.String)
            {
                idDaSessao = sessaoEl.GetString();
            }

            switch (tipoEl.GetString())
            {
                case "assistant":
                    return DoAssistente(raiz);

                case "user":
                    return DoUsuario(raiz);

                case "result":
                    respostaFinal = Texto(raiz, "result") ?? "";
                    falhou = raiz.TryGetProperty("is_error", out var erroEl) &&
                             erroEl.ValueKind == JsonValueKind.True;

                    // Numa falha, o 'subtype' dá a categoria ("error_during_execution") e o
                    // corpo do 'result' dá o que realmente aconteceu — inclusive a mensagem de
                    // cota esgotada. Juntar os dois é o que permite reconhecê-la depois.
                    motivo = falhou
                        ? string.Join(": ", new[] { Texto(raiz, "subtype"), respostaFinal }
                            .Where(parte => !string.IsNullOrWhiteSpace(parte)))
                        : null;

                    if (falhou && string.IsNullOrWhiteSpace(motivo))
                    {
                        motivo = "erro desconhecido";
                    }

                    consumo = LerConsumo(raiz);

                    return [];

                default:
                    return [];
            }
        }
    }

    /// <summary>
    /// O que o turno custou, do evento <c>result</c>: o bloco <c>usage</c> traz a contagem de
    /// tokens e <c>total_cost_usd</c>, o valor.
    ///
    /// <para>Os quatro contadores são lidos separadamente de propósito. Somar tudo num número só
    /// esconderia justamente o que interessa medir aqui: quanto da entrada veio do cache. Uma
    /// conversa que reaproveita o prefixo e uma que o reescreve a cada turno têm o mesmo total de
    /// entrada e custos muito diferentes.</para>
    ///
    /// <para>Um <c>result</c> sem <c>usage</c> — turno que morreu cedo, versão do CLI que não o
    /// emite — devolve <c>null</c> em vez de zero: "não sei" e "não gastou" são coisas
    /// diferentes para quem lê o relatório.</para>
    /// </summary>
    private static ConsumoDeTokens? LerConsumo(JsonElement raiz)
    {
        var custo = raiz.TryGetProperty("total_cost_usd", out var custoEl) &&
                    custoEl.ValueKind == JsonValueKind.Number
            ? custoEl.GetDecimal()
            : (decimal?)null;

        if (!raiz.TryGetProperty("usage", out var uso) || uso.ValueKind != JsonValueKind.Object)
        {
            return custo is null ? null : new ConsumoDeTokens(CustoUsd: custo, Turnos: 1);
        }

        return new ConsumoDeTokens(
            Inteiro(uso, "input_tokens"),
            Inteiro(uso, "output_tokens"),
            Inteiro(uso, "cache_creation_input_tokens"),
            Inteiro(uso, "cache_read_input_tokens"),
            custo,
            Turnos: 1);
    }

    private static long Inteiro(JsonElement objeto, string propriedade) =>
        objeto.TryGetProperty(propriedade, out var valor) &&
        valor.ValueKind == JsonValueKind.Number &&
        valor.TryGetInt64(out var numero)
            ? numero
            : 0;

    private static List<EventoDeAgente> DoAssistente(JsonElement raiz)
    {
        var eventos = new List<EventoDeAgente>();

        foreach (var bloco in BlocosDeConteudo(raiz))
        {
            switch (Texto(bloco, "type"))
            {
                case "text" when Texto(bloco, "text") is { Length: > 0 } texto:
                    eventos.Add(new TextoDoAgente(texto));
                    break;

                case "thinking":
                    eventos.Add(new AgentePensando());
                    break;

                case "tool_use":
                    eventos.Add(new UsoDeFerramenta(
                        Texto(bloco, "name") ?? "?",
                        DescreverEntrada(bloco)));
                    break;
            }
        }

        return eventos;
    }

    private static List<EventoDeAgente> DoUsuario(JsonElement raiz)
    {
        var eventos = new List<EventoDeAgente>();

        foreach (var bloco in BlocosDeConteudo(raiz))
        {
            if (Texto(bloco, "type") != "tool_result" ||
                !bloco.TryGetProperty("is_error", out var erroEl) ||
                erroEl.ValueKind != JsonValueKind.True)
            {
                continue;
            }

            eventos.Add(new FalhaDeFerramenta(ConteudoDeResultado(bloco)));
        }

        return eventos;
    }

    private static IEnumerable<JsonElement> BlocosDeConteudo(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("message", out var mensagem) ||
            mensagem.ValueKind != JsonValueKind.Object ||
            !mensagem.TryGetProperty("content", out var conteudo) ||
            conteudo.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return conteudo.EnumerateArray();
    }

    /// <summary>
    /// O conteúdo de um tool_result é string numas ferramentas e lista de blocos noutras.
    /// A interface só quer uma linha legível, então normalizamos os dois casos aqui.
    /// </summary>
    private static string ConteudoDeResultado(JsonElement bloco)
    {
        if (!bloco.TryGetProperty("content", out var conteudo))
        {
            return "erro sem detalhe";
        }

        if (conteudo.ValueKind == JsonValueKind.String)
        {
            return Limpar(conteudo.GetString() ?? "");
        }

        if (conteudo.ValueKind == JsonValueKind.Array)
        {
            var textos = conteudo.EnumerateArray()
                .Select(item => Texto(item, "text"))
                .OfType<string>();

            return Limpar(string.Join(" ", textos));
        }

        return "erro sem detalhe";
    }

    private static string Limpar(string texto) => texto
        .Replace("<tool_use_error>", "")
        .Replace("</tool_use_error>", "")
        .ReplaceLineEndings(" ")
        .Trim();

    /// <summary>
    /// Achata os argumentos de uma chamada de ferramenta em "campo=valor, campo=valor",
    /// truncando valores longos — o conteúdo inteiro de um arquivo Markdown não cabe (nem faz
    /// sentido) numa linha de progresso no console.
    /// </summary>
    private static string DescreverEntrada(JsonElement bloco)
    {
        if (!bloco.TryGetProperty("input", out var entrada) ||
            entrada.ValueKind != JsonValueKind.Object)
        {
            return "";
        }

        return string.Join(", ", entrada.EnumerateObject()
            .Select(campo => $"{campo.Name}={Resumir(campo.Value)}"));
    }

    private static string Resumir(JsonElement valor)
    {
        const int limite = 60;

        var texto = (valor.ValueKind == JsonValueKind.String ? valor.GetString() ?? "" : valor.GetRawText())
            .ReplaceLineEndings(" ")
            .Trim();

        return texto.Length <= limite ? texto : string.Concat(texto.AsSpan(0, limite), "...");
    }

    private static string? Texto(JsonElement elemento, string propriedade) =>
        elemento.ValueKind == JsonValueKind.Object &&
        elemento.TryGetProperty(propriedade, out var valor) &&
        valor.ValueKind == JsonValueKind.String
            ? valor.GetString()
            : null;

    /// <summary>
    /// Mata a árvore de processos no cancelamento: o Claude Code lança subprocessos (o
    /// servidor MCP, entre outros) que sobreviveriam ao pai.
    /// </summary>
    private static void Encerrar(Process processo)
    {
        try
        {
            if (!processo.HasExited)
            {
                processo.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Já saiu entre a checagem e o Kill.
        }
        catch (SystemException)
        {
            // Sem permissão ou processo já colhido — nada a fazer.
        }
    }
}
