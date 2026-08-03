using MainForge.Agents;
using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Cli;

/// <summary>
/// Roda o Agente Configurador sobre um sistema importado em Systems/, gerando a base de
/// conhecimento em Knowledge/. É a operação mais cara em tokens do aplicativo (o agente lê o
/// livro inteiro), por isso pede confirmação explícita antes de começar.
///
/// <para><b>Retomar em vez de recomeçar.</b> Como a execução é longa e cara, ela tem chance
/// real de ser interrompida no meio — cota esgotada, Ctrl+C, máquina desligada. O progresso é
/// registrado a cada arquivo gravado, então uma segunda execução começa perguntando o que
/// falta e gera só isso. Recomeçar do zero continua possível, mas passou a ser uma escolha
/// consciente do usuário em vez do comportamento padrão.</para>
/// </summary>
internal static class FluxoDoConfigurador
{
    /// <param name="sistemaEscolhido">
    /// Já vem preenchido quando o fluxo é chamado logo após uma importação ou uma adição de
    /// livro — nesse caso não faz sentido perguntar de novo qual sistema processar.
    /// </param>
    /// <param name="modoSugerido">
    /// Vem preenchido quando quem chama já sabe a situação (uma expansão recém-adicionada, por
    /// exemplo). Ainda assim é validado contra o que existe em disco.
    /// </param>
    public static async Task ExecutarAsync(
        ContextoDoAplicativo contexto,
        CancellationToken cancelamento,
        SistemaRpg? sistemaEscolhido = null,
        ModoDoConfigurador? modoSugerido = null)
    {
        var caminhos = contexto.Caminhos;
        var sistemas = SistemaRpg.DescobrirImportados(caminhos);

        if (sistemas.Count == 0)
        {
            ConsoleUi.Aviso("Nenhum sistema importado ainda.");
            ConsoleUi.Info("Use a opção \"Importar um sistema de RPG\" no menu principal.");
            return;
        }

        var escolhido = sistemaEscolhido ?? ConsoleUi.Escolher(
            "Qual sistema processar?",
            sistemas,
            sistema => DescreverEscolha(caminhos, sistema));

        if (escolhido is null)
        {
            return;
        }

        var pdfs = Directory
            .EnumerateFiles(escolhido.DiretorioSistemas(caminhos), "*.pdf", SearchOption.AllDirectories)
            .ToList();

        if (pdfs.Count == 0)
        {
            ConsoleUi.Erro($"Nenhum PDF em {escolhido.DiretorioSistemas(caminhos)}.");
            return;
        }

        // Reconstruir antes de olhar o estado indexa bases geradas por versões anteriores do
        // aplicativo (que não tinham index.md) sem gastar um token.
        IndiceDeConhecimento.Reconstruir(caminhos, escolhido.Id);

        var estado = EstadoDoProcessamento.Carregar(caminhos, escolhido.Id);
        estado.SincronizarComDisco();

        ConsoleUi.Titulo($"Processar '{escolhido.Id}'");
        MostrarSituacao(estado, pdfs);

        var modo = EscolherModo(estado, modoSugerido);

        if (modo is null)
        {
            return;
        }

        if (modo == ModoDoConfigurador.Completo && estado.TemHistorico)
        {
            estado.Limpar();
            estado.SincronizarComDisco();
        }

        estado.Salvar();

        if (!Confirmar(modo.Value, estado))
        {
            return;
        }

        var opcoes = contexto.ExigirClaudeCode();

        if (opcoes is null)
        {
            return;
        }

        var livrosNovos = estado.LivrosPendentes.Select(livro => livro.Arquivo).ToList();

        using var sessao = new SessaoDeAgente(opcoes, DefinicaoDeAgente.Configurador, caminhos);

        ConsoleUi.Titulo("Configurador trabalhando");
        ConsoleUi.Detalhe("Ctrl+C interrompe — o que já foi gerado fica salvo e a próxima execução continua daqui.");
        ProgressoDoAgente.Pensando("O Configurador");

        string resposta;

        try
        {
            resposta = await sessao.EnviarAsync(
                PromptDoConfigurador.Montar(modo.Value, escolhido, estado, livrosNovos),
                ProgressoDoAgente.Impressora(),
                cancelamento);
        }
        catch (OperationCanceledException)
        {
            RelatarInterrupcao(caminhos, escolhido);
            throw;
        }
        catch (Exception excecao)
        {
            ConsoleUi.Erro($"Falha ao processar '{escolhido.Id}': {excecao.Message}");
            RelatarInterrupcao(caminhos, escolhido);
            return;
        }

        ConsoleUi.Info("");
        ConsoleUi.Info(resposta);

        Concluir(caminhos, escolhido, livrosNovos);
    }

