using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// A conversão dos livros para texto é o que decide quanto da cota da assinatura o
/// processamento vai custar. O que precisa valer: converter só o que mudou (senão o usuário
/// paga a espera de novo a cada execução), nunca deixar meio arquivo em disco (o agente leria
/// um livro truncado sem ter como saber) e nunca derrubar o processamento quando o markitdown
/// falha num livro.
///
/// <para>O markitdown de verdade não roda aqui — ele é uma dependência externa e opcional. O
/// que os testes exercitam é o contrato do lado do aplicativo, com um programa de mentira que
/// se comporta como ele.</para>
/// </summary>
public sealed class ConversorDeLivrosTestes : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "mainforge-conversao-" + Guid.NewGuid());
    private readonly CaminhosDoProjeto _caminhos;

    public ConversorDeLivrosTestes()
    {
        _caminhos = new CaminhosDoProjeto(_raiz);
        Directory.CreateDirectory(_raiz);
    }

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    private string GravarLivro(string nome = "Livro.pdf", string fonte = "base")
    {
        var caminho = Path.Combine(_caminhos.Entrada, "Aventura&Cia", fonte, nome);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, "conteudo do pdf");
        return caminho;
    }

    /// <summary>Um PDF de verdade, para os caminhos que passam pelo extrator interno.</summary>
    private string GravarLivroDeVerdade(string nome = "Regras.pdf", string fonte = "base")
    {
        var caminho = Path.Combine(_caminhos.Entrada, "Aventura&Cia", fonte, nome);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "RegrasTeste.pdf"), caminho);
        return caminho;
    }

    /// <summary>
    /// Um "markitdown" que copia a entrada para a saída. O aplicativo o chama como
    /// <c>&lt;programa&gt; &lt;entrada&gt; -o &lt;saída&gt;</c>, então o script recebe %1 %2 %3.
    ///
    /// <para>É um <c>.cmd</c> de propósito, e não um <c>cmd /c</c>: é a forma que o pip instala
    /// no Windows, e é a que exercita a passagem de um caminho com <c>&amp;</c> — o nome de pasta
    /// "D&amp;D5e" é literalmente o do sistema que o usuário tem importado.</para>
    /// </summary>
    private ProgramaDeConversao ProgramaFalso(bool falhando = false)
    {
        var script = Path.Combine(_raiz, falhando ? "falha.cmd" : "converte.cmd");

        File.WriteAllText(script, falhando
            ? "@echo problema no PDF 1>&2\r\n@exit /b 1\r\n"
            : "@copy \"%~1\" \"%~3\" >nul\r\n");

        return new ProgramaDeConversao(script, [], "markitdown de teste");
    }

    private Task<IReadOnlyList<ConversaoDeLivro>> ConverterAsync(ProgramaDeConversao programa) =>
        ConversorDeLivros.ConverterSistemaAsync(_caminhos, "Aventura&Cia", programa);

    [Fact]
    public void CaminhoDoTexto_FicaNaPastaDerivadaAoLadoDoPdf()
    {
        var pdf = Path.Combine("Input", "Aventura&Cia", "base", "Livro Base.pdf");

        var texto = ConversorDeLivros.CaminhoDoTexto(pdf);

        Assert.Equal(
            Path.Combine("Input", "Aventura&Cia", "base", ConversorDeLivros.NomeDaPastaDeTexto, "Livro Base.md"),
            texto);
    }

    [Fact]
    public async Task Converter_GeraOTextoDoLivroNaPastaDaFonte()
    {
        var pdf = GravarLivro();

        var resultado = Assert.Single(await ConverterAsync(ProgramaFalso()));

        Assert.Equal(SituacaoDaConversao.Convertido, resultado.Situacao);
        Assert.Equal("Input/Aventura&Cia/base/_texto/Livro.md", resultado.CaminhoDoTexto);
        Assert.Equal("conteudo do pdf", File.ReadAllText(ConversorDeLivros.CaminhoDoTexto(pdf)).Trim());
    }

    /// <summary>
    /// Converter de novo o que já está convertido é minutos de espera por nada — e é o caso
    /// comum, porque o processamento costuma ser retomado várias vezes sobre os mesmos livros.
    /// </summary>
    [Fact]
    public async Task Converter_LivroJaConvertido_NaoEhRefeito()
    {
        GravarLivro();
        await ConverterAsync(ProgramaFalso());

        var resultado = Assert.Single(await ConverterAsync(ProgramaFalso(falhando: true)));

        Assert.Equal(SituacaoDaConversao.JaEstavaPronto, resultado.Situacao);
    }

    [Fact]
    public async Task Converter_PdfTrocadoDepoisDaConversao_EhConvertidoDeNovo()
    {
        var pdf = GravarLivro();
        await ConverterAsync(ProgramaFalso());

        File.WriteAllText(pdf, "outra edicao do livro");
        File.SetLastWriteTimeUtc(pdf, DateTime.UtcNow.AddMinutes(5));

        var resultado = Assert.Single(await ConverterAsync(ProgramaFalso()));

        Assert.Equal(SituacaoDaConversao.Convertido, resultado.Situacao);
        Assert.Equal("outra edicao do livro", File.ReadAllText(ConversorDeLivros.CaminhoDoTexto(pdf)).Trim());
    }

    /// <summary>
    /// PDF protegido ou só com imagem faz o markitdown falhar. Isso não pode derrubar o
    /// processamento nem deixar um .md vazio para trás: o agente cairia no PDF achando que tem
    /// texto, ou leria um arquivo em branco como se o livro não dissesse nada.
    /// </summary>
    [Fact]
    public async Task Converter_QuandoOProgramaFalha_NaoDeixaArquivoPelaMetade()
    {
        var pdf = GravarLivro();

        var resultado = Assert.Single(await ConverterAsync(ProgramaFalso(falhando: true)));

        Assert.Equal(SituacaoDaConversao.Falhou, resultado.Situacao);
        Assert.Contains("problema no PDF", resultado.Detalhe);
        Assert.False(File.Exists(ConversorDeLivros.CaminhoDoTexto(pdf)));
        Assert.Null(ConversorDeLivros.TextoAtualizadoDe(pdf));
    }

    [Fact]
    public async Task Converter_CadaLivroVaiParaAPastaDaPropriaFonte()
    {
        GravarLivro("Basico.pdf");
        GravarLivro("Compendio-Arcano.pdf", fonte: "Compendio-Arcano");

        var caminhos = (await ConverterAsync(ProgramaFalso()))
            .Select(conversao => conversao.CaminhoDoTexto)
            .Order()
            .ToList();

        Assert.Equal(
            ["Input/Aventura&Cia/base/_texto/Basico.md", "Input/Aventura&Cia/Compendio-Arcano/_texto/Compendio-Arcano.md"],
            caminhos);
    }

    /// <summary>
    /// Sem markitdown na máquina, o livro tem que virar texto do mesmo jeito. Era o buraco que
    /// deixava o processamento sem saída nenhuma: sem texto convertido e sem o poppler que o
    /// <c>Read</c> do Claude Code usa para rasterizar PDF, o agente não alcançava o livro por
    /// caminho algum.
    /// </summary>
    [Fact]
    public async Task Converter_SemMarkitdown_UsaOExtratorInterno()
    {
        var pdf = GravarLivroDeVerdade();

        var resultado = Assert.Single(await ConversorDeLivros.ConverterSistemaAsync(_caminhos, "Aventura&Cia", programa: null));

        Assert.Equal(SituacaoDaConversao.Convertido, resultado.Situacao);
        Assert.Equal(ConversorDeLivros.ExtratorInterno, resultado.Conversor);
        Assert.Contains("Guerreiro", File.ReadAllText(ConversorDeLivros.CaminhoDoTexto(pdf)));
    }

    [Fact]
    public async Task Converter_MarkitdownFalhouNesteLivro_CaiParaOExtratorInterno()
    {
        var pdf = GravarLivroDeVerdade();

        var resultado = Assert.Single(await ConverterAsync(ProgramaFalso(falhando: true)));

        Assert.Equal(SituacaoDaConversao.Convertido, resultado.Situacao);
        Assert.Equal(ConversorDeLivros.ExtratorInterno, resultado.Conversor);
        Assert.Contains("markitdown", resultado.Detalhe);
        Assert.True(File.Exists(ConversorDeLivros.CaminhoDoTexto(pdf)));
    }

    /// <summary>
    /// Quando nem o markitdown nem o extrator dão conta (PDF protegido, digitalizado), o relato
    /// precisa trazer os dois motivos: é o que diz ao usuário se o problema é o livro ou a
    /// instalação.
    /// </summary>
    [Fact]
    public async Task Converter_QuandoOsDoisFalham_ORelatoTrazOsDoisMotivos()
    {
        GravarLivro("Nao é um pdf.pdf");

        var resultado = Assert.Single(await ConverterAsync(ProgramaFalso(falhando: true)));

        Assert.Equal(SituacaoDaConversao.Falhou, resultado.Situacao);
        Assert.Contains("markitdown:", resultado.Detalhe);
        Assert.Contains("extrator interno:", resultado.Detalhe);
    }

    /// <summary>
    /// O prompt do Configurador manda ler o texto convertido; um .md mais velho que o PDF não é
    /// mais aquele livro, e mandar ler assim mesmo entregaria a edição errada das regras.
    /// </summary>
    [Fact]
    public async Task TextoAtualizadoDe_SoValeEnquantoOTextoForMaisNovoQueOPdf()
    {
        var pdf = GravarLivro();

        Assert.Null(ConversorDeLivros.TextoAtualizadoDe(pdf));

        await ConverterAsync(ProgramaFalso());
        Assert.NotNull(ConversorDeLivros.TextoAtualizadoDe(pdf));

        File.SetLastWriteTimeUtc(pdf, DateTime.UtcNow.AddHours(1));
        Assert.Null(ConversorDeLivros.TextoAtualizadoDe(pdf));
    }
}
