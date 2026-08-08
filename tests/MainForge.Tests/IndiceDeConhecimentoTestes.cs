using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// A reconstrução parcial — só a cadeia de pastas até o arquivo gravado — precisa produzir o
/// mesmo índice que a completa. É a otimização que tira o processamento de uma base grande do
/// tempo quadrático, e ela só vale se o resultado for indistinguível.
/// </summary>
public sealed class IndiceParcialTestes : IDisposable
{
    private const string Sistema = "Aventura&Cia";

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "mainforge-indice-parcial-" + Guid.NewGuid());
    private readonly CaminhosDoProjeto _caminhos;

    public IndiceParcialTestes() => _caminhos = new CaminhosDoProjeto(_raiz);

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    private void Gravar(string relativo, string conteudo)
    {
        var completo = Path.Combine(_caminhos.Conhecimento, Sistema, relativo.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(completo)!);
        File.WriteAllText(completo, conteudo);
    }

    private string LerIndiceDe(string pasta) =>
        File.ReadAllText(Path.Combine(
            _caminhos.Conhecimento,
            Sistema,
            pasta.Replace('/', Path.DirectorySeparatorChar),
            SistemaRpg.NomeDoIndice));

    private void MontarBase()
    {
        Gravar("base/Criacao-de-Personagem.md", "# Criação\n\nO passo a passo.");
        Gravar("base/Classes/Guerreiro.md", "# Guerreiro\n\nA classe marcial.");
        Gravar("base/Classes/Mago.md", "# Mago\n\nA classe arcana.");
        Gravar("base/Magias/Circulo-1.md", "# Círculo 1\n\nAs magias iniciais.");
        Gravar("Compendio-Arcano/Classes/Bruxo.md", "# Bruxo\n\nA classe do pacto.");
    }

    /// <summary>
    /// A propriedade que a otimização precisa preservar: gravar arquivo por arquivo com a
    /// reconstrução parcial dá o mesmo resultado que reconstruir tudo no fim.
    /// </summary>
    [Fact]
    public async Task ReconstrucaoParcialDaOMesmoResultadoQueACompleta()
    {
        await EscritorDeConhecimento.EscreverAsync(
            _caminhos, Sistema, "base/Classes/Guerreiro.md", "# Guerreiro", "A classe marcial");
        await EscritorDeConhecimento.EscreverAsync(
            _caminhos, Sistema, "base/Classes/Mago.md", "# Mago", "A classe arcana");
        await EscritorDeConhecimento.EscreverAsync(
            _caminhos, Sistema, "Compendio-Arcano/Classes/Bruxo.md", "# Bruxo", "A classe do pacto");

        var porGravacao = new[] { "", "base", "base/Classes", "Compendio-Arcano", "Compendio-Arcano/Classes" }
            .ToDictionary(pasta => pasta, LerIndiceDe);

        IndiceDeConhecimento.Reconstruir(_caminhos, Sistema);

        foreach (var (pasta, esperado) in porGravacao)
        {
            Assert.Equal(esperado, LerIndiceDe(pasta));
        }
    }

    /// <summary>
    /// Uma pasta nova precisa aparecer na lista de subpastas de todos os níveis acima dela, e
    /// não só do pai imediato.
    /// </summary>
    [Fact]
    public void ReconstruirAte_PastaNova_ApareceEmTodaACadeiaAcima()
    {
        MontarBase();
        IndiceDeConhecimento.Reconstruir(_caminhos, Sistema);

        Gravar("base/Equipamentos/Armas.md", "# Armas\n\nA tabela de armas.");

        IndiceDeConhecimento.ReconstruirAte(
            _caminhos,
            Sistema,
            "base/Equipamentos/Armas.md",
            new Dictionary<string, string> { ["base/Equipamentos/Armas.md"] = "Tabela de armas" });

        Assert.Contains("Armas.md", LerIndiceDe("base/Equipamentos"));
        Assert.Contains("Tabela de armas", LerIndiceDe("base/Equipamentos"));
        Assert.Contains("Equipamentos", LerIndiceDe("base"));
    }

    /// <summary>
    /// O contrário também precisa valer: o que não está na cadeia não pode ser tocado. Se fosse,
    /// a otimização não estaria otimizando nada.
    /// </summary>
    [Fact]
    public void ReconstruirAte_NaoMexeNoIndiceDeOutroRamo()
    {
        MontarBase();
        IndiceDeConhecimento.Reconstruir(_caminhos, Sistema);

        var outroRamo = Path.Combine(
            _caminhos.Conhecimento, Sistema, "Compendio-Arcano", "Classes", SistemaRpg.NomeDoIndice);

        var antes = File.GetLastWriteTimeUtc(outroRamo);

        Gravar("base/Classes/Ladino.md", "# Ladino");
        IndiceDeConhecimento.ReconstruirAte(_caminhos, Sistema, "base/Classes/Ladino.md");

        Assert.Equal(antes, File.GetLastWriteTimeUtc(outroRamo));
        Assert.Contains("Ladino.md", LerIndiceDe("base/Classes"));
    }

    /// <summary>
    /// A descrição de uma subpasta é repetida no índice do nível de cima. Numa reconstrução
    /// parcial ela não é recalculada — precisa vir do index.md que a subpasta já tem.
    /// </summary>
    [Fact]
    public void ReconstruirAte_PreservaADescricaoDasSubpastasQueNaoForamRefeitas()
    {
        MontarBase();

        EscritorDeConhecimento.DescreverPasta(
            _caminhos, Sistema, "base/Magias", "Todas as magias por círculo.");

        Gravar("base/Classes/Ladino.md", "# Ladino");
        IndiceDeConhecimento.ReconstruirAte(_caminhos, Sistema, "base/Classes/Ladino.md");

        Assert.Contains("Todas as magias por círculo.", LerIndiceDe("base"));
    }
}

