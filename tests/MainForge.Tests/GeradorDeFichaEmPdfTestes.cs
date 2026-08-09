using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// A ficha em PDF que o aplicativo refaz sozinho, a partir do que o dossiê já guarda.
///
/// <para>O que estes testes protegem é a <b>diferença entre conferir e refazer</b>. Regerar um
/// PDF que já está lá custa nada em dinheiro e muito em confiança: a cópia guardada no histórico
/// daquele nível é substituída, e o arquivo que o usuário tinha aberto na mesa muda de baixo
/// dele. Não regerar quando ele sumiu é o oposto — deixa o personagem sem a única coisa que ele
/// existe para produzir, com todos os valores dele já gravados no disco.</para>
/// </summary>
public class GeradorDeFichaEmPdfTestes : IDisposable
{
    private const string Sistema = "Aventura&Cia";

    private static readonly string CaminhoFichaFixture =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "FichaTeste.pdf");

    private readonly string _raiz;
    private readonly CaminhosDoProjeto _caminhos;

    public GeradorDeFichaEmPdfTestes()
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

    /// <summary>
    /// O caso que motiva a classe: o dossiê está inteiro e o PDF não está em <c>Output/</c>.
    /// Antes disto, a única forma de tê-lo de volta era pagar uma conversa.
    /// </summary>
    [Fact]
    public void Garantir_SemPdfEmOutput_GeraEAponta()
    {
        PrepararTemplate();
        var personagem = Personagem(("Nome", "Thoradin"));

        var resultado = GeradorDeFichaEmPdf.Garantir(_caminhos, personagem);

        Assert.Equal(SituacaoDaFichaEmPdf.Gerada, resultado.Situacao);
        Assert.True(File.Exists(Path.Combine(_raiz, resultado.Caminho!)));

        var salvo = RepositorioDePersonagens.Carregar(_caminhos, Sistema, personagem.Id)!;
        Assert.Equal(resultado.Caminho, salvo.FichaGerada);
        Assert.Equal(StatusDoPersonagem.Concluido, salvo.Status);
        Assert.NotNull(salvo.FichaGeradaEm);
    }

    /// <summary>
    /// A ficha gerada é a ficha em branco do sistema preenchida com o que o dossiê guarda — ela
    /// precisa poder ser lida de volta, que é do que dependem a evolução e a exportação.
    /// </summary>
    [Fact]
    public void Garantir_AFichaGerada_TemOsValoresDoDossie()
    {
        PrepararTemplate();
        var personagem = Personagem(("Nome", "Thoradin"));

        var resultado = GeradorDeFichaEmPdf.Garantir(_caminhos, personagem);

        var lida = LeitorDeFichaPreenchida.Ler(Path.Combine(_raiz, resultado.Caminho!));
        Assert.Equal("Thoradin", lida.Valores["Nome"]);
    }

    /// <summary>
    /// Com o PDF no lugar, ver a ficha não pode reescrevê-lo: isso substituiria a cópia daquele
    /// nível no histórico e trocaria, sem aviso, o arquivo que o usuário já tem em mãos.
    /// </summary>
    [Fact]
    public void Garantir_ComPdfEmOutput_NaoRegeraNada()
    {
        PrepararTemplate();
        var personagem = Personagem(("Nome", "Thoradin"));

        var primeira = GeradorDeFichaEmPdf.Garantir(_caminhos, personagem);
        var arquivo = Path.Combine(_raiz, primeira.Caminho!);
        var escritoEm = File.GetLastWriteTimeUtc(arquivo);

        var segunda = GeradorDeFichaEmPdf.Garantir(
            _caminhos,
            RepositorioDePersonagens.Carregar(_caminhos, Sistema, personagem.Id)!);

        Assert.Equal(SituacaoDaFichaEmPdf.JaExistia, segunda.Situacao);
        Assert.Equal(primeira.Caminho, segunda.Caminho);
        Assert.Equal(escritoEm, File.GetLastWriteTimeUtc(arquivo));
    }

    /// <summary>
    /// Apagar o PDF de <c>Output/</c> é o gesto mais comum de quem arruma a pasta. O dossiê
    /// sobrevive a ele, e é dele que o arquivo volta.
    /// </summary>
    [Fact]
    public void Garantir_PdfApagadoDeOutput_GeraDeNovo()
    {
        PrepararTemplate();
        var personagem = Personagem(("Nome", "Thoradin"));

        var primeira = GeradorDeFichaEmPdf.Garantir(_caminhos, personagem);
        File.Delete(Path.Combine(_raiz, primeira.Caminho!));

        var segunda = GeradorDeFichaEmPdf.Garantir(
            _caminhos,
            RepositorioDePersonagens.Carregar(_caminhos, Sistema, personagem.Id)!);

        Assert.Equal(SituacaoDaFichaEmPdf.Gerada, segunda.Situacao);
        Assert.True(File.Exists(Path.Combine(_raiz, segunda.Caminho!)));
    }

    /// <summary>
    /// A cópia do histórico daquele nível continua sendo uma só: refazer o PDF do nível 3 não
    /// pode somar um segundo registro do nível 3 ao dossiê.
    /// </summary>
    [Fact]
    public void Garantir_PdfApagado_NaoDuplicaOHistoricoDoNivel()
    {
        PrepararTemplate();
        var personagem = Personagem(("Nome", "Thoradin"), ("Nivel", "3"));

        var primeira = GeradorDeFichaEmPdf.Garantir(_caminhos, personagem);
        File.Delete(Path.Combine(_raiz, primeira.Caminho!));

        GeradorDeFichaEmPdf.Garantir(
            _caminhos,
            RepositorioDePersonagens.Carregar(_caminhos, Sistema, personagem.Id)!);

        var salvo = RepositorioDePersonagens.Carregar(_caminhos, Sistema, personagem.Id)!;
        Assert.Single(salvo.Fichas);
        Assert.Equal(3, salvo.Fichas[0].Nivel);
    }

    /// <summary>
    /// Um campo que o template não conhece não pode reprovar a ficha inteira — é a mesma regra
    /// que a importação já aplica, e a ficha em branco pode ser de outra edição.
    /// </summary>
    [Fact]
    public void Garantir_CampoForaDoModelo_FicaDeForaSemDerrubarAGeracao()
    {
        PrepararTemplate();
        var personagem = Personagem(("Nome", "Thoradin"), ("CampoQueNaoExiste", "x"));

        var resultado = GeradorDeFichaEmPdf.Garantir(_caminhos, personagem);

        Assert.Equal(SituacaoDaFichaEmPdf.Gerada, resultado.Situacao);
        Assert.Equal(["CampoQueNaoExiste"], resultado.CamposIgnorados);
    }

    /// <summary>
    /// O campo que não coube no PDF continua no dossiê: ele é o estado do personagem, e o
    /// template certo (o da edição dele) saberia escrevê-lo.
    /// </summary>
    [Fact]
    public void Garantir_CampoForaDoModelo_ContinuaNoDossie()
    {
        PrepararTemplate();
        var personagem = Personagem(("Nome", "Thoradin"), ("CampoQueNaoExiste", "x"));

        GeradorDeFichaEmPdf.Garantir(_caminhos, personagem);

        var salvo = RepositorioDePersonagens.Carregar(_caminhos, Sistema, personagem.Id)!;
        Assert.Equal("x", salvo.Campos["CampoQueNaoExiste"]);
    }

    /// <summary>
    /// Sem ficha em branco não há como gerar. O aplicativo diz isso e segue — quem importou um
    /// sistema por pacote não tem <c>Templates/</c> e não pode ser levado a um erro sem saída.
    /// </summary>
    [Fact]
    public void Garantir_SistemaSemFichaEmBranco_ExplicaEmVezDeExplodir()
    {
        var personagem = Personagem(("Nome", "Thoradin"));

        var resultado = GeradorDeFichaEmPdf.Garantir(_caminhos, personagem);

        Assert.Equal(SituacaoDaFichaEmPdf.NaoDeuParaGerar, resultado.Situacao);
        Assert.NotNull(resultado.Motivo);
        Assert.Equal(
            StatusDoPersonagem.Desenvolvendo,
            RepositorioDePersonagens.Carregar(_caminhos, Sistema, personagem.Id)!.Status);
    }

    /// <summary>
    /// Dossiê sem campo nenhum é o personagem concluído por uma versão do aplicativo que não os
    /// guardava. Não há o que escrever, e inventar um PDF em branco seria pior que não ter.
    /// </summary>
    [Fact]
    public void Garantir_DossieSemCampos_NaoGeraFichaEmBranco()
    {
        PrepararTemplate();
        var personagem = Personagem();

        var resultado = GeradorDeFichaEmPdf.Garantir(_caminhos, personagem);

        Assert.Equal(SituacaoDaFichaEmPdf.SemCampos, resultado.Situacao);
        Assert.False(Directory.Exists(_caminhos.SaidaPersonagens) &&
                     Directory.EnumerateFiles(_caminhos.SaidaPersonagens).Any());
    }

    /// <summary>
    /// <c>Produzir</c> é o que a importação usa: ela monta o dossiê inteiro e o salva uma vez só,
    /// então a geração não pode gravar nada por conta própria.
    /// </summary>
    [Fact]
    public void Produzir_NaoMexeNoDossie()
    {
        PrepararTemplate();
        var personagem = Personagem(("Nome", "Thoradin"));

        var resultado = GeradorDeFichaEmPdf.Produzir(_caminhos, personagem);

        Assert.Equal(SituacaoDaFichaEmPdf.Gerada, resultado.Situacao);

        var salvo = RepositorioDePersonagens.Carregar(_caminhos, Sistema, personagem.Id)!;
        Assert.Null(salvo.FichaGerada);
        Assert.Equal(StatusDoPersonagem.Desenvolvendo, salvo.Status);
    }

    private Personagem Personagem(params (string Campo, string Valor)[] campos)
    {
        var personagem = RepositorioDePersonagens.Criar(_caminhos, Sistema, "Thoradin", ["base"]);

        personagem.Campos = campos.ToDictionary(par => par.Campo, par => par.Valor, StringComparer.Ordinal);
        RepositorioDePersonagens.Salvar(_caminhos, personagem);

        return personagem;
    }

    private void PrepararTemplate()
    {
        var diretorio = Path.Combine(_caminhos.Modelos, Sistema);
        Directory.CreateDirectory(diretorio);
        File.Copy(CaminhoFichaFixture, Path.Combine(diretorio, "Ficha.pdf"), overwrite: true);
    }
}
