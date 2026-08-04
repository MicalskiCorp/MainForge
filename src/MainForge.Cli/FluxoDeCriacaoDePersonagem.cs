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

        var fontes = EscolherFontes(contexto.Caminhos, escolhido);

        if (fontes is null)
        {
            return;
        }

        var opcoes = contexto.ExigirClaudeCode();

        if (opcoes is null)
        {
            return;
        }

        var agente = DefinicaoDeAgente.DungeonMasterLimitadoA(escolhido, fontes.Recusadas);

        using var sessao = new SessaoDeAgente(opcoes, agente, contexto.Caminhos);

        Directory.CreateDirectory(contexto.Caminhos.SaidaPersonagens);
        var fichasConhecidas = FichasEmSaida(contexto.Caminhos);

        ConsoleUi.Titulo($"Dungeon Master — criando personagem em '{escolhido.Id}'");
        ConsoleUi.Detalhe("Converse normalmente. Digite /sair para encerrar a conversa e voltar ao menu.");

        var proximaMensagem = PrimeiraMensagem(escolhido, fontes);

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

    /// <summary>As fontes que valem nesta mesa e as que ficaram de fora.</summary>
    private sealed record FontesDaMesa(
        IReadOnlyList<FonteDoSistema> Escolhidas,
        IReadOnlyList<FonteDoSistema> Recusadas);

    /// <summary>
    /// Pergunta quais expansões valem para este personagem. O jogo base entra sempre — é o que
    /// define o sistema —, então a pergunta é só sobre o que é opcional.
    ///
    /// <para>Não é uma pergunta de conveniência: cada mesa combina quais compêndios estão em
    /// jogo, e um personagem com uma subclasse de um livro que o grupo não usa é um personagem
    /// inválido. O que não for escolhido aqui vira negação de leitura, então o agente não
    /// consegue oferecê-lo nem por engano.</para>
    ///
    /// <para>Devolve <c>null</c> quando o usuário desiste, e uma seleção vazia de expansões
    /// quando ele quer só o jogo base.</para>
    /// </summary>
    private static FontesDaMesa? EscolherFontes(CaminhosDoProjeto caminhos, SistemaRpg sistema)
    {
        var comConhecimento = sistema.DescobrirFontesComConhecimento(caminhos);
        var expansoes = comConhecimento.Where(fonte => !fonte.EhBase).ToList();

        // Sistema ainda no layout antigo, sem pasta de fonte: tudo que existe vale, e não há
        // escolha a fazer. Continuar sem nenhuma fonte deixaria o agente sem base nenhuma.
        if (comConhecimento.Count == 0 || expansoes.Count == 0)
        {
            return new FontesDaMesa(comConhecimento, []);
        }

        var escolhidas = ConsoleUi.EscolherVarios(
            $"Quais expansões de '{sistema.Id}' esta mesa usa?",
            expansoes,
            fonte => $"{fonte.Id}  ({DescreverFonte(caminhos, sistema, fonte)})");

        ConsoleUi.Info("");
        ConsoleUi.Sucesso(escolhidas.Count == 0
            ? "Só o jogo base."
            : $"Jogo base + {string.Join(", ", escolhidas.Select(fonte => fonte.Id))}.");

        var recusadas = expansoes.Except(escolhidas).ToList();

        if (recusadas.Count > 0)
        {
            ConsoleUi.Detalhe($"Fora desta mesa: {string.Join(", ", recusadas.Select(fonte => fonte.Id))} — o agente não vai conseguir ler.");
        }

        return new FontesDaMesa([FonteDoSistema.Base, .. escolhidas], recusadas);
    }

    /// <summary>
    /// A primeira mensagem é automática: o sistema e as expansões já foram escolhidos no menu,
    /// então não faz sentido o agente começar perguntando. Os caminhos vão escritos por extenso
    /// porque deduzi-los do nome do sistema é onde o agente erra — nome com '&amp;' ou acento
    /// vira uma leitura recusada antes de a conversa começar.
    /// </summary>
    private static string PrimeiraMensagem(SistemaRpg sistema, FontesDaMesa fontes)
    {
        var texto = new System.Text.StringBuilder()
            .AppendLine($"Quero criar um personagem no sistema '{sistema.Id}'.")
            .AppendLine()
            .AppendLine("Esta mesa usa exatamente estas fontes de regra, e nenhuma outra:");

        foreach (var fonte in fontes.Escolhidas)
        {
            texto.AppendLine($"- {fonte.Rotulo}: Knowledge/{sistema.Id}/{fonte.Id}/index.md");
        }

        if (fontes.Recusadas.Count > 0)
        {
            texto
                .AppendLine()
                .AppendLine("Fora desta mesa (a leitura destas pastas está negada, não tente abri-las):")
                .AppendLine(string.Join(", ", fontes.Recusadas.Select(fonte => $"Knowledge/{sistema.Id}/{fonte.Id}/")));
        }

        return texto
            .AppendLine()
            .AppendLine($"A ficha do sistema está em Knowledge/{sistema.Id}/, fora das pastas de fonte:")
            .AppendLine($"{string.Join(" e ", SistemaRpg.ArquivosDaFicha)}.")
            .AppendLine()
            .AppendLine("Me conduza pelo processo, um passo de cada vez, seguindo as regras dessas fontes.")
            .ToString();
    }

    private static string DescreverFonte(CaminhosDoProjeto caminhos, SistemaRpg sistema, FonteDoSistema fonte)
    {
        var diretorio = sistema.DiretorioConhecimentoDaFonte(caminhos, fonte);

        var arquivos = Directory
            .EnumerateFiles(diretorio, "*.md", SearchOption.AllDirectories)
            .Count(arquivo => !Path.GetFileName(arquivo).Equals(SistemaRpg.NomeDoIndice, StringComparison.OrdinalIgnoreCase));

        return $"{arquivos} arquivo(s) de regra";
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

        var expansoes = sistema.DescobrirFontesComConhecimento(caminhos).Count(fonte => !fonte.EhBase);

        if (expansoes > 0)
        {
            avisos.Add($"{expansoes} expansão(ões) disponível(is)");
        }

        return avisos.Count == 0 ? sistema.Id : $"{sistema.Id}  ({string.Join("; ", avisos)})";
    }

    private static HashSet<string> FichasEmSaida(CaminhosDoProjeto caminhos) =>
        Directory.Exists(caminhos.SaidaPersonagens)
            ? [.. Directory.EnumerateFiles(caminhos.SaidaPersonagens, "*.pdf", SearchOption.TopDirectoryOnly)]
            : [];
}
