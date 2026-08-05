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
        var (executavel, argumentos) = LocalizarServidor();

        var configuracao = new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                [DefinicaoDeAgente.NomeDoServidorMcp] = new JsonObject
                {
                    ["command"] = executavel,
                    ["args"] = new JsonArray([.. argumentos.Select(argumento => (JsonNode)argumento), caminhos.Raiz]),
                },
            },
        };

        var caminho = Path.Combine(Path.GetTempPath(), $"mainforge-mcp-{Guid.NewGuid():N}.json");
        File.WriteAllText(caminho, configuracao.ToJsonString());

        return new ConfiguracaoDoServidorMcp(caminho);
    }

    /// <summary>
    /// Como lançar o servidor MCP, em duas formas — e a ordem importa.
    ///
    /// <list type="number">
    ///   <item>O executável irmão <c>MainForge.Mcp</c>, quando existe. É o que a compilação de
    ///   desenvolvimento produz, e o que o harness de validação usa.</item>
    ///   <item>O <b>próprio processo</b>, com <c>--mcp</c>. É o caminho do aplicativo publicado
    ///   como binário único: ali não há executável irmão, e é justamente por não haver que o
    ///   download é um arquivo só.</item>
    /// </list>
    ///
    /// <para>Rodando por <c>dotnet MainForge.Cli.dll</c>, o processo é o <c>dotnet</c> — aí o que
    /// vai na linha de comando é a DLL antes do <c>--mcp</c>, senão o servidor nunca subiria.</para>
    /// </summary>
    private static (string Executavel, IReadOnlyList<string> Argumentos) LocalizarServidor()
    {
        var nome = OperatingSystem.IsWindows() ? "MainForge.Mcp.exe" : "MainForge.Mcp";
        var irmao = Path.Combine(AppContext.BaseDirectory, nome);

        if (File.Exists(irmao))
        {
            return (irmao, []);
        }

        var processo = Environment.ProcessPath
            ?? throw new FileNotFoundException(
                $"Servidor MCP '{nome}' não encontrado em '{AppContext.BaseDirectory}' e não foi " +
                "possível descobrir o executável em execução. Rode 'dotnet build MainForge.sln'.",
                irmao);

        var nomeDoProcesso = Path.GetFileNameWithoutExtension(processo);

        if (nomeDoProcesso.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var dll = Path.Combine(AppContext.BaseDirectory, "MainForge.Cli.dll");

            if (File.Exists(dll))
            {
                return (processo, [dll, "--mcp"]);
            }
        }

        return (processo, ["--mcp"]);
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
