using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Cli;

/// <summary>
/// Tudo que se faz com sistema de RPG numa tela só: ver o que existe, trazer um novo,
/// acrescentar livros, processar (ou continuar processando) e levar um sistema pronto para
/// outra máquina.
///
/// <para><b>Por que estão juntos.</b> Eram cinco opções do menu principal que só faziam sentido
/// em sequência — importar, adicionar, processar — e obrigavam quem abria o aplicativo a
/// conhecer a ordem antes de escolher. Aqui o panorama vem primeiro e as ações vêm depois dele,
/// que é a ordem em que a pergunta aparece na cabeça de quem está usando.</para>
/// </summary>
internal static class MenuDeSistemas
{
    public static async Task ExecutarAsync(ContextoDoAplicativo contexto, CancellationToken cancelamento)
    {
        while (true)
        {
            FluxoDeSistemas.Executar(contexto);

            var incompletos = Incompletos(contexto.Caminhos);

            ConsoleUi.Titulo("Sistemas");
            ConsoleUi.Info("  1) Novo sistema (livros do jogo base + ficha)");
            ConsoleUi.Info("  2) Adicionar livros a um sistema (expansão ou jogo base)");
            ConsoleUi.Info(incompletos.Count == 0
                ? "  3) Processar um sistema"
                : $"  3) Processar — {incompletos.Count} sistema(s) com processamento incompleto");
            ConsoleUi.Info("  4) Exportar um sistema pronto");
            ConsoleUi.Info("  5) Importar um pacote de sistema");
            ConsoleUi.Info("  0) Voltar");

            switch (ConsoleUi.LerLinha("\nEscolha: "))
            {
                case "1":
                    await FluxoDeImportacao.ExecutarAsync(contexto, cancelamento);
                    break;

                case "2":
                    await FluxoDeAdicaoDeLivro.ExecutarAsync(contexto, cancelamento);
                    break;

                case "3":
                    await ProcessarAsync(contexto, cancelamento);
                    break;

                case "4":
                    FluxoDePacotes.Exportar(contexto);
                    break;

                case "5":
                    FluxoDePacotes.Importar(contexto);
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

    /// <summary>
    /// Escolhe o sistema a processar, com os que ficaram pela metade no topo da lista.
    ///
    /// <para>A ordem não é estética: um sistema incompleto é o único caso em que processar de
    /// novo é barato — o agente lê só o que falta. Deixá-lo misturado à lista fazia a retomada
    /// parecer tão cara quanto um processamento novo, e o usuário adiava por isso.</para>
    /// </summary>
    private static async Task ProcessarAsync(ContextoDoAplicativo contexto, CancellationToken cancelamento)
    {
        var caminhos = contexto.Caminhos;
        var sistemas = SistemaRpg.DescobrirImportados(caminhos);

        if (sistemas.Count == 0)
        {
            ConsoleUi.Aviso("Nenhum sistema importado ainda — comece pela opção 1.");
            return;
        }

        var incompletos = Incompletos(caminhos).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ordenados = sistemas
            .OrderByDescending(sistema => incompletos.Contains(sistema.Id))
            .ThenBy(sistema => sistema.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (incompletos.Count > 0)
        {
            ConsoleUi.Info("");
            ConsoleUi.Aviso("Continuar um processamento incompleto lê só o que falta — não relê os livros.");
        }

        var escolhido = ConsoleUi.Escolher(
            "Qual sistema processar?",
            ordenados,
            sistema => Descrever(caminhos, sistema, incompletos.Contains(sistema.Id)));

        if (escolhido is null)
        {
            return;
        }

        await FluxoDoConfigurador.ExecutarAsync(contexto, cancelamento, escolhido);
    }

    private static string Descrever(CaminhosDoProjeto caminhos, SistemaRpg sistema, bool incompleto)
    {
        if (!sistema.TemConhecimento(caminhos))
        {
            return $"{sistema.Id}  (nunca processado)";
        }

        var estado = EstadoDoProcessamento.Carregar(caminhos, sistema.Id);
        estado.SincronizarComDisco();

        return incompleto
            ? $"{sistema.Id}  (INCOMPLETO: {estado.Resumo()} — continuar de onde parou)"
            : $"{sistema.Id}  ({estado.Resumo()})";
    }

    /// <summary>Os sistemas com arquivo do plano ou livro ainda pendente.</summary>
    private static IReadOnlyList<string> Incompletos(CaminhosDoProjeto caminhos)
    {
        var incompletos = new List<string>();

        foreach (var sistema in SistemaRpg.DescobrirImportados(caminhos))
        {
            if (!sistema.TemConhecimento(caminhos))
            {
                continue; // nunca processado não é "incompleto": é um começo, e caro
            }

            var estado = EstadoDoProcessamento.Carregar(caminhos, sistema.Id);
            estado.SincronizarComDisco();

            if (estado.Pendentes.Count > 0 || estado.LivrosPendentes.Count > 0)
            {
                incompletos.Add(sistema.Id);
            }
        }

        return incompletos;
    }
}
