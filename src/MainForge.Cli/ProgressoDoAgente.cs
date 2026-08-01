using MainForge.Agents;

namespace MainForge.Cli;

/// <summary>
/// Mostra no console o que o agente está fazendo enquanto o turno corre. Sem isto o
/// aplicativo fica mudo por dezenas de segundos enquanto o Configurador lê um livro inteiro
/// e escreve vários arquivos.
/// </summary>
internal static class ProgressoDoAgente
{
    public static Action<UsoDeFerramenta> Impressora() => uso =>
        ConsoleUi.Detalhe($"    · {uso.Nome}({uso.Entrada})");

    public static void Pensando(string quem) => ConsoleUi.Detalhe($"    · {quem} está pensando...");
}
