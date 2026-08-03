using MainForge.Agents;
using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Cli;

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
            sistema => DescreverSistema(contexto.Caminhos, sistema));

        if (escolhido is null)
        {
            return;
        }

        var opcoes = contexto.ExigirClaudeCode();

        if (opcoes is null)
        {
            return;
        }

        using var sessao = new SessaoDeAgente(opcoes, DefinicaoDeAgente.DungeonMaster, contexto.Caminhos);

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
                ConsoleUi.Detalhe($"Conversa encerrada. Ela fica guardada no Claude Code — dá para revê-la com 'claude --resume {sessao.IdDaSessao}'.");
                return;
            }
        }
    }

    /// <summary>
    /// Avisa antes da conversa o que só apareceria no meio dela: sem ficha em Templates/ não
    /// sai PDF nenhum, e com processamento pela metade o agente vai esbarrar em regra que não
    /// foi extraída.
    /// </summary>
    private static string DescreverSistema(CaminhosDoProjeto caminhos, SistemaRpg sistema)
    {
        var avisos = new List<string>();

        if (!Directory.Exists(sistema.DiretorioModelo(caminhos)))
        {
            avisos.Add("sem ficha em Templates/ — não dá para gerar o PDF");
        }

        var estado = EstadoDoProcessamento.Carregar(caminhos, sistema.Id);
        estado.SincronizarComDisco();

        if (estado.Pendentes.Count > 0 || estado.LivrosPendentes.Count > 0)
        {
            avisos.Add($"processamento incompleto: {estado.Resumo()}");
        }

        return avisos.Count == 0 ? sistema.Id : $"{sistema.Id}  ({string.Join("; ", avisos)})";
    }

    private static HashSet<string> FichasEmSaida(CaminhosDoProjeto caminhos) =>
        Directory.Exists(caminhos.SaidaPersonagens)
            ? [.. Directory.EnumerateFiles(caminhos.SaidaPersonagens, "*.pdf", SearchOption.TopDirectoryOnly)]
            : [];
}
