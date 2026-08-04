using MainForge.Agents;
using MainForge.ClaudeCode;
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

        if (!GarantirLayoutComFontes(caminhos, escolhido))
        {
            return;
        }

        // Reconstruir antes de olhar o estado indexa bases geradas por versões anteriores do
        // aplicativo (que não tinham index.md) sem gastar um token.
        IndiceDeConhecimento.Reconstruir(caminhos, escolhido.Id);

        var estado = EstadoDoProcessamento.Carregar(caminhos, escolhido.Id);
        estado.SincronizarComDisco();

        ConsoleUi.Titulo($"Processar '{escolhido.Id}'");
        MostrarSituacao(estado, pdfs, escolhido.DiretorioSistemas(caminhos));

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
        var mensagem = PromptDoConfigurador.Montar(modo.Value, escolhido, estado);

        // Processar é longo e não interativo: se a cota acabar no meio, o aplicativo espera a
        // janela virar e retoma sozinho, por mais que ela demore. Devolver o controle ao usuário
        // aqui só jogaria fora o contexto da conversa — e com ele o livro que o agente já leu.
        using var sessao = new SessaoDeAgente(
            opcoes,
            DefinicaoDeAgente.Configurador,
            caminhos,
            PoliticaDeLimiteDeUso.ProcessamentoLongo);

        ConsoleUi.Titulo("Configurador trabalhando");
        ConsoleUi.Detalhe("Se a cota acabar, a espera pela próxima janela é automática — pode deixar rodando.");
        ConsoleUi.Detalhe("Ctrl+C interrompe — o que já foi gerado fica salvo e a próxima execução continua daqui.");
        ProgressoDoAgente.Pensando("O Configurador");

        string resposta;

        try
        {
            resposta = await sessao.EnviarAsync(mensagem, ProgressoDoAgente.Impressora(), cancelamento);
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

    /// <summary>
    /// Um sistema do layout antigo precisa ser separado por fonte antes de ser processado. Sem
    /// isso o agente receberia um mapa de pastas que não corresponde ao disco — e o conteúdo
    /// sairia fora de qualquer fonte, que é o mesmo que não dar ao usuário a escolha das
    /// expansões. Migrar é só mover arquivo, então a pergunta é só para o usuário não ver o
    /// próprio projeto mudar de forma sem aviso.
    /// </summary>
    private static bool GarantirLayoutComFontes(CaminhosDoProjeto caminhos, SistemaRpg sistema)
    {
        if (!sistema.PrecisaMigrarParaFontes(caminhos))
        {
            return true;
        }

        ConsoleUi.Info("");
        ConsoleUi.Aviso($"'{sistema.Id}' ainda está sem separação entre jogo base e expansões.");
        ConsoleUi.Info($"Antes de processar, os livros e o conhecimento dele vão para a pasta '{FonteDoSistema.IdDaBase}/',");
        ConsoleUi.Info("para que cada compêndio futuro tenha pasta própria e possa ser escolhido (ou não)");
        ConsoleUi.Detalhe("na criação de personagem. É só mover arquivo: não relê livro e não custa tokens.");

        if (!ConsoleUi.Confirmar("Migrar agora e seguir?"))
        {
            ConsoleUi.Info("Processamento cancelado — a separação por fonte é pré-requisito.");
            return false;
        }

        var resultado = MigracaoDeFontes.Migrar(caminhos, sistema.Id);

        ConsoleUi.Sucesso(
            $"  {resultado.Livros.Count} livro(s) e {resultado.Conhecimento.Count} item(ns) de " +
            $"conhecimento movidos para {FonteDoSistema.IdDaBase}/.");

        return true;
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

    private static void MostrarSituacao(
        EstadoDoProcessamento estado,
        IReadOnlyList<string> pdfs,
        string diretorioDoSistema)
    {
        ConsoleUi.Info($"{pdfs.Count} PDF(s) no sistema:");

        foreach (var pdf in pdfs)
        {
            var nome = Path.GetFileName(pdf);
            var lido = estado.Livros.Any(livro =>
                livro.Arquivo.Equals(nome, StringComparison.OrdinalIgnoreCase) &&
                livro.Estado == EstadoDoItem.Concluido);

            var situacao = lido ? "já processado" : "ainda não lido";

            // O caminho relativo mostra a fonte junto do nome: é o que deixa ver, de relance,
            // que um compêndio caiu na pasta do jogo base por engano.
            var relativo = Path.GetRelativePath(diretorioDoSistema, pdf);
            ConsoleUi.Detalhe($"  · {relativo} ({new FileInfo(pdf).Length / 1024} KB) — {situacao}");
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
                    "janela de uso.");
                break;
        }

        ConsoleUi.Info("");
        ConsoleUi.Info(
            "Se a cota acabar no meio, o aplicativo mostra a contagem regressiva, espera a próxima " +
            "janela abrir — mesmo que seja a semanal, daqui a dias — e retoma a mesma conversa " +
            "sozinho, sem reler o que já leu. Basta deixar a janela do aplicativo aberta; Ctrl+C " +
            "cancela a espera a qualquer momento.");

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
        var faltando = SistemaRpg.ArquivosDaFicha
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
