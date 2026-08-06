using System.Text;
using MainForge.Agents;
using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Cli;

/// <summary>Em que situação a conversa com o Dungeon Master está sendo aberta.</summary>
internal enum ModoDaConversa
{
    /// <summary>Personagem novo: da folha em branco até o PDF.</summary>
    Criacao,

    /// <summary>Criação interrompida antes do fim: continuar de onde parou.</summary>
    Retomada,

    /// <summary>Personagem pronto que vai mudar: subir de nível, trocar equipamento, corrigir algo.</summary>
    Evolucao,
}

/// <summary>
/// A conversa com o Agente Dungeon Master em torno de <b>um</b> personagem — criando-o,
/// retomando-o ou evoluindo-o. O agente conduz, valida as escolhas contra a base de
/// conhecimento e, no fim, gera a ficha em PDF.
///
/// <para><b>Por que os três modos moram no mesmo fluxo.</b> Depois da primeira mensagem eles são
/// a mesma coisa: uma conversa livre com o mesmo agente, as mesmas fontes e o mesmo dossiê sendo
/// atualizado. O que muda é só como a conversa começa — e é exatamente isso que decide se o
/// agente vai reler a base inteira ou continuar do que já está escrito.</para>
///
/// <para><b>O dossiê é o que sobrevive.</b> Cada bloco de decisões fechado vira uma gravação em
/// <c>Personagens/&lt;Sistema&gt;/&lt;Id&gt;/</c>, feita pelo próprio agente. Antes disso, fechar
/// a janela no meio de uma criação jogava fora tudo — a conversa vivia só dentro do processo do
/// Claude Code.</para>
/// </summary>
internal static class FluxoDePersonagem
{
    public static async Task ExecutarAsync(
        ContextoDoAplicativo contexto,
        Personagem personagem,
        ModoDaConversa modo,
        string? pedidoInicial,
        CancellationToken cancelamento)
    {
        var caminhos = contexto.Caminhos;
        var sistema = new SistemaRpg(personagem.Sistema);
        var fontes = FontesDaMesaDoPersonagem(caminhos, sistema, personagem);

        var opcoes = contexto.ExigirClaudeCode();

        if (opcoes is null)
        {
            return;
        }

        var agente = DefinicaoDeAgente.DungeonMasterLimitadoA(sistema, fontes.Escolhidas, fontes.Recusadas);

        // Retomar a conversa anterior traz de volta a base que o agente já leu — o gasto mais
        // caro desta conversa, e o único que não dá para refazer de graça. Quando ela não existe
        // mais do lado do Claude Code, a sessão recomeça sozinha e o ficha.md é o que a repõe.
        using var sessao = new SessaoDeAgente(
            opcoes,
            agente,
            caminhos,
            retomarSessao: modo == ModoDaConversa.Criacao ? null : personagem.IdDaSessao);

        Directory.CreateDirectory(caminhos.SaidaPersonagens);

        ConsoleUi.Titulo($"Dungeon Master — {Titulo(modo)} '{personagem.Rotulo}' ({sistema.Id})");
        ConsoleUi.Detalhe($"Dossiê: Personagens/{sistema.Id}/{personagem.Id}/");
        ConsoleUi.Detalhe("Converse normalmente. Digite /sair para encerrar — o que já foi decidido fica salvo.");

        if (modo != ModoDaConversa.Criacao && personagem.IdDaSessao is not null)
        {
            ConsoleUi.Detalhe("Retomando a conversa anterior; se ela tiver expirado, o agente parte do dossiê.");
        }

        var proximaMensagem = PrimeiraMensagem(modo, sistema, personagem, fontes, pedidoInicial, caminhos);
        var fichaConhecida = personagem.FichaGerada;

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
                RelatarOndeParou(caminhos, personagem);
                return;
            }

            // A cada turno, porque quem grava o dossiê é o agente pelas ferramentas: reler é como
            // a interface descobre que o personagem ganhou nome, resumo ou ficha.
            RepositorioDePersonagens.RegistrarSessao(caminhos, personagem, sessao.IdDaSessao);
            personagem = RepositorioDePersonagens.Carregar(caminhos, personagem.Sistema, personagem.Id) ?? personagem;

