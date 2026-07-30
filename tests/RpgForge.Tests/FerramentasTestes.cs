using System.Text.Json;
using Anthropic.Helpers.Beta;
using Anthropic.Models.Beta.Messages;
using RpgForge.Agents;
using RpgForge.Core;
using RpgForge.Tools;

namespace RpgForge.Tests;

public class FerramentasTestes : IDisposable
{
    private readonly string _raiz;
    private readonly CaminhosDoProjeto _caminhos;

    public FerramentasTestes()
    {
        _raiz = Path.Combine(Path.GetTempPath(), "rpgforge-tests-" + Guid.NewGuid());
        _caminhos = new CaminhosDoProjeto(_raiz);
    }

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    private static BetaToolUseBlock ChamadaComEntrada(object entrada)
    {
        var elemento = JsonSerializer.SerializeToElement(entrada);
        var dicionario = elemento.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

        return new BetaToolUseBlock
        {
            ID = "chamada-teste",
            Name = "ferramenta-teste",
            Input = dicionario,
        };
    }

    [Fact]
    public async Task ListarSistemas_RetornaSubpastasDeSystemsEmOrdem()
    {
        Directory.CreateDirectory(Path.Combine(_caminhos.Sistemas, "Tormenta20"));
        Directory.CreateDirectory(Path.Combine(_caminhos.Sistemas, "Aventura&Cia"));

        var ferramenta = new FerramentaListarSistemas(_caminhos);
        var resultado = await ferramenta.ExecuteAsync(ChamadaComEntrada(new { }), CancellationToken.None);

        Assert.True(resultado.TryPickString(out var json));
        var sistemas = JsonSerializer.Deserialize<List<string>>(json!)!;
        Assert.Equal(["Aventura&Cia", "Tormenta20"], sistemas);
    }

    [Fact]
    public async Task EscreverELerArquivoConhecimento_RoundTrip()
    {
        var escrever = new FerramentaEscreverArquivoConhecimento(_caminhos);
        var ler = new FerramentaLerArquivoConhecimento(_caminhos);

        await escrever.ExecuteAsync(
            ChamadaComEntrada(new { sistema = "Aventura&Cia", caminho = "Classes/Guerreiro.md", conteudo = "# Guerreiro" }),
            CancellationToken.None);

        var resultado = await ler.ExecuteAsync(
            ChamadaComEntrada(new { sistema = "Aventura&Cia", caminho = "Classes/Guerreiro.md" }),
            CancellationToken.None);

        Assert.True(resultado.TryPickString(out var conteudo));
        Assert.Equal("# Guerreiro", conteudo);
    }

    [Fact]
    public async Task LerArquivoConhecimento_ArquivoInexistente_LancaBetaToolError()
    {
        var ler = new FerramentaLerArquivoConhecimento(_caminhos);

        await Assert.ThrowsAsync<BetaToolError>(() => ler.ExecuteAsync(
            ChamadaComEntrada(new { sistema = "Aventura&Cia", caminho = "Nada.md" }),
            CancellationToken.None));
    }

    [Fact]
    public async Task EscreverArquivoConhecimento_RejeitaCaminhoQueNaoTerminaEmMd()
    {
        var escrever = new FerramentaEscreverArquivoConhecimento(_caminhos);

        await Assert.ThrowsAsync<BetaToolError>(() => escrever.ExecuteAsync(
            ChamadaComEntrada(new { sistema = "Aventura&Cia", caminho = "Classes/Guerreiro.txt", conteudo = "x" }),
            CancellationToken.None));
    }

