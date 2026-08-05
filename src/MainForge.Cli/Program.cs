using MainForge.Cli;
using MainForge.Core;

// Modo servidor MCP: o mesmo executável atende os dois papéis.
//
// Não é economia de arquivo — é o que torna possível distribuir o aplicativo como um binário
// só. O servidor MCP é lançado pelo Claude Code como processo à parte; enquanto ele era um
// segundo executável, o download precisava carregar dois programas (e, num publish
// self-contained, dois runtimes .NET inteiros) que ainda por cima podiam ficar em versões
// diferentes. Aqui eles são sempre o mesmo binário.
if (args is ["--mcp", ..])
{
    return await ModoServidorMcp.RodarAsync(args.Length > 1 ? args[1] : null);
}

// Interface do MainForge em modo console. A interface gráfica (WPF, projeto MainForge.App)
// virá depois — os fluxos de negócio moram nos agentes e nas ferramentas, não aqui, então
// trocar de interface não exige reescrever nada disso.
ConsoleUi.Preparar();

var caminhos = CaminhosDoProjeto.Descobrir();

// Instalação recém-baixada não tem as pastas de dados. Criá-las aqui é o que faz o aplicativo
// funcionar no primeiro clique, em vez de reclamar de diretório inexistente antes de o usuário
// ter feito nada.
var pastasCriadas = caminhos.GarantirEstrutura();

var contexto = new ContextoDoAplicativo(caminhos);

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

if (pastasCriadas.Count > 0)
{
    ConsoleUi.Detalhe($"Pastas criadas agora: {string.Join(", ", pastasCriadas)}");
}

if (!contexto.TemClaudeCode)
{
    ConsoleUi.Aviso("\nClaude Code não encontrado — os agentes não vão rodar. Veja as opções 6 e 7.");
}

while (true)
{
    ConsoleUi.Titulo("Menu principal");
    ConsoleUi.Info("  1) Ver sistemas e base de conhecimento");
    ConsoleUi.Info("  2) Importar um sistema de RPG (livros + ficha)");
    ConsoleUi.Info("  3) Adicionar livro a um sistema (compêndio/expansão)");
    ConsoleUi.Info("  4) Processar um sistema (Agente Configurador)");
    ConsoleUi.Info("  5) Criar um personagem (Agente Dungeon Master)");
    ConsoleUi.Info($"  6) Verificar o Claude Code   [{contexto.DescreverClaudeCode()}]");
    ConsoleUi.Info("  7) Dependências do aplicativo");
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
                await FluxoDeAdicaoDeLivro.ExecutarAsync(contexto, cancelamentoAtual.Token);
                ConsoleUi.Pausar();
                break;

            case "4":
                await FluxoDoConfigurador.ExecutarAsync(contexto, cancelamentoAtual.Token);
                ConsoleUi.Pausar();
                break;

            case "5":
                await FluxoDeCriacaoDePersonagem.ExecutarAsync(contexto, cancelamentoAtual.Token);
                ConsoleUi.Pausar();
                break;

            case "6":
                await FluxoDoClaudeCode.ExecutarAsync(contexto, cancelamentoAtual.Token);
                ConsoleUi.Pausar();
                break;

            case "7":
                await FluxoDeDependencias.ExecutarAsync(contexto, cancelamentoAtual.Token);
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
