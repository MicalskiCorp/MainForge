using MainForge.Agents;
using MainForge.ClaudeCode;

namespace MainForge.Cli;

/// <summary>
/// Mostra no console o que o agente está fazendo enquanto o turno corre. Sem isto o
/// aplicativo fica mudo por dezenas de segundos enquanto o Configurador lê um livro inteiro
/// e escreve vários arquivos.
/// </summary>
internal static class ProgressoDoAgente
{
    public static Action<EventoDeAgente> Impressora()
    {
        // A espera por cota é avisada a cada 30 segundos, mas uma janela semanal esgotada faz
        // isso por dias: imprimir cada aviso encheria o console de milhares de linhas e o
        // usuário perderia de vista o que o agente estava fazendo antes de parar.
        var ultimoRestante = TimeSpan.MaxValue;

        return evento =>
        {
            switch (evento)
            {
                case UsoDeFerramenta uso:
                    ConsoleUi.Detalhe($"    · {NomeAmigavel(uso.Nome)}({uso.Entrada})");
                    break;

                // Erro de ferramenta é quase sempre o guardrail funcionando (o agente tentou algo
                // fora do escopo dele). Mostrar em vez de esconder: se o allowlist estiver apertado
                // demais para a tarefa, é assim que isso aparece.
                case FalhaDeFerramenta erro:
                    ConsoleUi.Aviso($"    · recusado: {erro.Detalhe}");
                    break;

                case AguardandoLimiteDeUso espera:
                    if (ultimoRestante == TimeSpan.MaxValue)
                    {
                        ConsoleUi.Aviso($"\n    · {espera.Mensagem}");
                        ConsoleUi.Info(
                            "      A cota da assinatura acabou. O trabalho já feito está salvo e a conversa");
                        ConsoleUi.Info(
                            "      continua sozinha assim que a janela virar — não precisa fazer nada.");
                        ConsoleUi.Info(
                            "      Ctrl+C cancela a espera; o que já foi gerado fica salvo de qualquer forma.");
                    }
                    else if (ultimoRestante - espera.Restante < PassoDoAviso(espera.Restante))
                    {
                        break;
                    }

                    ultimoRestante = espera.Restante;

                    var ate = espera.Ate is { } momento ? $" (por volta de {Momento(momento)})" : "";
                    ConsoleUi.Detalhe($"    · aguardando {SessaoDeAgente.Descrever(espera.Restante)}{ate}...");
                    break;
            }
        };
    }

    /// <summary>
    /// De quanto em quanto tempo repetir a contagem regressiva. Quanto mais longa a espera,
    /// mais raro o aviso: numa espera de dias, uma linha por hora basta para o usuário saber que
    /// o aplicativo continua vivo, e é o que impede a espera de sepultar o histórico do console.
    /// </summary>
    private static TimeSpan PassoDoAviso(TimeSpan restante) => restante switch
    {
        { TotalHours: > 6 } => TimeSpan.FromHours(1),
        { TotalHours: > 1 } => TimeSpan.FromMinutes(15),
        { TotalMinutes: > 10 } => TimeSpan.FromMinutes(5),
        _ => TimeSpan.FromMinutes(1),
    };

    /// <summary>A data só aparece quando a janela vira noutro dia — senão é ruído.</summary>
    private static string Momento(DateTimeOffset quando) =>
        quando.Date == DateTimeOffset.Now.Date ? $"{quando:HH:mm}" : $"{quando:dd/MM HH:mm}";

    public static void Pensando(string quem) => ConsoleUi.Detalhe($"    · {quem} está pensando...");

    /// <summary>
    /// As ferramentas do servidor MCP chegam como <c>mcp__mainforge__preencher_ficha_personagem</c>;
    /// o prefixo é ruído para quem está olhando o progresso.
    /// </summary>
    private static string NomeAmigavel(string nome)
    {
        const string prefixo = "mcp__";

        if (!nome.StartsWith(prefixo, StringComparison.Ordinal))
        {
            return nome;
        }

        var ultimoSeparador = nome.LastIndexOf("__", StringComparison.Ordinal);

        return ultimoSeparador >= 0 && ultimoSeparador + 2 < nome.Length
            ? nome[(ultimoSeparador + 2)..]
            : nome;
    }
}
