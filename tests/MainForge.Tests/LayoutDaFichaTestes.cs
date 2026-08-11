using System.Runtime.CompilerServices;
using MainForge.Tools;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace MainForge.Tests;

/// <summary>
/// Trava a resposta que evita o erro que já aconteceu de verdade: a ficha de D&amp;D 5e em
/// português teve os rótulos traduzidos e reordenados no idioma novo, mas os campos do AcroForm
/// ficaram nas posições da ordem em inglês. Quem só recebia a lista de nomes parecia o pareamento
/// pelo nome, e o PDF saía com o valor de uma perícia na linha de outra.
///
/// <para>Por isso os testes montam uma ficha em que <b>nome e rótulo discordam de propósito</b>:
/// o que precisa valer é a posição impressa.</para>
/// </summary>
public class LayoutDaFichaTestes : IDisposable
{
    private readonly string _diretorio;

    public LayoutDaFichaTestes()
    {
        // Desenhar texto exige fonte, e quem registra o resolvedor do projeto é o construtor
        // estático de LayoutDaFicha. Sem isto o teste dependeria da ordem de execução.
        RuntimeHelpers.RunClassConstructor(typeof(LayoutDaFicha).TypeHandle);

        _diretorio = Path.Combine(Path.GetTempPath(), "mainforge-layout-" + Guid.NewGuid());
        Directory.CreateDirectory(_diretorio);
    }

