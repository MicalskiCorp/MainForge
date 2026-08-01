using MainForge.ClaudeCode;

namespace MainForge.Cli;

/// <summary>
/// Mostra no console o que o agente está fazendo enquanto o turno corre. Sem isto o
/// aplicativo fica mudo por dezenas de segundos enquanto o Configurador lê um livro inteiro
/// e escreve vários arquivos.
/// </summary>
internal static class ProgressoDoAgente
{
    public static Action<EventoDeAgente> Impressora() => evento =>
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
        }
    };

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
