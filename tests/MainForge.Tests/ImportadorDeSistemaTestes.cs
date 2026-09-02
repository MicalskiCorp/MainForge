using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Tests;

public sealed class ImportadorDeSistemaTestes : IDisposable
{
    private static readonly string CaminhoFichaFixture =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "FichaTeste.pdf");

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "mainforge-import-" + Guid.NewGuid());
    private readonly string _origem = Path.Combine(Path.GetTempPath(), "mainforge-origem-" + Guid.NewGuid());
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

        Assert.True(File.Exists(Path.Combine(_caminhos.Entrada, "Aventura&Cia", "base", "Livro Basico.pdf")));
        Assert.True(File.Exists(Path.Combine(_caminhos.Modelos, "Aventura&Cia", "Ficha.pdf")));
    }

    /// <summary>
    /// A validação do sistema nasce aqui, e não só quando o Configurador roda: quem importa uma
    /// ficha para conferir um personagem pronto talvez nunca processe os livros, e sem isto ele
    /// ficaria sem conferência nenhuma. O que a importação já responde é o que não depende de
    /// livro — quais campos existem, quais são caixa de marcação, o idioma da ficha.
    /// </summary>
    [Fact]
    public void Importar_GravaOEsqueletoDeValidacaoDoSistema()
    {
        var livro = CriarArquivoDeOrigem("Livro Basico.pdf");
        var ficha = CriarArquivoDeOrigem("Ficha.pdf");

        var resultado = ImportadorDeSistema.Importar(_caminhos, "Aventura&Cia", [livro], ficha);

        Assert.NotNull(resultado.Regras);
        Assert.Equal(OrigemDasRegras.EstruturaDaFicha, resultado.Regras.Origem);

        var gravadas = RegrasDaFicha.Carregar(_caminhos, "Aventura&Cia");

        Assert.NotNull(gravadas);
        Assert.Contains(gravadas.Campos, regra => regra.Campo == "Nome");

        // Esqueleto não exige nada: as regras do jogo só existem depois de alguém ler os livros.
        Assert.All(gravadas.Campos, regra => Assert.False(regra.Obrigatorio));

        Assert.True(File.Exists(Path.Combine(
            _caminhos.Conhecimento, "Aventura&Cia", SistemaRpg.NomeDaValidacaoDaFicha)));
    }

    /// <summary>
    /// Importar um sistema é trazer o jogo base dele: os livros caem em <c>base/</c> sem
    /// pergunta nenhuma, e é isso que dá a toda expansão futura uma pasta irmã com que ser
    /// comparada na hora de escolher o que aquela mesa usa.
    /// </summary>
    [Fact]
    public void Importar_ColocaOsLivrosNaFonteBase()
    {
        var livro = CriarArquivoDeOrigem("Livro Basico.pdf");
        var ficha = CriarArquivoDeOrigem("Ficha.pdf");

        var resultado = ImportadorDeSistema.Importar(_caminhos, "Aventura&Cia", [livro], ficha);

        Assert.True(resultado.Fonte.EhBase);
        Assert.Equal([FonteDoSistema.Base], resultado.Sistema.DescobrirFontes(_caminhos));
        Assert.False(resultado.Sistema.PrecisaMigrarParaFontes(_caminhos));
    }

    [Fact]
    public void Importar_VariosLivros_CopiaTodos()
    {
        var primeiro = CriarArquivoDeOrigem("Livro1.pdf");
        var segundo = CriarArquivoDeOrigem("Livro2.pdf");
        var ficha = CriarArquivoDeOrigem("Ficha.pdf");

        var resultado = ImportadorDeSistema.Importar(_caminhos, "Tormenta20", [primeiro, segundo], ficha);

        Assert.Equal(2, resultado.Livros.Count);
        Assert.True(File.Exists(Path.Combine(_caminhos.Entrada, "Tormenta20", "base", "Livro2.pdf")));
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
        Assert.False(Directory.Exists(Path.Combine(_caminhos.Entrada, "Sistema")));
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

    /// <summary>
    /// A expansão entra numa pasta própria, ao lado de <c>base/</c> e sem tocá-la. É essa
    /// separação em disco que a criação de personagem transforma numa escolha do usuário.
    /// </summary>
    [Fact]
    public void AdicionarLivros_ExpansaoVaiParaPastaPropriaSemMexerNaBase()
    {
        var basico = CriarArquivoDeOrigem("Livro Basico.pdf");
        var ficha = CriarArquivoDeOrigem("Ficha.pdf");
        ImportadorDeSistema.Importar(_caminhos, "Aventura&Cia", [basico], ficha);

        var compendio = CriarArquivoDeOrigem("Compendio.pdf");
        var adicionados = ImportadorDeSistema.AdicionarLivros(
            _caminhos, "Aventura&Cia", FonteDoSistema.Criar("Compendio-Arcano"), [compendio]);

        Assert.Equal(["Compendio.pdf"], adicionados);
        Assert.True(File.Exists(Path.Combine(_caminhos.Entrada, "Aventura&Cia", "Compendio-Arcano", "Compendio.pdf")));
        Assert.True(File.Exists(Path.Combine(_caminhos.Entrada, "Aventura&Cia", "base", "Livro Basico.pdf")));

        Assert.Equal(
            [FonteDoSistema.Base, new FonteDoSistema("Compendio-Arcano")],
            new SistemaRpg("Aventura&Cia").DescobrirFontes(_caminhos));
    }

    /// <summary>
    /// Um segundo livro do próprio jogo base não vira expansão — é a mesma fonte, e precisa
    /// valer em toda mesa como o primeiro.
    /// </summary>
    [Fact]
    public void AdicionarLivros_NaFonteBase_CaiJuntoDoLivroBasico()
    {
        var basico = CriarArquivoDeOrigem("Livro Basico.pdf");
        var ficha = CriarArquivoDeOrigem("Ficha.pdf");
        ImportadorDeSistema.Importar(_caminhos, "Aventura&Cia", [basico], ficha);

        var segundo = CriarArquivoDeOrigem("Guia do Mestre.pdf");
        ImportadorDeSistema.AdicionarLivros(_caminhos, "Aventura&Cia", FonteDoSistema.Base, [segundo]);

        Assert.True(File.Exists(Path.Combine(_caminhos.Entrada, "Aventura&Cia", "base", "Guia do Mestre.pdf")));
        Assert.Single(new SistemaRpg("Aventura&Cia").DescobrirFontes(_caminhos));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("sub/pasta")]
    [InlineData("  ")]
    public void AdicionarLivros_NomeDeExpansaoInvalido_Rejeita(string nome)
    {
        Assert.Throws<ArgumentException>(() => FonteDoSistema.Criar(nome));
    }

    /// <summary>
    /// Um compêndio sozinho não descreve a criação de personagem inteira: processá-lo sem o
    /// livro básico produziria uma base cheia de buracos, e o erro só apareceria muito depois,
    /// no meio de uma conversa com o Dungeon Master.
    /// </summary>
    [Fact]
    public void AdicionarLivros_SistemaQueNaoExiste_Rejeita()
    {
        var compendio = CriarArquivoDeOrigem("Compendio.pdf");

        var excecao = Assert.Throws<InvalidOperationException>(() => ImportadorDeSistema.AdicionarLivros(
            _caminhos, "SistemaInexistente", FonteDoSistema.Criar("Compendio-Arcano"), [compendio]));

        Assert.Contains("não foi importado", excecao.Message);
    }

    [Fact]
    public void AdicionarLivros_ArquivoQueNaoEPdf_RejeitaSemCopiarNada()
    {
        var basico = CriarArquivoDeOrigem("Livro Basico.pdf");
        var ficha = CriarArquivoDeOrigem("Ficha.pdf");
        ImportadorDeSistema.Importar(_caminhos, "Aventura&Cia", [basico], ficha);

        var naoPdf = Path.Combine(_origem, "expansao.txt");
        File.WriteAllText(naoPdf, "isto nao e um pdf");

        Assert.Throws<ArgumentException>(() => ImportadorDeSistema.AdicionarLivros(
            _caminhos, "Aventura&Cia", FonteDoSistema.Criar("Compendio-Arcano"), [naoPdf]));

        Assert.False(Directory.Exists(Path.Combine(_caminhos.Entrada, "Aventura&Cia", "Compendio-Arcano")));
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
