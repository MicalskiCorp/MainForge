using RpgForge.Agents;
using RpgForge.Core;

namespace RpgForge.Cli;

/// <summary>
/// Conversa com o Agente Dungeon Master para criar um personagem. O usuário escolhe o
/// sistema no menu e a partir daí é uma conversa livre: o agente conduz, pergunta, valida as
/// escolhas contra a base de conhecimento e, no fim, gera a ficha em PDF.
/// </summary>
internal static class FluxoDeCriacaoDePersonagem
{
    public static async Task ExecutarAsync(ContextoDoAplicativo contexto, CancellationToken cancelamento)
    {
        var prontos = SistemaRpg.DescobrirProntos(contexto.Caminhos);

        if (prontos.Count == 0)
        {
            ConsoleUi.Aviso("Nenhum sistema tem base de conhecimento ainda.");
            ConsoleUi.Info("Use a opção de processar um sistema (Agente Configurador) primeiro.");
            return;
        }

        var escolhido = ConsoleUi.Escolher(
            "Criar personagem em qual sistema?",
            prontos,
            sistema => Directory.Exists(sistema.DiretorioModelo(contexto.Caminhos))
                ? sistema.Id
                : $"{sistema.Id}  (sem ficha em Templates/ — não dá para gerar o PDF)");

        if (escolhido is null)
        {
            return;
        }

        var mensagens = contexto.ObterServicoDeMensagens();

        if (mensagens is null)
        {
            return;
        }

        var sessao = new SessaoDeAgente(mensagens, DefinicaoDeAgente.DungeonMaster, contexto.Caminhos, contexto.Opcoes!);

        Directory.CreateDirectory(contexto.Caminhos.SaidaPersonagens);
        var fichasConhecidas = FichasEmSaida(contexto.Caminhos);

        ConsoleUi.Titulo($"Dungeon Master — criando personagem em '{escolhido.Id}'");
        ConsoleUi.Detalhe("Converse normalmente. Digite /sair para encerrar a conversa e voltar ao menu.");

        // A primeira mensagem é automática: o sistema já foi escolhido no menu, então não faz
        // sentido o agente começar perguntando qual é.
        var proximaMensagem =
            $"Quero criar um personagem no sistema '{escolhido.Id}'. Me conduza pelo processo, " +
            "um passo de cada vez, seguindo as regras desse sistema.";

        while (true)
        {
            ConsoleUi.Info("");
            ProgressoDoAgente.Pensando("O Dungeon Master");

            string resposta;

            try
            {
                resposta = await sessao.EnviarAsync(proximaMensagem, ProgressoDoAgente.Impressora(), cancelamento);
            }
            catch (Exception excecao) when (excecao is not OperationCanceledException)
            {
                ConsoleUi.Erro($"Falha na conversa: {excecao.Message}");
                return;
            }

            ConsoleUi.Info("");
            ConsoleUi.EscreverColorido("Dungeon Master:", ConsoleColor.Magenta);
            ConsoleUi.Info(resposta);

            var novas = FichasEmSaida(contexto.Caminhos).Except(fichasConhecidas).ToList();

            if (novas.Count > 0)
            {
                fichasConhecidas = FichasEmSaida(contexto.Caminhos);

                foreach (var ficha in novas)
                {
                    ConsoleUi.Sucesso($"\nFicha gerada: {Path.GetRelativePath(contexto.Caminhos.Raiz, ficha)}");
                }

                if (!ConsoleUi.Confirmar("Continuar a conversa?"))
                {
                    return;
                }
            }

            ConsoleUi.Info("");
            proximaMensagem = ConsoleUi.LerLinha("Você: ");

            if (proximaMensagem.Length == 0)
            {
                proximaMensagem = "Pode continuar.";
            }

            if (proximaMensagem.Equals("/sair", StringComparison.OrdinalIgnoreCase))
            {
                ConsoleUi.Detalhe("Conversa encerrada. O histórico dela não é salvo.");
                return;
            }
        }
    }

    private static HashSet<string> FichasEmSaida(CaminhosDoProjeto caminhos) =>
        Directory.Exists(caminhos.SaidaPersonagens)
            ? [.. Directory.EnumerateFiles(caminhos.SaidaPersonagens, "*.pdf", SearchOption.TopDirectoryOnly)]
            : [];
}
