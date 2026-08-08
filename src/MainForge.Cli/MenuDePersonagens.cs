using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Cli;

/// <summary>
/// Tudo que se faz com personagem numa tela só: ver os que existem, criar, continuar um que
/// ficou pela metade, evoluir um pronto e descontinuar o que não vai mais ser jogado.
///
/// <para><b>Por que estão juntos.</b> São a mesma pergunta feita em momentos diferentes — "o que
/// eu faço com este personagem?". Separadas em opções de menu principal, cada uma obrigava o
/// usuário a saber de antemão em que estado o personagem estava para escolher a porta certa;
/// aqui a lista mostra o estado e as ações se aplicam a ele.</para>
/// </summary>
internal static class MenuDePersonagens
{
    public static async Task ExecutarAsync(ContextoDoAplicativo contexto, CancellationToken cancelamento)
    {
        while (true)
        {
            var personagens = RepositorioDePersonagens.Listar(contexto.Caminhos);

            Listar(personagens);

            ConsoleUi.Titulo("Personagens");
            ConsoleUi.Info("  1) Criar personagem");
            ConsoleUi.Info("  2) Importar personagem (ficha em PDF ou pacote)");
            ConsoleUi.Info("  3) Continuar um em desenvolvimento");
            ConsoleUi.Info("  4) Evoluir ou alterar um pronto (nível, inventário, correções)");
            ConsoleUi.Info("  5) Exportar personagem (com o histórico de níveis)");
            ConsoleUi.Info("  6) Descontinuar ou reativar");
            ConsoleUi.Info("  0) Voltar");

            switch (ConsoleUi.LerLinha("\nEscolha: "))
            {
                case "1":
                    await CriarAsync(contexto, cancelamento);
                    break;

                case "2":
                    await FluxoDeImportacaoDePersonagem.ExecutarAsync(contexto, cancelamento);
                    break;

                case "3":
                    await ContinuarAsync(contexto, personagens, cancelamento);
                    break;

                case "4":
                    await EvoluirAsync(contexto, personagens, cancelamento);
                    break;

                case "5":
                    FluxoDePacoteDePersonagem.Exportar(contexto, personagens);
                    break;

                case "6":
                    MudarStatus(contexto, personagens);
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

    private static void Listar(IReadOnlyList<Personagem> personagens)
    {
        ConsoleUi.Titulo("Seus personagens");

        if (personagens.Count == 0)
        {
            ConsoleUi.Detalhe("Nenhum ainda. A opção 1 começa o primeiro; a 2 traz um que já existe em PDF.");
            return;
        }

        ConsoleUi.Info("");
        ConsoleUi.Info($"  {"Personagem",-24} {"Sistema",-18} {"Situação",-16} {"Atualizado",-12} Resumo");
        ConsoleUi.Detalhe($"  {new string('-', 24)} {new string('-', 18)} {new string('-', 16)} {new string('-', 12)} {new string('-', 40)}");

        foreach (var personagem in personagens)
        {
            var linha =
                $"  {Encurtar(personagem.Rotulo, 24),-24} {Encurtar(personagem.Sistema, 18),-18} " +
                $"{Personagem.DescreverStatus(personagem.Status),-16} {personagem.AtualizadoEm,-12:dd/MM/yyyy} " +
                Encurtar(personagem.Resumo, 44);

            switch (personagem.Status)
            {
                case StatusDoPersonagem.Concluido:
                    ConsoleUi.Sucesso(linha);
                    break;
                case StatusDoPersonagem.Descontinuado:
                    ConsoleUi.Detalhe(linha);
                    break;
                default:
                    ConsoleUi.Info(linha);
                    break;
            }
        }

        var emAndamento = personagens.Count(personagem => personagem.Status == StatusDoPersonagem.Desenvolvendo);

        if (emAndamento > 0)
        {
            ConsoleUi.Info("");
            ConsoleUi.Aviso($"{emAndamento} personagem(ns) em desenvolvimento — a opção 3 continua de onde parou.");
        }

        var consumo = personagens.Aggregate(ConsumoDeTokens.Zero, (total, personagem) => total + personagem.Consumo);

        if (!consumo.Vazio)
        {
            ConsoleUi.Info("");
            ConsoleUi.Detalhe($"Cota consumida por todos eles: {consumo.Descrever()}.");
        }
    }

    private static async Task CriarAsync(ContextoDoAplicativo contexto, CancellationToken cancelamento)
    {
        var caminhos = contexto.Caminhos;
        var prontos = SistemaRpg.DescobrirProntos(caminhos);

        if (prontos.Count == 0)
        {
            ConsoleUi.Aviso("Nenhum sistema tem base de conhecimento ainda.");
            ConsoleUi.Info("Processe um sistema no menu 'Sistemas' antes — é ela que o Dungeon Master usa.");
            return;
        }

        var sistema = ConsoleUi.Escolher(
            "Criar personagem em qual sistema?",
            prontos,
            escolhido => DescreverSistema(caminhos, escolhido));

        if (sistema is null)
        {
            return;
        }

        var fontes = FluxoDePersonagem.PerguntarFontes(caminhos, sistema);

        if (fontes is null)
        {
            return;
        }

        ConsoleUi.Info("");
        ConsoleUi.Detalhe("O nome vira a pasta do personagem e pode mudar depois, na conversa.");
        var nome = ConsoleUi.LerLinha("Nome (ou apelido) do personagem: ");

        if (nome.Length == 0)
        {
            nome = $"Rascunho-{DateTime.Now:yyyyMMdd-HHmm}";
            ConsoleUi.Detalhe($"Sem nome por enquanto: vai como '{nome}'.");
        }

        var personagem = RepositorioDePersonagens.Criar(
            caminhos,
            sistema.Id,
            nome,
            [.. fontes.Escolhidas.Select(fonte => fonte.Id)]);

        await FluxoDePersonagem.ExecutarAsync(contexto, personagem, ModoDaConversa.Criacao, null, cancelamento);
    }

    private static async Task ContinuarAsync(
        ContextoDoAplicativo contexto,
        IReadOnlyList<Personagem> personagens,
        CancellationToken cancelamento)
    {
        var pendentes = personagens
            .Where(personagem => personagem.Status == StatusDoPersonagem.Desenvolvendo)
            .ToList();

        if (pendentes.Count == 0)
        {
            ConsoleUi.Info("Nenhum personagem em desenvolvimento.");
            return;
        }

        var escolhido = ConsoleUi.Escolher("Continuar qual personagem?", pendentes, Descrever);

        if (escolhido is null)
        {
            return;
        }

        MostrarDossie(contexto.Caminhos, escolhido);

        await FluxoDePersonagem.ExecutarAsync(contexto, escolhido, ModoDaConversa.Retomada, null, cancelamento);
    }

    private static async Task EvoluirAsync(
        ContextoDoAplicativo contexto,
        IReadOnlyList<Personagem> personagens,
        CancellationToken cancelamento)
    {
        var prontos = personagens
            .Where(personagem => personagem.Status == StatusDoPersonagem.Concluido)
            .ToList();

        if (prontos.Count == 0)
        {
            ConsoleUi.Info("Nenhum personagem concluído ainda.");
            ConsoleUi.Detalhe("Um personagem passa a 'concluído' quando a ficha em PDF dele é gerada.");
            return;
        }

        var escolhido = ConsoleUi.Escolher("Alterar qual personagem?", prontos, Descrever);

        if (escolhido is null)
        {
            return;
        }

        var pedido = EscolherAlteracao();

        if (pedido is null)
        {
            return;
        }

        MostrarDossie(contexto.Caminhos, escolhido);
        MostrarHistoricoDeFichas(escolhido);

        ConsoleUi.Info("");
        ConsoleUi.Detalhe("A ficha em PDF é gerada de novo ao final, com os valores atualizados.");

        await FluxoDePersonagem.ExecutarAsync(contexto, escolhido, ModoDaConversa.Evolucao, pedido, cancelamento);
    }

    /// <summary>
    /// O que vai mudar no personagem. As opções prontas existem para a conversa já começar no
    /// assunto certo — o agente sabe de saída se vai conferir progressão de nível ou tabela de
    /// preços, em vez de gastar um turno perguntando.
    /// </summary>
    private static string? EscolherAlteracao()
    {
        List<OpcaoDeAlteracao> opcoes =
        [
            new("Subir de nível (progressão, magias, características novas)",
                "Quero subir este personagem de nível. Confira na base o que a progressão dá neste nível — " +
                "pontos de vida, características de classe, magias, perícias — e me pergunte o que for escolha minha."),
            new("Mudar equipamento ou inventário",
                "Quero mexer no equipamento/inventário deste personagem. Confira as regras de custo, carga e " +
                "requisitos antes de aceitar cada item."),
            new("Corrigir ou completar dados da ficha",
                "Quero corrigir dados deste personagem. Confira cada mudança contra as regras antes de aplicá-la."),
            new("Outra coisa (eu digito)", null),
        ];

        var escolha = ConsoleUi.Escolher("O que muda neste personagem?", opcoes, opcao => opcao.Rotulo);

        if (escolha is null)
        {
            return null;
        }

        if (escolha.Pedido is { } pronto)
        {
            return pronto;
        }

        var digitado = ConsoleUi.LerLinha("\nO que você quer mudar: ");

        return digitado.Length > 0 ? digitado : null;
    }

    private sealed record OpcaoDeAlteracao(string Rotulo, string? Pedido);

    private static void MudarStatus(ContextoDoAplicativo contexto, IReadOnlyList<Personagem> personagens)
    {
        if (personagens.Count == 0)
        {
            ConsoleUi.Info("Nenhum personagem ainda.");
            return;
        }

        var escolhido = ConsoleUi.Escolher("Mudar a situação de qual personagem?", personagens, Descrever);

        if (escolhido is null)
        {
            return;
        }

        if (escolhido.Status == StatusDoPersonagem.Descontinuado)
        {
            ConsoleUi.Info("");
            ConsoleUi.Info($"'{escolhido.Rotulo}' está descontinuado.");

            var voltaPara = escolhido.FichaGerada is null
                ? StatusDoPersonagem.Desenvolvendo
                : StatusDoPersonagem.Concluido;

            if (ConsoleUi.Confirmar($"Reativar como {Personagem.DescreverStatus(voltaPara)}?"))
            {
                RepositorioDePersonagens.MudarStatus(contexto.Caminhos, escolhido, voltaPara);
                ConsoleUi.Sucesso($"'{escolhido.Rotulo}' reativado.");
            }

            return;
        }

        ConsoleUi.Info("");
        ConsoleUi.Detalhe("Descontinuar só tira o personagem das listas de trabalho — nada é apagado do disco,");
        ConsoleUi.Detalhe("e dá para reativá-lo por aqui a qualquer momento.");

        if (ConsoleUi.Confirmar($"Descontinuar '{escolhido.Rotulo}'?"))
        {
            RepositorioDePersonagens.MudarStatus(contexto.Caminhos, escolhido, StatusDoPersonagem.Descontinuado);
            ConsoleUi.Sucesso($"'{escolhido.Rotulo}' descontinuado.");
        }
    }

    /// <summary>
    /// Mostra o começo do que o agente já escreveu sobre o personagem. É o que responde "de onde
    /// eu vou continuar?" antes de a conversa (e o gasto de cota) começar.
    /// </summary>
    private static void MostrarDossie(CaminhosDoProjeto caminhos, Personagem personagem)
    {
        var texto = RepositorioDePersonagens.LerFichaEmTexto(caminhos, personagem.Sistema, personagem.Id);

        if (texto.Length == 0)
        {
            ConsoleUi.Info("");
            ConsoleUi.Aviso("O agente ainda não gravou o estado deste personagem.");
            ConsoleUi.Detalhe("A conversa anterior no Claude Code, se ainda existir, é o que vai repor o contexto.");
            return;
        }

        var linhas = texto.ReplaceLineEndings("\n").Split('\n');

        ConsoleUi.Titulo($"Onde '{personagem.Rotulo}' parou");

        foreach (var linha in linhas.Take(20))
        {
            ConsoleUi.Detalhe(linha);
        }

        if (linhas.Length > 20)
        {
            ConsoleUi.Detalhe($"... e mais {linhas.Length - 20} linha(s) em Personagens/{personagem.Sistema}/{personagem.Id}/{RepositorioDePersonagens.NomeDaFichaEmTexto}.");
        }
    }

    /// <summary>
    /// As fichas já guardadas, uma por nível. Aparece antes de evoluir porque é a informação que
    /// muda a decisão: quem vai subir de nível quer saber de que nível está saindo, e quem errou
    /// a evolução anterior precisa saber que o PDF de antes ainda existe.
    /// </summary>
    private static void MostrarHistoricoDeFichas(Personagem personagem)
    {
        if (personagem.Fichas.Count == 0)
        {
            return;
        }

        ConsoleUi.Info("");
        ConsoleUi.Info($"Fichas guardadas de '{personagem.Rotulo}':");

        foreach (var ficha in personagem.Fichas)
        {
            ConsoleUi.Detalhe($"  {ficha.Rotulo,-24} {ficha.Em:dd/MM/yyyy}  {ficha.Arquivo}");
        }

        ConsoleUi.Detalhe($"A ficha atual continua em {personagem.FichaGerada} — em Output/ fica só ela.");
    }

    private static string Descrever(Personagem personagem)
    {
        var detalhes = new List<string> { personagem.Sistema };

        if (personagem.Resumo.Length > 0)
        {
            detalhes.Add(personagem.Resumo);
        }

        detalhes.Add($"atualizado em {personagem.AtualizadoEm:dd/MM/yyyy}");

        return $"{personagem.Rotulo}  ({string.Join("; ", detalhes)})";
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

    private static string Encurtar(string texto, int largura) =>
        texto.Length <= largura ? texto : string.Concat(texto.AsSpan(0, largura - 3), "...");
}
