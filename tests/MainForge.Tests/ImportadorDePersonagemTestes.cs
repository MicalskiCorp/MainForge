using MainForge.Core;
using MainForge.Tools;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace MainForge.Tests;

/// <summary>
/// A entrada de um personagem que já existe: o usuário aponta a ficha em PDF preenchida e ela
/// vira um personagem igual aos criados aqui.
///
/// <para>O que estes testes protegem é o <b>round-trip</b>. Ler valores de um AcroForm é fácil;
/// difícil é lê-los de forma que <see cref="PreenchedorDeFicha"/> consiga reescrevê-los — e é
/// disso que dependem todas as funcionalidades prometidas ao personagem importado: evoluir,
/// corrigir e gerar a ficha de novo. Um valor que volta com o separador de linha errado, ou com
/// nome de campo que o modelo não tem, só falha na primeira geração de PDF depois de uma
/// conversa inteira já paga.</para>
/// </summary>
public class ImportadorDePersonagemTestes : IDisposable
{
    private const string Sistema = "Aventura&Cia";

    private static readonly string CaminhoFichaFixture =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "FichaTeste.pdf");

    private static readonly string CaminhoLivroFixture =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "RegrasTeste.pdf");

    private readonly string _raiz;
    private readonly string _foraDoProjeto;
    private readonly CaminhosDoProjeto _caminhos;

    public ImportadorDePersonagemTestes()
    {
        var identificador = Guid.NewGuid();
        _raiz = Path.Combine(Path.GetTempPath(), "mainforge-tests-" + identificador);
        _foraDoProjeto = Path.Combine(Path.GetTempPath(), "mainforge-externo-" + identificador);
        _caminhos = new CaminhosDoProjeto(_raiz);

        Directory.CreateDirectory(_foraDoProjeto);
    }

    public void Dispose()
    {
        foreach (var diretorio in new[] { _raiz, _foraDoProjeto })
        {
            if (Directory.Exists(diretorio))
            {
                Directory.Delete(diretorio, recursive: true);
            }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Ler_DevolveOsCamposPreenchidosESeparaOsEmBranco()
    {
        PrepararTemplate(Sistema);
        var ficha = LeitorDeFichaPreenchida.Ler(FichaPreenchidaEmDisco(new() { ["Nome"] = "Thoradin" }));

        Assert.Equal("Thoradin", ficha.Valores["Nome"]);
        Assert.DoesNotContain("Nome", ficha.CamposEmBranco);
        Assert.Equal(ficha.Valores.Count + ficha.CamposEmBranco.Count, ficha.TotalDeCampos);
    }

    /// <summary>
    /// O preenchedor grava <c>\r</c> em campo de várias linhas, porque é o separador que o leitor
    /// de PDF entende. Se a leitura o devolvesse assim, o dossiê sairia com o texto todo numa
    /// linha só e a regravação empilharia um <c>\r</c> em cima do outro.
    /// </summary>
    [Fact]
    public void Ler_CampoDeVariasLinhas_DevolveQuebraDeLinhaNormal()
    {
        PrepararTemplate(Sistema);
        AcrescentarCampoDeVariasLinhas(CaminhoDoTemplate(Sistema), "Historia");

        var ficha = LeitorDeFichaPreenchida.Ler(FichaPreenchidaEmDisco(new()
        {
            ["Nome"] = "Thoradin",
            ["Historia"] = "Nunca me perco.\nSou obcecado por hierarquia.",
        }));

        Assert.Equal("Nunca me perco.\nSou obcecado por hierarquia.", ficha.Valores["Historia"]);
    }

    /// <summary>
    /// Metade de uma ficha de RPG é caixa de marcação. Marcada precisa voltar como algo que o
    /// preenchedor aceite de volta; desmarcada é campo vazio, e não o texto "Off" — que iria
    /// parar no dossiê como se fosse um valor.
    /// </summary>
    [Theory]
    [InlineData("true", true)]
    [InlineData("", false)]
    public void Ler_CaixaDeMarcacao_MarcadaViraValorEDesmarcadaViraBranco(string preenchidoCom, bool marcada)
    {
        PrepararTemplate(Sistema);
        AcrescentarCaixaDeMarcacao(CaminhoDoTemplate(Sistema), "Escudo");

        var ficha = LeitorDeFichaPreenchida.Ler(FichaPreenchidaEmDisco(new()
        {
            ["Nome"] = "Thoradin",
            ["Escudo"] = preenchidoCom,
        }));

        if (marcada)
        {
            Assert.Equal("true", ficha.Valores["Escudo"]);
        }
        else
        {
            Assert.DoesNotContain("Escudo", ficha.Valores.Keys);
            Assert.Contains("Escudo", ficha.CamposEmBranco);
        }
    }

    /// <summary>
    /// Ficha digitalizada ou achatada é o erro mais provável desta tela, e a mensagem precisa
    /// dizer o que fazer — senão o usuário tenta o mesmo arquivo de novo.
    /// </summary>
    [Fact]
    public void Ler_PdfSemFormulario_DizQueAFichaPrecisaSerEditavel()
    {
        var excecao = Assert.Throws<InvalidOperationException>(() =>
            LeitorDeFichaPreenchida.Ler(CaminhoLivroFixture));

        Assert.Contains("AcroForm", excecao.Message);
        Assert.Contains("digitalizado", excecao.Message);
    }

    [Fact]
    public void Ler_ArquivoInexistente_LancaFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() =>
            LeitorDeFichaPreenchida.Ler(Path.Combine(_foraDoProjeto, "nao-existe.pdf")));
    }

    /// <summary>
    /// A ficha diz de que sistema ela é: os nomes dos campos são os da ficha em branco de onde
    /// ela saiu. Sem isso, a tela pediria ao usuário para adivinhar — e importar no sistema
    /// errado produz um personagem que nunca gera PDF.
    /// </summary>
    [Fact]
    public void Ranquear_PoeOSistemaDaFichaNaFrente()
    {
        PrepararTemplate(Sistema);
        PrepararTemplate("OutroSistema");
        RenomearCampo(CaminhoDoTemplate("OutroSistema"), "Nome", "CampoDeOutraEdicao");

        var ficha = LeitorDeFichaPreenchida.Ler(FichaPreenchidaEmDisco(new() { ["Nome"] = "Thoradin" }));

        var ranque = ImportadorDePersonagem.Ranquear(_caminhos, ficha);

        Assert.Equal(Sistema, ranque[0].Sistema.Id);
        Assert.True(ranque[0].Reconhecida);
        Assert.False(ranque.Single(candidato => candidato.Sistema.Id == "OutroSistema").Reconhecida);
    }

    [Fact]
    public void Importar_CriaOPersonagemConcluidoComOsValoresDaFicha()
    {
        PrepararTemplate(Sistema);
        var ficha = LeitorDeFichaPreenchida.Ler(FichaPreenchidaEmDisco(new() { ["Nome"] = "Thoradin" }));

        var resultado = ImportadorDePersonagem.Importar(
            _caminhos, new SistemaRpg(Sistema), "Thoradin", ["base"], ficha);

        var doDisco = RepositorioDePersonagens.Carregar(_caminhos, Sistema, resultado.Personagem.Id);

        Assert.NotNull(doDisco);
        Assert.Equal(StatusDoPersonagem.Concluido, doDisco.Status);
        Assert.Equal("Thoradin", doDisco.Campos["Nome"]);
        Assert.Equal(["base"], doDisco.Fontes);
        Assert.True(File.Exists(Path.Combine(_raiz, doDisco.FichaGerada!)));
    }

    /// <summary>
    /// O dossiê é o que o Dungeon Master lê na primeira conversa. Ele precisa trazer os valores
    /// e dizer, sem depender de interpretação, que nada ali foi conferido — um dossiê que se
    /// apresenta como verdade validada faz o agente construir em cima de erro sem desconfiar.
    /// </summary>
    [Fact]
    public void Importar_EscreveODossieComOsValoresEOAvisoDeQueNadaFoiConferido()
    {
        PrepararTemplate(Sistema);
        var ficha = LeitorDeFichaPreenchida.Ler(FichaPreenchidaEmDisco(new() { ["Nome"] = "Thoradin" }, "MinhaFicha.pdf"));

        var resultado = ImportadorDePersonagem.Importar(
            _caminhos, new SistemaRpg(Sistema), "Thoradin", ["base"], ficha);

        var dossie = RepositorioDePersonagens.LerFichaEmTexto(_caminhos, Sistema, resultado.Personagem.Id);

        Assert.Contains("Thoradin", dossie);
        Assert.Contains("MinhaFicha.pdf", dossie);
        Assert.Contains("conferido", dossie);
    }

    /// <summary>
    /// A promessa da importação: o personagem faz tudo que os outros fazem. A primeira coisa que
    /// uma evolução precisa é conseguir gerar a ficha de novo a partir do que foi importado.
    /// </summary>
    [Fact]
    public void Importar_OsCamposGuardadosGeramAFichaDeNovo()
    {
        PrepararTemplate(Sistema);
        var ficha = LeitorDeFichaPreenchida.Ler(FichaPreenchidaEmDisco(new() { ["Nome"] = "Thoradin" }));

        var resultado = ImportadorDePersonagem.Importar(
            _caminhos, new SistemaRpg(Sistema), "Thoradin", ["base"], ficha);

        PreenchedorDeFicha.Preencher(
            _caminhos, Sistema, null, resultado.Personagem.Campos, "Thoradin-nivel-2.pdf");

        using var documento = PdfReader.Open(
            Path.Combine(_caminhos.SaidaPersonagens, "Thoradin-nivel-2.pdf"), PdfDocumentOpenMode.Import);

        var campo = (PdfSharp.Pdf.AcroForms.PdfTextField)documento.AcroForm!.Fields["Nome"]!;
        Assert.Equal("Thoradin", campo.Text);
    }

    /// <summary>
    /// Ficha de outra edição, ou adaptada pela mesa, traz campo que a ficha em branco do sistema
    /// não tem. Ele não pode entrar em <c>Campos</c>: <c>preencher_ficha_personagem</c> recusa a
    /// geração inteira por um nome de campo desconhecido — o valor seria perdido junto.
    /// </summary>
    [Fact]
    public void Importar_CampoQueOModeloNaoTem_FicaNoDossieMasForaDaGeracao()
    {
        PrepararTemplate(Sistema);
        PrepararTemplate("EdicaoAntiga");
        RenomearCampo(CaminhoDoTemplate("EdicaoAntiga"), "Nome", "NomeDoHeroi");

        var ficha = LeitorDeFichaPreenchida.Ler(
            FichaPreenchidaEmDisco(new() { ["NomeDoHeroi"] = "Thoradin" }, sistema: "EdicaoAntiga"));

        var resultado = ImportadorDePersonagem.Importar(
            _caminhos, new SistemaRpg(Sistema), "Thoradin", ["base"], ficha);

        Assert.Empty(resultado.Personagem.Campos);
        Assert.Equal("Thoradin", resultado.CamposForaDoModelo["NomeDoHeroi"]);
        Assert.Contains(
            "NomeDoHeroi",
            RepositorioDePersonagens.LerFichaEmTexto(_caminhos, Sistema, resultado.Personagem.Id));
    }

    /// <summary>
    /// Sistema sem <c>Templates/</c> — o que chegou por pacote — importa do mesmo jeito: o que
    /// falta ali é a geração do PDF, não o personagem. Descartar os valores por não ter com que
    /// compará-los perderia a ficha inteira.
    /// </summary>
    [Fact]
    public void Importar_SistemaSemFichaEmBranco_GuardaTodosOsValores()
    {
        PrepararTemplate(Sistema);
        var ficha = LeitorDeFichaPreenchida.Ler(FichaPreenchidaEmDisco(new() { ["Nome"] = "Thoradin" }));

        var resultado = ImportadorDePersonagem.Importar(
            _caminhos, new SistemaRpg("SistemaDePacote"), "Thoradin", [], ficha);

        Assert.Equal("Thoradin", resultado.Personagem.Campos["Nome"]);
        Assert.Empty(resultado.CamposForaDoModelo);
    }

    /// <summary>
    /// A ficha que fica em <c>Output/</c> é a ficha em branco do sistema preenchida, e não o
    /// arquivo trazido: é a mesma que a primeira evolução produziria. Sem isso, a pasta de
    /// entrega tinha um PDF com uma cara na importação e outra logo depois.
    /// </summary>
    [Fact]
    public void Importar_GeraAFichaNaFichaEmBrancoDoSistema()
    {
        PrepararTemplate(Sistema);
        var ficha = LeitorDeFichaPreenchida.Ler(FichaPreenchidaEmDisco(new() { ["Nome"] = "Thoradin" }));

        var resultado = ImportadorDePersonagem.Importar(
            _caminhos, new SistemaRpg(Sistema), "Thoradin", ["base"], ficha);

        Assert.True(resultado.GeradaNoModeloDoSistema);

        var naSaida = Path.Combine(_raiz, resultado.FichaNaSaida);
        Assert.True(File.Exists(naSaida));
        Assert.Equal("Thoradin", LeitorDeFichaPreenchida.Ler(naSaida).Valores["Nome"]);
    }

    /// <summary>
    /// Sem ficha em branco não há como gerar — e a importação continua tendo de funcionar: um
    /// sistema que chegou por pacote não tem <c>Templates/</c>, e o PDF que o usuário trouxe é o
    /// que ele tem na mesa. Deixá-lo sem nada em <c>Output/</c> seria pior que entregá-lo como
    /// veio.
    /// </summary>
    [Fact]
    public void Importar_SistemaSemFichaEmBranco_CopiaOArquivoTrazido()
    {
        PrepararTemplate(Sistema);
        var trazida = FichaPreenchidaEmDisco(new() { ["Nome"] = "Thoradin" });

        var resultado = ImportadorDePersonagem.Importar(
            _caminhos, new SistemaRpg("SistemaDePacote"), "Thoradin", [], LeitorDeFichaPreenchida.Ler(trazida));

        Assert.False(resultado.GeradaNoModeloDoSistema);

        var naSaida = Path.Combine(_raiz, resultado.FichaNaSaida);
        Assert.True(File.Exists(naSaida));
        Assert.Equal(new FileInfo(trazida).Length, new FileInfo(naSaida).Length);
    }

    /// <summary>
    /// Importar duas vezes a mesma ficha são dois personagens — o mesmo que vale para dois
    /// "Thoradin" criados na mão. Uma cópia de PDF sobrescrevendo a outra apagaria a ficha do
    /// primeiro.
    /// </summary>
    [Fact]
    public void Importar_DuasVezes_NaoSobrescreveOPrimeiro()
    {
        PrepararTemplate(Sistema);
        var ficha = LeitorDeFichaPreenchida.Ler(FichaPreenchidaEmDisco(new() { ["Nome"] = "Thoradin" }));

        var primeiro = ImportadorDePersonagem.Importar(_caminhos, new SistemaRpg(Sistema), "Thoradin", ["base"], ficha);
        var segundo = ImportadorDePersonagem.Importar(_caminhos, new SistemaRpg(Sistema), "Thoradin", ["base"], ficha);

        Assert.NotEqual(primeiro.Personagem.Id, segundo.Personagem.Id);
        Assert.NotEqual(primeiro.FichaNaSaida, segundo.FichaNaSaida);
        Assert.True(File.Exists(Path.Combine(_raiz, primeiro.FichaNaSaida)));
        Assert.True(File.Exists(Path.Combine(_raiz, segundo.FichaNaSaida)));
    }

    /// <summary>
    /// Numa ficha de mesa, o campo do jogador está preenchido tanto quanto o do personagem —
    /// e importar o personagem com o nome de quem o joga é o engano que essa vizinhança produz.
    /// </summary>
    [Fact]
    public void SugerirNome_PreferOCampoDoPersonagemAoDoJogador()
    {
        var ficha = new FichaPreenchida(
            "Ficha.pdf",
            new Dictionary<string, string>
            {
                ["Jogador"] = "Ana",
                ["Nome do Personagem"] = "Thoradin",
            },
            []);

        Assert.Equal("Thoradin", ImportadorDePersonagem.SugerirNome(ficha));
    }

    [Fact]
    public void SugerirNome_SemCampoDeNome_DevolveVazio()
    {
        var ficha = new FichaPreenchida(
            "Ficha.pdf",
            new Dictionary<string, string> { ["Classe"] = "Guerreiro" },
            []);

        Assert.Equal("", ImportadorDePersonagem.SugerirNome(ficha));
    }

    private string CaminhoDoTemplate(string sistema) =>
        Path.Combine(_caminhos.Modelos, sistema, "Ficha.pdf");

    /// <summary>
    /// Um sistema como ele existe depois de importado: livro em <c>Input/</c> e ficha em branco
    /// em <c>Templates/</c>. Os dois lados importam — é <c>Input/</c> que faz o sistema aparecer
    /// na lista de candidatos, e <c>Templates/</c> que decide o quanto ele parece com a ficha
    /// trazida.
    /// </summary>
    private void PrepararTemplate(string sistema)
    {
        Directory.CreateDirectory(Path.Combine(_caminhos.Entrada, sistema, "base"));

        var diretorio = Path.Combine(_caminhos.Modelos, sistema);
        Directory.CreateDirectory(diretorio);
        File.Copy(CaminhoFichaFixture, Path.Combine(diretorio, "Ficha.pdf"), overwrite: true);
    }

    /// <summary>
    /// A ficha que o usuário traz: preenchida pelo próprio aplicativo (é o que garante que ela
    /// tem a cara de uma ficha real do sistema) e movida para <b>fora</b> da raiz do projeto —
    /// que é de onde ela vem na vida real, e o que este fluxo precisa aceitar.
    /// </summary>
    private string FichaPreenchidaEmDisco(
        Dictionary<string, string> valores,
        string nomeDoArquivo = "Ficha-do-Thoradin.pdf",
        string? sistema = null)
    {
        PreenchedorDeFicha.Preencher(_caminhos, sistema ?? Sistema, null, valores, "temporaria.pdf");

        var destino = Path.Combine(_foraDoProjeto, nomeDoArquivo);
        File.Move(Path.Combine(_caminhos.SaidaPersonagens, "temporaria.pdf"), destino, overwrite: true);

        return destino;
    }

    private static void RenomearCampo(string caminhoPdf, string de, string para)
    {
        using var documento = PdfReader.Open(caminhoPdf, PdfDocumentOpenMode.Modify);
        documento.AcroForm!.Fields[de]!.Elements["/T"] = new PdfString(para);
        documento.Save(caminhoPdf);
    }

    /// <summary>
    /// O PDFsharp 6.2 lê AcroForm mas não oferece API para criar campo, então o campo novo é
    /// montado no dicionário do PDF na mão — o mesmo caminho de <c>FerramentasTestes</c>.
    /// </summary>
    private static void AcrescentarCampoDeVariasLinhas(string caminhoPdf, string nomeDoCampo)
    {
        AcrescentarCampo(caminhoPdf, nomeDoCampo, campo =>
        {
            campo.Elements["/FT"] = new PdfName("/Tx");
            campo.Elements["/DA"] = new PdfString("/Helv 10 Tf 0 g");

            // Bit 13 de /Ff: campo de várias linhas.
            campo.Elements["/Ff"] = new PdfInteger(1 << 12);
        });
    }

    private static void AcrescentarCaixaDeMarcacao(string caminhoPdf, string nomeDoCampo)
    {
        AcrescentarCampo(caminhoPdf, nomeDoCampo, campo =>
        {
            campo.Elements["/FT"] = new PdfName("/Btn");
            campo.Elements["/V"] = new PdfName("/Off");
            campo.Elements["/AS"] = new PdfName("/Off");

            var aparencias = new PdfDictionary(campo.Owner);
            aparencias.Elements["/Yes"] = new PdfDictionary(campo.Owner);
            aparencias.Elements["/Off"] = new PdfDictionary(campo.Owner);
            var aparencia = new PdfDictionary(campo.Owner);
            aparencia.Elements["/N"] = aparencias;
            campo.Elements["/AP"] = aparencia;
        });
    }

    private static void AcrescentarCampo(string caminhoPdf, string nomeDoCampo, Action<PdfDictionary> configurar)
    {
        using var documento = PdfReader.Open(caminhoPdf, PdfDocumentOpenMode.Modify);

        var campo = new PdfDictionary(documento);
        campo.Elements["/Type"] = new PdfName("/Annot");
        campo.Elements["/Subtype"] = new PdfName("/Widget");
        campo.Elements["/T"] = new PdfString(nomeDoCampo);
        campo.Elements["/Rect"] = new PdfRectangle(new PdfSharp.Drawing.XRect(10, 30, 200, 80));

        configurar(campo);

        documento.Internals.AddObject(campo);
        var referencia = PdfSharp.Pdf.Advanced.PdfInternals.GetReference(campo)!;

        documento.AcroForm!.Elements.GetArray("/Fields")!.Elements.Add(referencia);

        var anotacoes = documento.Pages[0].Elements.GetArray("/Annots");

        if (anotacoes is null)
        {
            anotacoes = new PdfArray(documento);
            documento.Pages[0].Elements["/Annots"] = anotacoes;
        }

        anotacoes.Elements.Add(referencia);

        documento.Save(caminhoPdf);
    }
}
