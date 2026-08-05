using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// O extrator é o piso do aplicativo: é ele que garante que existe um caminho até o texto do
/// livro sem nenhuma instalação além da do próprio programa. Sem esse piso, uma máquina sem
/// Python (markitdown) e sem poppler (o <c>pdftoppm</c> que o <c>Read</c> do Claude Code usa para
/// rasterizar PDF) não conseguia processar sistema nenhum.
/// </summary>
public sealed class ExtratorDeTextoDePdfTestes : IDisposable
{
    private static readonly string LivroDeVerdade =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "RegrasTeste.pdf");

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "mainforge-extrator-" + Guid.NewGuid());

    public ExtratorDeTextoDePdfTestes() => Directory.CreateDirectory(_raiz);

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    private string Destino(string nome = "Regras.md") => Path.Combine(_raiz, "_texto", nome);

    [Fact]
    public void Extrair_TrazOTextoDoLivro()
    {
        var destino = Destino();

        var paginas = ExtratorDeTextoDePdf.ExtrairPara(LivroDeVerdade, destino);

        var texto = File.ReadAllText(destino);

        Assert.Equal(1, paginas);
        Assert.Contains("Guerreiro: recebe +2 em Forca", texto);
        Assert.Contains("Pontos de Vida iniciais = 10 + Constituicao", texto);
    }

    /// <summary>
    /// O número da página vira título de seção porque é o que a busca mostra ao agente e o que
    /// permite voltar ao PDF original para conferir um trecho.
    /// </summary>
    [Fact]
    public void Extrair_MarcaAPaginaDeOndeVeioCadaTrecho()
    {
        ExtratorDeTextoDePdf.ExtrairPara(LivroDeVerdade, Destino());

        Assert.Contains("## Página 1", File.ReadAllText(Destino()));
    }

    [Fact]
    public void Extrair_ArquivoQueNaoEhPdf_ExplicaEmVezDeQuebrar()
    {
        var falso = Path.Combine(_raiz, "Falso.pdf");
        File.WriteAllText(falso, "isto nao e um pdf");

        var erro = Assert.Throws<ErroDeFerramenta>(() => ExtratorDeTextoDePdf.ExtrairPara(falso, Destino()));

        Assert.Contains("não foi possível abrir o PDF", erro.Message);
        Assert.False(File.Exists(Destino()));
    }
}
