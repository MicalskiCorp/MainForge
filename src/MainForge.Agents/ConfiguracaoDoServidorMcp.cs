using System.Text.Json.Nodes;
using MainForge.Core;

namespace MainForge.Agents;

/// <summary>
/// Gera o arquivo <c>--mcp-config</c> que manda o Claude Code lançar o servidor MCP do
/// MainForge, e o apaga quando a sessão acaba. O arquivo é temporário de propósito: ele
/// carrega o caminho absoluto da raiz do projeto em uso, que muda de execução para execução.
/// </summary>
public sealed class ConfiguracaoDoServidorMcp : IDisposable
{
    private ConfiguracaoDoServidorMcp(string caminho) => Caminho = caminho;

    /// <summary>Caminho do JSON a passar em <c>--mcp-config</c>.</summary>
    public string Caminho { get; }

    public static ConfiguracaoDoServidorMcp Criar(CaminhosDoProjeto caminhos)
    {
        var executavel = LocalizarServidor();

        var configuracao = new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                [DefinicaoDeAgente.NomeDoServidorMcp] = new JsonObject
                {
                    ["command"] = executavel,
                    ["args"] = new JsonArray(caminhos.Raiz),
                },
            },
        };

        var caminho = Path.Combine(Path.GetTempPath(), $"mainforge-mcp-{Guid.NewGuid():N}.json");
        File.WriteAllText(caminho, configuracao.ToJsonString());

        return new ConfiguracaoDoServidorMcp(caminho);
    }

    /// <summary>
    /// O servidor MCP é um executável irmão: MainForge.Mcp é referenciado pelo projeto da
    /// interface só para o binário dele parar na mesma pasta de saída.
    /// </summary>
    private static string LocalizarServidor()
    {
        var nome = OperatingSystem.IsWindows() ? "MainForge.Mcp.exe" : "MainForge.Mcp";
        var caminho = Path.Combine(AppContext.BaseDirectory, nome);

        return File.Exists(caminho)
            ? caminho
            : throw new FileNotFoundException(
                $"Servidor MCP '{nome}' não encontrado em '{AppContext.BaseDirectory}'. " +
                "Rode 'dotnet build MainForge.sln' — ele precisa estar na mesma pasta do aplicativo.",
                caminho);
    }

    public void Dispose()
    {
        try
        {
            File.Delete(Caminho);
        }
        catch (IOException)
        {
            // Arquivo temporário órfão não justifica derrubar o aplicativo.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
