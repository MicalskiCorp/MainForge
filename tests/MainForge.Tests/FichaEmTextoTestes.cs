using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// O desenho da ficha em texto, que é o que a interface mostra de um personagem pronto.
///
/// <para>O que estes testes protegem é o <b>desenho continuar um desenho</b>. O modelo é arte de
/// texto: bordas, colunas e pontilhados que só se sustentam enquanto cada valor ocupa a largura
/// do marcador que ele substituiu. Uma substituição ingênua produz uma ficha ilegível — e o erro
/// não aparece em nenhum teste de comportamento, só na tela de quem foi ver o personagem.</para>
/// </summary>
public class FichaEmTextoTestes : IDisposable
{
    private const string Sistema = "Aventura&Cia";

    /// <summary>
    /// Um modelo com a forma dos de verdade: o desenho no corpo do arquivo e, depois da primeira
    /// seção, o exemplo preenchido com valores fictícios que não são de personagem nenhum.
    /// </summary>
    private const string Modelo = """
        # Modelo da Ficha em Texto

        Substitua cada `{{Campo}}` pelo valor correspondente.

        ```
        +--------------------------------+
        |  Nome: {{Nome}}                |
        |  Classe: {{Classe}}            |
        |  [{{Veterano}}] Veterano       |
        +--------------------------------+
        ```

        ## Exemplo preenchido

        ```
        +--------------------------------+
        |  Nome: Bran de Pedravale       |
        +--------------------------------+
        ```
        """;

    private static readonly HashSet<string> SemMarcacoes = [];

    private readonly string _raiz;
    private readonly CaminhosDoProjeto _caminhos;

    public FichaEmTextoTestes()
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

    [Fact]
    public void Desenhar_TrocaOsMarcadoresPelosValores()
    {
        var desenho = FichaEmTexto.Desenhar(
            Modelo,
            new Dictionary<string, string> { ["Nome"] = "Bran", ["Classe"] = "Guerreiro" },
            SemMarcacoes);

        Assert.Contains("Nome: Bran", desenho);
        Assert.Contains("Classe: Guerreiro", desenho);
        Assert.DoesNotContain("{{", desenho);
    }

    /// <summary>
    /// Valor menor que o marcador é completado com espaços, e valor maior come os espaços que
    /// vêm depois dele: nos dois casos a borda da direita fica na mesma coluna em que o modelo a
    /// desenhou.
    /// </summary>
    [Theory]
    [InlineData("Bran")]
    [InlineData("Thoradin")]
    [InlineData("Thoradin do Norte")]
    public void Desenhar_MantemALarguraDoDesenho(string nome)
    {
        var desenho = FichaEmTexto.Desenhar(
            Modelo,
            new Dictionary<string, string> { ["Nome"] = nome },
            SemMarcacoes);

        var linhas = desenho.Split('\n');
        var borda = linhas[0].Length;

        Assert.Equal(borda, linhas.Single(linha => linha.Contains("Nome:")).Length);
    }

    /// <summary>
    /// O valor não é cortado para caber. Um nome longo desloca a borda daquela linha; perdê-lo
    /// pela metade seria mostrar um personagem que não existe.
    /// </summary>
    [Fact]
    public void Desenhar_ValorMaiorQueOEspacoDisponivel_NaoEhCortado()
    {
        var nome = new string('A', 200);

        var desenho = FichaEmTexto.Desenhar(
            Modelo,
            new Dictionary<string, string> { ["Nome"] = nome },
            SemMarcacoes);

        Assert.Contains(nome, desenho);
    }

    /// <summary>
    /// O exemplo preenchido do modelo tem valores fictícios. Mostrá-lo embaixo da ficha de quem
    /// se foi ver seria apresentar outro personagem como se fosse o dele.
    /// </summary>
    [Fact]
    public void Desenhar_IgnoraOQueVemDepoisDaPrimeiraSecao()
    {
        var desenho = FichaEmTexto.Desenhar(
            Modelo,
            new Dictionary<string, string> { ["Nome"] = "Thoradin" },
            SemMarcacoes);

        Assert.DoesNotContain("Bran de Pedravale", desenho);
    }

    /// <summary>
    /// Campo sem valor sai em branco, e não com o marcador cru nem com o nome do campo: a ficha
    /// de um personagem tem dezenas de campos que ele não usa.
    /// </summary>
    [Fact]
    public void Desenhar_CampoSemValor_SaiEmBranco()
    {
        var desenho = FichaEmTexto.Desenhar(
            Modelo,
            new Dictionary<string, string> { ["Nome"] = "Thoradin" },
            SemMarcacoes);

        Assert.DoesNotContain("Classe: {{", desenho);
        Assert.Contains("|  Classe:", desenho);
    }

