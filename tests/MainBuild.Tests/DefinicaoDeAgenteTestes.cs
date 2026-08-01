using MainBuild.Agents;
using MainBuild.Core;

namespace MainBuild.Tests;

public class DefinicaoDeAgenteTestes
{
    [Fact]
    public void Configurador_CarregaSeuPromptDeSistemaDoDisco()
    {
        var caminhos = CaminhosDoProjeto.Descobrir();

        var prompt = DefinicaoDeAgente.Configurador.CarregarPromptDeSistema(caminhos);

        Assert.Contains("Agente: Configurador", prompt);
    }

    [Fact]
    public void DungeonMaster_CarregaSeuPromptDeSistemaDoDisco()
    {
        var caminhos = CaminhosDoProjeto.Descobrir();

        var prompt = DefinicaoDeAgente.DungeonMaster.CarregarPromptDeSistema(caminhos);

        Assert.Contains("Agente: Dungeon Master", prompt);
    }

    [Fact]
    public void Configurador_NuncaInclueFerramentasExclusivasDoDungeonMaster()
    {
        Assert.DoesNotContain("preencher_ficha_personagem", DefinicaoDeAgente.Configurador.FerramentasPermitidas);
    }

    [Fact]
    public void DungeonMaster_NuncaInclueFerramentasExclusivasDoConfigurador()
    {
        Assert.DoesNotContain("escrever_arquivo_conhecimento", DefinicaoDeAgente.DungeonMaster.FerramentasPermitidas);
    }
}