            ConsoleUi.Info("");
            ConsoleUi.EscreverColorido("Dungeon Master:", ConsoleColor.Magenta);
            ConsoleUi.Info(resposta);

            if (personagem.FichaGerada is { } ficha && ficha != fichaConhecida)
            {
                fichaConhecida = ficha;

                ConsoleUi.Sucesso($"\nFicha gerada: {ficha}");
                ConsoleUi.Detalhe($"Personagem marcado como {Personagem.DescreverStatus(personagem.Status)}.");

                if (!ConsoleUi.Confirmar("Continuar a conversa?"))
                {
                    return;
                }
            }

            ConsoleUi.Info("");
            var digitado = ConsoleUi.LerLinhaOuFim("Você: ");

            // Fim da entrada encerra a conversa como o /sair. Tratá-lo como linha vazia — que
            // aqui significa "pode continuar" — mandaria turno atrás de turno ao agente, gastando
            // a cota da assinatura sem ninguém na frente do console para ler a resposta.
            if (digitado is null || digitado.Equals("/sair", StringComparison.OrdinalIgnoreCase))
            {
                if (digitado is null)
                {
                    ConsoleUi.Info("");
                    ConsoleUi.Aviso("Entrada encerrada — fechando a conversa.");
                }

                RelatarOndeParou(caminhos, personagem);
                ConsoleUi.Detalhe($"A conversa fica guardada no Claude Code — dá para revê-la com 'claude --resume {sessao.IdDaSessao}'.");
                return;
            }

