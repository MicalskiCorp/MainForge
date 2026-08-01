using System.Text.Json.Nodes;
using MainForge.Core;
using MainForge.Mcp;

namespace MainForge.Tests;

/// <summary>
/// Exercita o servidor MCP pelo protocolo de verdade — JSON-RPC linha a linha sobre
/// TextReader/TextWriter, do mesmo jeito que o Claude Code o usa. Como o servidor foi escrito
/// à mão em vez de vir de um SDK, é aqui que erros de protocolo apareceriam: id ecoado
/// errado, notificação respondida (o que trava o cliente), falha de ferramenta virando erro
/// de protocolo em vez de <c>isError</c>.
/// </summary>
public class ServidorMcpTestes : IDisposable
{
    private static readonly string CaminhoFichaFixture =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "FichaTeste.pdf");

    private readonly string _raiz;
    private readonly CaminhosDoProjeto _caminhos;

    public ServidorMcpTestes()
    {
        _raiz = Path.Combine(Path.GetTempPath(), "mainforge-mcp-tests-" + Guid.NewGuid());
        _caminhos = new CaminhosDoProjeto(_raiz);
    }

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Manda as linhas pelo servidor e devolve as respostas, já como JSON.</summary>
    private async Task<List<JsonObject>> ConversarAsync(params string[] pedidos)
    {
        var servidor = new ServidorMcp(new CatalogoDeFerramentas(_caminhos));

        using var entrada = new StringReader(string.Join("\n", pedidos));
        await using var saida = new StringWriter();

        await servidor.RodarAsync(entrada, saida);

        return saida.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(linha => (JsonObject)JsonNode.Parse(linha.Trim())!)
            .ToList();
    }

    private static string Chamada(int id, string ferramenta, object argumentos) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["method"] = "tools/call",
        ["params"] = new JsonObject
        {
            ["name"] = ferramenta,
            ["arguments"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(argumentos)),
        },
    }.ToJsonString();

    private static string TextoDoResultado(JsonObject resposta) =>
        resposta["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool EhErro(JsonObject resposta) =>
        resposta["result"]!["isError"]!.GetValue<bool>();

    private void PrepararTemplate(string nomeArquivo = "Ficha.pdf")
    {
        var diretorioModelo = Path.Combine(_caminhos.Modelos, "Aventura&Cia");
        Directory.CreateDirectory(diretorioModelo);
        File.Copy(CaminhoFichaFixture, Path.Combine(diretorioModelo, nomeArquivo));
    }

    [Fact]
    public async Task Initialize_EcoaAVersaoPedidaPeloClienteEAnunciaFerramentas()
    {
        var respostas = await ConversarAsync(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}""");

        var resultado = Assert.Single(respostas)["result"]!;
        Assert.Equal("2025-06-18", resultado["protocolVersion"]!.GetValue<string>());
        Assert.NotNull(resultado["capabilities"]!["tools"]);
        Assert.Equal("mainforge", resultado["serverInfo"]!["name"]!.GetValue<string>());
    }

    /// <summary>
    /// Responder a uma notificação é violação do JSON-RPC e trava clientes que casam
    /// resposta com id.
    /// </summary>
    [Fact]
    public async Task Notificacao_NaoRecebeResposta()
    {
        var respostas = await ConversarAsync(
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            """{"jsonrpc":"2.0","id":7,"method":"ping"}""");

        var resposta = Assert.Single(respostas);
        Assert.Equal(7, resposta["id"]!.GetValue<int>());
    }

    [Fact]
    public async Task ToolsList_AnunciaExatamenteAsTresFerramentasComEsquema()
    {
        var respostas = await ConversarAsync("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");

        var ferramentas = (JsonArray)Assert.Single(respostas)["result"]!["tools"]!;

        var nomes = ferramentas.Select(f => f!["name"]!.GetValue<string>()).OrderBy(n => n).ToList();

        Assert.Equal(
            ["escrever_arquivo_conhecimento", "listar_campos_da_ficha", "preencher_ficha_personagem"],
            nomes);

        // Sem inputSchema válido o Claude Code descarta a ferramenta silenciosamente.
        Assert.All(ferramentas, f => Assert.Equal("object", f!["inputSchema"]!["type"]!.GetValue<string>()));
    }

    [Fact]
    public async Task EscreverArquivoConhecimento_GravaOArquivo()
    {
        var respostas = await ConversarAsync(Chamada(1, "escrever_arquivo_conhecimento", new
        {
            sistema = "Aventura&Cia",
            caminho = "Classes/Guerreiro.md",
            conteudo = "# Guerreiro",
        }));

        var resposta = Assert.Single(respostas);
        Assert.False(EhErro(resposta));
        Assert.Contains("Guerreiro.md", TextoDoResultado(resposta));

        var esperado = Path.Combine(_caminhos.Conhecimento, "Aventura&Cia", "Classes", "Guerreiro.md");
        Assert.Equal("# Guerreiro", await File.ReadAllTextAsync(esperado));
    }

    /// <summary>
    /// Uma tentativa de escapar do diretório permitido precisa voltar como resultado com
    /// <c>isError</c> — e não como erro de protocolo, que abortaria a sessão inteira em vez de
    /// deixar o agente se corrigir.
    /// </summary>
    [Fact]
    public async Task EscreverArquivoConhecimento_PathTraversal_VoltaComoIsErrorENaoGravaNada()
    {
        var respostas = await ConversarAsync(Chamada(1, "escrever_arquivo_conhecimento", new
        {
            sistema = "Aventura&Cia",
            caminho = "../../invadido.md",
            conteudo = "nao deveria existir",
        }));

        var resposta = Assert.Single(respostas);
        Assert.Null(resposta["error"]);
        Assert.True(EhErro(resposta));
        Assert.False(File.Exists(Path.Combine(_raiz, "invadido.md")));
    }

    [Fact]
    public async Task ChamadaSemCampoObrigatorio_VoltaComoIsErrorExplicando()
    {
        var respostas = await ConversarAsync(Chamada(1, "escrever_arquivo_conhecimento", new
        {
            sistema = "Aventura&Cia",
        }));

        var resposta = Assert.Single(respostas);
        Assert.True(EhErro(resposta));
        Assert.Contains("caminho", TextoDoResultado(resposta));
    }

    [Fact]
    public async Task ListarCamposDaFicha_DevolveOsNomesDoAcroForm()
    {
        PrepararTemplate();

        var respostas = await ConversarAsync(Chamada(1, "listar_campos_da_ficha", new { sistema = "Aventura&Cia" }));

        var resposta = Assert.Single(respostas);
        Assert.False(EhErro(resposta));
        Assert.Contains("Nome", TextoDoResultado(resposta));
    }

    [Fact]
    public async Task PreencherFichaPersonagem_GeraOPdfEmOutputPersonagens()
    {
        PrepararTemplate();

        var respostas = await ConversarAsync(Chamada(1, "preencher_ficha_personagem", new
        {
            sistema = "Aventura&Cia",
            campos = new { Nome = "Thoradin" },
            nomeArquivoSaida = "Thoradin.pdf",
        }));

        var resposta = Assert.Single(respostas);
        Assert.False(EhErro(resposta));
        Assert.True(File.Exists(Path.Combine(_caminhos.SaidaPersonagens, "Thoradin.pdf")));
    }

    [Fact]
    public async Task FerramentaDesconhecida_VoltaComoIsError()
    {
        var respostas = await ConversarAsync(Chamada(1, "ferramenta_que_nao_existe", new { }));

        Assert.True(EhErro(Assert.Single(respostas)));
    }

    [Fact]
    public async Task MetodoDesconhecido_VoltaComoErroDeProtocolo()
    {
        var respostas = await ConversarAsync("""{"jsonrpc":"2.0","id":1,"method":"resources/list"}""");

        var resposta = Assert.Single(respostas);
        Assert.Equal(-32601, resposta["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task JsonInvalido_NaoDerrubaOServidor()
    {
        var respostas = await ConversarAsync(
            "isto nao e json",
            """{"jsonrpc":"2.0","id":2,"method":"ping"}""");

        Assert.Equal(2, respostas.Count);
        Assert.Equal(-32700, respostas[0]["error"]!["code"]!.GetValue<int>());
        Assert.Equal(2, respostas[1]["id"]!.GetValue<int>());
    }
}
