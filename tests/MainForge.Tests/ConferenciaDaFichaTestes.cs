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

    /// <summary>
    /// As caixas de moeda da ficha de D&amp;D são rotuladas com duas letras — "PC", "PP", "PE",
    /// "PO", "PL" —, e palavra de duas letras não distingue nada: ela é descartada antes da
    /// comparação. Com o conjunto vazio de um lado, "não casa" era o resultado inevitável, e as
    /// cinco caixas eram acusadas de estar na linha errada por não haver como conferir que estavam
    /// na certa.
    ///
    /// <para>Isto só apareceu quando a régua do leiaute passou a enxergar esses campos: enquanto
    /// eles não tinham rótulo, a conferência se calava por outro motivo. Uma conferência que dá
    /// alarme falso é desligada, e aí não confere mais nada.</para>
    /// </summary>
    [Fact]
    public void Conferir_RotuloCurtoDemaisParaDistinguir_NaoAcusaNada()
    {
        CriarFicha(("PC", "CP"), ("PO", "GP"));

        CriarModelo(
            """
            {{CP}}  PC (cobre)
            {{GP}}  PO (ouro)
            """);

        Assert.Empty(Divergencias());
    }

    /// <summary>
    /// Monta a linha como a ficha de D&amp;D 5e a desenha: <c>[caixa] [valor] Rótulo</c>. O valor
    /// fica colado no texto e a caixa de marcação fica bem mais longe, do outro lado dele.
    /// </summary>
    private void CriarFichaComMarcacaoDistante(params (string Rotulo, string Marcacao, string Valor)[] linhas)
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
                grafico.DrawString(
                    linhas[i].Rotulo, fonte, XBrushes.Black, new XPoint(64, Altura - Base(i) - 2));
            }
        }

        var anotacoes = new PdfArray(documento);
        var campos = new PdfArray(documento);

        void Widget(string nome, string tipo, double esquerda, double largura, int indice)
        {
            var widget = new PdfDictionary(documento);
            documento.Internals.AddObject(widget);

            widget.Elements.SetName("/Type", "/Annot");
            widget.Elements.SetName("/Subtype", "/Widget");
            widget.Elements.SetName("/FT", tipo);
            widget.Elements.SetString("/T", nome);
            widget.Elements.SetString("/DA", "/Helv 9 Tf 0 g");
            widget.Elements.SetInteger("/F", 4);
            widget.Elements.SetRectangle(
                "/Rect",
                new PdfRectangle(
                    new XPoint(esquerda, Base(indice)),
                    new XPoint(esquerda + largura, Base(indice) + 9)));

            anotacoes.Elements.Add(widget.Reference!);
            campos.Elements.Add(widget.Reference!);
        }

        for (var i = 0; i < linhas.Length; i++)
        {
            // A caixa fica a ~24 pt do rótulo (longe demais para valer sozinha) e o valor, a ~4 pt.
            Widget(linhas[i].Marcacao, "/Btn", 20, 9, i);
            Widget(linhas[i].Valor, "/Tx", 40, 20, i);
        }

        pagina.Elements.SetObject("/Annots", anotacoes);

        var formulario = new PdfDictionary(documento);
        documento.Internals.AddObject(formulario);
        formulario.Elements.SetObject("/Fields", campos);
        formulario.Elements.SetString("/DA", "/Helv 9 Tf 0 g");
        documento.Internals.Catalog.Elements.SetReference("/AcroForm", formulario);

        documento.Save(Path.Combine(diretorio, "Ficha.pdf"));
    }

    /// <summary>
    /// A caixa de proficiência fica a 23,6 pt do rótulo na ficha de D&amp;D 5e — longe demais para
    /// o corte de confiança —, enquanto o valor da mesma linha fica a 4 pt. O valor era conferido e
    /// a caixa não, e a caixa é quem diz <b>em que o personagem é proficiente</b>: eram 24 campos
    /// mudos, 6 testes de resistência e 18 perícias.
    ///
    /// <para>Com o rótulo da linha confirmado pelo campo que o tem colado, a caixa passa a ser
    /// julgada — e um pareamento trocado nela é acusado.</para>
    /// </summary>
    [Fact]
    public void Conferir_MarcacaoDistanteTrocada_EAcusadaPeloRotuloConfirmadoDaLinha()
    {
        CriarFichaComMarcacaoDistante(
            ("Forca", "Check Box 11", "ST Strength"),
            ("Carisma", "Check Box 22", "ST Charisma"));

        CriarModelo(
            """
            [{{Check Box 22}}] {{ST Strength}}  Forca
            [{{Check Box 11}}] {{ST Charisma}}  Carisma
            """);

        var divergencias = Divergencias();

        Assert.Equal(2, divergencias.Count);
        Assert.All(divergencias, d => Assert.Equal(TipoDeDivergencia.CampoDeOutraLinha, d.Tipo));
        Assert.Contains(divergencias, d => d.Campo == "Check Box 11");
        Assert.Contains(divergencias, d => d.Campo == "Check Box 22");
    }

    /// <summary>O mesmo leiaute, pareado certo, continua passando sem ruído.</summary>
    [Fact]
    public void Conferir_MarcacaoDistanteNoLugarCerto_NaoAcusaNada()
    {
        CriarFichaComMarcacaoDistante(
            ("Forca", "Check Box 11", "ST Strength"),
            ("Carisma", "Check Box 22", "ST Charisma"));

        CriarModelo(
            """
            [{{Check Box 11}}] {{ST Strength}}  Forca
            [{{Check Box 22}}] {{ST Charisma}}  Carisma
            """);

        Assert.Empty(Divergencias());
    }

    /// <summary>A ficha de perícias de D&amp;D 5e em português: o nome do campo é sempre o de outra linha.</summary>
    private static readonly (string Rotulo, string Campo)[] PericiasTrocadas =
    [
        ("Acrobacia (Des)", "Acrobatics"),
        ("Arcanismo (Int)", "Animal"),
        ("Atletismo (For)", "Arcana"),
        ("Atuação (Car)", "Athletics"),
        ("Furtividade (Des)", "History"),
        ("História (Int)", "Insight"),
        ("Intuição (Sab)", "Investigation"),
        ("Investigação (Int)", "Medicine"),
        ("Lidar com Animais (Sab)", "Nature"),
        ("Medicina (Sab)", "Perception"),
    ];

    /// <summary>O desenho montado pelo nome dos campos: cada perícia com o campo "dela" em inglês.</summary>
    private const string DesenhoPeloNome =
        """
        {{Acrobatics}}  Acrobacia (Des)
        {{Arcana}}  Arcanismo (Int)
        {{Athletics}}  Atletismo (For)
        {{Animal}}  Atuação (Car)
        {{Nature}}  Furtividade (Des)
        {{History}}  História (Int)
        {{Insight}}  Intuição (Sab)
        {{Investigation}}  Investigação (Int)
        {{Perception}}  Lidar com Animais (Sab)
        {{Medicine}}  Medicina (Sab)
        """;

    /// <summary>
    /// O erro que a conferência existe para pegar erra quase todas as linhas — e por isso mesmo
    /// ela o aprovava: abaixo da metade de acerto, supunha "idioma diferente" e se calava. Como
    /// ficha e modelo estão no mesmo idioma, as linhas têm de ser julgadas, e a causa, avisada.
    /// </summary>
    [Fact]
    public void Conferir_ModeloQuaseTodoPareadoPeloNome_ReprovaEAvisaACausa()
    {
        CriarFicha(PericiasTrocadas);
        CriarModelo(DesenhoPeloNome);

        var resultado = ConferenciaDaFicha.Conferir(_caminhos, Sistema);

        Assert.False(resultado.Aprovada);
        Assert.Contains(resultado.Divergencias, d => d.Campo == "Arcana" && d.CampoEsperado == "Animal");
        Assert.Contains(resultado.Avisos, aviso => aviso.Contains("NOME dos campos"));
    }

    /// <summary>
    /// "Medicina (Sab)" e "Percepção (Sab)" dividem a palavra "Sab". Bastar uma palavra em comum
    /// aceitava o campo de uma perícia na linha de outra do mesmo atributo — o erro sobrevivia
    /// exatamente onde a troca era entre perícias de Sabedoria.
    /// </summary>
    [Fact]
    public void Conferir_CampoDeOutraPericiaDoMesmoAtributo_EAcusado()
    {
        CriarFicha(("Medicina (Sab)", "Perception"), ("Percepção (Sab)", "Persuasion"));
        CriarModelo(
            """
            {{Perception}}  Medicina (Sab)
            {{Perception}}  Percepção (Sab)
            """);

        Assert.Contains(Divergencias(), d => d.Tipo == TipoDeDivergencia.CampoRepetido);

        CriarModelo(
            """
            {{Persuasion}}  Medicina (Sab)
            {{Perception}}  Percepção (Sab)
            """);

        var divergencias = Divergencias();

        Assert.Equal(2, divergencias.Count);
        Assert.Contains(divergencias, d => d.Campo == "Perception" && d.CampoEsperado == "Persuasion");
    }

    /// <summary>
    /// Monta uma página só com campos de texto <b>sem rótulo</b>, em blocos de uma coluna: cada
    /// bloco é uma lista de linhas coladas, e entre um bloco e outro fica um vão — como a lista de
    /// truques e a de magias de 1º nível na ficha de D&amp;D 5e.
    /// </summary>
    private void CriarFichaDeListas(params string[][] blocos)
    {
        var diretorio = Path.Combine(_caminhos.Modelos, Sistema);
        Directory.CreateDirectory(diretorio);

        using var documento = new PdfDocument();
        var pagina = documento.AddPage();
        pagina.Width = XUnit.FromPoint(300);
        pagina.Height = XUnit.FromPoint(Altura);

        var anotacoes = new PdfArray(documento);
        var campos = new PdfArray(documento);
        var baseAtual = Altura - 40;

        foreach (var bloco in blocos)
        {
            foreach (var nome in bloco)
            {
                var widget = new PdfDictionary(documento);
                documento.Internals.AddObject(widget);

                widget.Elements.SetName("/Type", "/Annot");
                widget.Elements.SetName("/Subtype", "/Widget");
                widget.Elements.SetName("/FT", "/Tx");
                widget.Elements.SetString("/T", nome);
                widget.Elements.SetString("/DA", "/Helv 9 Tf 0 g");
                widget.Elements.SetInteger("/F", 4);
                widget.Elements.SetRectangle(
                    "/Rect",
                    new PdfRectangle(new XPoint(40, baseAtual), new XPoint(200, baseAtual + 10)));

                anotacoes.Elements.Add(widget.Reference!);
                campos.Elements.Add(widget.Reference!);
                baseAtual -= 14;
            }

            // O vão do cabeçalho do bloco seguinte ("NIVEL 1 ...").
            baseAtual -= 60;
        }

        pagina.Elements.SetObject("/Annots", anotacoes);

        var formulario = new PdfDictionary(documento);
        documento.Internals.AddObject(formulario);
        formulario.Elements.SetObject("/Fields", campos);
        formulario.Elements.SetString("/DA", "/Helv 9 Tf 0 g");
        documento.Internals.Catalog.Elements.SetReference("/AcroForm", formulario);

        documento.Save(Path.Combine(diretorio, "Ficha.pdf"));
    }

    /// <summary>
    /// O erro dos truques: a ficha numera os campos fora da ordem impressa (<c>Spells 1015</c> é a
    /// primeira linha do 1º nível), e um desenho numerado em sequência punha o segundo truque no
    /// bloco de 1º nível e deixava um buraco na lista de truques. Sem rótulo nenhum, só a coluna
    /// impressa diz qual linha é qual.
    /// </summary>
    [Fact]
    public void Conferir_ListaSemRotuloNumeradaEmSequencia_AcusaPelaColunaImpressa()
    {
        CriarFichaDeListas(
            ["Spells 1014", "Spells 1016", "Spells 1017", "Spells 1018"],
            ["Spells 1015", "Spells 1023", "Spells 1024", "Spells 1025"]);

        CriarModelo(
            """
            NIVEL 0 - TRUQUES
              {{Spells 1014}}
              {{Spells 1015}}
              {{Spells 1016}}
              {{Spells 1017}}
            NIVEL 1
              {{Spells 1018}}
              {{Spells 1023}}
              {{Spells 1024}}
              {{Spells 1025}}
            """);

        var divergencias = Divergencias();

        Assert.Contains(divergencias, d => d.Campo == "Spells 1015" && d.CampoEsperado == "Spells 1016");
        Assert.Contains(divergencias, d => d.Campo == "Spells 1018" && d.CampoEsperado == "Spells 1015");
    }

    [Fact]
    public void Conferir_ListaSemRotuloNaOrdemImpressa_NaoAcusaNada()
    {
        CriarFichaDeListas(
            ["Spells 1014", "Spells 1016", "Spells 1017", "Spells 1018"],
            ["Spells 1015", "Spells 1023", "Spells 1024", "Spells 1025"]);

        CriarModelo(
            """
            NIVEL 0 - TRUQUES
              {{Spells 1014}}
              {{Spells 1016}}
              {{Spells 1017}}
              {{Spells 1018}}
            NIVEL 1
              {{Spells 1015}}
              {{Spells 1023}}
              {{Spells 1024}}
              {{Spells 1025}}
            """);

        Assert.Empty(Divergencias());
    }

    /// <summary>
    /// Base mapeada antes da conferência existir, ou trazida de outra instalação: ninguém a
    /// reconferia, e o agente seguia o de-para errado a cada geração. A correção troca o campo
    /// de cada linha pelo que está impresso nela, no próprio arquivo, e o resultado passa.
    /// </summary>
    [Fact]
    public void Corrigir_ModeloPareadoPeloNome_FicaIgualAFichaImpressa()
    {
        CriarFicha(PericiasTrocadas);
        CriarModelo(DesenhoPeloNome);

        var correcao = ConferenciaDaFicha.Corrigir(_caminhos, Sistema);

        Assert.True(correcao.Trocados > 0);
        Assert.True(correcao.Depois.Aprovada, string.Join("\n", correcao.Depois.Divergencias.Select(d => d.Descrever())));

        var modelo = File.ReadAllText(Path.Combine(
            CaminhosDoProjeto.ResolverDentroDe(_caminhos.Conhecimento, Sistema), SistemaRpg.NomeDoModeloEmTexto));

        Assert.Contains("{{Animal}}  Arcanismo (Int)", modelo);

        // {{Perception}} virou {{Nature}}, quatro letras a menos: os espaços compensam, e o
        // rótulo continua na mesma coluna do desenho.
        var lidar = modelo.Split('\n').Single(linha => linha.Contains("Lidar com Animais"));
        Assert.StartsWith("{{Nature}}      Lidar com Animais", lidar.TrimStart());
        Assert.Equal(
            DesenhoPeloNome.Split('\n').Single(linha => linha.Contains("Lidar")).TrimStart().IndexOf("Lidar"),
            lidar.TrimStart().IndexOf("Lidar"));
    }

    [Fact]
    public void Corrigir_TruquesNumeradosEmSequencia_VoltamParaAColunaCerta()
    {
        CriarFichaDeListas(
            ["Spells 1014", "Spells 1016", "Spells 1017", "Spells 1018"],
            ["Spells 1015", "Spells 1023", "Spells 1024", "Spells 1025"]);

        CriarModelo(
            """
            NIVEL 0 - TRUQUES
              {{Spells 1014}}
              {{Spells 1015}}
              {{Spells 1016}}
              {{Spells 1017}}
            NIVEL 1
              {{Spells 1018}}
              {{Spells 1023}}
              {{Spells 1024}}
              {{Spells 1025}}
            """);

        var correcao = ConferenciaDaFicha.Corrigir(_caminhos, Sistema);

        Assert.True(correcao.Depois.Aprovada);
        Assert.Empty(Divergencias());
    }

    /// <summary>Modelo certo não é tocado: nem uma troca, nem o arquivo reescrito.</summary>
    [Fact]
    public void Corrigir_ModeloCerto_NaoMudaNada()
    {
        CriarFichaTrocada();
        CriarModelo(
            """
            {{Animal}}  Arcanismo (Int)
            {{Arcana}}  Atletismo (For)
            """);

        var caminhoDoModelo = Path.Combine(
            CaminhosDoProjeto.ResolverDentroDe(_caminhos.Conhecimento, Sistema), SistemaRpg.NomeDoModeloEmTexto);
        var antes = File.ReadAllText(caminhoDoModelo);

        var correcao = ConferenciaDaFicha.Corrigir(_caminhos, Sistema);

        Assert.Equal(0, correcao.Trocados);
        Assert.Equal(antes, File.ReadAllText(caminhoDoModelo));
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
