using System.Globalization;
using System.Text.RegularExpressions;

namespace MainForge.ClaudeCode;

/// <summary>
/// A cota da assinatura acabou. <paramref name="Liberacao"/> é quando a próxima janela abre,
/// quando o Claude Code informa — ele costuma mandar o instante como época Unix logo depois da
/// mensagem ("Claude AI usage limit reached|1712345678").
/// </summary>
public sealed record LimiteDeUso(string Mensagem, DateTimeOffset? Liberacao);

/// <summary>
/// Reconhece, no texto de erro do Claude Code, a diferença entre "acabou a cota" e "deu
/// errado".
///
/// <para>A distinção importa porque as duas falhas pedem reações opostas: erro de verdade é
/// para mostrar ao usuário e parar; cota esgotada é para esperar a próxima janela e continuar
/// de onde parou. Como o consumo sai da assinatura, e não de créditos, esperar é literalmente
/// a única saída — não existe "pagar mais" para destravar.</para>
///
/// <para>A detecção é por texto porque é o que o CLI oferece: em modo headless a cota esgotada
/// chega como um <c>result</c> com <c>is_error</c> e a mensagem no corpo, sem código de erro
/// dedicado. Por isso ela só roda no caminho de falha — procurar essas expressões no texto
/// normal do agente daria falso positivo na primeira vez que ele explicasse o que é um limite
/// de uso.</para>
/// </summary>
public static partial class DetectorDeLimiteDeUso
{
    /// <summary>
    /// As formas em que o Claude Code já disse "acabou a cota". A lista é frouxa de propósito:
    /// deixar de reconhecer uma delas custa um processamento inteiro (o aplicativo desiste em
    /// vez de esperar), enquanto reconhecer demais custa uma espera à toa que o Ctrl+C desfaz.
    ///
    /// <para><c>session limit</c> entrou depois de o CLI responder
    /// <c>success: You've hit your session limit · resets 4:10pm</c> — sem ela o processamento
    /// morria como erro comum, e o usuário via "Falha ao processar" no lugar da contagem
    /// regressiva.</para>
    /// </summary>
    private static readonly string[] Sinais =
    [
        // Cobre "usage limit reached", "usage limit exceeded" e "upgrade to increase your usage limit".
        "usage limit",
        "session limit",
        "rate limit",
        "rate_limit",
        "quota exceeded",
        "limite de uso",
        "5-hour limit",
        "5 hour limit",
        "weekly limit",
        "out of usage",
        "hit your limit",
    ];

    /// <summary>
    /// Devolve o limite reconhecido no texto, ou <c>null</c> se aquilo é outra falha qualquer.
    /// </summary>
    public static LimiteDeUso? Detectar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        if (!Sinais.Any(sinal => texto.Contains(sinal, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return new LimiteDeUso(Resumir(texto), Liberacao(texto));
    }

    /// <summary>
    /// O instante em que a janela abre. Duas formas conhecidas: a época Unix depois de uma
    /// barra vertical, e um "resets/try again at HH:MM" em texto.
    /// </summary>
    private static DateTimeOffset? Liberacao(string texto)
    {
        var epoca = Epoca().Match(texto);

        if (epoca.Success && long.TryParse(epoca.Groups[1].Value, out var valor))
        {
            // Menos de 13 dígitos é segundo; a partir daí, milissegundo.
            var momento = epoca.Groups[1].Value.Length >= 13
                ? DateTimeOffset.FromUnixTimeMilliseconds(valor)
                : DateTimeOffset.FromUnixTimeSeconds(valor);

            // Época plausível: nem no passado distante, nem daqui a meses.
            if (momento > DateTimeOffset.UtcNow.AddDays(-1) && momento < DateTimeOffset.UtcNow.AddDays(30))
            {
                return momento.ToLocalTime();
            }
        }

        var horario = Horario().Match(texto);

        if (!horario.Success)
        {
            return null;
        }

        if (!int.TryParse(horario.Groups[1].Value, out var hora))
        {
            return null;
        }

        var minuto = horario.Groups[2].Success
            ? int.Parse(horario.Groups[2].Value, CultureInfo.InvariantCulture)
            : 0;

        var sufixo = horario.Groups[3].Value.ToLowerInvariant();

        hora = sufixo switch
        {
            "pm" when hora < 12 => hora + 12,
            "am" when hora == 12 => 0,
            _ => hora,
        };

        if (hora > 23 || minuto > 59)
        {
            return null;
        }

        var agora = DateTimeOffset.Now;
        var alvo = new DateTimeOffset(agora.Year, agora.Month, agora.Day, hora, minuto, 0, agora.Offset);

        // "libera às 3h" dito às 22h só pode ser o dia seguinte.
        return alvo <= agora ? alvo.AddDays(1) : alvo;
    }

    /// <summary>
    /// A mensagem crua traz época Unix e ruído de CLI. O usuário só precisa da primeira linha
    /// legível.
    /// </summary>
    private static string Resumir(string texto)
    {
        var primeira = texto
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(linha => Sinais.Any(sinal => linha.Contains(sinal, StringComparison.OrdinalIgnoreCase)))
            ?? texto.Trim();

        var barra = primeira.IndexOf('|');

        if (barra > 0)
        {
            primeira = primeira[..barra].Trim();
        }

        // O 'subtype' do CLI vem grudado na frente ("success: ", "error_during_execution: ") e
        // não diz nada a quem está lendo — no caso do "success" chega a contradizer a mensagem.
        primeira = Subtipo().Replace(primeira, "", 1);

        const int limite = 200;

        return primeira.Length <= limite ? primeira : string.Concat(primeira.AsSpan(0, limite), "...");
    }

    [GeneratedRegex(@"\|\s*(\d{10,13})")]
    private static partial Regex Epoca();

    [GeneratedRegex(@"^[a-z_]+:\s+", RegexOptions.IgnoreCase)]
    private static partial Regex Subtipo();

    [GeneratedRegex(@"(?:resets?|try again|retry)\D{0,20}?(\d{1,2})(?::(\d{2}))?\s*(am|pm)?", RegexOptions.IgnoreCase)]
    private static partial Regex Horario();
}
