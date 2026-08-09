using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// Testa as operações que sobraram em C# depois da migração para o Claude Code: escrever a
/// base de conhecimento e manipular o AcroForm da ficha. As antigas ferramentas de leitura e
/// listagem sumiram porque agora são o <c>Read</c> e o <c>Glob</c> embutidos do Claude Code —
/// não há o que testar aqui sobre elas.
/// </summary>
public class FerramentasTestes : IDisposable
{
    private static readonly string CaminhoFichaFixture =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "FichaTeste.pdf");

    private readonly string _raiz;
    private readonly CaminhosDoProjeto _caminhos;

    public FerramentasTestes()
    {
        _raiz = Path.Combine(Path.GetTempPath(), "mainforge-tests-" + Guid.NewGuid());
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

    private string PrepararTemplate(string nomeArquivo = "Ficha.pdf")
    {
        var diretorioModelo = Path.Combine(_caminhos.Modelos, "Aventura&Cia");
        Directory.CreateDirectory(diretorioModelo);
        var destino = Path.Combine(diretorioModelo, nomeArquivo);
        File.Copy(CaminhoFichaFixture, destino);
        return destino;
    }

    [Fact]
    public async Task EscreverConhecimento_GravaOArquivoEDevolveCaminhoRelativo()
    {
        var gravado = await EscritorDeConhecimento.EscreverAsync(
            _caminhos, "Aventura&Cia", "Classes/Guerreiro.md", "# Guerreiro");

        var esperado = Path.Combine(_caminhos.Conhecimento, "Aventura&Cia", "Classes", "Guerreiro.md");
        Assert.True(File.Exists(esperado));
        Assert.Equal("# Guerreiro", await File.ReadAllTextAsync(esperado));
        Assert.Equal(Path.GetRelativePath(_raiz, esperado), gravado);
    }

    [Fact]
    public async Task EscreverConhecimento_RejeitaCaminhoQueNaoTerminaEmMd()
    {
        await Assert.ThrowsAsync<ErroDeFerramenta>(() => EscritorDeConhecimento.EscreverAsync(
            _caminhos, "Aventura&Cia", "Classes/Guerreiro.txt", "x"));
    }

    /// <summary>
    /// O confinamento de diretório é a camada do guardrail que não depende de configuração
    /// externa nenhuma — é o que garante que "o Configurador só escreve em Sistemas/" valha
    /// mesmo que uma regra de permissão do Claude Code seja afrouxada por engano.
    /// </summary>
    [Theory]
    [InlineData("../fora", "Arquivo.md")]
    [InlineData("Aventura&Cia", "../../fora.md")]
    [InlineData("Aventura&Cia", "../../../Windows/System32/fora.md")]
    public async Task EscreverConhecimento_RejeitaEscapeDoDiretorioPermitido(string sistema, string caminho)
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => EscritorDeConhecimento.EscreverAsync(
            _caminhos, sistema, caminho, "conteudo"));
    }

    [Fact]
    public void ListarCamposDaFicha_DevolveOsNomesDoAcroForm()
    {
        PrepararTemplate();

        var campos = PreenchedorDeFicha.ListarCampos(_caminhos, "Aventura&Cia", arquivoModelo: null);

        Assert.Contains("Nome", campos);
    }

    [Fact]
    public void ListarCamposDaFicha_SistemaSemTemplate_LancaErroDeFerramenta()
    {
        Assert.Throws<ErroDeFerramenta>(() =>
            PreenchedorDeFicha.ListarCampos(_caminhos, "SistemaInexistente", arquivoModelo: null));
    }

    /// <summary>
    /// Quem desenha a ficha em texto precisa saber qual campo é caixa de marcação: ali cabe um
    /// "X" ou um espaço, e não o "true" que está guardado no dossiê.
    /// </summary>
    [Fact]
    public void ListarCamposDeMarcacao_SeparaAsCaixasDosCamposDeTexto()
    {
        PrepararTemplateComCheckBox();

        var marcacoes = PreenchedorDeFicha.ListarCamposDeMarcacao(_caminhos, "Aventura&Cia");

        Assert.Contains("Check Box 12", marcacoes);
        Assert.DoesNotContain("Nome", marcacoes);
    }

    /// <summary>
    /// Sem ficha em branco não há resposta, e não haver resposta não pode derrubar a exibição da
    /// ficha: um sistema que chegou por pacote não tem <c>Templates/</c> nenhum.
    /// </summary>
    [Fact]
    public void ListarCamposDeMarcacao_SistemaSemTemplate_DevolveVazio()
    {
        Assert.Empty(PreenchedorDeFicha.ListarCamposDeMarcacao(_caminhos, "SistemaInexistente"));
    }

    /// <summary>
    /// A ficha de fixture só tem campo de texto. As fichas reais de RPG são metade caixas de
    /// marcação (proficiências, magias preparadas), então o template de teste ganha uma —
    /// montada no dicionário do PDF na mão porque o PDFsharp 6.2 lê AcroForm, mas não oferece
    /// API para criar campo.
    /// </summary>
    /// <summary>
    /// Acrescenta ao template um campo de texto de várias linhas — o tipo de campo dos traços de
    /// personalidade, ideais e história, que é onde o desenho gerado pelo PdfSharp falha.
    /// </summary>
    private string PrepararTemplateComCampoDeVariasLinhas(string nomeDoCampo = "Historia")
    {
        var caminho = PrepararTemplate();

        using (var documento = PdfSharp.Pdf.IO.PdfReader.Open(caminho, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify))
        {
            var campo = new PdfSharp.Pdf.PdfDictionary(documento);
            campo.Elements["/Type"] = new PdfSharp.Pdf.PdfName("/Annot");
            campo.Elements["/Subtype"] = new PdfSharp.Pdf.PdfName("/Widget");
            campo.Elements["/FT"] = new PdfSharp.Pdf.PdfName("/Tx");
            campo.Elements["/T"] = new PdfSharp.Pdf.PdfString(nomeDoCampo);
            campo.Elements["/DA"] = new PdfSharp.Pdf.PdfString("/Helv 10 Tf 0 g");
            campo.Elements["/Rect"] = new PdfSharp.Pdf.PdfRectangle(new PdfSharp.Drawing.XRect(10, 30, 200, 80));

            // Bit 13 de /Ff: campo de várias linhas.
            campo.Elements["/Ff"] = new PdfSharp.Pdf.PdfInteger(1 << 12);

            documento.Internals.AddObject(campo);
            var referencia = PdfSharp.Pdf.Advanced.PdfInternals.GetReference(campo)!;

            documento.AcroForm!.Elements.GetArray("/Fields")!.Elements.Add(referencia);

            var anotacoes = documento.Pages[0].Elements.GetArray("/Annots");

            if (anotacoes is null)
            {
                anotacoes = new PdfSharp.Pdf.PdfArray(documento);
                documento.Pages[0].Elements["/Annots"] = anotacoes;
            }

            anotacoes.Elements.Add(referencia);

            documento.Save(caminho);
        }

        return caminho;
    }

    private string PrepararTemplateComCheckBox(string nomeDoCampo = "Check Box 12")
    {
        var caminho = PrepararTemplate();

        using (var documento = PdfSharp.Pdf.IO.PdfReader.Open(caminho, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify))
        {
            var campo = new PdfSharp.Pdf.PdfDictionary(documento);
            campo.Elements["/Type"] = new PdfSharp.Pdf.PdfName("/Annot");
            campo.Elements["/Subtype"] = new PdfSharp.Pdf.PdfName("/Widget");
            campo.Elements["/FT"] = new PdfSharp.Pdf.PdfName("/Btn");
            campo.Elements["/T"] = new PdfSharp.Pdf.PdfString(nomeDoCampo);
            campo.Elements["/V"] = new PdfSharp.Pdf.PdfName("/Off");
            campo.Elements["/AS"] = new PdfSharp.Pdf.PdfName("/Off");
            campo.Elements["/Rect"] = new PdfSharp.Pdf.PdfRectangle(new PdfSharp.Drawing.XRect(10, 10, 12, 12));

            // O par de aparências é o que dá nome aos estados: é de "/Yes" aqui que sai o
            // CheckedName que o preenchedor aceita como valor.
            var aparencias = new PdfSharp.Pdf.PdfDictionary(documento);
            aparencias.Elements["/Yes"] = new PdfSharp.Pdf.PdfDictionary(documento);
            aparencias.Elements["/Off"] = new PdfSharp.Pdf.PdfDictionary(documento);
            var aparencia = new PdfSharp.Pdf.PdfDictionary(documento);
            aparencia.Elements["/N"] = aparencias;
            campo.Elements["/AP"] = aparencia;

            documento.Internals.AddObject(campo);
            var referencia = PdfSharp.Pdf.Advanced.PdfInternals.GetReference(campo)!;

            documento.AcroForm!.Elements.GetArray("/Fields")!.Elements.Add(referencia);

            var anotacoes = documento.Pages[0].Elements.GetArray("/Annots");

            if (anotacoes is null)
            {
                anotacoes = new PdfSharp.Pdf.PdfArray(documento);
                documento.Pages[0].Elements["/Annots"] = anotacoes;
            }

            anotacoes.Elements.Add(referencia);

            documento.Save(caminho);
        }

        return caminho;
    }

    private static bool LerMarcacao(string caminhoPdf, string nomeDoCampo)
    {
        using var documento = PdfSharp.Pdf.IO.PdfReader.Open(caminhoPdf, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        return ((PdfSharp.Pdf.AcroForms.PdfCheckBoxField)documento.AcroForm!.Fields[nomeDoCampo]!).Checked;
    }

    /// <summary>
    /// O Ficha-Mapeamento.md que o Configurador escreve descreve as caixas com o vocabulário do
    /// próprio PDF ("Yes"/"Off"), e é isso que o Dungeon Master manda de volta. Recusar esses
    /// valores reprovava a ficha inteira por diferença de grafia.
    /// </summary>
    [Theory]
    [InlineData("true", true)]
    [InlineData("Yes", true)]
    [InlineData("/Yes", true)]
    [InlineData("on", true)]
    [InlineData("Sim", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("Off", false)]
    [InlineData("No", false)]
    [InlineData("Não", false)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void PreencherFicha_CaixaDeMarcacao_AceitaOVocabularioDoPdfEODeTrueFalse(string valor, bool esperado)
    {
        PrepararTemplateComCheckBox();

        PreenchedorDeFicha.Preencher(
            _caminhos, "Aventura&Cia", null,
            new Dictionary<string, string> { ["Check Box 12"] = valor },
            "Saida.pdf");

        Assert.Equal(esperado, LerMarcacao(Path.Combine(_caminhos.SaidaPersonagens, "Saida.pdf"), "Check Box 12"));
    }

    /// <summary>
    /// A recusa continua existindo para valor sem sentido — e a mensagem precisa dizer o que
    /// vale, senão o agente só tem como adivinhar de novo.
    /// </summary>
    [Fact]
    public void PreencherFicha_CaixaDeMarcacao_ValorSemSentido_ErroDizOQueEAceito()
    {
        PrepararTemplateComCheckBox();

        var excecao = Assert.Throws<ErroDeFerramenta>(() => PreenchedorDeFicha.Preencher(
            _caminhos, "Aventura&Cia", null,
            new Dictionary<string, string> { ["Check Box 12"] = "talvez" },
            "Saida.pdf"));

        Assert.Contains("talvez", excecao.Message);
        Assert.Contains("true/false", excecao.Message);
        Assert.Contains("Yes", excecao.Message);
    }

    [Fact]
    public void PreencherFicha_PreencheCampoDeTextoESalvaEmOutput()
    {
        PrepararTemplate();

        var gerado = PreenchedorDeFicha.Preencher(
            _caminhos, "Aventura&Cia", null,
            new Dictionary<string, string> { ["Nome"] = "Thoradin" },
            "Thoradin.pdf");

        var caminhoSaida = Path.Combine(_caminhos.SaidaPersonagens, "Thoradin.pdf");
        Assert.True(File.Exists(caminhoSaida));
        Assert.Equal(Path.GetRelativePath(_raiz, caminhoSaida), gerado);

        using var documento = PdfSharp.Pdf.IO.PdfReader.Open(caminhoSaida, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        var campoNome = (PdfSharp.Pdf.AcroForms.PdfTextField)documento.AcroForm!.Fields["Nome"]!;
        Assert.Equal("Thoradin", campoNome.Text);
    }

    /// <summary>
    /// Sem esta marca, o leitor de PDF mostra o desenho que o PdfSharp gerou — que põe o valor
    /// inteiro numa linha só, cortada na borda do quadro, e ignora a cor e o corpo de letra
    /// pedidos pelo template. Era o que fazia o campo "traços de personalidade" só aparecer certo
    /// depois de alguém clicar nele e editá-lo, porque o clique força o leitor a refazer o
    /// desenho — que é justamente o que a marca pede que ele faça ao abrir o arquivo.
    /// </summary>
    [Fact]
    public void PreencherFicha_PedeAoLeitorQueRedesenheOsCampos()
    {
        PrepararTemplate();

        PreenchedorDeFicha.Preencher(
            _caminhos, "Aventura&Cia", null,
            new Dictionary<string, string> { ["Nome"] = "Thoradin" },
            "Thoradin.pdf");

        using var documento = PdfSharp.Pdf.IO.PdfReader.Open(
            Path.Combine(_caminhos.SaidaPersonagens, "Thoradin.pdf"), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);

        Assert.True(documento.AcroForm!.Elements.GetBoolean("/NeedAppearances"));
    }

    /// <summary>
    /// Num campo de várias linhas o separador vira <c>\r</c>, que é a forma que o leitor entende
    /// ao redesenhar. Num campo de uma linha, a quebra vira espaço: ela não teria como ser
    /// exibida, e deixá-la passar renderiza um caractere de controle ou corta o resto do valor.
    /// </summary>
    [Fact]
    public void PreencherFicha_QuebraDeLinha_SegueOTipoDoCampo()
    {
        PrepararTemplateComCampoDeVariasLinhas();

        PreenchedorDeFicha.Preencher(
            _caminhos, "Aventura&Cia", null,
            new Dictionary<string, string>
            {
                ["Historia"] = "Nunca me perco.\nSou obcecado por hierarquia.",
                ["Nome"] = "Thoradin\ndo Norte",
            },
            "Saida.pdf");

        using var documento = PdfSharp.Pdf.IO.PdfReader.Open(
            Path.Combine(_caminhos.SaidaPersonagens, "Saida.pdf"), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);

        var historia = ((PdfSharp.Pdf.AcroForms.PdfTextField)documento.AcroForm!.Fields["Historia"]!).Text;
        var nome = ((PdfSharp.Pdf.AcroForms.PdfTextField)documento.AcroForm.Fields["Nome"]!).Text;

        Assert.Equal("Nunca me perco.\rSou obcecado por hierarquia.", historia);
        Assert.Equal("Thoradin do Norte", nome);
    }

    /// <summary>
    /// A mensagem precisa listar os campos disponíveis: é lendo isso que o agente corrige a
    /// própria chamada sem uma nova rodada de perguntas ao usuário.
    /// </summary>
    [Fact]
    public void PreencherFicha_CampoInexistente_ErroListaOsCamposDisponiveis()
    {
        PrepararTemplate();

        var excecao = Assert.Throws<ErroDeFerramenta>(() => PreenchedorDeFicha.Preencher(
            _caminhos, "Aventura&Cia", null,
            new Dictionary<string, string> { ["CampoQueNaoExiste"] = "x" },
            "Saida.pdf"));

        Assert.Contains("CampoQueNaoExiste", excecao.Message);
        Assert.Contains("Nome", excecao.Message);
    }

    [Fact]
    public void PreencherFicha_MaisDeUmTemplate_ExigeArquivoModelo()
    {
        PrepararTemplate("FichaA.pdf");
        PrepararTemplate("FichaB.pdf");

        var excecao = Assert.Throws<ErroDeFerramenta>(() => PreenchedorDeFicha.Preencher(
            _caminhos, "Aventura&Cia", null,
            new Dictionary<string, string> { ["Nome"] = "Thoradin" },
            "Saida.pdf"));

        Assert.Contains("arquivoModelo", excecao.Message);
    }

    [Fact]
    public void PreencherFicha_RejeitaNomeDeSaidaSemExtensaoPdf()
    {
        PrepararTemplate();

        Assert.Throws<ErroDeFerramenta>(() => PreenchedorDeFicha.Preencher(
            _caminhos, "Aventura&Cia", null,
            new Dictionary<string, string> { ["Nome"] = "Thoradin" },
            "Thoradin"));
    }

    [Fact]
    public void PreencherFicha_RejeitaSaidaForaDeOutputPersonagens()
    {
        PrepararTemplate();

        Assert.Throws<UnauthorizedAccessException>(() => PreenchedorDeFicha.Preencher(
            _caminhos, "Aventura&Cia", null,
            new Dictionary<string, string> { ["Nome"] = "Thoradin" },
            "../../fora.pdf"));
    }
}