    /// <summary>
    /// Numa caixa de marcação cabe "X" ou espaço. O valor guardado ("true", "Yes", o nome do
    /// estado no PDF) dentro do desenho viraria um quadro de dezoito casas.
    /// </summary>
    [Theory]
    [InlineData("true", "[X]")]
    [InlineData("Yes", "[X]")]
    [InlineData("Off", "[ ]")]
    [InlineData("", "[ ]")]
    public void Desenhar_CaixaDeMarcacao_ViraXOuEspaco(string valor, string esperado)
    {
        var desenho = FichaEmTexto.Desenhar(
            Modelo,
            new Dictionary<string, string> { ["Veterano"] = valor },
            new HashSet<string> { "Veterano" });

        Assert.Contains(esperado, desenho);
    }

    [Fact]
    public void Desenhar_CaixaSemValorNenhum_FicaDesmarcada()
    {
        var desenho = FichaEmTexto.Desenhar(Modelo, new Dictionary<string, string>(), new HashSet<string> { "Veterano" });

        Assert.Contains("[ ]", desenho);
    }

    /// <summary>
    /// História e traços de personalidade são campos de várias linhas no PDF. Uma quebra de linha
    /// dentro da arte de texto joga o resto do valor fora do quadro e desmonta o desenho dali
    /// para baixo.
    /// </summary>
    [Fact]
    public void Desenhar_ValorDeVariasLinhas_ViraUmaLinhaSo()
    {
        var desenho = FichaEmTexto.Desenhar(
            Modelo,
            new Dictionary<string, string> { ["Nome"] = "Thoradin\ndo Norte" },
            SemMarcacoes);

        Assert.Contains("Nome: Thoradin do Norte", desenho);
        Assert.Equal(5, desenho.Split('\n').Length);
    }

    /// <summary>
    /// Há campo de AcroForm chamado "Race " — com espaço no fim —, e o modelo o escreve como
    /// está. Exigir o nome exato dos dois lados faria o marcador sair vazio por causa de um
    /// espaço que ninguém vê.
    /// </summary>
    [Fact]
    public void Desenhar_NomeComEspacoSobrando_AindaEncontraOValor()
    {
        var desenho = FichaEmTexto.Desenhar(
            "```\nRaca: {{Raca }}\n```",
            new Dictionary<string, string> { ["Raca"] = "Anao" },
            SemMarcacoes);

        Assert.Contains("Raca: Anao", desenho);
    }

    /// <summary>
    /// Modelo sem seção nenhuma (todo o arquivo é o desenho) continua valendo: a regra de parar
    /// na primeira seção é sobre o que vem <em>depois</em> dela, não uma exigência de tê-la.
    /// </summary>
    [Fact]
    public void Desenhar_ModeloSemSecoes_UsaTodosOsBlocos()
    {
        var desenho = FichaEmTexto.Desenhar(
            "```\nPagina 1: {{Nome}}\n```\n\n```\nPagina 2: {{Classe}}\n```",
            new Dictionary<string, string> { ["Nome"] = "Thoradin", ["Classe"] = "Guerreiro" },
            SemMarcacoes);

        Assert.Contains("Pagina 1: Thoradin", desenho);
        Assert.Contains("Pagina 2: Guerreiro", desenho);
    }

    [Fact]
    public void Montar_UsaOModeloDoSistemaEOsCamposDoPersonagem()
    {
        GravarModelo(Modelo);

        var personagem = new Personagem
        {
            Id = "Thoradin",
            Sistema = Sistema,
            Nome = "Thoradin",
            Campos = new Dictionary<string, string> { ["Nome"] = "Thoradin", ["Classe"] = "Guerreiro" },
        };

        var desenho = FichaEmTexto.Montar(_caminhos, personagem);

        Assert.NotNull(desenho);
        Assert.Contains("Nome: Thoradin", desenho);
        Assert.Contains("Classe: Guerreiro", desenho);
    }

    /// <summary>
    /// Um sistema processado por uma versão antiga do Configurador não tem modelo em texto. A
    /// ausência é uma resposta — a interface mostra o dossiê no lugar —, e não um erro.
    /// </summary>
    [Fact]
    public void Montar_SistemaSemModeloEmTexto_DevolveNulo()
    {
        var personagem = new Personagem { Id = "Thoradin", Sistema = Sistema };

        Assert.Null(FichaEmTexto.Montar(_caminhos, personagem));
    }

    private void GravarModelo(string conteudo)
    {
        var caminho = FichaEmTexto.CaminhoDoModelo(_caminhos, Sistema);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, conteudo);
    }
}
