using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// O <c>Ficha-Validacao.json</c> nasce na importação do sistema, montado do próprio AcroForm, e é
/// refinado pelo Configurador depois. Estes testes prendem o que a importação já consegue
/// responder sozinha — inclusive a pergunta que decide a folha extra de magias — e a regra do
/// escritor de conhecimento, que é o outro lado da mesma ficha de idioma.
/// </summary>
public class RegrasDaFichaTestes : IDisposable
{
    private readonly string _raiz;
    private readonly CaminhosDoProjeto _caminhos;

    private const string Sistema = "Aventura&Cia";

    public RegrasDaFichaTestes()
    {
        _raiz = Path.Combine(Path.GetTempPath(), "mainforge-regras-" + Guid.NewGuid());
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

    private static CampoDaFicha Campo(
        string nome,
        string rotulo,
        bool marcacao = false,
        bool variasLinhas = false,
        double altura = 12) =>
        new(nome, 1, 1, 1, rotulo, DirecaoDoRotulo.Direita, 2, marcacao, variasLinhas, altura);

    [Fact]
    public void Esqueleto_UmaEntradaPorCampo_SemExigenciaNenhuma()
    {
        var regras = RegrasDaFicha.Esqueleto([Campo("Nome", "Nome"), Campo("Insp", "Inspiração", marcacao: true)]);

        Assert.Equal(2, regras.Campos.Count);
        Assert.Equal(OrigemDasRegras.EstruturaDaFicha, regras.Origem);

        // A importação não leu livro nenhum: exigir qualquer coisa aqui seria chute, e chute
        // reprova personagem legítimo.
        Assert.All(regras.Campos, regra => Assert.False(regra.Obrigatorio));

        Assert.Equal(TipoDoCampo.Marcacao, regras.Regra("Insp")!.Tipo);
        Assert.Equal(TipoDoCampo.Texto, regras.Regra("Nome")!.Tipo);
    }

    /// <summary>
    /// O rótulo gravado aqui vai para a mensagem que o usuário lê, e um rótulo errado ali é pior
    /// que nenhum. Numa ficha de três colunas com o texto a 80 pt do seu campo, o rótulo da coluna
    /// seguinte fica a 20 pt e vence a disputa — sem este corte, o campo de Força se apresentaria
    /// ao usuário como "Destreza:".
    /// </summary>
    [Fact]
    public void Esqueleto_RotuloDistante_NaoEGravadoComoNomeDoCampo()
    {
        var regras = RegrasDaFicha.Esqueleto(
        [
            new CampoDaFicha("Forca", 1, 1, 1, "Destreza:", DirecaoDoRotulo.Direita, 20, false),
            new CampoDaFicha("Nome", 1, 2, 1, "Nome:", DirecaoDoRotulo.Esquerda, 3, false),
        ]);

        Assert.Equal("", regras.Regra("Forca")!.Rotulo);
        Assert.Equal("Nome:", regras.Regra("Nome")!.Rotulo);
    }

    /// <summary>Rótulo acima ou abaixo é ambíguo por construção, mesmo colado.</summary>
    [Fact]
    public void Esqueleto_RotuloAcimaOuAbaixo_NaoEGravado()
    {
        var regras = RegrasDaFicha.Esqueleto(
            [new CampoDaFicha("Forca", 1, 1, 1, "FORÇA", DirecaoDoRotulo.Acima, 2, false)]);

        Assert.Equal("", regras.Regra("Forca")!.Rotulo);
    }

    [Fact]
    public void Esqueleto_FichaSemNadaDeMagia_NaoUsaMagias()
    {
        var regras = RegrasDaFicha.Esqueleto([Campo("Nome", "Nome"), Campo("Forca", "FORÇA")]);

        Assert.False(regras.UsaMagias);
        Assert.Empty(regras.CamposDeMagia);
    }

    /// <summary>
    /// O campo de magias é reconhecido pelo rótulo impresso, e não pelo nome interno do PDF —
    /// a mesma ordem de confiança do resto do projeto, porque numa ficha traduzida os dois
    /// divergem.
    /// </summary>
    [Fact]
    public void Esqueleto_CampoRotuladoComoMagia_EReconhecidoPeloRotulo()
    {
        var regras = RegrasDaFicha.Esqueleto([Campo("Text 42", "Magias conhecidas")]);

        Assert.True(regras.UsaMagias);
        Assert.Equal(["Text 42"], regras.CamposDeMagia);
    }

    /// <summary>
    /// A pergunta que decide a folha extra: trinta linhas de uma linha cada é uma lista de nomes,
    /// e é o caso em que a folha faz falta.
    /// </summary>
    [Fact]
    public void Esqueleto_CamposDeMagiaDeUmaLinha_NaoComportamAsMagiasPorExtenso()
    {
        var regras = RegrasDaFicha.Esqueleto(
        [
            Campo("Magia1", "Magias", altura: 12),
            Campo("Magia2", "Magias", altura: 12),
        ]);

        Assert.True(regras.UsaMagias);
        Assert.False(regras.TrazMagiasPorExtenso);
    }

    [Fact]
    public void Esqueleto_QuadroGrandeDeMagias_ComportaAsMagiasPorExtenso()
    {
        var regras = RegrasDaFicha.Esqueleto(
            [Campo("Magias", "Magias conhecidas", variasLinhas: true, altura: 240)]);

        Assert.True(regras.TrazMagiasPorExtenso);
    }

    /// <summary>
    /// Campo multilinha baixo é um campo de uma linha com a marca errada — comum em ficha feita à
    /// mão. Tratá-lo como quadro de descrições deixaria o conjurador sem a folha extra.
    /// </summary>
    [Fact]
    public void Esqueleto_CampoMultilinhaMasBaixo_NaoContaComoEspacoParaDescricao()
    {
        var regras = RegrasDaFicha.Esqueleto(
            [Campo("Magias", "Magias", variasLinhas: true, altura: 14)]);

        Assert.False(regras.TrazMagiasPorExtenso);
    }

    /// <summary>
    /// Numa ficha, "magia" nomeia duas coisas que não são magia: o quadro de <b>ataques</b> (que
    /// costuma ser "ataques e magias") e o <b>cabeçalho da conjuração</b> — classe conjuradora,
    /// habilidade-chave, CD do teste. Todos casam com "spell" e nenhum é uma magia.
    ///
    /// <para>Contá-los fazia dois estragos de uma vez na ficha de D&amp;D 5e: o quadro de ataques,
    /// multilinha e alto, dava ao sistema um "espaço para as magias por extenso" que ele não tem —
    /// e com isso tirava do conjurador a folha extra —, e o cabeçalho fazia a folha listar
    /// "Sabedoria" e "Clérigo" no meio das magias do personagem.</para>
    /// </summary>
    [Fact]
    public void Esqueleto_QuadroDeAtaquesECabecalhoDeConjuracao_NaoSaoCamposDeMagia()
    {
        var regras = RegrasDaFicha.Esqueleto(
        [
            Campo("AttacksSpellcasting", "ATAQUES E MAGIAS", variasLinhas: true, altura: 114),
            Campo("Spellcasting Class 2", "CLASSE DE CONJURADOR"),
            Campo("SpellcastingAbility 2", "HABILIDADE CHAVE"),
            Campo("SpellSaveDC 2", "CD DO TR"),
            Campo("SpellAtkBonus 2", "BÔNUS DE ATAQUE"),
            Campo("Spells 1014", ""),
        ]);

        Assert.True(regras.UsaMagias);
        Assert.Equal(["Spells 1014"], regras.CamposDeMagia);

        // O quadro de ataques não vale como espaço para as descrições: é uma linha por golpe.
        Assert.False(regras.TrazMagiasPorExtenso);
    }

    [Fact]
    public void Esqueleto_FichaEmIngles_GravaOIdiomaDetectado()
    {
        var regras = RegrasDaFicha.Esqueleto(
        [
            Campo("F1", "Strength"), Campo("F2", "Dexterity"), Campo("F3", "Constitution"),
            Campo("F4", "Intelligence"), Campo("F5", "Wisdom"), Campo("F6", "Charisma"),
        ]);

        Assert.Equal(IdiomaDetectado.Ingles, regras.Idioma);
        Assert.False(regras.EmPortugues);
    }

    [Fact]
    public void GravarECarregar_PreservaAsRegras()
    {
        RegrasDaFicha.Gravar(_caminhos, Sistema, new RegrasDaFicha
        {
            Origem = OrigemDasRegras.Configurador,
            UsaMagias = true,
            CamposDeMagia = ["Magias"],
            MagiasPorExtenso = false,
            Idioma = IdiomaDetectado.Ingles,
            Campos =
            [
                new RegraDeCampo
                {
                    Campo = "Forca",
                    Rotulo = "FORÇA",
                    Tipo = TipoDoCampo.Inteiro,
                    Minimo = 3,
                    Maximo = 20,
                    Observacao = "atributo não passa de 20",
                },
            ],
        });

        var lidas = RegrasDaFicha.Carregar(_caminhos, Sistema);

        Assert.NotNull(lidas);
        Assert.Equal(OrigemDasRegras.Configurador, lidas.Origem);
        Assert.Equal(IdiomaDetectado.Ingles, lidas.Idioma);
        Assert.False(lidas.TrazMagiasPorExtenso);

        var forca = Assert.Single(lidas.Campos);
        Assert.Equal(20, forca.Maximo);
        Assert.Equal("FORÇA", forca.Rotulo);
    }

    /// <summary>
    /// Sistema mapeado por uma versão anterior não tem o arquivo, e isso não pode virar exceção no
    /// meio de uma criação de personagem: ele continua funcionando, só sem conferência.
    /// </summary>
    [Fact]
    public void Carregar_SistemaSemArquivo_DevolveNulo() =>
        Assert.Null(RegrasDaFicha.Carregar(_caminhos, Sistema));

    [Fact]
    public void Carregar_ArquivoCorrompido_DevolveNuloEmVezDeExplodir()
    {
        Directory.CreateDirectory(Path.Combine(_caminhos.Conhecimento, Sistema));
        File.WriteAllText(RegrasDaFicha.Caminho(_caminhos, Sistema), "{ isto não é json");

        Assert.Null(RegrasDaFicha.Carregar(_caminhos, Sistema));
    }

    private void GravarRegrasDoIdioma(IdiomaDetectado idioma) =>
        RegrasDaFicha.Gravar(_caminhos, Sistema, new RegrasDaFicha { Idioma = idioma });

    /// <summary>
    /// A regra do nome em inglês é uma recusa em código, e não um pedido no prompt: um pedido
    /// esquecido no meio de um processamento de duas horas só aparece meses depois, quando alguém
    /// procura "Fireball" na base e não acha.
    /// </summary>
    [Fact]
    public async Task EscreverConhecimento_MagiaSemNomeEmIngles_ERecusadaENaoGravaNada()
    {
        GravarRegrasDoIdioma(IdiomaDetectado.Ingles);

        var erro = await Assert.ThrowsAsync<ErroDeFerramenta>(() => EscritorDeConhecimento.EscreverAsync(
            _caminhos, Sistema, "base/Magias/Circulo-3.md", "# Catálogo\n\n## Bola de Fogo\n\ntexto"));

        Assert.Contains("Bola de Fogo", erro.Message);
        Assert.Contains("inglês", erro.Message);

        Assert.False(File.Exists(
            Path.Combine(_caminhos.Conhecimento, Sistema, "base", "Magias", "Circulo-3.md")));
    }

    [Fact]
    public async Task EscreverConhecimento_MagiaComNomeEmInglesETraducao_EGravada()
    {
        GravarRegrasDoIdioma(IdiomaDetectado.Ingles);

        await EscritorDeConhecimento.EscreverAsync(
            _caminhos, Sistema, "base/Magias/Circulo-3.md",
            "# Catálogo\n\n## Fireball (Bola de Fogo)\n\ntexto");

        Assert.True(File.Exists(
            Path.Combine(_caminhos.Conhecimento, Sistema, "base", "Magias", "Circulo-3.md")));
    }

    /// <summary>A exceção da regra: sistema em português não tem original em inglês a preservar.</summary>
    [Fact]
    public async Task EscreverConhecimento_SistemaEmPortugues_AceitaOsNomesEmPortugues()
    {
        GravarRegrasDoIdioma(IdiomaDetectado.Portugues);

        await EscritorDeConhecimento.EscreverAsync(
            _caminhos, Sistema, "base/Magias/Circulo-3.md", "# Catálogo\n\n## Bola de Fogo\n\ntexto");

        Assert.True(File.Exists(
            Path.Combine(_caminhos.Conhecimento, Sistema, "base", "Magias", "Circulo-3.md")));
    }

    /// <summary>
    /// Fora de uma pasta de magias a regra não opina: um nome entre parênteses no meio de um
    /// arquivo de classe não é nome de magia, e recusar por isso travaria o processamento.
    /// </summary>
    [Fact]
    public async Task EscreverConhecimento_ForaDePastaDeMagias_NaoCobraNadaDosTitulos()
    {
        GravarRegrasDoIdioma(IdiomaDetectado.Ingles);

        await EscritorDeConhecimento.EscreverAsync(
            _caminhos, Sistema, "base/Classes/Mago.md", "# Mago\n\n## Progressão\n\n## Truques");

        Assert.True(File.Exists(Path.Combine(_caminhos.Conhecimento, Sistema, "base", "Classes", "Mago.md")));
    }

    /// <summary>
    /// Sistema sem o arquivo de regras não tem idioma conhecido, e cobrar o nome em inglês sem
    /// saber o idioma seria chute — a base de todo sistema anterior a esta versão seria recusada.
    /// </summary>
    [Fact]
    public async Task EscreverConhecimento_SistemaSemRegras_NaoCobraONomeEmIngles()
    {
        await EscritorDeConhecimento.EscreverAsync(
            _caminhos, Sistema, "base/Magias/Circulo-3.md", "# Catálogo\n\n## Bola de Fogo\n\ntexto");

        Assert.True(File.Exists(
            Path.Combine(_caminhos.Conhecimento, Sistema, "base", "Magias", "Circulo-3.md")));
    }
}