    [Fact]
    public async Task ListarConhecimento_RejeitaPathTraversalNoSistema()
    {
        var ferramenta = new FerramentaListarConhecimento(_caminhos);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ferramenta.ExecuteAsync(
            ChamadaComEntrada(new { sistema = "../fora" }),
            CancellationToken.None));
    }

    [Fact]
    public async Task LerPdfDoSistema_DevolveBlocoDeDocumento()
    {
        var diretorioSistema = Path.Combine(_caminhos.Sistemas, "Aventura&Cia");
        Directory.CreateDirectory(diretorioSistema);
        var caminhoPdf = Path.Combine(diretorioSistema, "Livro.pdf");
        await File.WriteAllBytesAsync(caminhoPdf, "%PDF-1.4 conteudo falso"u8.ToArray());

        var ferramenta = new FerramentaLerPdfDoSistema(_caminhos);
        var resultado = await ferramenta.ExecuteAsync(
            ChamadaComEntrada(new { sistema = "Aventura&Cia", arquivo = "Livro.pdf" }),
            CancellationToken.None);

        Assert.True(resultado.TryPickBlocks(out var blocos));
        var bloco = Assert.Single(blocos!);
        Assert.True(bloco.TryPickBetaRequestDocument(out _));
    }

    [Fact]
    public async Task LerPdfDoSistema_ArquivoNaoPdf_LancaBetaToolError()
    {
        var diretorioSistema = Path.Combine(_caminhos.Sistemas, "Aventura&Cia");
        Directory.CreateDirectory(diretorioSistema);
        await File.WriteAllTextAsync(Path.Combine(diretorioSistema, "nota.txt"), "oi");

        var ferramenta = new FerramentaLerPdfDoSistema(_caminhos);

        await Assert.ThrowsAsync<BetaToolError>(() => ferramenta.ExecuteAsync(
            ChamadaComEntrada(new { sistema = "Aventura&Cia", arquivo = "nota.txt" }),
            CancellationToken.None));
    }

    [Fact]
    public void ResolverFerramentas_ConfiguradorResolveTodasAsSuasFerramentas()
    {
        var ferramentas = DefinicaoDeAgente.Configurador.ResolverFerramentas(_caminhos);

        var nomes = ferramentas.Select(f => f.Name).ToList();
        Assert.Equal(DefinicaoDeAgente.Configurador.FerramentasPermitidas.Count, nomes.Count);
        Assert.Equal(DefinicaoDeAgente.Configurador.FerramentasPermitidas.OrderBy(n => n), nomes.OrderBy(n => n));
    }

    [Fact]
    public void ResolverFerramentas_DungeonMasterResolveTodasAsSuasFerramentas()
    {
        var ferramentas = DefinicaoDeAgente.DungeonMaster.ResolverFerramentas(_caminhos);

        var nomes = ferramentas.Select(f => f.Name).ToList();
        Assert.Equal(DefinicaoDeAgente.DungeonMaster.FerramentasPermitidas.Count, nomes.Count);
        Assert.Equal(DefinicaoDeAgente.DungeonMaster.FerramentasPermitidas.OrderBy(n => n), nomes.OrderBy(n => n));
    }

    private static readonly string CaminhoFichaFixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "FichaTeste.pdf");

    [Fact]
    public async Task PreencherFichaPersonagem_PreencheCampoDeTextoESalvaEmOutput()
    {
        var diretorioModelo = Path.Combine(_caminhos.Modelos, "Aventura&Cia");
        Directory.CreateDirectory(diretorioModelo);
        File.Copy(CaminhoFichaFixture, Path.Combine(diretorioModelo, "Ficha.pdf"));

        var ferramenta = new FerramentaPreencherFichaPersonagem(_caminhos);
        await ferramenta.ExecuteAsync(
            ChamadaComEntrada(new { sistema = "Aventura&Cia", campos = new { Nome = "Thoradin" }, nomeArquivoSaida = "Thoradin.pdf" }),
            CancellationToken.None);

        var caminhoSaida = Path.Combine(_caminhos.SaidaPersonagens, "Thoradin.pdf");
        Assert.True(File.Exists(caminhoSaida));

        using var documentoGerado = PdfSharp.Pdf.IO.PdfReader.Open(caminhoSaida, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        var campoNome = (PdfSharp.Pdf.AcroForms.PdfTextField)documentoGerado.AcroForm!.Fields["Nome"]!;
        Assert.Equal("Thoradin", campoNome.Text);
    }

    [Fact]
    public async Task PreencherFichaPersonagem_CampoInexistente_LancaBetaToolErrorComListaDeCamposDisponiveis()
    {
        var diretorioModelo = Path.Combine(_caminhos.Modelos, "Aventura&Cia");
        Directory.CreateDirectory(diretorioModelo);
        File.Copy(CaminhoFichaFixture, Path.Combine(diretorioModelo, "Ficha.pdf"));

        var ferramenta = new FerramentaPreencherFichaPersonagem(_caminhos);

        var excecao = await Assert.ThrowsAsync<BetaToolError>(() => ferramenta.ExecuteAsync(
            ChamadaComEntrada(new { sistema = "Aventura&Cia", campos = new { CampoQueNaoExiste = "x" }, nomeArquivoSaida = "Saida.pdf" }),
            CancellationToken.None));

        Assert.True(excecao.Content.TryPickString(out var mensagem));
        Assert.Contains("CampoQueNaoExiste", mensagem);
        Assert.Contains("Nome", mensagem);
    }

    [Fact]
    public async Task PreencherFichaPersonagem_MaisDeUmTemplate_ExigeArquivoModelo()
    {
        var diretorioModelo = Path.Combine(_caminhos.Modelos, "Aventura&Cia");
        Directory.CreateDirectory(diretorioModelo);
        File.Copy(CaminhoFichaFixture, Path.Combine(diretorioModelo, "FichaA.pdf"));
        File.Copy(CaminhoFichaFixture, Path.Combine(diretorioModelo, "FichaB.pdf"));

        var ferramenta = new FerramentaPreencherFichaPersonagem(_caminhos);

        await Assert.ThrowsAsync<BetaToolError>(() => ferramenta.ExecuteAsync(
            ChamadaComEntrada(new { sistema = "Aventura&Cia", campos = new { Nome = "Thoradin" }, nomeArquivoSaida = "Saida.pdf" }),
            CancellationToken.None));
    }

    [Fact]
    public async Task PreencherFichaPersonagem_RejeitaNomeDeSaidaSemExtensaoPdf()
    {
        var diretorioModelo = Path.Combine(_caminhos.Modelos, "Aventura&Cia");
        Directory.CreateDirectory(diretorioModelo);
        File.Copy(CaminhoFichaFixture, Path.Combine(diretorioModelo, "Ficha.pdf"));

        var ferramenta = new FerramentaPreencherFichaPersonagem(_caminhos);

        await Assert.ThrowsAsync<BetaToolError>(() => ferramenta.ExecuteAsync(
            ChamadaComEntrada(new { sistema = "Aventura&Cia", campos = new { Nome = "Thoradin" }, nomeArquivoSaida = "Thoradin" }),
            CancellationToken.None));
    }
}
