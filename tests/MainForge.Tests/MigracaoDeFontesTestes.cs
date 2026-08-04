using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// A migração mexe no dado mais caro do aplicativo: uma base de conhecimento que já custou a
/// leitura dos livros inteiros. O que estes testes protegem é que ela seja um <b>mover</b> e
/// não um <b>refazer</b> — nada se perde, o registro de progresso continua apontando para os
/// arquivos certos, e rodar de novo não desarruma o que já está arrumado.
/// </summary>
public sealed class MigracaoDeFontesTestes : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "mainforge-migracao-" + Guid.NewGuid());
    private readonly CaminhosDoProjeto _caminhos;
    private readonly SistemaRpg _sistema = new("Aventura&Cia");

    public MigracaoDeFontesTestes() => _caminhos = new CaminhosDoProjeto(_raiz);

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    /// <summary>Monta um sistema no layout antigo: tudo solto na pasta do sistema.</summary>
    private void MontarLayoutAntigo()
    {
        Gravar(Path.Combine(_caminhos.Sistemas, "Aventura&Cia", "Livro Base.pdf"), "%PDF-falso");
        Gravar(Path.Combine(_caminhos.Conhecimento, "Aventura&Cia", "Regras-Fundamentais.md"), "# Regras\n\nO d20.\n");
        Gravar(Path.Combine(_caminhos.Conhecimento, "Aventura&Cia", "Classes", "Monge.md"), "# Monge\n\nDado de vida d8.\n");
        Gravar(Path.Combine(_caminhos.Conhecimento, "Aventura&Cia", "Ficha-Mapeamento.md"), "# Mapeamento\n");
        Gravar(Path.Combine(_caminhos.Conhecimento, "Aventura&Cia", "Ficha-ModeloEmTexto.md"), "# Modelo\n");
    }

    private static void Gravar(string caminho, string conteudo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, conteudo);
    }

    private string NoConhecimento(params string[] partes) =>
        Path.Combine([_caminhos.Conhecimento, "Aventura&Cia", .. partes]);

    [Fact]
    public void Migrar_LevaLivrosEConhecimentoParaBase()
    {
        MontarLayoutAntigo();

        var resultado = MigracaoDeFontes.Migrar(_caminhos, "Aventura&Cia");

        Assert.True(resultado.MoveuAlgo);
        Assert.True(File.Exists(Path.Combine(_caminhos.Sistemas, "Aventura&Cia", "base", "Livro Base.pdf")));
        Assert.True(File.Exists(NoConhecimento("base", "Regras-Fundamentais.md")));
        Assert.True(File.Exists(NoConhecimento("base", "Classes", "Monge.md")));

        Assert.False(File.Exists(Path.Combine(_caminhos.Sistemas, "Aventura&Cia", "Livro Base.pdf")));
        Assert.False(Directory.Exists(NoConhecimento("Classes")));
    }

    /// <summary>
    /// A ficha é do sistema, não de uma fonte: se ela descesse para <c>base/</c>, uma mesa que
    /// usasse só uma expansão ficaria sem saber como preencher o PDF.
    /// </summary>
    [Fact]
    public void Migrar_DeixaOsArquivosDaFichaNaRaizDoSistema()
    {
        MontarLayoutAntigo();

        MigracaoDeFontes.Migrar(_caminhos, "Aventura&Cia");

        foreach (var arquivo in SistemaRpg.ArquivosDaFicha)
        {
            Assert.True(File.Exists(NoConhecimento(arquivo)), $"{arquivo} deveria continuar na raiz");
            Assert.False(File.Exists(NoConhecimento("base", arquivo)), $"{arquivo} não deveria ter descido");
        }
    }

    /// <summary>
    /// Sem reapontar o plano, o próximo processamento veria todo arquivo já pronto como
    /// pendente e mandaria o agente gerar de novo o que já foi pago.
    /// </summary>
    [Fact]
    public void Migrar_ReapontaOPlanoParaOsCaminhosNovos()
    {
        MontarLayoutAntigo();

        var antes = EstadoDoProcessamento.Carregar(_caminhos, "Aventura&Cia");
        antes.RegistrarPlano([new ItemDoPlano { Caminho = "Classes/Monge.md", Descricao = "Classe monge." }]);
        antes.SincronizarComDisco();
        antes.Salvar();

        MigracaoDeFontes.Migrar(_caminhos, "Aventura&Cia");

        var depois = EstadoDoProcessamento.Carregar(_caminhos, "Aventura&Cia");
        depois.SincronizarComDisco();

        Assert.Contains(depois.Concluidos, item => item.Caminho == "base/Classes/Monge.md");
        Assert.Empty(depois.Pendentes);
        Assert.Equal("Classe monge.", depois.Plano.Single(item => item.Caminho == "base/Classes/Monge.md").Descricao);
    }

    [Fact]
    public void Migrar_RegistraOsLivrosComAFonteBase()
    {
        MontarLayoutAntigo();

        MigracaoDeFontes.Migrar(_caminhos, "Aventura&Cia");

        var estado = EstadoDoProcessamento.Carregar(_caminhos, "Aventura&Cia");
        estado.SincronizarComDisco();

        Assert.Equal(FonteDoSistema.IdDaBase, estado.Livros.Single().Fonte);
    }

    [Fact]
    public void Migrar_SistemaJaNoLayoutNovo_NaoMexeEmNada()
    {
        MontarLayoutAntigo();
        MigracaoDeFontes.Migrar(_caminhos, "Aventura&Cia");

        Gravar(NoConhecimento("Compendio-Arcano", "Subclasses.md"), "# Subclasses\n");

        var segunda = MigracaoDeFontes.Migrar(_caminhos, "Aventura&Cia");

        Assert.False(segunda.MoveuAlgo);
        Assert.True(File.Exists(NoConhecimento("Compendio-Arcano", "Subclasses.md")));
        Assert.True(File.Exists(NoConhecimento("base", "Classes", "Monge.md")));
    }

    [Fact]
    public void PrecisaMigrarParaFontes_VerdadeSoAntesDaMigracao()
    {
        MontarLayoutAntigo();
        Assert.True(_sistema.PrecisaMigrarParaFontes(_caminhos));

        MigracaoDeFontes.Migrar(_caminhos, "Aventura&Cia");
        Assert.False(_sistema.PrecisaMigrarParaFontes(_caminhos));
    }

    /// <summary>
    /// Um sistema em que só existem os arquivos da ficha e o índice não tem conteúdo fora de
    /// fonte nenhuma — oferecer migração ali seria oferecer uma operação que não faz nada.
    /// </summary>
    [Fact]
    public void PrecisaMigrarParaFontes_SoComArquivosDoSistema_EFalso()
    {
        Gravar(NoConhecimento("Ficha-Mapeamento.md"), "# Mapeamento\n");
        Gravar(NoConhecimento(SistemaRpg.NomeDoIndice), "# Indice\n");

        Assert.False(_sistema.PrecisaMigrarParaFontes(_caminhos));
    }

    [Fact]
    public void Migrar_ReconstroiOIndiceDaBase()
    {
        MontarLayoutAntigo();

        MigracaoDeFontes.Migrar(_caminhos, "Aventura&Cia");

        Assert.True(File.Exists(NoConhecimento("base", SistemaRpg.NomeDoIndice)));
        Assert.Contains("base", File.ReadAllText(NoConhecimento(SistemaRpg.NomeDoIndice)));
    }
}
