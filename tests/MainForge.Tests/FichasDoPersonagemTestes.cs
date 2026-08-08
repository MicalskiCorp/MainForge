using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// As duas regras de arquivo de uma ficha: <c>Output/</c> guarda <b>uma</b> por personagem — a
/// atual — e o dossiê guarda <b>uma por nível</b> concluído.
///
/// <para>Elas se sustentam uma na outra. Manter só a ficha atual em <c>Output/</c> é o que faz a
/// pasta continuar respondendo "qual é a ficha do Thoradin?" depois de dez evoluções; e isso só
/// não perde história porque cada nível concluído já foi copiado para o histórico antes de o PDF
/// ser reescrito.</para>
/// </summary>
public sealed class FichasDoPersonagemTestes : IDisposable
{
    private const string Sistema = "Aventura&Cia";

    private readonly string _raiz;
    private readonly CaminhosDoProjeto _caminhos;

    public FichasDoPersonagemTestes()
    {
        _raiz = Path.Combine(Path.GetTempPath(), "mainforge-fichas-" + Guid.NewGuid());
        _caminhos = new CaminhosDoProjeto(_raiz);
        _caminhos.GarantirEstrutura();
    }

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    [Fact]
    public void RegistrarFichaGerada_GuardaUmaCopiaNoNivelInformado()
    {
        var personagem = Criar("Thoradin");

        Gerar(personagem, nivel: 3);

        var doDisco = RepositorioDePersonagens.Carregar(_caminhos, Sistema, personagem.Id)!;
        var registro = Assert.Single(doDisco.Fichas);

        Assert.Equal(3, registro.Nivel);
        Assert.Equal($"Personagens/{Sistema}/{personagem.Id}/Fichas/nivel-03.pdf", registro.Arquivo);
        Assert.True(File.Exists(Path.Combine(_raiz, registro.Arquivo)));
    }

    /// <summary>
    /// Um registro por nível, não um por geração: corrigir a ficha do nível 3 três vezes deixa
    /// uma ficha do nível 3 — a última, que é a que ficou valendo.
    /// </summary>
    [Fact]
    public void RegistrarFichaGerada_MesmoNivelDuasVezes_SubstituiORegistro()
    {
        var personagem = Criar("Thoradin");

        Gerar(personagem, nivel: 3, valorDoNome: "Thoradin");
        Gerar(personagem, nivel: 3, valorDoNome: "Thoradin, o Bravo");

        var doDisco = RepositorioDePersonagens.Carregar(_caminhos, Sistema, personagem.Id)!;
        var registro = Assert.Single(doDisco.Fichas);

        Assert.Equal(3, registro.Nivel);
        Assert.Equal(
            "Thoradin, o Bravo",
            LerCampo(Path.Combine(_raiz, registro.Arquivo), "Nome"));
    }

    [Fact]
    public void RegistrarFichaGerada_NiveisDiferentes_SaoRegistrosDiferentesEmOrdem()
    {
        var personagem = Criar("Thoradin");

        Gerar(personagem, nivel: 2);
        Gerar(personagem, nivel: 3);
        Gerar(personagem, nivel: 1);

        var doDisco = RepositorioDePersonagens.Carregar(_caminhos, Sistema, personagem.Id)!;

        Assert.Equal([1, 2, 3], doDisco.Fichas.Select(ficha => ficha.Nivel));
        Assert.Equal(3, Directory.EnumerateFiles(FichasDoPersonagem.Diretorio(_caminhos, Sistema, personagem.Id)).Count());
    }

    /// <summary>
    /// O pedido é literal: uma ficha por personagem em <c>Output/</c>. Evoluir não pode somar um
    /// PDF à pasta — depois de três níveis, nada diria qual dos quatro arquivos é o que vale hoje.
    /// </summary>
    [Fact]
    public void RegistrarFichaGerada_DepoisDeVariosNiveis_DeixaUmPdfSoEmOutput()
    {
        var personagem = Criar("Thoradin");

        Gerar(personagem, nivel: 1);
        Gerar(personagem, nivel: 2);
        Gerar(personagem, nivel: 3);

        var naSaida = Directory.EnumerateFiles(_caminhos.SaidaPersonagens, "*.pdf").ToList();

        Assert.Equal($"{personagem.Id}.pdf", Path.GetFileName(Assert.Single(naSaida)));
    }

    /// <summary>
    /// A ficha anterior com outro nome — a cópia da importação, ou a de uma versão em que o agente
    /// escolhia o nome do arquivo — precisa sair de <c>Output/</c> quando a nova entra.
    /// </summary>
    [Fact]
    public void RegistrarFichaGerada_FichaAnteriorComOutroNome_ESubstituida()
    {
        var personagem = Criar("Thoradin");

        PreenchedorDeFicha.Preencher(_caminhos, Sistema, null, new Dictionary<string, string>(), "Thoradin-antiga.pdf");
        RepositorioDePersonagens.RegistrarFichaGerada(
            _caminhos, Sistema, personagem.Id, "Output/Personagens/Thoradin-antiga.pdf", new Dictionary<string, string>(), nivel: 1);

        Gerar(personagem, nivel: 2);

        Assert.False(File.Exists(Path.Combine(_caminhos.SaidaPersonagens, "Thoradin-antiga.pdf")));
        Assert.True(File.Exists(Path.Combine(_caminhos.SaidaPersonagens, $"{personagem.Id}.pdf")));
    }