    private static string DescreverEscolha(CaminhosDoProjeto caminhos, SistemaRpg sistema)
    {
        if (!sistema.TemConhecimento(caminhos))
        {
            return $"{sistema.Id}  (nunca processado)";
        }

        var estado = EstadoDoProcessamento.Carregar(caminhos, sistema.Id);
        estado.SincronizarComDisco();

        return $"{sistema.Id}  ({estado.Resumo()})";
    }

    private static void MostrarSituacao(EstadoDoProcessamento estado, IReadOnlyList<string> pdfs)
    {
        ConsoleUi.Info($"{pdfs.Count} PDF(s) no sistema:");

        foreach (var pdf in pdfs)
        {
            var nome = Path.GetFileName(pdf);
            var lido = estado.Livros.Any(livro =>
                livro.Arquivo.Equals(nome, StringComparison.OrdinalIgnoreCase) &&
                livro.Estado == EstadoDoItem.Concluido);

            var situacao = lido ? "já processado" : "ainda não lido";
            ConsoleUi.Detalhe($"  · {nome} ({new FileInfo(pdf).Length / 1024} KB) — {situacao}");
        }

        if (estado.TemHistorico)
        {
            ConsoleUi.Info("");
            ConsoleUi.Sucesso($"Progresso registrado: {estado.Resumo()}.");

            foreach (var item in estado.Pendentes.Take(10))
            {
                ConsoleUi.Detalhe($"  · falta {item.Caminho}");
            }

            if (estado.Pendentes.Count > 10)
            {
                ConsoleUi.Detalhe($"  · ... e mais {estado.Pendentes.Count - 10}");
            }
        }
    }

    /// <summary>
    /// Decide entre continuar e recomeçar. Só pergunta quando as duas opções fazem sentido —
    /// num sistema nunca processado não há o que continuar.
    /// </summary>
    private static ModoDoConfigurador? EscolherModo(EstadoDoProcessamento estado, ModoDoConfigurador? sugerido)
    {
        if (!estado.TemHistorico)
        {
            return ModoDoConfigurador.Completo;
        }

        if (sugerido == ModoDoConfigurador.Expansao && estado.LivrosPendentes.Count > 0)
        {
            return ModoDoConfigurador.Expansao;
        }

        var opcoes = new List<OpcaoDeModo>
        {
            new($"Continuar de onde parou ({estado.Resumo()})", ModoDoConfigurador.Retomada),
            new("Recomeçar do zero (relê os livros e regera tudo — caro)", ModoDoConfigurador.Completo),
        };

        if (estado.LivrosPendentes.Count > 0)
        {
            opcoes.Insert(0, new OpcaoDeModo(
                "Ler só os livros ainda não incorporados: " +
                string.Join(", ", estado.LivrosPendentes.Select(livro => livro.Arquivo)),
                ModoDoConfigurador.Expansao));
        }

        return ConsoleUi.Escolher("Como processar?", opcoes, opcao => opcao.Rotulo)?.Modo;
    }

    private sealed record OpcaoDeModo(string Rotulo, ModoDoConfigurador Modo);

    private static bool Confirmar(ModoDoConfigurador modo, EstadoDoProcessamento estado)
    {
        switch (modo)
        {
            case ModoDoConfigurador.Retomada:
                ConsoleUi.Info("");
                ConsoleUi.Info($"O agente vai gerar os {estado.Pendentes.Count} arquivo(s) que faltam, lendo dos");
                ConsoleUi.Info("livros só as partes necessárias.");
                break;

            case ModoDoConfigurador.Expansao:
                ConsoleUi.Info("");
                ConsoleUi.Info("O agente vai consultar o índice da base, ler apenas os livros ainda não");
                ConsoleUi.Info("incorporados e preencher as lacunas — sem regerar o que já está pronto.");
                break;

            default:
                ConsoleUi.Aviso(
                    "O agente vai ler esses PDFs inteiros. É a operação mais cara do aplicativo e ela " +
                    "consome a cota da sua assinatura do Claude Code — um livro grande pode esgotar a " +
                    "janela de uso. Se a cota acabar no meio, o aplicativo espera a próxima janela e " +
                    "continua sozinho.");
                break;
        }

        return ConsoleUi.Confirmar("Começar o processamento?");
    }

