using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// Testa as operações que sobraram em C# depois da migração para o Claude Code: escrever a
/// base de conhecimento e manipular o AcroForm da ficha. As antigas ferramentas de leitura e
/// listagem sumiram porque agora são o <c>Read</c> e o <c>Glob</c> embutidos do Claude Code —
/// não há o que testar aqui sobre elas.
/// </summary>
public class FerramentasTestes : IDisposable
{
    private static readonly string CaminhoFichaFixture =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "FichaTeste.pdf");

    private readonly string _raiz;
    private readonly CaminhosDoProjeto _caminhos;

    public FerramentasTestes()
    {
        _raiz = Path.Combine(Path.GetTempPath(), "mainforge-tests-" + Guid.NewGuid());
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

    private string PrepararTemplate(string nomeArquivo = "Ficha.pdf")
    {
        var diretorioModelo = Path.Combine(_caminhos.Modelos, "Aventura&Cia");
        Directory.CreateDirectory(diretorioModelo);
        var destino = Path.Combine(diretorioModelo, nomeArquivo);
        File.Copy(CaminhoFichaFixture, destino);
        return destino;
    }

    [Fact]
    public async Task EscreverConhecimento_GravaOArquivoEDevolveCaminhoRelativo()
    {
        var gravado = await EscritorDeConhecimento.EscreverAsync(
            _caminhos, "Aventura&Cia", "Classes/Guerreiro.md", "# Guerreiro");

        var esperado = Path.Combine(_caminhos.Conhecimento, "Aventura&Cia", "Classes", "Guerreiro.md");
        Assert.True(File.Exists(esperado));
        Assert.Equal("# Guerreiro", await File.ReadAllTextAsync(esperado));
        Assert.Equal(Path.GetRelativePath(_raiz, esperado), gravado);
    }

    [Fact]
    public async Task EscreverConhecimento_RejeitaCaminhoQueNaoTerminaEmMd()
    {
        await Assert.ThrowsAsync<ErroDeFerramenta>(() => EscritorDeConhecimento.EscreverAsync(
            _caminhos, "Aventura&Cia", "Classes/Guerreiro.txt", "x"));
    }

    /// <summary>
    /// O confinamento de diretório é a camada do guardrail que não depende de configuração
    /// externa nenhuma — é o que garante que "o Configurador só escreve em Knowledge/" valha
    /// mesmo que uma regra de permissão do Claude Code seja afrouxada por engano.
    /// </summary>
    [Theory]
    [InlineData("../fora", "Arquivo.md")]
    [InlineData("Aventura&Cia", "../../fora.md")]
    [InlineData("Aventura&Cia", "../../../Windows/System32/fora.md")]
    public async Task EscreverConhecimento_RejeitaEscapeDoDiretorioPermitido(string sistema, string caminho)
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => EscritorDeConhecimento.EscreverAsync(
            _caminhos, sistema, caminho, "conteudo"));
    }

    [Fact]
    public void ListarCamposDaFicha_DevolveOsNomesDoAcroForm()
    {
        PrepararTemplate();

        var campos = PreenchedorDeFicha.ListarCampos(_caminhos, "Aventura&Cia", arquivoModelo: null);

        Assert.Contains("Nome", campos);
    }

    [Fact]
    public void ListarCamposDaFicha_SistemaSemTemplate_LancaErroDeFerramenta()
    {
        Assert.Throws<ErroDeFerramenta>(() =>
            PreenchedorDeFicha.ListarCampos(_caminhos, "SistemaInexistente", arquivoModelo: null));
    }

    [Fact]
    public void PreencherFicha_PreencheCampoDeTextoESalvaEmOutput()
    {
        PrepararTemplate();

        var gerado = PreenchedorDeFicha.Preencher(
            _caminhos, "Aventura&Cia", null,
            new Dictionary<string, string> { ["Nome"] = "Thoradin" },
            "Thoradin.pdf");

        var caminhoSaida = Path.Combine(_caminhos.SaidaPersonagens, "Thoradin.pdf");
        Assert.True(File.Exists(caminhoSaida));
        Assert.Equal(Path.GetRelativePath(_raiz, caminhoSaida), gerado);

        using var documento = PdfSharp.Pdf.IO.PdfReader.Open(caminhoSaida, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        var campoNome = (PdfSharp.Pdf.AcroForms.PdfTextField)documento.AcroForm!.Fields["Nome"]!;
        Assert.Equal("Thoradin", campoNome.Text);
    }

    /// <summary>
    /// A mensagem precisa listar os campos disponíveis: é lendo isso que o agente corrige a
    /// própria chamada sem uma nova rodada de perguntas ao usuário.
    /// </summary>
    [Fact]
    public void PreencherFicha_CampoInexistente_ErroListaOsCamposDisponiveis()
    {
        PrepararTemplate();

        var excecao = Assert.Throws<ErroDeFerramenta>(() => PreenchedorDeFicha.Preencher(
            _caminhos, "Aventura&Cia", null,
            new Dictionary<string, string> { ["CampoQueNaoExiste"] = "x" },
            "Saida.pdf"));

        Assert.Contains("CampoQueNaoExiste", excecao.Message);
        Assert.Contains("Nome", excecao.Message);
    }

    [Fact]
    public void PreencherFicha_MaisDeUmTemplate_ExigeArquivoModelo()
    {
        PrepararTemplate("FichaA.pdf");
        PrepararTemplate("FichaB.pdf");

        var excecao = Assert.Throws<ErroDeFerramenta>(() => PreenchedorDeFicha.Preencher(
            _caminhos, "Aventura&Cia", null,
            new Dictionary<string, string> { ["Nome"] = "Thoradin" },
            "Saida.pdf"));

        Assert.Contains("arquivoModelo", excecao.Message);
    }

    [Fact]
    public void PreencherFicha_RejeitaNomeDeSaidaSemExtensaoPdf()
    {
        PrepararTemplate();

        Assert.Throws<ErroDeFerramenta>(() => PreenchedorDeFicha.Preencher(
            _caminhos, "Aventura&Cia", null,
            new Dictionary<string, string> { ["Nome"] = "Thoradin" },
            "Thoradin"));
    }

    [Fact]
    public void PreencherFicha_RejeitaSaidaForaDeOutputPersonagens()
    {
        PrepararTemplate();

        Assert.Throws<UnauthorizedAccessException>(() => PreenchedorDeFicha.Preencher(
            _caminhos, "Aventura&Cia", null,
            new Dictionary<string, string> { ["Nome"] = "Thoradin" },
            "../../fora.pdf"));
    }
}
