using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// O sumário do livro e o contexto das buscas — as duas coisas que tiram round-trip do
/// Configurador.
///
/// <para>A economia aqui não é o tamanho do resultado, é o número de chamadas: cada chamada de
/// ferramenta é um turno, e um turno reenvia a conversa inteira ao modelo. Por isso os dois
/// existem, e por isso o contexto vem <em>junto</em> do resultado da busca em vez de numa
/// leitura seguinte.</para>
/// </summary>
public sealed class EstruturaDoLivroTestes : IDisposable
{
    private const string Sistema = "Aventura&Cia";

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "mainforge-estrutura-" + Guid.NewGuid());
    private readonly CaminhosDoProjeto _caminhos;

    public EstruturaDoLivroTestes()
    {
        _caminhos = new CaminhosDoProjeto(_raiz);
        GravarTexto("base", "Livro-Base", Livro);
    }

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    private const string Livro = """
        # Livro Base

        Introdução ao sistema.

        ## Criação de Personagem

        Escolha uma raça e uma classe.

        ### Racas

        Anao, elfo, humano.

        ## Equipamentos

        ```
        # isto é um comentário dentro de um quadro, não um título
        ```

        ### Armas

        A carga máxima é Forca vezes 5 quilos.
        Armas leves podem ser usadas com a mão secundária.

        ## Magias

        Bola de fogo, cura leve.
        """;

    private void GravarTexto(string fonte, string livro, string conteudo)
    {
        var pasta = Path.Combine(
            _caminhos.Entrada, Sistema, fonte, ConversorDeLivros.NomeDaPastaDeTexto);

        Directory.CreateDirectory(pasta);
        File.WriteAllText(Path.Combine(pasta, livro + ".md"), conteudo);
    }

    [Fact]
    public void Levantar_DevolveOsTitulosComNivelELinha()
    {
        var titulos = EstruturaDoLivro.Levantar(_caminhos, Sistema).Single().Titulos;

        Assert.Equal("Livro Base", titulos[0].Titulo);
        Assert.Equal(1, titulos[0].Nivel);

        var armas = titulos.Single(titulo => titulo.Titulo == "Armas");
        Assert.Equal(3, armas.Nivel);

        // A linha é o que vai virar offset de Read: precisa apontar para o título.
        var linhas = Livro.ReplaceLineEndings("\n").Split('\n');
        Assert.Equal("### Armas", linhas[armas.Linha - 1]);
    }

    /// <summary>
    /// Livro de RPG convertido tem bloco de código onde havia quadro ou tabela, e cerquilha
    /// dentro dele é conteúdo. Tratá-la como título encheria o sumário de lixo.
    /// </summary>
    [Fact]
    public void Levantar_IgnoraCerquilhaDentroDeBlocoDeCodigo()
    {
        var titulos = EstruturaDoLivro.Levantar(_caminhos, Sistema).Single().Titulos;

        Assert.DoesNotContain(titulos, titulo => titulo.Titulo.Contains("comentário"));
    }

    /// <summary>
    /// Num livro grande, o sumário raso é o que cabe: só os capítulos, para escolher onde
    /// descer.
    /// </summary>
    [Fact]
    public void Levantar_ComNivelMaximo_SoTrazOsTitulosMaisAltos()
    {
        var titulos = EstruturaDoLivro.Levantar(_caminhos, Sistema, nivelMaximo: 2).Single().Titulos;

        Assert.All(titulos, titulo => Assert.True(titulo.Nivel <= 2));
        Assert.Contains(titulos, titulo => titulo.Titulo == "Equipamentos");
        Assert.DoesNotContain(titulos, titulo => titulo.Titulo == "Armas");
    }

    [Fact]
    public void Levantar_SemTextoConvertido_ExplicaOQueFazer()
    {
        var vazio = new CaminhosDoProjeto(Path.Combine(_raiz, "outro"));
        Directory.CreateDirectory(Path.Combine(vazio.Entrada, Sistema));

        var erro = Assert.Throws<ErroDeFerramenta>(() => EstruturaDoLivro.Levantar(vazio, Sistema));

        Assert.Contains("reprocessar", erro.Message);
    }

    /// <summary>
    /// O ponto da mudança: a busca devolve o trecho, e o agente não precisa de um Read depois.
    /// </summary>
    [Fact]
    public void Busca_ComContexto_TrazAsLinhasEmVolta()
    {
        var ocorrencia = BuscaNosLivros
            .Procurar(_caminhos, Sistema, "carga", linhasDeContexto: 6)
            .Single();

        Assert.NotNull(ocorrencia.Contexto);
        Assert.Contains("Forca vezes 5", ocorrencia.Contexto);

        // O que veio junto é a vizinhança de verdade, não só a linha repetida.
        Assert.Contains("mão secundária", ocorrencia.Contexto);
    }

    [Fact]
    public void Busca_SemContexto_ContinuaMagra()
    {
        var ocorrencia = BuscaNosLivros.Procurar(_caminhos, Sistema, "carga").Single();

        Assert.Null(ocorrencia.Contexto);

        // A seção é o título mais próximo acima da linha — é ela que orienta o Read seguinte.
        Assert.Equal("Armas", ocorrencia.Secao);
    }

    /// <summary>
    /// O contexto não pode desfazer a economia que veio fazer: sem teto, trinta ocorrências com
    /// duzentas linhas cada devolveriam mais do que o arquivo inteiro.
    /// </summary>
    [Fact]
    public void Busca_ContextoExagerado_ELimitado()
    {
        var ocorrencia = BuscaNosLivros
            .Procurar(_caminhos, Sistema, "carga", linhasDeContexto: 5000)
            .Single();

        var linhas = ocorrencia.Contexto!.ReplaceLineEndings("\n").Split('\n').Length;

        Assert.True(linhas <= BuscaEmTexto.ContextoMaximo + 1, $"veio com {linhas} linhas");
    }

    /// <summary>O resultado formatado precisa carregar o trecho, senão o agente não o enxerga.</summary>
    [Fact]
    public void Descrever_ComContexto_MostraOTrechoEDispensaORead()
    {
        var ocorrencias = BuscaNosLivros.Procurar(_caminhos, Sistema, "carga", linhasDeContexto: 6);

        var texto = BuscaNosLivros.Descrever(ocorrencias, "carga", 30);

        Assert.Contains("Forca vezes 5", texto);
        Assert.Contains("Só abra o arquivo com Read se precisar", texto);
    }
}
