using MainForge.ClaudeCode;

namespace MainForge.Tests;

/// <summary>
/// Distinguir "acabou a cota" de "deu errado" é o que decide entre esperar e desistir. Como a
/// distinção é feita por texto — o CLI não dá código de erro para isso —, o risco real está nos
/// dois extremos: não reconhecer um limite (o usuário perde o processamento) e reconhecer um
/// que não existe (o aplicativo dorme por nada).
/// </summary>
public class LimiteDeUsoTestes
{
    [Theory]
    [InlineData("Claude AI usage limit reached")]
    [InlineData("error_during_execution: 5-hour limit reached for your plan")]
    [InlineData("API Error: 429 rate limit exceeded")]
    [InlineData("Weekly limit reached — upgrade to increase your usage limit")]
    public void Detectar_ReconheceAsFormasConhecidasDeCotaEsgotada(string texto)
    {
        Assert.NotNull(DetectorDeLimiteDeUso.Detectar(texto));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("error_during_execution: tool 'Bash' is not allowed")]
    [InlineData("O Claude Code terminou com código 1 sem produzir resposta.")]
    public void Detectar_NaoConfundeOutrasFalhasComCotaEsgotada(string? texto)
    {
        Assert.Null(DetectorDeLimiteDeUso.Detectar(texto));
    }

    /// <summary>
    /// A época Unix depois da barra é como o Claude Code informa quando a janela vira — é ela
    /// que permite esperar o tempo certo em vez de tentar de novo no escuro.
    /// </summary>
    [Fact]
    public void Detectar_LeAEpocaUnixDepoisDaBarraComoHoraDaLiberacao()
    {
        var liberacao = DateTimeOffset.UtcNow.AddHours(2);
        var texto = $"Claude AI usage limit reached|{liberacao.ToUnixTimeSeconds()}";

        var limite = DetectorDeLimiteDeUso.Detectar(texto);

        Assert.NotNull(limite!.Liberacao);
        Assert.Equal(liberacao.ToUnixTimeSeconds(), limite.Liberacao!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public void Detectar_EpocaImplausivel_EIgnoradaEmVezDeVirarEsperaAbsurda()
    {
        var daquiADoisAnos = DateTimeOffset.UtcNow.AddYears(2).ToUnixTimeSeconds();

        var limite = DetectorDeLimiteDeUso.Detectar($"Claude AI usage limit reached|{daquiADoisAnos}");

        Assert.NotNull(limite);
        Assert.Null(limite!.Liberacao);
    }

    [Fact]
    public void Detectar_MensagemFicaLegivelSemAEpocaCrua()
    {
        var limite = DetectorDeLimiteDeUso.Detectar(
            $"error_during_execution: Claude AI usage limit reached|{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}");

        Assert.DoesNotContain("|", limite!.Mensagem);
        Assert.Contains("usage limit reached", limite.Mensagem);
    }
}
