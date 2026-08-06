using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// O dossiê é o que faz uma criação interrompida ser retomável. Os testes cobrem o ciclo
/// inteiro — nasce em desenvolvimento, o agente grava o estado, a ficha em PDF fecha a criação —
/// e as duas coisas que não podem acontecer nunca: dois personagens de mesmo nome se
/// sobrescreverem, e um caminho vindo do modelo escrever fora de <c>Personagens/</c>.
/// </summary>
public sealed class RepositorioDePersonagensTestes : IDisposable
{
    private const string Sistema = "Aventura&Cia";

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "mainforge-personagens-" + Guid.NewGuid());
    private readonly CaminhosDoProjeto _caminhos;

    public RepositorioDePersonagensTestes() => _caminhos = new CaminhosDoProjeto(_raiz);

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    private Personagem Criar(string nome = "Thoradin") =>
        RepositorioDePersonagens.Criar(_caminhos, Sistema, nome, ["base"]);

    [Fact]
    public void Criar_NasceEmDesenvolvimentoEApareceNaLista()
    {
        var personagem = Criar();

        Assert.Equal(StatusDoPersonagem.Desenvolvendo, personagem.Status);
        Assert.Equal("Thoradin", personagem.Id);

        var listado = Assert.Single(RepositorioDePersonagens.Listar(_caminhos));
        Assert.Equal("Thoradin", listado.Rotulo);
        Assert.Equal(Sistema, listado.Sistema);
    }

    /// <summary>
    /// Dois personagens de mesmo nome são dois personagens. Sobrescrever o primeiro apagaria uma
    /// criação inteira sem avisar ninguém.
    /// </summary>
    [Fact]
    public void Criar_ComNomeRepetido_NaoSobrescreveOAnterior()
    {
        Criar();
        var segundo = Criar();

        Assert.Equal("Thoradin-2", segundo.Id);
        Assert.Equal(2, RepositorioDePersonagens.Listar(_caminhos).Count);
    }

    /// <summary>Nome de personagem é texto livre — "Aza'thor, o Bravo" não pode derrubar a criação.</summary>
    [Fact]
    public void Criar_ComNomeQueNaoServeDePasta_Higieniza()
    {
        var personagem = RepositorioDePersonagens.Criar(_caminhos, Sistema, "Aza/thor: o Bravo", ["base"]);

        Assert.DoesNotContain('/', personagem.Id);
        Assert.DoesNotContain(':', personagem.Id);
        Assert.NotNull(RepositorioDePersonagens.Carregar(_caminhos, Sistema, personagem.Id));
    }

    [Fact]
    public void Registrar_GravaOEstadoQueOAgenteEscreveu()
    {
        var personagem = Criar();

        RepositorioDePersonagens.Registrar(
            _caminhos,
            Sistema,
            personagem.Id,
            nome: "Thoradin Barba-de-Ferro",
            resumo: "Anao guerreiro de nivel 1",
            fichaEmTexto: "# Thoradin\n\nForca 16.",
            campos: new Dictionary<string, string> { ["Nome"] = "Thoradin" });

        var lido = RepositorioDePersonagens.Carregar(_caminhos, Sistema, personagem.Id)!;

        Assert.Equal("Thoradin Barba-de-Ferro", lido.Nome);
        Assert.Equal("Anao guerreiro de nivel 1", lido.Resumo);
        Assert.Equal("Thoradin", lido.Campos["Nome"]);
        Assert.Contains("Forca 16", RepositorioDePersonagens.LerFichaEmTexto(_caminhos, Sistema, personagem.Id));

        // Continua em desenvolvimento: o que fecha a criação é a ficha em PDF, não a gravação.
        Assert.Equal(StatusDoPersonagem.Desenvolvendo, lido.Status);
    }

    [Fact]
    public void Registrar_PersonagemInexistente_ExplicaEmVezDeCriarUmSolto()
    {
        var erro = Assert.Throws<ErroDeFerramenta>(() =>
            RepositorioDePersonagens.Registrar(_caminhos, Sistema, "Ninguem", null, null, "texto", null));

        Assert.Contains("Ninguem", erro.Message);
    }

    /// <summary>
    /// Descontinuado é decisão do usuário: o agente não pode contornar isso gravando por cima e
    /// ressuscitando o personagem no meio de uma conversa.
    /// </summary>
    [Fact]
    public void Registrar_PersonagemDescontinuado_ERecusado()
    {
        var personagem = Criar();
        RepositorioDePersonagens.MudarStatus(_caminhos, personagem, StatusDoPersonagem.Descontinuado);

        Assert.Throws<ErroDeFerramenta>(() =>
            RepositorioDePersonagens.Registrar(_caminhos, Sistema, personagem.Id, null, null, "texto", null));
    }

    [Fact]
    public void RegistrarFichaGerada_FechaACriacaoELigaOPdf()
    {
        var personagem = Criar();

        RepositorioDePersonagens.RegistrarFichaGerada(
            _caminhos,
            Sistema,
            personagem.Id,
            @"Output\Personagens\Thoradin.pdf",
            new Dictionary<string, string> { ["Nome"] = "Thoradin" });

        var lido = RepositorioDePersonagens.Carregar(_caminhos, Sistema, personagem.Id)!;

        Assert.Equal(StatusDoPersonagem.Concluido, lido.Status);
        Assert.Equal("Output/Personagens/Thoradin.pdf", lido.FichaGerada);
        Assert.Contains(lido.Historico, anotacao => anotacao.Texto.Contains("Ficha gerada"));
    }

    [Fact]
    public void MudarStatus_RegistraATransicaoNoHistorico()
    {
        var personagem = Criar();

        RepositorioDePersonagens.MudarStatus(_caminhos, personagem, StatusDoPersonagem.Descontinuado);

        var lido = RepositorioDePersonagens.Carregar(_caminhos, Sistema, personagem.Id)!;

        Assert.Equal(StatusDoPersonagem.Descontinuado, lido.Status);
        Assert.Contains(lido.Historico, anotacao => anotacao.Texto.Contains("descontinuado"));
    }

    [Fact]
    public void RegistrarSessao_GuardaAConversaQueRetomaOPersonagem()
    {
        var personagem = Criar();

        RepositorioDePersonagens.RegistrarSessao(_caminhos, personagem, "sessao-123");

        Assert.Equal("sessao-123", RepositorioDePersonagens.Carregar(_caminhos, Sistema, personagem.Id)!.IdDaSessao);
    }

    /// <summary>
    /// O identificador do personagem chega pelo modelo, então é caminho não confiável como
    /// qualquer outro.
    /// </summary>
    [Fact]
    public void Registrar_ComIdentificadorQueEscapaDaPasta_ERecusado()
    {
        Assert.Throws<UnauthorizedAccessException>(() =>
            RepositorioDePersonagens.Registrar(_caminhos, Sistema, "../../Windows", null, null, "texto", null));
    }

    /// <summary>
    /// Um JSON corrompido tira um personagem da lista; derrubar o aplicativo ao abrir o menu
    /// tiraria todos.
    /// </summary>
    [Fact]
    public void Carregar_ComArquivoCorrompido_DevolveNuloEmVezDeExplodir()
    {
        var personagem = Criar();
        var caminho = Path.Combine(
            RepositorioDePersonagens.DiretorioDoPersonagem(_caminhos, Sistema, personagem.Id),
            RepositorioDePersonagens.NomeDoArquivo);

        File.WriteAllText(caminho, "{ isto não é json");

        Assert.Null(RepositorioDePersonagens.Carregar(_caminhos, Sistema, personagem.Id));
        Assert.Empty(RepositorioDePersonagens.Listar(_caminhos));
    }
}
