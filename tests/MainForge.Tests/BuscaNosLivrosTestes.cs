using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// A busca é o que substitui, dentro do guardrail, as ferramentas de busca negadas a todo
/// agente. Ela precisa achar apesar do acento (o agente escreve "Pontos de Vida", o livro traz
/// "Pontos de Vida" com acento noutra palavra da linha), dizer em que seção o trecho está — é
/// isso que evita a leitura do arquivo inteiro — e nunca alcançar arquivo fora de
/// <c>Input/&lt;sistema&gt;/</c>.
/// </summary>
public sealed class BuscaNosLivrosTestes : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "mainforge-busca-" + Guid.NewGuid());
    private readonly CaminhosDoProjeto _caminhos;

    public BuscaNosLivrosTestes()
    {
        _caminhos = new CaminhosDoProjeto(_raiz);

        GravarTexto("Livro Base", """
            # Livro Base

            ## Criacao de Personagem

            Escolha uma raca e uma classe.

            ## Magias

            Bola de Fogo: 8d6 de dano de fogo em uma area.
            Mãos Flamejantes: 3d6 de dano em cone.
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    private void GravarTexto(string livro, string conteudo, string fonte = "base")
    {
        var caminho = Path.Combine(
            _caminhos.Entrada, "Aventura&Cia", fonte, ConversorDeLivros.NomeDaPastaDeTexto, livro + ".md");

        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, conteudo);
    }

    private IReadOnlyList<OcorrenciaNoLivro> Procurar(string termo, string? livro = null, int maximo = 30) =>
        BuscaNosLivros.Procurar(_caminhos, "Aventura&Cia", termo, livro, maximo);

    [Fact]
    public void Procurar_DevolveLinhaESecaoDoTrecho()
    {
        var ocorrencia = Assert.Single(Procurar("Bola de Fogo"));

        Assert.Equal("Input/Aventura&Cia/base/_texto/Livro Base.md", ocorrencia.Livro);
        Assert.Equal("Magias", ocorrencia.Secao);
        Assert.Contains("8d6", ocorrencia.Trecho);
        Assert.Equal(9, ocorrencia.Linha);
    }

    [Fact]
    public void Procurar_IgnoraAcentoEMaiuscula()
    {
        Assert.Single(Procurar("maos flamejantes"));
        Assert.Single(Procurar("CRIAÇÃO DE PERSONAGEM"));
    }

    [Fact]
    public void Procurar_LimitaAsOcorrenciasParaNaoDespejarOLivroInteiro()
    {
        Assert.Equal(2, Procurar("de", maximo: 2).Count);
    }

    [Fact]
    public void Procurar_RestritaAUmLivro_IgnoraOsDemais()
    {
        GravarTexto("Compendio-Arcano", "Bola de Fogo aprimorada.", fonte: "Compendio-Arcano");

        var ocorrencia = Assert.Single(Procurar("Bola de Fogo", livro: "Compendio-Arcano.pdf"));

        Assert.Contains("Compendio-Arcano", ocorrencia.Livro);
    }

    [Fact]
    public void Procurar_SemTextoConvertido_ExplicaOQueFazerEmVezDeDevolverVazio()
    {
        Directory.CreateDirectory(Path.Combine(_caminhos.Entrada, "Outro"));

        var erro = Assert.Throws<ErroDeFerramenta>(
            () => BuscaNosLivros.Procurar(_caminhos, "Outro", "magia"));

        Assert.Contains("Nenhum texto convertido", erro.Message);
    }

    /// <summary>
    /// O nome do sistema vem do modelo, então é um caminho não confiável como qualquer outro —
    /// e o confinamento é aplicado aqui, em C#, não por lista de permissão.
    /// </summary>
    [Fact]
    public void Procurar_SistemaQueEscapaDaPastaDeSistemas_ERecusado()
    {
        Assert.Throws<UnauthorizedAccessException>(
            () => BuscaNosLivros.Procurar(_caminhos, "../../Windows", "magia"));
    }
}