/// <summary>
/// O índice é o que permite ao Dungeon Master achar uma regra sem abrir a base inteira, então
/// o que estes testes protegem é a propriedade "o índice diz a verdade sobre a pasta": não
/// perde descrição já registrada, não lista arquivo que sumiu, e não quebra com nome de
/// arquivo hostil.
/// </summary>
public sealed class IndiceDeConhecimentoTestes : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "mainforge-indice-" + Guid.NewGuid());
    private readonly CaminhosDoProjeto _caminhos;

    public IndiceDeConhecimentoTestes() => _caminhos = new CaminhosDoProjeto(_raiz);

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    private void Gravar(string caminhoRelativo, string conteudo)
    {
        var completo = Path.Combine(_caminhos.Conhecimento, "Aventura&Cia", caminhoRelativo);
        Directory.CreateDirectory(Path.GetDirectoryName(completo)!);
        File.WriteAllText(completo, conteudo);
    }

    private string LerIndice(string pasta) =>
        File.ReadAllText(Path.Combine(_caminhos.Conhecimento, "Aventura&Cia", pasta, IndiceDeConhecimento.NomeDoArquivo));

    [Fact]
    public void Reconstruir_CriaUmIndiceEmCadaNivel()
    {
        Gravar("Regras.md", "# Regras");
        Gravar(Path.Combine("Classes", "Guerreiro.md"), "# Guerreiro");
        Gravar(Path.Combine("Classes", "Subclasses", "Campeao.md"), "# Campeao");

        IndiceDeConhecimento.Reconstruir(_caminhos, "Aventura&Cia");

        Assert.Contains("Classes", LerIndice(""));
        Assert.Contains("Guerreiro.md", LerIndice("Classes"));
        Assert.Contains("Campeao.md", LerIndice(Path.Combine("Classes", "Subclasses")));
        Assert.True(File.Exists(Path.Combine(_caminhos.Conhecimento, IndiceDeConhecimento.NomeDoArquivo)));
    }

    /// <summary>
    /// O caso que justifica o índice existir: sem descrição informada, ele ainda diz alguma
    /// coisa útil sobre cada arquivo — e diz de graça, lendo o conteúdo em vez de perguntar ao
    /// modelo. É assim que uma base gerada antes dos índices é indexada.
    /// </summary>
    [Fact]
    public void Reconstruir_SemDescricaoInformada_DerivaDoConteudoDoArquivo()
    {
        Gravar("Racas.md", "# Racas\n\nAs linhagens jogaveis e seus bonus de atributo.\n");

        IndiceDeConhecimento.Reconstruir(_caminhos, "Aventura&Cia");

        Assert.Contains("As linhagens jogaveis", LerIndice(""));
    }

    [Fact]
    public void Reconstruir_PreservaDescricaoRegistradaAntes()
    {
        Gravar(Path.Combine("Classes", "Guerreiro.md"), "# Guerreiro");

        IndiceDeConhecimento.Reconstruir(
            _caminhos,
            "Aventura&Cia",
            new Dictionary<string, string> { ["Classes/Guerreiro.md"] = "Classe Guerreiro: dado de vida d10." });

        // Uma segunda passada, provocada por outra gravação qualquer, não pode apagar o que a
        // primeira registrou.
        Gravar(Path.Combine("Classes", "Mago.md"), "# Mago");
        IndiceDeConhecimento.Reconstruir(_caminhos, "Aventura&Cia");

        var indice = LerIndice("Classes");
        Assert.Contains("dado de vida d10", indice);
        Assert.Contains("Mago.md", indice);
    }

    [Fact]
    public void Reconstruir_ArquivoApagado_SaiDoIndice()
    {
        Gravar(Path.Combine("Classes", "Guerreiro.md"), "# Guerreiro");
        Gravar(Path.Combine("Classes", "Bardo.md"), "# Bardo");
        IndiceDeConhecimento.Reconstruir(_caminhos, "Aventura&Cia");

        File.Delete(Path.Combine(_caminhos.Conhecimento, "Aventura&Cia", "Classes", "Bardo.md"));
        IndiceDeConhecimento.Reconstruir(_caminhos, "Aventura&Cia");

        Assert.DoesNotContain("Bardo.md", LerIndice("Classes"));
    }

    /// <summary>
    /// Descrição com barra vertical partiria a linha em colunas a mais e faria o índice ser
    /// relido errado na próxima reconstrução — a descrição voltaria truncada.
    /// </summary>
    [Fact]
    public void Reconstruir_DescricaoComBarraVertical_SobreviveAoIdaEVolta()
    {
        Gravar("Dados.md", "# Dados");

        IndiceDeConhecimento.Reconstruir(
            _caminhos,
            "Aventura&Cia",
            new Dictionary<string, string> { ["Dados.md"] = "Rolagens: d20 | vantagem | desvantagem." });

        IndiceDeConhecimento.Reconstruir(_caminhos, "Aventura&Cia");

        Assert.Equal(
            "Rolagens: d20 | vantagem | desvantagem.",
            IndiceDeConhecimento.DescricaoRegistrada(_caminhos, "Aventura&Cia", "Dados.md"));
    }

    [Fact]
    public void DescreverPasta_RegistraADescricaoNoIndiceDaPastaEDoNivelDeCima()
    {
        Gravar(Path.Combine("Magia", "Truques.md"), "# Truques");
        IndiceDeConhecimento.Reconstruir(_caminhos, "Aventura&Cia");

        EscritorDeConhecimento.DescreverPasta(_caminhos, "Aventura&Cia", "Magia", "Tudo sobre conjuracao e magias.");

        Assert.Contains("Tudo sobre conjuracao", LerIndice("Magia"));
        Assert.Contains("Tudo sobre conjuracao", LerIndice(""));
    }

    [Fact]
    public void AtualizarIndiceRaiz_ListaOsSistemasComADescricaoDeCadaUm()
    {
        Gravar("Regras.md", "# Regras");
        IndiceDeConhecimento.Reconstruir(_caminhos, "Aventura&Cia");
        EscritorDeConhecimento.DescreverPasta(_caminhos, "Aventura&Cia", "", "Aventura & Cia, 1a edicao.");

        var raiz = File.ReadAllText(Path.Combine(_caminhos.Conhecimento, IndiceDeConhecimento.NomeDoArquivo));

        Assert.Contains("Aventura&Cia", raiz);
        Assert.Contains("Aventura & Cia, 1a edicao.", raiz);
    }

    /// <summary>
    /// O agente usa o destino do link como caminho de <c>Read</c>, não como URL. Codificar o
    /// nome inteiro apontava "D&amp;D5e" para <c>D%26D5e/index.md</c> — um diretório que não
    /// existe —, e a navegação pelo índice raiz começava com uma leitura recusada.
    /// </summary>
    [Fact]
    public void AtualizarIndiceRaiz_LinkDoSistemaEOCaminhoRealEmDisco()
    {
        Gravar("Regras.md", "# Regras");
        IndiceDeConhecimento.Reconstruir(_caminhos, "Aventura&Cia");

        var raiz = File.ReadAllText(Path.Combine(_caminhos.Conhecimento, IndiceDeConhecimento.NomeDoArquivo));

        Assert.Contains("(Aventura&Cia/index.md)", raiz);
        Assert.DoesNotContain("%26", raiz);
    }

    /// <summary>Espaço e parêntese continuam codificados: crus, quebram a sintaxe do link.</summary>
    [Fact]
    public void Reconstruir_NomeComEspacoEParenteses_ContinuaCodificadoNoLink()
    {
        Gravar("Regras Gerais (PT-BR).md", "# Regras");

        IndiceDeConhecimento.Reconstruir(_caminhos, "Aventura&Cia");

        Assert.Contains("(Regras%20Gerais%20%28PT-BR%29.md)", LerIndice(""));
    }

    [Fact]
    public void Reconstruir_SistemaInexistente_NaoFazNadaENaoQuebra()
    {
        Assert.Empty(IndiceDeConhecimento.Reconstruir(_caminhos, "SistemaQueNaoExiste"));
    }
}
