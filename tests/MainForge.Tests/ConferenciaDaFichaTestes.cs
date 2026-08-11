using System.Runtime.CompilerServices;
using MainForge.Core;
using MainForge.Tools;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace MainForge.Tests;

/// <summary>
/// A conferência existe para pegar, por máquina, o defeito que só aparecia abrindo o PDF: o
/// desenho em texto dizendo que um campo é de uma linha e o PDF imprimindo-o em outra. Estes
/// testes montam fichas em que <b>nome e rótulo discordam de propósito</b> — a mesma armadilha da
/// ficha de D&amp;D 5e em português — e conferem que a resolução por duas chaves a encontra.
/// </summary>
public class ConferenciaDaFichaTestes : IDisposable
{
    private readonly string _raiz;
    private readonly CaminhosDoProjeto _caminhos;

    private const string Sistema = "Aventura&Cia";
    private const double Altura = 400;

    public ConferenciaDaFichaTestes()
    {
        // Desenhar texto exige fonte, e quem registra o resolvedor do projeto é o construtor
        // estático das classes que abrem AcroForm. Sem isto o teste dependeria da ordem de
        // execução para passar.
        RuntimeHelpers.RunClassConstructor(typeof(LayoutDaFicha).TypeHandle);

        _raiz = Path.Combine(Path.GetTempPath(), "mainforge-conferencia-" + Guid.NewGuid());
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

    /// <summary>
    /// Monta a ficha em branco do sistema: uma linha por item, com o rótulo impresso à direita do
    /// campo daquela linha.
    ///
    /// <para>O AcroForm é montado a mão porque o PDFsharp 6.2 não tem API pública para criar campo
    /// de formulário — só para ler e preencher.</para>
    /// </summary>
    private void CriarFicha(params (string Rotulo, string Campo)[] linhas)
    {
        var diretorio = Path.Combine(_caminhos.Modelos, Sistema);
        Directory.CreateDirectory(diretorio);

        using var documento = new PdfDocument();
        var pagina = documento.AddPage();
        pagina.Width = XUnit.FromPoint(300);
        pagina.Height = XUnit.FromPoint(Altura);

        using (var grafico = XGraphics.FromPdfPage(pagina))
        {
            var fonte = new XFont("Arial", 9);

            for (var i = 0; i < linhas.Length; i++)
            {
                // Colado à direita do campo: é o que RotuloConfiavel exige para julgar.
                grafico.DrawString(
                    linhas[i].Rotulo, fonte, XBrushes.Black, new XPoint(64, Altura - Base(i) - 2));
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
                new PdfRectangle(new XPoint(20, Base(i)), new XPoint(60, Base(i) + 10)));

            anotacoes.Elements.Add(widget.Reference!);
            campos.Elements.Add(widget.Reference!);
        }

        pagina.Elements.SetObject("/Annots", anotacoes);

        var formulario = new PdfDictionary(documento);
        documento.Internals.AddObject(formulario);
        formulario.Elements.SetObject("/Fields", campos);
        formulario.Elements.SetString("/DA", "/Helv 9 Tf 0 g");
        documento.Internals.Catalog.Elements.SetReference("/AcroForm", formulario);

        documento.Save(Path.Combine(diretorio, "Ficha.pdf"));
    }

    private static double Base(int indice) => Altura - 40 - (indice * 20);

    /// <summary>A ficha das armadilhas: o nome de cada campo é o da perícia da linha seguinte.</summary>
    private void CriarFichaTrocada() =>
        CriarFicha(("Arcanismo (Int)", "Animal"), ("Atletismo (For)", "Arcana"));

    private void CriarModelo(string desenho)
    {
        var diretorio = CaminhosDoProjeto.ResolverDentroDe(_caminhos.Conhecimento, Sistema);
        Directory.CreateDirectory(diretorio);

        File.WriteAllText(
            Path.Combine(diretorio, SistemaRpg.NomeDoModeloEmTexto),
            $"# Modelo\n\n```\n{desenho}\n```\n");
    }

    private IReadOnlyList<DivergenciaDaFicha> Divergencias() =>
        ConferenciaDaFicha.Conferir(_caminhos, Sistema).Divergencias;

    /// <summary>
    /// O modelo certo é o que segue a linha impressa, mesmo que o nome do campo diga outra coisa.
    /// </summary>
    [Fact]
    public void Conferir_ModeloQueSegueALinhaImpressa_NaoAcusaNada()
    {
        CriarFichaTrocada();
        CriarModelo(
            """
            {{Animal}}  Arcanismo (Int)
            {{Arcana}}  Atletismo (For)
            """);

        Assert.Empty(Divergencias());
    }

    /// <summary>
    /// O erro real: alguém pareia pelo nome do campo, e o valor de Arcanismo vai para a linha de
    /// Atletismo. Com as duas chaves, a conferência não só acusa — ela diz quem pertence à linha.
    /// </summary>
    [Fact]
    public void Conferir_ModeloPareadoPeloNomeDoCampo_AcusaEDizQualCampoPertenceALinha()
    {
        CriarFichaTrocada();
        CriarModelo(
            """
            {{Arcana}}  Arcanismo (Int)
            {{Animal}}  Atletismo (For)
            """);

        var divergencias = Divergencias();

        Assert.Equal(2, divergencias.Count);
        Assert.All(divergencias, d => Assert.Equal(TipoDeDivergencia.CampoDeOutraLinha, d.Tipo));

        var arcana = divergencias.Single(d => d.Campo == "Arcana");
        Assert.Equal("Atletismo (For)", arcana.RotuloNaFicha);
        Assert.Contains("Arcanismo", arcana.RotuloNoModelo);
        Assert.Equal("Animal", arcana.CampoEsperado);
    }

    /// <summary>
    /// Metade das perícias tem "(Int)" no rótulo. Se bastasse dividir uma palavra com a linha,
    /// todas empatariam e a conferência não saberia sugerir nada — o desempate é por quantas
    /// palavras cada campo divide com ela.
    /// </summary>
    [Fact]
    public void Conferir_VariosCamposComOMesmoSufixo_SugereOQueMaisCasa()
    {
        CriarFicha(
            ("Arcanismo (Int)", "Animal"),
            ("História (Int)", "Insight"),
            ("Atletismo (For)", "Arcana"));

        CriarModelo(
            """
            {{Arcana}}  Arcanismo (Int)
            {{Insight}}  História (Int)
            {{Animal}}  Atletismo (For)
            """);

        var arcana = Assert.Single(Divergencias(), d => d.Campo == "Arcana");

        Assert.Equal(TipoDeDivergencia.CampoDeOutraLinha, arcana.Tipo);
        Assert.Equal("Animal", arcana.CampoEsperado);
    }

    [Fact]
    public void Conferir_CampoQueNaoExisteNaFicha_EAcusado()
    {
        CriarFichaTrocada();
        CriarModelo(
            """
            {{Animal}}  Arcanismo (Int)
            {{Inventado}}  Atletismo (For)
            """);

        var divergencia = Assert.Single(Divergencias());

        Assert.Equal("Inventado", divergencia.Campo);
        Assert.Equal(TipoDeDivergencia.CampoInexistente, divergencia.Tipo);
    }

    /// <summary>
    /// Duas linhas apontando para o mesmo campo é o rastro de quem pareou pelo nome: uma perícia
    /// rouba o campo da outra. Sem acusar isso, a linha errada fica escondida atrás da certa.
    /// </summary>
    [Fact]
    public void Conferir_CampoUsadoEmDuasLinhas_EAcusado()
    {
        CriarFichaTrocada();
        CriarModelo(
            """
            {{Animal}}  Arcanismo (Int)
            {{Animal}}  Atletismo (For)
            """);

        var divergencia = Assert.Single(Divergencias());

        Assert.Equal("Animal", divergencia.Campo);
        Assert.Equal(TipoDeDivergencia.CampoRepetido, divergencia.Tipo);
    }

    /// <summary>
    /// Linha que o desenho inventou: nem o campo nomeado está ali, nem existe campo com aquele
    /// rótulo. É incongruência entre os dois arquivos, e não troca de campo entre linhas.
    /// </summary>
    [Fact]
    public void Conferir_LinhaQueNaoExisteNaFicha_EAcusadaComoSemCorrespondencia()
    {
        CriarFichaTrocada();
        CriarModelo(
            """
            {{Animal}}  Pilotagem (Des)
            {{Arcana}}  Atletismo (For)
            """);

        var divergencia = Assert.Single(Divergencias(), d => d.Campo == "Animal");

        Assert.Equal(TipoDeDivergencia.LinhaSemCorrespondencia, divergencia.Tipo);
        Assert.Equal("Arcanismo (Int)", divergencia.RotuloNaFicha);
    }

    /// <summary>
    /// Conferência que dá alarme falso é desligada, e aí não confere mais nada: linha sem texto
    /// nenhum ao lado do campo não é reprovada, é deixada para o olho humano.
    /// </summary>
    [Fact]
    public void Conferir_LinhaSemRotuloNoDesenho_NaoAcusaNada()
    {
        CriarFichaTrocada();
        CriarModelo(
            """
            {{Animal}}
            {{Arcana}}
            """);

        Assert.Empty(Divergencias());
    }

    [Fact]
    public void Conferir_SistemaSemModeloEmTexto_LancaErroDeFerramenta()
    {
        CriarFichaTrocada();

        Assert.Throws<ErroDeFerramenta>(() => ConferenciaDaFicha.Conferir(_caminhos, Sistema));
    }

    /// <summary>Uma ficha oficial em inglês, com rótulos suficientes para o idioma ser detectável.</summary>
    private void CriarFichaEmIngles() =>
        CriarFicha(
            ("Strength", "F1"), ("Dexterity", "F2"), ("Constitution", "F3"),
            ("Intelligence", "F4"), ("Wisdom", "F5"), ("Charisma", "F6"),
            ("Speed", "F7"), ("Level", "F8"), ("Skills", "F9"));

    /// <summary>
    /// A regra do projeto: a base de conhecimento acompanha o idioma da ficha, e o usuário precisa
    /// ser avisado quando a ficha não está em português.
    /// </summary>
    [Fact]
    public void Conferir_FichaEmIngles_AvisaOIdiomaEmPortugues()
    {
        CriarFichaEmIngles();
        CriarModelo(
            """
            {{F1}}  Strength
            {{F2}}  Dexterity
            {{F3}}  Constitution
            {{F4}}  Intelligence
            {{F5}}  Wisdom
            {{F6}}  Charisma
            {{F7}}  Speed
            {{F8}}  Level
            {{F9}}  Skills
            """);

        var resultado = ConferenciaDaFicha.Conferir(_caminhos, Sistema);

        Assert.Equal(IdiomaDetectado.Ingles, resultado.IdiomaDaFichaEmBranco);
        Assert.Empty(resultado.Divergencias);

        // O aviso vai ao usuário, e a conversa com ele é sempre em português.
        Assert.Contains(resultado.Avisos, aviso => aviso.Contains("inglês") && aviso.Contains("português"));
    }

    /// <summary>
    /// Ficha num idioma e modelo em outro: nenhum rótulo casa. Acusar linha por linha esconderia a
    /// causa dentro do sintoma, então sai um aviso só — e nenhuma divergência de linha.
    /// </summary>
    [Fact]
    public void Conferir_FichaEModeloEmIdiomasDiferentes_AvisaEmVezDeAcusarCadaLinha()
    {
        CriarFichaEmIngles();
        CriarModelo(
            """
            {{F1}}  Força
            {{F2}}  Destreza
            {{F3}}  Constituição
            {{F4}}  Inteligência
            {{F5}}  Sabedoria
            {{F6}}  Carisma
            {{F7}}  Deslocamento
            {{F8}}  Nível
            {{F9}}  Perícias
            """);

        var resultado = ConferenciaDaFicha.Conferir(_caminhos, Sistema);

        Assert.Empty(resultado.Divergencias);
        Assert.Contains(resultado.Avisos, aviso => aviso.Contains("idiomas diferentes")
            || aviso.Contains(SistemaRpg.NomeDoModeloEmTexto));
    }
}