    /// <summary>
    /// Apagar a anterior não pode virar apagar a história: o registro do nível 1 continua no
    /// dossiê depois de o PDF daquele nível sair de <c>Output/</c>.
    /// </summary>
    [Fact]
    public void RegistrarFichaGerada_SubstituirEmOutput_NaoApagaOHistorico()
    {
        var personagem = Criar("Thoradin");

        Gerar(personagem, nivel: 1);
        Gerar(personagem, nivel: 2);

        var doDisco = RepositorioDePersonagens.Carregar(_caminhos, Sistema, personagem.Id)!;

        Assert.Equal(2, doDisco.Fichas.Count);
        Assert.All(doDisco.Fichas, ficha => Assert.True(File.Exists(Path.Combine(_raiz, ficha.Arquivo))));
    }

    /// <summary>
    /// Dois personagens de mesmo nome em sistemas diferentes disputariam o mesmo arquivo em
    /// <c>Output/</c> — e o segundo apagaria a ficha do primeiro, o que só apareceria no dia em
    /// que alguém procurasse o PDF que sumiu.
    /// </summary>
    [Fact]
    public void NomeNaSaida_MesmoNomeEmOutroSistema_NaoDisputaOArquivo()
    {
        var doPrimeiro = Criar("Thoradin");
        Gerar(doPrimeiro, nivel: 1);

        var doOutroSistema = RepositorioDePersonagens.Criar(_caminhos, "OutroSistema", "Thoradin", ["base"]);

        Assert.Equal($"OutroSistema-{doOutroSistema.Id}.pdf", FichasDoPersonagem.NomeNaSaida(_caminhos, doOutroSistema));
    }

    /// <summary>
    /// Sem nível informado, o registro existe do mesmo jeito e num lugar só — senão cada geração
    /// sem nível somaria um arquivo ao histórico, que é justamente o que ele não deve virar.
    /// </summary>
    [Fact]
    public void RegistrarFichaGerada_SemNivel_GuardaNumRegistroUnico()
    {
        var personagem = Criar("Thoradin");

        Gerar(personagem, nivel: null);
        Gerar(personagem, nivel: null);

        var doDisco = RepositorioDePersonagens.Carregar(_caminhos, Sistema, personagem.Id)!;
        var registro = Assert.Single(doDisco.Fichas);

        Assert.Null(registro.Nivel);
        Assert.EndsWith("sem-nivel.pdf", registro.Arquivo);
    }

    /// <summary>
    /// Quando o agente não informa o nível, os campos da própria ficha respondem — mas só quando
    /// o nome do campo não deixa dúvida. "Nível de magia" preenchido com 1 não faz o personagem
    /// de nível 5 virar nível 1.
    /// </summary>
    [Theory]
    [InlineData("Nível", "5", 5)]
    [InlineData("Level", "5", 5)]
    [InlineData("Nivel do Personagem", "5", 5)]
    [InlineData("Nível de Magia", "1", null)]
    [InlineData("Nível", "não sei", null)]
    [InlineData("Classe", "Guerreiro", null)]
    public void DeduzirNivel_SoAceitaOCampoQueNaoDeixaDuvida(string campo, string valor, int? esperado)
    {
        Assert.Equal(esperado, FichasDoPersonagem.DeduzirNivel(new Dictionary<string, string> { [campo] = valor }));
    }

    [Fact]
    public void RegistrarFichaGerada_SemNivelInformado_UsaODaFicha()
    {
        var personagem = Criar("Thoradin");

        RepositorioDePersonagens.RegistrarFichaGerada(
            _caminhos,
            Sistema,
            personagem.Id,
            GerarPdf($"{personagem.Id}.pdf", "Thoradin"),
            new Dictionary<string, string> { ["Nome"] = "Thoradin", ["Nivel"] = "4" });

        var doDisco = RepositorioDePersonagens.Carregar(_caminhos, Sistema, personagem.Id)!;

        Assert.Equal(4, Assert.Single(doDisco.Fichas).Nivel);
    }

    private Personagem Criar(string nome)
    {
        PrepararTemplate();
        return RepositorioDePersonagens.Criar(_caminhos, Sistema, nome, [FonteDoSistema.IdDaBase]);
    }

    /// <summary>
    /// Gera a ficha como o servidor MCP a geraria: o nome do arquivo em <c>Output/</c> é do
    /// aplicativo, e o que quem chama pede é ignorado quando há personagem.
    /// </summary>
    private void Gerar(Personagem personagem, int? nivel, string valorDoNome = "Thoradin")
    {
        var gerado = GerarPdf(FichasDoPersonagem.NomeNaSaida(_caminhos, personagem), valorDoNome);

        RepositorioDePersonagens.RegistrarFichaGerada(
            _caminhos,
            Sistema,
            personagem.Id,
            gerado,
            new Dictionary<string, string> { ["Nome"] = valorDoNome },
            nivel);
    }

    private string GerarPdf(string nomeArquivo, string valorDoNome) =>
        PreenchedorDeFicha.Preencher(
            _caminhos,
            Sistema,
            null,
            new Dictionary<string, string> { ["Nome"] = valorDoNome },
            nomeArquivo);

    private static string LerCampo(string caminhoPdf, string campo)
    {
        using var documento = PdfSharp.Pdf.IO.PdfReader.Open(caminhoPdf, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        return ((PdfSharp.Pdf.AcroForms.PdfTextField)documento.AcroForm!.Fields[campo]!).Text;
    }

    private void PrepararTemplate()
    {
        var diretorio = Path.Combine(_caminhos.Modelos, Sistema);
        Directory.CreateDirectory(diretorio);

        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "FichaTeste.pdf"),
            Path.Combine(diretorio, "Ficha.pdf"),
            overwrite: true);
    }
}
