using MainForge.Core;

namespace MainForge.Tests;

/// <summary>
/// A restrição atravessa a fronteira de processo — do aplicativo para o servidor MCP, pelo
/// ambiente. O que se testa aqui é justamente a travessia: se ela se perde no caminho, uma
/// ferramenta que lê <c>Sistemas/</c> passa a enxergar as expansões que a mesa recusou.
/// </summary>
public sealed class EscopoDaSessaoTestes
{
    [Fact]
    public void Serializar_ELer_PreservamSistemaEFontes()
    {
        var original = new EscopoDaSessao("Aventura&Cia", ["base", "Compendio-Arcano"]);

        var lida = EscopoDaSessao.Ler(original.Serializar());

        Assert.NotNull(lida);
        Assert.Equal("Aventura&Cia", lida.Sistema);
        Assert.Equal(["base", "Compendio-Arcano"], lida.Fontes);
    }

    [Fact]
    public void Permite_SoAsFontesDaMesaEDoSistemaDaMesa()
    {
        var restricao = new EscopoDaSessao("Aventura&Cia", ["base"]);

        Assert.True(restricao.Permite("Aventura&Cia", "base"));
        Assert.True(restricao.Permite("aventura&cia", "BASE"));
        Assert.False(restricao.Permite("Aventura&Cia", "Compendio-Arcano"));
        Assert.False(restricao.Permite("OutroSistema", "base"));
    }

    /// <summary>
    /// Texto ausente ou corrompido vira <c>null</c>, que significa "sem restrição". É o
    /// comportamento certo — o Configurador roda assim —, mas é justamente por isso que quem
    /// concede acesso com base nisso precisa tratar o <c>null</c> de propósito.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("isto não é json")]
    [InlineData("{}")]
    [InlineData("""{ "sistema": "X", "fontes": [] }""")]
    public void Ler_TextoInvalido_DevolveNulo(string? texto)
    {
        Assert.Null(EscopoDaSessao.Ler(texto));
    }
}
