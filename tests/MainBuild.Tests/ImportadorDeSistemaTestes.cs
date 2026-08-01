using MainBuild.Core;
using MainBuild.Tools;

namespace MainBuild.Tests;

public sealed class ImportadorDeSistemaTestes : IDisposable
{
    private static readonly string CaminhoFichaFixture =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "FichaTeste.pdf");

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "mainbuild-import-" + Guid.NewGuid());
    private readonly string _origem = Path.Combine(Path.GetTempPath(), "mainbuild-origem-" + Guid.NewGuid());
    private readonly CaminhosDoProjeto _caminhos;

    public ImportadorDeSistemaTestes()
    {
        _caminhos = new CaminhosDoProjeto(_raiz);
        Directory.CreateDirectory(_origem);
    }

    public void Dispose()
    {
        foreach (var diretorio in new[] { _raiz, _origem })
        {
            if (Directory.Exists(diretorio))
            {
                Directory.Delete(diretorio, recursive: true);
            }
        }
    }

    /// <summary>
    /// Copia o fixture de ficha AcroForm para fora do projeto, simulando o arquivo que o
    /// usuário escolheria no disco dele.
    /// </summary>
    private string CriarArquivoDeOrigem(string nome)
    {
        var destino = Path.Combine(_origem, nome);
        File.Copy(CaminhoFichaFixture, destino);
        return destino;
    }

    [Fact]
    public void Importar_CopiaLivrosEFichaEDevolveOsCampos()
    {
        var livro = CriarArquivoDeOrigem("Livro Basico.pdf");
        var ficha = CriarArquivoDeOrigem("Ficha.pdf");

        var resultado = ImportadorDeSistema.Importar(_caminhos, "Aventura&Cia", [livro], ficha);

        Assert.Equal("Aventura&Cia", resultado.Sistema.Id);
        Assert.Equal(["Livro Basico.pdf"], resultado.Livros);
        Assert.Equal("Ficha.pdf", resultado.Ficha);
        Assert.Contains("Nome", resultado.CamposDaFicha);

        Assert.True(File.Exists(Path.Combine(_caminhos.Sistemas, "Aventura&Cia", "Livro Basico.pdf")));
        Assert.True(File.Exists(Path.Combine(_caminhos.Modelos, "Aventura&Cia", "Ficha.pdf")));
    }

    [Fact]
    public void Importar_VariosLivros_CopiaTodos()
    {
        var primeiro = CriarArquivoDeOrigem("Livro1.pdf");
        var segundo = CriarArquivoDeOrigem("Livro2.pdf");
        var ficha = CriarArquivoDeOrigem("Ficha.pdf");

        var resultado = ImportadorDeSistema.Importar(_caminhos, "Tormenta20", [primeiro, segundo], ficha);

        Assert.Equal(2, resultado.Livros.Count);
        Assert.True(File.Exists(Path.Combine(_caminhos.Sistemas, "Tormenta20", "Livro2.pdf")));
    }

    [Fact]
    public void Importar_FichaSemAcroForm_Rejeita()
    {
        var livro = CriarArquivoDeOrigem("Livro.pdf");

        // Um PDF de verdade, válido, mas sem campos de formulário: é o caso do usuário que
        // pega a ficha "bonita" do livro em vez da versão preenchível.
        var fichaSemCampos = Path.Combine(_origem, "FichaChapada.pdf");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "RegrasTeste.pdf"), fichaSemCampos);

        var excecao = Assert.Throws<InvalidOperationException>(
            () => ImportadorDeSistema.Importar(_caminhos, "Sistema", [livro], fichaSemCampos));

        Assert.Contains("AcroForm", excecao.Message);
        Assert.False(Directory.Exists(Path.Combine(_caminhos.Sistemas, "Sistema")));
    }

    [Fact]
    public void Importar_ArquivoQueNaoEPdf_Rejeita()
    {
        var naoPdf = Path.Combine(_origem, "regras.txt");
        File.WriteAllText(naoPdf, "isto nao e um pdf");
        var ficha = CriarArquivoDeOrigem("Ficha.pdf");

        Assert.Throws<ArgumentException>(
            () => ImportadorDeSistema.Importar(_caminhos, "Sistema", [naoPdf], ficha));
    }

    [Fact]
    public void Importar_ArquivoInexistente_Rejeita()
    {
        var ficha = CriarArquivoDeOrigem("Ficha.pdf");

        Assert.Throws<FileNotFoundException>(() => ImportadorDeSistema.Importar(
            _caminhos, "Sistema", [Path.Combine(_origem, "nao-existe.pdf")], ficha));
    }

    [Fact]
    public void Importar_SemNenhumLivro_Rejeita()
    {
        var ficha = CriarArquivoDeOrigem("Ficha.pdf");

        Assert.Throws<ArgumentException>(() => ImportadorDeSistema.Importar(_caminhos, "Sistema", [], ficha));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../fuga")]
    [InlineData("sub/pasta")]
    [InlineData(@"C:\Windows")]
    [InlineData("  ")]
    public void Importar_NomeDeSistemaInvalido_Rejeita(string nome)
    {
        var livro = CriarArquivoDeOrigem("Livro.pdf");
        var ficha = CriarArquivoDeOrigem("Ficha.pdf");

        Assert.Throws<ArgumentException>(() => ImportadorDeSistema.Importar(_caminhos, nome, [livro], ficha));
    }

    [Fact]
    public void Importar_SistemaJaExistente_SobrescreveSemFalhar()
    {
        var livro = CriarArquivoDeOrigem("Livro.pdf");
        var ficha = CriarArquivoDeOrigem("Ficha.pdf");

        ImportadorDeSistema.Importar(_caminhos, "Sistema", [livro], ficha);
        var resultado = ImportadorDeSistema.Importar(_caminhos, "Sistema", [livro], ficha);

        Assert.Single(resultado.Livros);
    }
}
