using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// A busca na base é o atalho que evita descer índice por índice — e é também a única
/// ferramenta do Dungeon Master que percorre <c>Sistemas/</c> por conta própria. Por isso o que
/// mais importa aqui não é achar: é <b>não</b> achar o que a mesa recusou. A negação de
/// <c>Read</c> por caminho que aplica a escolha das expansões não alcança o servidor MCP, então
/// é este código que precisa aplicá-la.
/// </summary>
public sealed class BuscaNoConhecimentoTestes : IDisposable
{
    private const string Sistema = "Aventura&Cia";

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "mainforge-busca-base-" + Guid.NewGuid());
    private readonly CaminhosDoProjeto _caminhos;

    public BuscaNoConhecimentoTestes()
    {
        _caminhos = new CaminhosDoProjeto(_raiz);

        Gravar("base/Classes/Guerreiro.md", """
            # Guerreiro

            ## Progressao

            No 3o nivel o guerreiro escolhe um arquétipo marcial.
            """);

        Gravar("base/Equipamentos/Armas.md", """
            # Armas

            Espada longa: 1d8 de dano cortante.
            """);

        Gravar("Compendio-Arcano/Classes/Guerreiro-Arcano.md", """
            # Guerreiro Arcano

            No 3o nivel o guerreiro arcano aprende truques de evocacao.
            """);

        Gravar("Ficha-Mapeamento.md", "# Mapeamento\n\nNome -> campo Nome.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    private void Gravar(string relativo, string conteudo)
    {
        var caminho = Path.Combine(_caminhos.Conhecimento, Sistema, relativo.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, conteudo);
    }

    private static RestricaoDeFontes SoABase => new(Sistema, ["base"]);

    [Fact]
    public void Procurar_DevolveArquivoLinhaESecao()
    {
        var ocorrencia = Assert.Single(BuscaNoConhecimento.Procurar(_caminhos, Sistema, "Espada longa"));

        Assert.Equal("Sistemas/Aventura&Cia/base/Equipamentos/Armas.md", ocorrencia.Arquivo);
        Assert.Equal("Armas", ocorrencia.Secao);
        Assert.Contains("1d8", ocorrencia.Trecho);
    }

    [Fact]
    public void Procurar_IgnoraAcentoEMaiuscula()
    {
        Assert.NotEmpty(BuscaNoConhecimento.Procurar(_caminhos, Sistema, "ARQUETIPO"));
    }

    /// <summary>
    /// O caso que justifica a ferramenta existir do jeito que existe: sem a restrição, a mesma
    /// busca traz o compêndio que o usuário deixou de fora — que é exatamente o conteúdo que a
    /// negação de leitura acabou de bloquear para o <c>Read</c> do agente.
    /// </summary>
    [Fact]
    public void Procurar_ComRestricao_NaoAlcancaFonteQueAMesaRecusou()
    {
        var semRestricao = BuscaNoConhecimento.Procurar(_caminhos, Sistema, "3o nivel");
        var comRestricao = BuscaNoConhecimento.Procurar(_caminhos, Sistema, "3o nivel", SoABase);

        Assert.Contains(semRestricao, ocorrencia => ocorrencia.Arquivo.Contains("Compendio-Arcano"));
        Assert.DoesNotContain(comRestricao, ocorrencia => ocorrencia.Arquivo.Contains("Compendio-Arcano"));
        Assert.NotEmpty(comRestricao);
    }

    /// <summary>
    /// Os arquivos da ficha ficam na raiz do sistema justamente por valerem com qualquer
    /// expansão — deixá-los de fora quebraria a mesa que só usa o jogo base.
    /// </summary>
    [Fact]
    public void Procurar_ComRestricao_ContinuaVendoOsArquivosDaRaizDoSistema()
    {
        Assert.Single(BuscaNoConhecimento.Procurar(_caminhos, Sistema, "campo Nome", SoABase));
    }

    [Fact]
    public void Procurar_ComRestricaoDeOutroSistema_ERecusada()
    {
        var erro = Assert.Throws<ErroDeFerramenta>(() =>
            BuscaNoConhecimento.Procurar(_caminhos, Sistema, "espada", new RestricaoDeFontes("OutroSistema", ["base"])));

        Assert.Contains("OutroSistema", erro.Message);
    }

    /// <summary>O índice é derivado: uma linha de tabela dele não é a regra que se procura.</summary>
    [Fact]
    public void Procurar_IgnoraOsIndices()
    {
        Gravar("base/index.md", "| [Armas.md](Armas.md) | Espada longa e outras armas |");

        Assert.DoesNotContain(
            BuscaNoConhecimento.Procurar(_caminhos, Sistema, "Espada longa"),
            ocorrencia => ocorrencia.Arquivo.EndsWith("index.md"));
    }

    /// <summary>
    /// O nome do sistema vem do modelo, então é caminho não confiável como qualquer outro — e o
    /// confinamento é aplicado aqui, em C#, não por lista de permissão.
    /// </summary>
    [Fact]
    public void Procurar_SistemaQueEscapaDaPastaDeSistemas_ERecusado()
    {
        Assert.Throws<UnauthorizedAccessException>(
            () => BuscaNoConhecimento.Procurar(_caminhos, "../../Windows", "magia"));
    }
}