            proximaMensagem = digitado.Length == 0 ? "Pode continuar." : digitado;
        }
    }

    /// <summary>As fontes que valem nesta mesa e as que ficaram de fora.</summary>
    internal sealed record FontesDaMesa(
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
    public static FontesDaMesa? PerguntarFontes(CaminhosDoProjeto caminhos, SistemaRpg sistema)
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
    /// As fontes de um personagem que já existe, reconferidas com o disco.
    ///
    /// <para>A mesa foi decidida quando o personagem nasceu e vale para sempre: evoluir com uma
    /// expansão que a mesa não usava produziria um personagem que ninguém pode jogar. O que muda
    /// é o disco — uma expansão nova processada desde então precisa entrar na lista de recusadas,
    /// senão ela apareceria liberada só por não ter sido negada.</para>
    /// </summary>
    private static FontesDaMesa FontesDaMesaDoPersonagem(
        CaminhosDoProjeto caminhos,
        SistemaRpg sistema,
        Personagem personagem)
    {
        var existentes = sistema.DescobrirFontesComConhecimento(caminhos);

        var escolhidas = existentes
            .Where(fonte => fonte.EhBase || personagem.Fontes.Contains(fonte.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var recusadas = existentes.Except(escolhidas).ToList();

        return new FontesDaMesa(escolhidas, recusadas);
    }

    private static string Titulo(ModoDaConversa modo) => modo switch
    {
        ModoDaConversa.Criacao => "criando",
        ModoDaConversa.Retomada => "continuando",
        _ => "evoluindo",
    };

    /// <summary>
    /// A primeira mensagem é automática: o sistema, as expansões e o personagem já foram
    /// escolhidos no menu, então não faz sentido o agente começar perguntando. Os caminhos vão
    /// escritos por extenso porque deduzi-los do nome do sistema é onde o agente erra — nome com
    /// '&amp;' ou acento vira uma leitura recusada antes de a conversa começar.
    /// </summary>
    private static string PrimeiraMensagem(
        ModoDaConversa modo,
        SistemaRpg sistema,
        Personagem personagem,
        FontesDaMesa fontes,
        string? pedidoInicial,
        CaminhosDoProjeto caminhos)
    {
        var texto = new StringBuilder();

        switch (modo)
        {
            case ModoDaConversa.Criacao:
                texto
                    .AppendLine($"Quero criar um personagem no sistema '{sistema.Id}'.")
                    .AppendLine();
                break;

            case ModoDaConversa.Retomada:
                texto
                    .AppendLine($"Continue a criação do personagem '{personagem.Rotulo}' no sistema '{sistema.Id}'.")
                    .AppendLine()
                    .AppendLine("Ela foi interrompida. NÃO recomece: leia primeiro o dossiê abaixo, retome do ponto")
                    .AppendLine("em que ele parou e me diga em uma linha onde estávamos antes de seguir.")
                    .AppendLine();
                break;

            default:
                texto
                    .AppendLine($"O personagem '{personagem.Rotulo}' do sistema '{sistema.Id}' já está pronto, e vai mudar.")
                    .AppendLine()
                    .AppendLine("O que o usuário quer:")
                    .AppendLine(pedidoInicial is { Length: > 0 } ? pedidoInicial : "(ele vai dizer a seguir)")
                    .AppendLine()
                    .AppendLine("Leia o dossiê abaixo, confira nas regras o que essa mudança permite e o que ela")
                    .AppendLine("obriga, aplique só o que muda e mantenha o resto como está. No fim, faça a")
                    .AppendLine("conferência visual e gere a ficha em PDF de novo.")
                    .AppendLine();
                break;
        }

        texto
            .AppendLine($"Identificador do personagem: {personagem.Id}")
            .AppendLine($"É este o valor do parâmetro 'personagem' em registrar_personagem e em preencher_ficha_personagem.")
            .AppendLine();

        if (modo != ModoDaConversa.Criacao)
        {
            texto
                .AppendLine("Dossiê do personagem (leia antes de qualquer outra coisa):")
                .AppendLine($"- Personagens/{sistema.Id}/{personagem.Id}/{RepositorioDePersonagens.NomeDaFichaEmTexto}")
                .AppendLine();
        }

        texto.AppendLine("Esta mesa usa exatamente estas fontes de regra, e nenhuma outra:");

        foreach (var fonte in fontes.Escolhidas)
        {
            texto.AppendLine($"- {fonte.Rotulo}: Sistemas/{sistema.Id}/{fonte.Id}/index.md");
        }

        if (fontes.Recusadas.Count > 0)
        {
            texto
                .AppendLine()
                .AppendLine("Fora desta mesa (a leitura destas pastas está negada, não tente abri-las):")
                .AppendLine(string.Join(", ", fontes.Recusadas.Select(fonte => $"Sistemas/{sistema.Id}/{fonte.Id}/")));
        }

        texto
            .AppendLine()
            .AppendLine($"A ficha do sistema está em Sistemas/{sistema.Id}/, fora das pastas de fonte:")
            .AppendLine($"{string.Join(" e ", SistemaRpg.ArquivosDaFicha)}.")
            .AppendLine()
            .AppendLine("Grave o estado do personagem com registrar_personagem a cada bloco de decisões fechado —")
            .AppendLine("é o que permite retomar esta conversa se ela for interrompida.");

        if (modo == ModoDaConversa.Criacao)
        {
            texto
                .AppendLine()
                .AppendLine("Me conduza pelo processo, um passo de cada vez, seguindo as regras dessas fontes.");
        }

        return texto.ToString();
    }

    private static void RelatarOndeParou(CaminhosDoProjeto caminhos, Personagem personagem)
    {
        var atual = RepositorioDePersonagens.Carregar(caminhos, personagem.Sistema, personagem.Id) ?? personagem;

        ConsoleUi.Info("");

        var temDossie = RepositorioDePersonagens
            .LerFichaEmTexto(caminhos, atual.Sistema, atual.Id).Length > 0;

        ConsoleUi.Sucesso(temDossie
            ? $"'{atual.Rotulo}' está salvo como {Personagem.DescreverStatus(atual.Status)} — dá para continuar pelo menu de personagens."
            : $"'{atual.Rotulo}' ficou registrado, mas o agente ainda não gravou nada do estado dele.");

        if (!temDossie)
        {
            ConsoleUi.Detalhe("Retomar vai funcionar pela conversa guardada no Claude Code, se ela ainda existir.");
        }
    }

    private static string DescreverFonte(CaminhosDoProjeto caminhos, SistemaRpg sistema, FonteDoSistema fonte)
    {
        var diretorio = sistema.DiretorioConhecimentoDaFonte(caminhos, fonte);

        var arquivos = Directory
            .EnumerateFiles(diretorio, "*.md", SearchOption.AllDirectories)
            .Count(arquivo => !Path.GetFileName(arquivo).Equals(SistemaRpg.NomeDoIndice, StringComparison.OrdinalIgnoreCase));

        return $"{arquivos} arquivo(s) de regra";
    }
}
