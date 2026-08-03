using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Tests;

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

    [Fact]
    public void Reconstruir_SistemaInexistente_NaoFazNadaENaoQuebra()
    {
        Assert.Empty(IndiceDeConhecimento.Reconstruir(_caminhos, "SistemaQueNaoExiste"));
    }
}