    /// <summary>
    /// Fecha o ciclo: reindexa, atualiza o registro e conta ao usuário o que saiu — inclusive
    /// o que ficou faltando, que é o que ele vai retomar da próxima vez.
    /// </summary>
    private static void Concluir(CaminhosDoProjeto caminhos, SistemaRpg sistema, IReadOnlyList<string> livrosLidos)
    {
        var diretorioConhecimento = sistema.DiretorioConhecimento(caminhos);

        if (!Directory.Exists(diretorioConhecimento))
        {
            ConsoleUi.Erro($"O agente terminou mas não criou {diretorioConhecimento}.");
            return;
        }

        IndiceDeConhecimento.Reconstruir(caminhos, sistema.Id);

        var estado = EstadoDoProcessamento.Carregar(caminhos, sistema.Id);
        estado.SincronizarComDisco();

        // Livro só conta como lido quando o agente terminou o turno sem deixar pendência: se
        // parou no meio, ainda há conteúdo dele que não virou arquivo nenhum.
        if (estado.Pendentes.Count == 0)
        {
            estado.MarcarLivrosConcluidos(livrosLidos);
        }

        estado.Salvar();

        var gerados = Directory
            .EnumerateFiles(diretorioConhecimento, "*.md", SearchOption.AllDirectories)
            .Where(arquivo => !Path.GetFileName(arquivo).Equals(IndiceDeConhecimento.NomeDoArquivo, StringComparison.OrdinalIgnoreCase))
            .ToList();

        ConsoleUi.Sucesso($"\n{gerados.Count} arquivo(s) de conhecimento em Knowledge/{sistema.Id}/:");

        foreach (var arquivo in gerados)
        {
            var relativo = Path.GetRelativePath(diretorioConhecimento, arquivo);
            ConsoleUi.Detalhe($"  · {relativo} ({new FileInfo(arquivo).Length / 1024.0:0.0} KB)");
        }

        ConsoleUi.Detalhe($"  · mais um {IndiceDeConhecimento.NomeDoArquivo} por nível, gerado automaticamente.");

        if (estado.Pendentes.Count > 0)
        {
            ConsoleUi.Aviso($"\nAinda faltam {estado.Pendentes.Count} arquivo(s) do plano:");

            foreach (var item in estado.Pendentes.Take(10))
            {
                ConsoleUi.Detalhe($"  · {item.Caminho}");
            }

            ConsoleUi.Info("Processe o sistema de novo para continuar de onde parou.");
        }

        AvisarSobreArquivosDaFicha(diretorioConhecimento);

        if (!Directory.Exists(sistema.DiretorioModelo(caminhos)))
        {
            ConsoleUi.Aviso(
                $"Falta a ficha editável em Templates/{sistema.Id}/ — sem ela o Dungeon Master " +
                "consegue criar o personagem, mas não gerar o PDF final.");
        }
    }

    private static void RelatarInterrupcao(CaminhosDoProjeto caminhos, SistemaRpg sistema)
    {
        if (!Directory.Exists(sistema.DiretorioConhecimento(caminhos)))
        {
            return;
        }

        IndiceDeConhecimento.Reconstruir(caminhos, sistema.Id);

        var estado = EstadoDoProcessamento.Carregar(caminhos, sistema.Id);
        estado.SincronizarComDisco();
        estado.Salvar();

        ConsoleUi.Info("");
        ConsoleUi.Sucesso($"O que já saiu está salvo: {estado.Resumo()}.");
        ConsoleUi.Info("Processe o sistema de novo para continuar de onde parou.");
    }

    /// <summary>
    /// Os dois arquivos da ficha têm nome fixo porque o Dungeon Master procura exatamente por
    /// eles. Se o Configurador não os produziu, o usuário precisa saber agora — e não no meio
    /// de uma criação de personagem, quando a conversa já custou tokens.
    /// </summary>
    private static void AvisarSobreArquivosDaFicha(string diretorioConhecimento)
    {
        string[] obrigatorios = ["Ficha-Mapeamento.md", "Ficha-ModeloEmTexto.md"];

        var faltando = obrigatorios
            .Where(nome => !File.Exists(Path.Combine(diretorioConhecimento, nome)))
            .ToList();

        if (faltando.Count == 0)
        {
            ConsoleUi.Sucesso("Mapeamento e modelo em texto da ficha gerados.");
            return;
        }

        ConsoleUi.Aviso($"Faltou o agente gerar: {string.Join(", ", faltando)}.");
        ConsoleUi.Info("O Dungeon Master precisa desses arquivos para mostrar e preencher a ficha —");
        ConsoleUi.Info("vale reprocessar o sistema.");
    }
}
