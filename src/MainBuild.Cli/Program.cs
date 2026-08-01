using MainBuild.Claude;
using MainBuild.Cli;
using MainBuild.Core;

// Interface do MainBuild em modo console. A interface gráfica (WPF, projeto MainBuild.App)
// virá depois — os fluxos de negócio moram nos agentes e nas ferramentas, não aqui, então
// trocar de interface não exige reescrever nada disso.

ConsoleUi.Preparar();

var contexto = new ContextoDoAplicativo(
    CaminhosDoProjeto.Descobrir(),
    new ArmazenamentoDeChaveApi());

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

ConsoleUi.Titulo("MainBuild — criação de fichas de RPG com a Claude API");
ConsoleUi.Detalhe($"Projeto: {contexto.Caminhos.Raiz}");

if (!contexto.TemChave)
{
    ConsoleUi.Aviso("\nNenhuma chave da Claude API configurada — comece pela opção 4.");
}

while (true)
{
    ConsoleUi.Titulo("Menu principal");
    ConsoleUi.Info("  1) Ver sistemas e base de conhecimento");
    ConsoleUi.Info("  2) Processar um sistema (Agente Configurador)");
    ConsoleUi.Info("  3) Criar um personagem (Agente Dungeon Master)");
    ConsoleUi.Info($"  4) Configurar a chave da Claude API   [{contexto.DescreverOrigemDaChave()}]");
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
                await FluxoDoConfigurador.ExecutarAsync(contexto, cancelamentoAtual.Token);
                ConsoleUi.Pausar();
                break;

            case "3":
                await FluxoDeCriacaoDePersonagem.ExecutarAsync(contexto, cancelamentoAtual.Token);
                ConsoleUi.Pausar();
                break;

            case "4":
                FluxoDaChaveApi.Executar(contexto);
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
