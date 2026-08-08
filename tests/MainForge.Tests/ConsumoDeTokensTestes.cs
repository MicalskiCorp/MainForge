using MainForge.Core;

namespace MainForge.Tests;

/// <summary>
/// A aritmética do consumo. Ela é simples, mas é a base de todo relatório de gasto do
/// aplicativo: somar errado aqui produz um número que o usuário vai usar para decidir entre
/// reprocessar um sistema e continuar de onde parou.
/// </summary>
public sealed class ConsumoDeTokensTestes
{
    [Fact]
    public void Somar_AcumulaCadaContadorEOsTurnos()
    {
        var primeiro = new ConsumoDeTokens(100, 20, 300, 400, 0.5m, 1);
        var segundo = new ConsumoDeTokens(10, 2, 30, 40, 0.25m, 1);

        var total = primeiro + segundo;

        Assert.Equal(110, total.Entrada);
        Assert.Equal(22, total.Saida);
        Assert.Equal(330, total.CacheCriado);
        Assert.Equal(440, total.CacheLido);
        Assert.Equal(0.75m, total.CustoUsd);
        Assert.Equal(2, total.Turnos);
    }

    /// <summary>
    /// Custo desconhecido de um lado não pode virar zero do outro: somar um turno sem custo
    /// informado a um que tem custo precisa preservar o que se sabe.
    /// </summary>
    [Fact]
    public void Somar_ComCustoDesconhecidoDeUmLado_PreservaOQueSeSabe()
    {
        var conhecido = new ConsumoDeTokens(CustoUsd: 1.5m, Turnos: 1);
        var desconhecido = new ConsumoDeTokens(Entrada: 10, Turnos: 1);

        Assert.Equal(1.5m, (conhecido + desconhecido).CustoUsd);
        Assert.Null((desconhecido + new ConsumoDeTokens(Turnos: 1)).CustoUsd);
    }

    /// <summary>
    /// É assim que a conversa com o Dungeon Master registra turno a turno: o total menos o que
    /// já tinha sido contado. Sem isto, o primeiro turno seria cobrado em todos os seguintes.
    /// </summary>
    [Fact]
    public void Subtrair_DaOGastoDoUltimoTurno()
    {
        var depoisDoPrimeiro = new ConsumoDeTokens(100, 20, 0, 0, 0.30m, 1);
        var depoisDoSegundo = new ConsumoDeTokens(160, 35, 0, 500, 0.50m, 2);

        var doSegundoTurno = depoisDoSegundo - depoisDoPrimeiro;

        Assert.Equal(60, doSegundoTurno.Entrada);
        Assert.Equal(15, doSegundoTurno.Saida);
        Assert.Equal(500, doSegundoTurno.CacheLido);
        Assert.Equal(0.20m, doSegundoTurno.CustoUsd);
        Assert.Equal(1, doSegundoTurno.Turnos);
    }

    /// <summary>
    /// Uma sessão recomeçada zera a contagem do lado do Claude Code. Subtrair um total maior
    /// produziria contagem negativa — que numa soma vira desconto de cota que nunca voltou.
    /// </summary>
    [Fact]
    public void Subtrair_NuncaProduzNegativo()
    {
        var menor = new ConsumoDeTokens(10, 5, 0, 0, 0.10m, 1);
        var maior = new ConsumoDeTokens(999, 999, 0, 0, 9.99m, 9);

        var diferenca = menor - maior;

        Assert.Equal(0, diferenca.Entrada);
        Assert.Equal(0, diferenca.Saida);
        Assert.Equal(0, diferenca.Turnos);
        Assert.Equal(0m, diferenca.CustoUsd);
    }

    [Fact]
    public void ProporcaoEmCache_ContaSobreTudoQueEntrou()
    {
        var consumo = new ConsumoDeTokens(Entrada: 100, CacheCriado: 100, CacheLido: 800);

        Assert.Equal(1000, consumo.TotalDeEntrada);
        Assert.Equal(0.8, consumo.ProporcaoEmCache, precision: 3);
    }

    [Fact]
    public void Zero_EVazio_ENaoQuebraNaDivisao()
    {
        Assert.True(ConsumoDeTokens.Zero.Vazio);
        Assert.Equal(0, ConsumoDeTokens.Zero.ProporcaoEmCache);
        Assert.Contains("não informado", ConsumoDeTokens.Zero.Descrever());
    }

    /// <summary>
    /// Um turno que só informou custo, sem contagem de tokens, ainda é um turno que aconteceu —
    /// tratá-lo como vazio esconderia o gasto do relatório.
    /// </summary>
    [Fact]
    public void ComTurnoRegistrado_NaoEVazio()
    {
        Assert.False(new ConsumoDeTokens(CustoUsd: 0.4m, Turnos: 1).Vazio);
    }

    [Theory]
    [InlineData(950, "950 tokens")]
    [InlineData(1_500, "1,5k tokens")]
    [InlineData(2_400_000, "2,4M tokens")]
    public void Descrever_AbreviaAOrdemDeGrandeza(long entrada, string esperado)
    {
        Assert.Contains(esperado, new ConsumoDeTokens(Entrada: entrada, Turnos: 1).Descrever());
    }
}