    public void Dispose()
    {
        if (Directory.Exists(_diretorio))
        {
            Directory.Delete(_diretorio, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Uma linha da ficha de teste: o texto impresso e o nome do campo que fica nela.</summary>
    private sealed record Linha(string Rotulo, string Campo);

    private const double Altura = 200;

    /// <summary>A base da linha <paramref name="indice"/>, em coordenada de PDF (cresce para cima).</summary>
    private static double BaseDaLinha(int indice) => 160 - (indice * 20);

    /// <summary>
    /// Monta uma ficha de uma página com uma linha por item, de cima para baixo: o rótulo
    /// impresso à esquerda e, à direita dele, o campo de texto daquela linha.
    ///
    /// <para>O AcroForm é montado a mão porque o PDFsharp 6.2 não tem API pública para criar
    /// campo de formulário — só para ler e preencher. São poucos dicionários, e escrevê-los aqui
    /// deixa explícito o que o teste está afirmando: este widget, neste retângulo, com este nome.</para>
    /// </summary>
    private string CriarFicha(params Linha[] linhas)
    {
        var caminho = Path.Combine(_diretorio, "Ficha.pdf");

        using (var documento = new PdfDocument())
        {
            var pagina = documento.AddPage();
            pagina.Width = XUnit.FromPoint(300);
            pagina.Height = XUnit.FromPoint(Altura);

            using (var grafico = XGraphics.FromPdfPage(pagina))
            {
                var fonte = new XFont("Arial", 9);

                for (var i = 0; i < linhas.Length; i++)
                {
                    // XGraphics conta do topo; a linha de base em PDF fica 2 pt acima do widget.
                    grafico.DrawString(
                        linhas[i].Rotulo, fonte, XBrushes.Black,
                        new XPoint(20, Altura - BaseDaLinha(i) - 2));
                }
            }

            var anotacoes = new PdfArray(documento);
            var campos = new PdfArray(documento);

            for (var i = 0; i < linhas.Length; i++)
            {
                var widget = new PdfDictionary(documento);
                documento.Internals.AddObject(widget);

                widget.Elements.SetName("/Type", "/Annot");
                widget.Elements.SetName("/Subtype", "/Widget");
                widget.Elements.SetName("/FT", "/Tx");
                widget.Elements.SetString("/T", linhas[i].Campo);
                widget.Elements.SetString("/DA", "/Helv 9 Tf 0 g");
                widget.Elements.SetInteger("/F", 4);
                widget.Elements.SetRectangle(
                    "/Rect",
                    new PdfRectangle(
                        new XPoint(70, BaseDaLinha(i)),
                        new XPoint(110, BaseDaLinha(i) + 10)));

                anotacoes.Elements.Add(widget.Reference!);
                campos.Elements.Add(widget.Reference!);
            }

            pagina.Elements.SetObject("/Annots", anotacoes);

            var formulario = new PdfDictionary(documento);
            documento.Internals.AddObject(formulario);
            formulario.Elements.SetObject("/Fields", campos);
            formulario.Elements.SetString("/DA", "/Helv 9 Tf 0 g");

            documento.Internals.Catalog.Elements.SetReference("/AcroForm", formulario);

            documento.Save(caminho);
        }

        return caminho;
    }

    [Fact]
    public void Ler_PareiaCadaCampoComORotuloDaMesmaLinha()
    {
        // Os nomes estão de propósito na ordem trocada em relação aos rótulos: é o defeito real.
        var caminho = CriarFicha(
            new Linha("Acrobacia", "Acrobatics"),
            new Linha("Arcanismo", "Animal"),
            new Linha("Atletismo", "Arcana"));

        var campos = LayoutDaFicha.Ler(caminho);

        Assert.Equal("Acrobacia", Rotulo(campos, "Acrobatics"));
        Assert.Equal("Arcanismo", Rotulo(campos, "Animal"));
        Assert.Equal("Atletismo", Rotulo(campos, "Arcana"));
    }

    /// <summary>
    /// A distância vai junto do rótulo justamente para que um pareamento frouxo dê para
    /// desconfiar. Num rótulo colado no campo ela é quase zero.
    /// </summary>
    [Fact]
    public void Ler_DizDeQueLadoEstaORotuloEQuaoPertoEle()
    {
        var caminho = CriarFicha(new Linha("Acrobacia", "Acrobatics"));

        var campo = LayoutDaFicha.Ler(caminho).Single();

        Assert.Equal(DirecaoDoRotulo.Esquerda, campo.Direcao);
        Assert.InRange(campo.DistanciaDoRotulo, 0, 20);
    }

    /// <summary>
    /// Campos que dividem a linha são a mesma entrada da ficha — a caixa de marcação e o valor
    /// da mesma perícia, por exemplo. Sem isso, o mapeamento perde de vista o que anda junto.
    /// </summary>
    [Fact]
    public void Ler_NumeraAsLinhasDeCimaParaBaixo()
    {
        var caminho = CriarFicha(
            new Linha("Acrobacia", "Acrobatics"),
            new Linha("Arcanismo", "Animal"));

        var campos = LayoutDaFicha.Ler(caminho);

        Assert.Equal(1, campos.Single(campo => campo.Nome == "Acrobatics").Linha);
        Assert.Equal(2, campos.Single(campo => campo.Nome == "Animal").Linha);
    }

    /// <summary>
    /// A ordem devolvida é a impressa, e não a do AcroForm nem a alfabética: é ela que o
    /// Ficha-Mapeamento.md segue, e é o que permite conferir a tabela contra a ficha.
    /// </summary>
    [Fact]
    public void Ler_DevolveOsCamposNaOrdemImpressa()
    {
        var caminho = CriarFicha(
            new Linha("Primeira", "zzz"),
            new Linha("Segunda", "aaa"),
            new Linha("Terceira", "mmm"));

        var campos = LayoutDaFicha.Ler(caminho);

        Assert.Equal(["zzz", "aaa", "mmm"], campos.Select(campo => campo.Nome));
        Assert.Equal([1, 2, 3], campos.Select(campo => campo.Ordem));
        Assert.All(campos, campo => Assert.Equal(1, campo.Pagina));
    }

    [Fact]
    public void Ler_FichaSemFormulario_LancaErroDeFerramenta()
    {
        var caminho = Path.Combine(_diretorio, "SemFormulario.pdf");

        using (var documento = new PdfDocument())
        {
            documento.AddPage();
            documento.Save(caminho);
        }

        Assert.Throws<ErroDeFerramenta>(() => LayoutDaFicha.Ler(caminho));
    }

    private static string? Rotulo(IReadOnlyList<CampoDaFicha> campos, string nome) =>
        campos.Single(campo => campo.Nome == nome).Rotulo;
}
