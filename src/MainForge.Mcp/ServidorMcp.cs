using System.Text.Json;
using System.Text.Json.Nodes;

namespace MainForge.Mcp;

/// <summary>
/// Servidor MCP sobre stdio: JSON-RPC 2.0, uma mensagem por linha. É deliberadamente escrito
/// à mão em vez de usar um SDK — são três ferramentas e um punhado de métodos do protocolo, e
/// assim o projeto não ganha dependência nova.
///
/// Regra de ouro do transporte stdio: <b>nada além de JSON-RPC pode sair na saída padrão</b>.
/// Qualquer diagnóstico vai para a saída de erro.
/// </summary>
public sealed class ServidorMcp(CatalogoDeFerramentas catalogo)
{
    private const string VersaoDoProtocoloPadrao = "2024-11-05";

    public async Task RodarAsync(TextReader entrada, TextWriter saida, CancellationToken cancelamento = default)
    {
        while (!cancelamento.IsCancellationRequested &&
               await entrada.ReadLineAsync(cancelamento) is { } linha)
        {
            if (string.IsNullOrWhiteSpace(linha))
            {
                continue;
            }

            var resposta = await ResponderAsync(linha, cancelamento);

            if (resposta is null)
            {
                continue; // notificação: o protocolo proíbe responder
            }

            await saida.WriteLineAsync(resposta.ToJsonString());
            await saida.FlushAsync(cancelamento);
        }
    }

    private async Task<JsonNode?> ResponderAsync(string linha, CancellationToken cancelamento)
    {
        JsonObject pedido;

        try
        {
            pedido = JsonNode.Parse(linha) as JsonObject
                ?? throw new JsonException("mensagem não é um objeto");
        }
        catch (JsonException excecao)
        {
            return Erro(null, -32700, $"JSON inválido: {excecao.Message}");
        }

        var id = pedido["id"];
        var metodo = pedido["method"]?.GetValue<string>();

        // Sem id é notificação — processa (quando faz sentido) e não responde.
        if (id is null)
        {
            return null;
        }

        return metodo switch
        {
            "initialize" => Inicializar(id, pedido),
            "ping" => Sucesso(id, new JsonObject()),
            "tools/list" => Sucesso(id, new JsonObject { ["tools"] = catalogo.Descrever() }),
            "tools/call" => await ChamarFerramentaAsync(id, pedido, cancelamento),
            null => Erro(id, -32600, "Pedido sem 'method'."),
            _ => Erro(id, -32601, $"Método não suportado: '{metodo}'."),
        };
    }

    private static JsonNode Inicializar(JsonNode id, JsonObject pedido)
    {
        // Ecoa a versão que o cliente pediu; se ele não pediu nenhuma, usa a nossa.
        var versao = pedido["params"]?["protocolVersion"]?.GetValue<string>() ?? VersaoDoProtocoloPadrao;

        return Sucesso(id, new JsonObject
        {
            ["protocolVersion"] = versao,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "mainforge",
                ["version"] = "1.0.0",
            },
        });
    }

    private async Task<JsonNode> ChamarFerramentaAsync(JsonNode id, JsonObject pedido, CancellationToken cancelamento)
    {
        var parametros = pedido["params"] as JsonObject;
        var nome = parametros?["name"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(nome))
        {
            return Erro(id, -32602, "tools/call sem 'name'.");
        }

        // O cliente pode omitir 'arguments' quando a ferramenta não tem parâmetros.
        var argumentos = parametros?["arguments"] as JsonObject ?? [];

        var resultado = await catalogo.ExecutarAsync(nome, argumentos, cancelamento);

        // Falha de ferramenta volta como resultado com isError, não como erro de protocolo:
        // assim o modelo lê a mensagem e corrige a chamada, em vez de o cliente abortar.
        return Sucesso(id, new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = resultado.Texto,
            }),
            ["isError"] = resultado.Erro,
        });
    }

    private static JsonNode Sucesso(JsonNode id, JsonObject resultado) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id.DeepClone(),
        ["result"] = resultado,
    };

    private static JsonNode Erro(JsonNode? id, int codigo, string mensagem) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject
        {
            ["code"] = codigo,
            ["message"] = mensagem,
        },
    };
}
