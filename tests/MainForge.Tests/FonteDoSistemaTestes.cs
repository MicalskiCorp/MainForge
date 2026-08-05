using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// A fonte é o que transforma "esta mesa não usa o compêndio X" numa regra que o aplicativo
/// consegue aplicar. Estes testes cobrem a descoberta das fontes em disco — é dela que sai a
/// lista oferecida na criação do personagem, e uma expansão que não aparece ali é uma expansão
/// que o usuário não tem como escolher.
/// </summary>
public sealed class FonteDoSistemaTestes : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "mainforge-fontes-" + Guid.NewGuid());
    private readonly CaminhosDoProjeto _caminhos;
    private readonly SistemaRpg _sistema = new("Aventura&Cia");

    public FonteDoSistemaTestes() => _caminhos = new CaminhosDoProjeto(_raiz);

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    private void Gravar(string caminho, string conteudo = "conteudo")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, conteudo);
    }

    private string EmSystems(params string[] partes) => Path.Combine([_caminhos.Entrada, "Aventura&Cia", .. partes]);

    private string EmKnowledge(params string[] partes) => Path.Combine([_caminhos.Conhecimento, "Aventura&Cia", .. partes]);

    [Fact]
    public void EhBase_SoParaAPastaBase()
    {
        Assert.True(FonteDoSistema.Base.EhBase);
        Assert.True(new FonteDoSistema("BASE").EhBase);
        Assert.False(new FonteDoSistema("Compendio-Arcano").EhBase);
    }

    /// <summary>O jogo base vem sempre primeiro: é a fonte que existe em toda mesa.</summary>
    [Fact]
    public void Ordenar_ColocaABasePrimeiroEORestoEmOrdem()
    {
        var ordenadas = FonteDoSistema.Ordenar(
        [
            new FonteDoSistema("Compendio-Sombrio"),
            new FonteDoSistema("Compendio-Arcano"),
            FonteDoSistema.Base,
            new FonteDoSistema("Aventuras"),
        ]);

        Assert.Equal(["base", "Aventuras", "Compendio-Arcano", "Compendio-Sombrio"], ordenadas.Select(fonte => fonte.Id));
    }

    [Fact]
    public void DescobrirFontes_ListaAsPastasComLivro()
    {
        Gravar(EmSystems("base", "Livro.pdf"));
        Gravar(EmSystems("Compendio-Arcano", "Compendio-Arcano.pdf"));
        Directory.CreateDirectory(EmSystems("Vazia"));

        Assert.Equal(["base", "Compendio-Arcano"], _sistema.DescobrirFontes(_caminhos).Select(fonte => fonte.Id));
    }

    /// <summary>
    /// Uma expansão importada mas ainda não processada não pode aparecer na criação de
    /// personagem: o usuário a marcaria e o agente não acharia regra nenhuma dentro dela.
    /// </summary>
    [Fact]
    public void DescobrirFontesComConhecimento_IgnoraExpansaoAindaNaoProcessada()
    {
        Gravar(EmSystems("base", "Livro.pdf"));
        Gravar(EmSystems("Compendio-Arcano", "Compendio-Arcano.pdf"));
        Gravar(EmKnowledge("base", "Classes", "Monge.md"), "# Monge");

        Assert.Equal(["base", "Compendio-Arcano"], _sistema.DescobrirFontes(_caminhos).Select(fonte => fonte.Id));
        Assert.Equal(["base"], _sistema.DescobrirFontesComConhecimento(_caminhos).Select(fonte => fonte.Id));
    }

    /// <summary>
    /// O recenseamento é o que diz ao Configurador em qual pasta de Sistemas/ o conteúdo de
    /// cada livro deve cair — errar aqui é gravar regra de compêndio como se fosse do básico.
    /// </summary>
    [Fact]
    public void EstadoDoProcessamento_RegistraCadaLivroComASuaFonte()
    {
        Gravar(EmSystems("base", "Livro Base.pdf"));
        Gravar(EmSystems("Compendio-Arcano", "Compendio-Arcano.pdf"));

        var estado = EstadoDoProcessamento.Carregar(_caminhos, "Aventura&Cia");
        estado.SincronizarComDisco();

        Assert.Equal("base", estado.Livros.Single(livro => livro.Arquivo == "Livro Base.pdf").Fonte);
        Assert.Equal("Compendio-Arcano", estado.Livros.Single(livro => livro.Arquivo == "Compendio-Arcano.pdf").Fonte);
    }

    [Fact]
    public void PendentesPorFonte_AgrupaOsLivrosNaoLidosComABasePrimeiro()
    {
        Gravar(EmSystems("Compendio-Arcano", "Compendio-Arcano.pdf"));
        Gravar(EmSystems("base", "Livro Base.pdf"));

        var estado = EstadoDoProcessamento.Carregar(_caminhos, "Aventura&Cia");
        estado.SincronizarComDisco();

        var porFonte = estado.PendentesPorFonte();

        Assert.Equal(["base", "Compendio-Arcano"], porFonte.Select(par => par.Fonte.Id));
        Assert.Equal(["Livro Base.pdf"], porFonte[0].Livros);
    }

    /// <summary>
    /// Mover um livro de fonte não pode obrigar a relê-lo: o conteúdo é o mesmo, só o destino
    /// dele em Sistemas/ mudou.
    /// </summary>
    [Fact]
    public void SincronizarComDisco_LivroQueMudouDeFonte_ContinuaMarcadoComoLido()
    {
        Gravar(EmSystems("base", "Compendio.pdf"));

        var estado = EstadoDoProcessamento.Carregar(_caminhos, "Aventura&Cia");
        estado.SincronizarComDisco();
        estado.MarcarLivrosConcluidos();
        estado.Salvar();

        Directory.CreateDirectory(EmSystems("Compendio-Arcano"));
        File.Move(EmSystems("base", "Compendio.pdf"), EmSystems("Compendio-Arcano", "Compendio.pdf"));

        var depois = EstadoDoProcessamento.Carregar(_caminhos, "Aventura&Cia");
        depois.SincronizarComDisco();

        var livro = depois.Livros.Single();
        Assert.Equal("Compendio-Arcano", livro.Fonte);
        Assert.Equal(EstadoDoItem.Concluido, livro.Estado);
    }
}
