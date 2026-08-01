using MainForge.Cli;
using MainForge.Core;

// Interface do MainForge em modo console. A interface gráfica (WPF, projeto MainForge.App)
// virá depois — os fluxos de negócio moram nos agentes e nas ferramentas, não aqui, então
// trocar de interface não exige reescrever nada disso.

ConsoleUi.Preparar();

var contexto = new ContextoDoAplicativo(CaminhosDoProjeto.Descobrir());

// Ctrl+C cancela só a operação em andamento, sem derrubar o aplicativo — e cada operação
// ganha um CancellationTokenSource novo, senão o primeiro Ctrl+C deixaria todas as
// operações seguintes canceladas de saída.
CancellationTokenSource? cancelamentoAtual = null;

Console.CancelKeyPress += (_, evento) =>
{
    if (cancelamentoAtual is { IsCancellationRequested: false })
    {
        evento.Cancel = true;
        cancelamentoAtual.Cancel();
    }
};

TelaInicial.Desenhar();
ConsoleUi.Detalhe($"Projeto: {contexto.Caminhos.Raiz}");

if (!contexto.TemClaudeCode)
{
    ConsoleUi.Aviso("\nClaude Code não encontrado — os agentes não vão rodar. Veja a opção 5.");
}

while (true)
{
    ConsoleUi.Titulo("Menu principal");
    ConsoleUi.Info("  1) Ver sistemas e base de conhecimento");
    ConsoleUi.Info("  2) Importar um sistema de RPG (livros + ficha)");
    ConsoleUi.Info("  3) Processar um sistema (Agente Configurador)");
    ConsoleUi.Info("  4) Criar um personagem (Agente Dungeon Master)");
    ConsoleUi.Info($"  5) Verificar o Claude Code   [{contexto.DescreverClaudeCode()}]");
    ConsoleUi.Info("  0) Sair");

    var escolha = ConsoleUi.LerLinha("\nEscolha: ");

    cancelamentoAtual?.Dispose();
    cancelamentoAtual = new CancellationTokenSource();

    try
    {
        switch (escolha)
        {
            case "1":
                FluxoDeSistemas.Executar(contexto);
                ConsoleUi.Pausar();
                break;

            case "2":
                await FluxoDeImportacao.ExecutarAsync(contexto, cancelamentoAtual.Token);
                ConsoleUi.Pausar();
                break;

            case "3":
                await FluxoDoConfigurador.ExecutarAsync(contexto, cancelamentoAtual.Token);
                ConsoleUi.Pausar();
                break;

            case "4":
                await FluxoDeCriacaoDePersonagem.ExecutarAsync(contexto, cancelamentoAtual.Token);
                ConsoleUi.Pausar();
                break;

            case "5":
                await FluxoDoClaudeCode.ExecutarAsync(contexto, cancelamentoAtual.Token);
                ConsoleUi.Pausar();
                break;

            case "0" or "":
                ConsoleUi.Info("Até a próxima.");
                return 0;

            default:
                ConsoleUi.Aviso("Opção inválida.");
                break;
        }
    }
    catch (OperationCanceledException)
    {
        ConsoleUi.Aviso("\nOperação cancelada.");
        ConsoleUi.Pausar();
    }
}
