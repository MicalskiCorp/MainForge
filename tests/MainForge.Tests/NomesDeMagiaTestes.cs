using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// A regra do nome em inglês é uma recusa: ela impede o Configurador de gravar um arquivo de
/// magias fora do padrão. Recusa que dá alarme falso trava um processamento de duas horas por
/// nada, então metade destes testes é sobre o que ela <b>não</b> pode acusar.
/// </summary>
public class NomesDeMagiaTestes
{
    [Theory]
    [InlineData("base/Magias/Circulo-3.md", true)]
    [InlineData("Compendio-Arcano/Magias/Novas.md", true)]
    [InlineData("base/Spells/Level-3.md", true)]
    [InlineData("base/Feiticos/Terceiro.md", true)]
    [InlineData("base/Classes/Mago.md", false)]
    [InlineData("base/Progressao.md", false)]
    public void EhArquivoDeMagias_OlhaAPasta(string caminho, bool esperado) =>
        Assert.Equal(esperado, NomesDeMagia.EhArquivoDeMagias(caminho));

    /// <summary>
    /// Um "Magias.md" dentro de Classes/ é a tabela de espaços daquela classe, não o catálogo de
    /// magias. Cobrar nome em inglês dos títulos dele acusaria "Magias de 1º Nível".
    /// </summary>
    [Fact]
    public void EhArquivoDeMagias_NomeDoArquivoNaoConta() =>
        Assert.False(NomesDeMagia.EhArquivoDeMagias("base/Classes/Magias.md"));

    [Fact]
    public void TitulosSemNomeEmIngles_ArquivoNoPadrao_NaoAcusaNada()
    {
        var conteudo = """
            # Magias de 3º Círculo

            ## Fireball (Bola de Fogo)

            Uma explosão de chamas surge num ponto à sua escolha.

            ## Counterspell (Dissipar Magia)

            Você interrompe a magia de outra criatura.
            """;

        Assert.Empty(NomesDeMagia.TitulosSemNomeEmIngles(conteudo));
    }

    [Fact]
    public void TitulosSemNomeEmIngles_MagiaSoEmPortugues_EAcusada()
    {
        var conteudo = """
            # Magias de 3º Círculo

            ## Fireball (Bola de Fogo)

            ## Dissipar Magia

            Você interrompe a magia de outra criatura.
            """;

        var acusado = Assert.Single(NomesDeMagia.TitulosSemNomeEmIngles(conteudo));

        Assert.Equal("Dissipar Magia", acusado);
    }

    /// <summary>
    /// O primeiro título é o do arquivo, não o de uma magia. Acusá-lo pediria "Magias de 3º
    /// Círculo (Magias de 3º Círculo)", que é pior que o problema.
    /// </summary>
    [Fact]
    public void TitulosSemNomeEmIngles_TituloDoArquivo_NaoEAcusado()
    {
        var conteudo = """
            # Catálogo do Terceiro Círculo

            ## Fireball (Bola de Fogo)
            """;

        Assert.Empty(NomesDeMagia.TitulosSemNomeEmIngles(conteudo));
    }

    /// <summary>
    /// Um arquivo por magia é uma granularidade legítima, e aí o único título é o nome da magia —
    /// e é ele que a regra cobra.
    /// </summary>
    [Fact]
    public void TitulosSemNomeEmIngles_ArquivoDeUmaMagiaSo_CobraOTituloUnico()
    {
        Assert.Equal("Bola de Fogo", Assert.Single(NomesDeMagia.TitulosSemNomeEmIngles("# Bola de Fogo\n\ntexto")));
        Assert.Empty(NomesDeMagia.TitulosSemNomeEmIngles("# Fireball (Bola de Fogo)\n\ntexto"));
    }

    /// <summary>
    /// Cabeçalho que organiza a seção não é nome de magia. Se ele fosse acusado, todo arquivo de
    /// magias bem escrito seria recusado.
    /// </summary>
    [Theory]
    [InlineData("## Magias de 3º Círculo")]
    [InlineData("## Truques")]
    [InlineData("## Como ler estas magias")]
    [InlineData("## Tabela de componentes")]
    [InlineData("## Escola: Evocação")]
    public void TitulosSemNomeEmIngles_CabecalhoOrganizador_NaoEAcusado(string titulo)
    {
        var conteudo = $"# Catálogo\n\n{titulo}\n\ntexto";

        Assert.Empty(NomesDeMagia.TitulosSemNomeEmIngles(conteudo));
    }

    /// <summary>Parênteses vazios ou sem letra não são tradução nenhuma.</summary>
    [Theory]
    [InlineData("## Fireball ()")]
    [InlineData("## Fireball (3)")]
    public void TitulosSemNomeEmIngles_ParentesesSemTraducao_EAcusado(string titulo)
    {
        var conteudo = $"# Catálogo\n\n{titulo}\n\ntexto";

        Assert.Single(NomesDeMagia.TitulosSemNomeEmIngles(conteudo));
    }

    [Fact]
    public void SemNomeEmIngles_CampoDaFicha_SeparaPorLinhaEPorVirgula()
    {
        const string valor = "Fireball (Bola de Fogo), Shield (Escudo)\nBola de Fogo; Mage Hand (Mão Mágica)";

        var acusadas = NomesDeMagia.SemNomeEmIngles(valor);

        Assert.Equal(["Bola de Fogo"], acusadas);
    }

    /// <summary>
    /// Linha de traços e espaços é o preenchimento em branco de um formulário, não magia. Acusá-la
    /// encheria a validação de violações sobre nada.
    /// </summary>
    [Fact]
    public void SemNomeEmIngles_LinhaSemLetra_EIgnorada() =>
        Assert.Empty(NomesDeMagia.SemNomeEmIngles("---\n , ;\n"));
}
