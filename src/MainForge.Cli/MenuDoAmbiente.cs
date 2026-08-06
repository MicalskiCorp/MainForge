namespace MainForge.Cli;

/// <summary>
/// O que esta máquina tem e se está funcionando: o Claude Code que roda os agentes e os
/// programas externos de que o aplicativo se aproveita.
///
/// <para><b>Por que estão juntos.</b> As duas telas respondem à mesma pergunta — "por que isto
/// não funciona aqui?" — e quem precisa de uma quase sempre precisa da outra. Como opções
/// separadas do menu principal, elas ocupavam dois lugares num menu que devia ser sobre RPG.</para>
/// </summary>
internal static class MenuDoAmbiente
{
    public static async Task ExecutarAsync(ContextoDoAplicativo contexto, CancellationToken cancelamento)
    {
        while (true)
        {
            ConsoleUi.Titulo("Ambiente");
            ConsoleUi.Info($"  1) Claude Code — login e teste   [{contexto.DescreverClaudeCode()}]");
            ConsoleUi.Info("  2) Dependências desta máquina");
            ConsoleUi.Info("  0) Voltar");

            switch (ConsoleUi.LerLinha("\nEscolha: "))
            {
                case "1":
                    await FluxoDoClaudeCode.ExecutarAsync(contexto, cancelamento);
                    break;

                case "2":
                    await FluxoDeDependencias.ExecutarAsync(contexto, cancelamento);
                    break;

                case "0" or "":
                    return;

                default:
                    ConsoleUi.Aviso("Opção inválida.");
                    break;
            }

            ConsoleUi.Pausar();
        }
    }
}
