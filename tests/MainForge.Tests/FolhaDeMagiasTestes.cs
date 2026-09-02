using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// A folha extra existe para o caso em que a ficha do sistema não comporta as magias por extenso.
/// Estes testes prendem as duas decisões que ela toma: <b>quando</b> sair (e quando não sair, que
/// é a maior parte das vezes) e <b>de onde</b> vêm as descrições — só das fontes daquela mesa.
/// </summary>
public class FolhaDeMagiasTestes : IDisposable
{
    private readonly string _raiz;
    private readonly CaminhosDoProjeto _caminhos;

    private const string Sistema = "Aventura&Cia";

    public FolhaDeMagiasTestes()
    {
        _raiz = Path.Combine(Path.GetTempPath(), "mainforge-folha-" + Guid.NewGuid());
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

    private void GravarRegras(bool usaMagias = true, bool? porExtenso = false)
    {
        RegrasDaFicha.Gravar(_caminhos, Sistema, new RegrasDaFicha
        {
            UsaMagias = usaMagias,
            CamposDeMagia = ["Magias"],
            MagiasPorExtenso = porExtenso,
            Campos = [new RegraDeCampo { Campo = "Magias", Rotulo = "Magias conhecidas" }],
        });
    }

    private void GravarMagia(string fonte, string titulo, string corpo)
    {
        var diretorio = Path.Combine(_caminhos.Conhecimento, Sistema, fonte, "Magias");
        Directory.CreateDirectory(diretorio);

        File.WriteAllText(
            Path.Combine(diretorio, $"{titulo.Split(' ')[0]}.md"),
            $"# Catálogo\n\n## {titulo}\n\n{corpo}\n\n## Outra (Outra)\n\nnada\n");
    }

    private Personagem Personagem(string magias, params string[] fontes)
    {
        var personagem = RepositorioDePersonagens.Criar(_caminhos, Sistema, "Thoradin", fontes);
        personagem.Campos = new Dictionary<string, string> { ["Magias"] = magias };

        return personagem;
    }

    [Fact]
    public void Produzir_SistemaSemMagias_NaoGeraNada()
    {
        GravarRegras(usaMagias: false);

        var resultado = FolhaDeMagias.Produzir(_caminhos, Personagem("Fireball (Bola de Fogo)"));

        Assert.Equal(SituacaoDaFolhaDeMagias.SistemaSemMagias, resultado.Situacao);
        Assert.Null(resultado.Caminho);
    }

    /// <summary>
    /// A ficha que já traz as magias por extenso não precisa da folha: ela seria uma segunda cópia
    /// da mesma coisa, e o usuário ficaria com dois arquivos sem saber qual levar à mesa.
    /// </summary>
    [Fact]
    public void Produzir_FichaComEspacoParaAsDescricoes_NaoGeraNada()
    {
        GravarRegras(porExtenso: true);

        var resultado = FolhaDeMagias.Produzir(_caminhos, Personagem("Fireball (Bola de Fogo)"));

        Assert.Equal(SituacaoDaFolhaDeMagias.FichaJaTemEspaco, resultado.Situacao);
    }

    [Fact]
    public void Produzir_PersonagemSemMagias_NaoGeraNada()
    {
        GravarRegras();

        var resultado = FolhaDeMagias.Produzir(_caminhos, Personagem(""));

        Assert.Equal(SituacaoDaFolhaDeMagias.PersonagemSemMagias, resultado.Situacao);
    }

    [Fact]
    public void Produzir_ConjuradorEmFichaSemEspaco_GeraOPdfAoLadoDaFicha()
    {
        GravarRegras();
        GravarMagia("base", "Fireball (Bola de Fogo)", "Uma explosão de chamas surge num ponto à sua escolha.");

        var resultado = FolhaDeMagias.Produzir(
            _caminhos, Personagem("Fireball (Bola de Fogo)\nShield (Escudo)"));

        Assert.Equal(SituacaoDaFolhaDeMagias.Gerada, resultado.Situacao);
        Assert.Equal(["Fireball (Bola de Fogo)", "Shield (Escudo)"], resultado.Magias);

        Assert.True(File.Exists(Path.Combine(_caminhos.SaidaPersonagens, "Thoradin-Magias.pdf")));
        Assert.Equal("Output/Personagens/Thoradin-Magias.pdf", resultado.Caminho);
    }

    /// <summary>
    /// A magia que a base não descreve entra na folha só com o nome, e o resultado diz quais são.
    /// Deixá-la de fora esconderia do jogador que ela está na ficha dele.
    /// </summary>
    [Fact]
    public void Produzir_MagiaForaDaBase_EntraSemDescricaoEEAvisada()
    {
        GravarRegras();
        GravarMagia("base", "Fireball (Bola de Fogo)", "Uma explosão de chamas.");

        var resultado = FolhaDeMagias.Produzir(
            _caminhos, Personagem("Fireball (Bola de Fogo)\nWish (Desejo)"));

        Assert.Equal(SituacaoDaFolhaDeMagias.Gerada, resultado.Situacao);
        Assert.Equal(["Wish (Desejo)"], resultado.SemDescricao);
    }

    /// <summary>
    /// A tradução varia entre quem escreveu a base e quem preencheu a ficha; o nome em inglês é a
    /// chave, e é por ele que as duas escritas casam.
    /// </summary>
    [Fact]
    public void Produzir_TraducaoDiferenteEntreFichaEBase_AindaAchaADescricao()
    {
        GravarRegras();
        GravarMagia("base", "Fireball (Bola de Fogo)", "Uma explosão de chamas.");

        var resultado = FolhaDeMagias.Produzir(_caminhos, Personagem("Fireball (Esfera Flamejante)"));

        Assert.Empty(resultado.SemDescricao);
    }

    /// <summary>
    /// A regra 9 da estrutura do projeto aplicada aqui: a expansão que a mesa dispensou não pode
    /// chegar ao jogador por esta porta só porque ela roda em C# e não passa pelas negações do
    /// agente.
    /// </summary>
    [Fact]
    public void Produzir_MagiaDeExpansaoForaDaMesa_NaoEntraNaFolha()
    {
        GravarRegras();
        GravarMagia("Compendio-Arcano", "Wish (Desejo)", "Você altera a realidade.");

        var resultado = FolhaDeMagias.Produzir(_caminhos, Personagem("Wish (Desejo)"));

        Assert.Equal(["Wish (Desejo)"], resultado.SemDescricao);
    }

    [Fact]
    public void Produzir_MagiaDeExpansaoDaMesa_EntraNaFolha()
    {
        GravarRegras();
        GravarMagia("Compendio-Arcano", "Wish (Desejo)", "Você altera a realidade.");

        var resultado = FolhaDeMagias.Produzir(
            _caminhos, Personagem("Wish (Desejo)", "Compendio-Arcano"));

        Assert.Empty(resultado.SemDescricao);
    }

    /// <summary>
    /// Sistema sem <c>Ficha-Validacao.json</c> — mapeado por uma versão anterior — não ganha folha
    /// nenhuma, em vez de ganhar uma errada. Sem as regras não há como saber se a ficha já comporta
    /// as descrições.
    /// </summary>
    [Fact]
    public void Produzir_SistemaSemRegras_NaoGeraNada()
    {
        var resultado = FolhaDeMagias.Produzir(_caminhos, Personagem("Fireball (Bola de Fogo)"));

        Assert.Equal(SituacaoDaFolhaDeMagias.SistemaSemMagias, resultado.Situacao);
    }

    /// <summary>
    /// O personagem que deixou de conjurar não pode ficar com a folha de ontem em Output/,
    /// descrevendo magias que ele não tem mais.
    /// </summary>
    [Fact]
    public void ApagarSeSobrou_RemoveAFolhaAntiga()
    {
        GravarRegras();
        GravarMagia("base", "Fireball (Bola de Fogo)", "Uma explosão de chamas.");

        var personagem = Personagem("Fireball (Bola de Fogo)");
        FolhaDeMagias.Produzir(_caminhos, personagem);

        var caminho = Path.Combine(_caminhos.SaidaPersonagens, "Thoradin-Magias.pdf");
        Assert.True(File.Exists(caminho));

        FolhaDeMagias.ApagarSeSobrou(_caminhos, personagem);

        Assert.False(File.Exists(caminho));
    }

    [Fact]
    public void MagiasDoPersonagem_RepetidaEmDoisCampos_ContaUmaVezSo()
    {
        var regras = new RegrasDaFicha { CamposDeMagia = ["Truques", "Magias"] };

        var magias = FolhaDeMagias.MagiasDoPersonagem(regras, new Dictionary<string, string>
        {
            ["Truques"] = "Light (Luz)",
            ["Magias"] = "Light (Luz)\nShield (Escudo)",
        });

        Assert.Equal(["Light (Luz)", "Shield (Escudo)"], magias);
    }
}
