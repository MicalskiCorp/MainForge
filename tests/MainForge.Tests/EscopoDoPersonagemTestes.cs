using System.Text.Json.Nodes;
using MainForge.Core;
using MainForge.Mcp;
using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// As ferramentas que gravam personagem obedecem ao escopo da conversa.
///
/// <para>É a regra 9 da estrutura do projeto aplicada onde ela faltava: a negação por caminho
/// para no agente, e o servidor MCP roda em outro processo. <c>registrar_personagem</c> grava o
/// estado <b>completo</b> do personagem — um identificador trocado não corrompe um pedaço,
/// substitui o personagem inteiro. Estes testes travam a recusa.</para>
/// </summary>
public sealed class EscopoDoPersonagemTestes : IDisposable
{
    private const string Sistema = "Aventura&Cia";
    private const string DestaConversa = "Thoradin";
    private const string DeOutraConversa = "Elowen";

    private readonly string _raiz;
    private readonly CaminhosDoProjeto _caminhos;

    public EscopoDoPersonagemTestes()
    {
        _raiz = Path.Combine(Path.GetTempPath(), "mainforge-escopo-" + Guid.NewGuid());
        _caminhos = new CaminhosDoProjeto(_raiz);
        _caminhos.GarantirEstrutura();

        // Os dois existem em disco de propósito: o que a conferência precisa impedir é gravar no
        // dossiê alheio que está lá, não errar num identificador inexistente.
        RepositorioDePersonagens.Criar(_caminhos, Sistema, DestaConversa, [FonteDoSistema.IdDaBase]);
        RepositorioDePersonagens.Criar(_caminhos, Sistema, DeOutraConversa, [FonteDoSistema.IdDaBase]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    private CatalogoDeFerramentas CatalogoDaConversa() => new(
        _caminhos,
        new EscopoDaSessao(Sistema, [FonteDoSistema.IdDaBase]) { Personagem = DestaConversa });

    private static JsonObject Argumentos(string sistema, string personagem) => new()
    {
        ["sistema"] = sistema,
        ["personagem"] = personagem,
        ["ficha"] = "# Ficha\n\nNivel 3.",
    };

    [Fact]
    public async Task RegistrarPersonagem_DoOutroPersonagem_ERecusado()
    {
        var resultado = await CatalogoDaConversa().ExecutarAsync(
            "registrar_personagem",
            Argumentos(Sistema, DeOutraConversa),
            CancellationToken.None);

        Assert.True(resultado.Erro);
        Assert.Contains(DestaConversa, resultado.Texto);
    }

    /// <summary>
    /// A recusa não pode ser só uma mensagem: o dossiê alheio precisa continuar intacto depois
    /// dela. É o dano de verdade — <c>ficha</c> é o estado completo, e gravá-lo apaga o resto.
    /// </summary>
    [Fact]
    public async Task RegistrarPersonagem_DoOutroPersonagem_NaoTocaNoDossieDele()
    {
        await CatalogoDaConversa().ExecutarAsync(
            "registrar_personagem",
            Argumentos(Sistema, DeOutraConversa),
            CancellationToken.None);

        Assert.Empty(RepositorioDePersonagens.LerFichaEmTexto(_caminhos, Sistema, DeOutraConversa));
    }

    [Fact]
    public async Task RegistrarPersonagem_DeOutroSistema_ERecusado()
    {
        var resultado = await CatalogoDaConversa().ExecutarAsync(
            "registrar_personagem",
            Argumentos("OutroSistema", DestaConversa),
            CancellationToken.None);

        Assert.True(resultado.Erro);
        Assert.Contains(Sistema, resultado.Texto);
    }

    [Fact]
    public async Task RegistrarPersonagem_DoPersonagemDaConversa_Grava()
    {
        var resultado = await CatalogoDaConversa().ExecutarAsync(
            "registrar_personagem",
            Argumentos(Sistema, DestaConversa),
            CancellationToken.None);

        Assert.False(resultado.Erro);
        Assert.NotNull(RepositorioDePersonagens.Carregar(_caminhos, Sistema, DestaConversa));
    }

    /// <summary>
    /// Maiúscula e acento não podem virar uma recusa: o identificador é nome de pasta, e a
    /// comparação em disco já ignora caixa.
    /// </summary>
    [Fact]
    public async Task RegistrarPersonagem_ComOutraCaixa_ContinuaSendoOMesmo()
    {
        var resultado = await CatalogoDaConversa().ExecutarAsync(
            "registrar_personagem",
            Argumentos(Sistema.ToUpperInvariant(), DestaConversa.ToUpperInvariant()),
            CancellationToken.None);

        Assert.False(resultado.Erro);
    }

    /// <summary>
    /// O Configurador e o servidor rodado à mão não têm mesa nenhuma. Recusar por ausência de
    /// escopo quebraria todo uso legítimo — quem quer confinar manda o escopo.
    /// </summary>
    [Fact]
    public async Task SemEscopo_QualquerPersonagemPassa()
    {
        var resultado = await new CatalogoDeFerramentas(_caminhos).ExecutarAsync(
            "registrar_personagem",
            Argumentos(Sistema, DeOutraConversa),
            CancellationToken.None);

        Assert.False(resultado.Erro);
    }

    /// <summary>
    /// Gerar a ficha de outro personagem escreveria um PDF em Output/ e ainda marcaria o dossiê
    /// alheio como concluído — a recusa precisa vir antes de o PDF existir.
    /// </summary>
    [Fact]
    public async Task PreencherFicha_DoOutroPersonagem_ERecusadaAntesDeGerarOPdf()
    {
        var argumentos = new JsonObject
        {
            ["sistema"] = Sistema,
            ["personagem"] = DeOutraConversa,
            ["campos"] = new JsonObject { ["Nome"] = "Elowen" },
            ["nomeArquivoSaida"] = "Elowen.pdf",
        };

        var resultado = await CatalogoDaConversa().ExecutarAsync(
            "preencher_ficha_personagem",
            argumentos,
            CancellationToken.None);

        Assert.True(resultado.Erro);
        Assert.False(File.Exists(Path.Combine(_caminhos.SaidaPersonagens, "Elowen.pdf")));
    }

    /// <summary>
    /// O escopo atravessa o processo pelo ambiente: se o personagem não sobreviver a essa
    /// viagem, a conferência não existe onde ela precisa existir.
    /// </summary>
    [Fact]
    public void OEscopo_LevaOPersonagemPelaSerializacao()
    {
        var original = new EscopoDaSessao(Sistema, ["base", "Compendio-Arcano"])
        {
            Personagem = DestaConversa,
        };

        var lido = EscopoDaSessao.Ler(original.Serializar());

        Assert.NotNull(lido);
        Assert.Equal(DestaConversa, lido.Personagem);
        Assert.True(lido.Permite(Sistema, "Compendio-Arcano"));
        Assert.False(lido.PermitePersonagem(DeOutraConversa));
    }

    /// <summary>
    /// Escopo gravado por uma versão anterior não tem o campo. Ele precisa continuar sendo lido
    /// como "sem limite de personagem", e não virar uma recusa de tudo.
    /// </summary>
    [Fact]
    public void EscopoSemPersonagem_NaoLimitaPersonagemNenhum()
    {
        var lido = EscopoDaSessao.Ler("""{"sistema":"Aventura&Cia","fontes":["base"]}""");

        Assert.NotNull(lido);
        Assert.Null(lido.Personagem);
        Assert.True(lido.PermitePersonagem("QualquerUm"));
    }
}
