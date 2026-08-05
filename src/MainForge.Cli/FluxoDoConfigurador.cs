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

        var fontesSemConhecimento = estado.FontesSemConhecimento(caminhos);
        var escolha = EscolherModo(estado, modoSugerido, fontesSemConhecimento);

        if (escolha is null)
        {
            return;
        }

        if (escolha.Acao == AcaoDoProcessamento.MarcarLivrosComoIncorporados)
        {
            MarcarLivrosComoIncorporados(estado);
            return;
        }

        if (escolha.Acao == AcaoDoProcessamento.RelerFontesSemConhecimento)
        {
            var devolvidos = estado.MarcarFontesComoPendentes(fontesSemConhecimento);
            ConsoleUi.Info("");
            ConsoleUi.Info($"Marcados para releitura: {string.Join(", ", devolvidos)}.");
        }

        var modo = escolha.Modo;

        if (modo == ModoDoConfigurador.Completo && estado.TemHistorico)
        {
            estado.Limpar();
            estado.SincronizarComDisco();
        }

        estado.Salvar();

        if (!Confirmar(modo, estado))
        {
            return;
        }

        var opcoes = contexto.ExigirClaudeCode();

        if (opcoes is null)
        {
            return;
        }

        var todosOsLivrosTemTexto = await ConverterLivrosParaTextoAsync(caminhos, escolhido, cancelamento);

        // Abrir PDF depende do poppler, que pode não existir aqui. Quando não existe e todo livro
        // tem texto, a leitura do PDF é um caminho que só leva a erro: negá-la poupa do agente um
        // turno por livro descobrindo isso na marra.
        var podeAbrirPdf = LeituraDePdf.Disponivel();
        var agente = podeAbrirPdf || !todosOsLivrosTemTexto
            ? DefinicaoDeAgente.Configurador
            : DefinicaoDeAgente.ConfiguradorSemAbrirPdf(DefinicaoDeAgente.Configurador);

        if (!podeAbrirPdf)
        {
            ConsoleUi.Info("");
            ConsoleUi.Aviso($"O {LeituraDePdf.ProgramaNecessario} (poppler) não está instalado nesta máquina.");
            ConsoleUi.Detalhe(LeituraDePdf.ComoInstalar);
        }

        var livrosNovos = estado.LivrosPendentes.Select(livro => livro.Arquivo).ToList();
        var conhecimentoAntes = ContarConhecimentoPorFonte(caminhos, escolhido);
        // Depois da conversão, de propósito: o prompt manda ler o .md de cada livro que passou a
        // ter um.
        var mensagem = PromptDoConfigurador.Montar(modo, escolhido, estado, caminhos, podeAbrirPdf);

        // Processar é longo e não interativo: se a cota acabar no meio, o aplicativo espera a
        // janela virar e retoma sozinho, por mais que ela demore. Devolver o controle ao usuário
        // aqui só jogaria fora o contexto da conversa — e com ele o livro que o agente já leu.
        using var sessao = new SessaoDeAgente(
            opcoes,
            agente,
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

        Concluir(caminhos, escolhido, livrosNovos, conhecimentoAntes);
    }

    /// <summary>
    /// Converte os livros em Markdown antes de o agente começar.
    ///
    /// <para><b>Por que aqui e não na importação.</b> É no início do processamento que a conversão
    /// paga: o agente que vai ler os livros é o que começa logo a seguir, e converter tudo na
    /// importação faria o usuário esperar por um livro que ele talvez nunca processe. Como o
    /// resultado fica em cache, a segunda execução não paga de novo.</para>
    ///
    /// <para>Com markitdown instalado, é ele quem converte; sem ele, o extrator interno. O
    /// aplicativo não depende de instalação nenhuma para chegar ao texto do livro — quando
    /// dependia, uma máquina sem Python e sem poppler não conseguia processar coisa alguma.</para>
    /// </summary>
    /// <returns>
    /// Se todos os livros do sistema têm texto utilizável — é o que decide se o agente ainda
    /// precisa do PDF.
    /// </returns>
    private static async Task<bool> ConverterLivrosParaTextoAsync(
        CaminhosDoProjeto caminhos,
        SistemaRpg sistema,
        CancellationToken cancelamento)
    {
        var programa = ConversorDeLivros.Localizar();

        ConsoleUi.Titulo("Convertendo os livros para texto");
        ConsoleUi.Info("Ler texto custa uma fração do que custa ler as páginas do PDF. Roda na sua máquina,");
        ConsoleUi.Info("não gasta cota, e o resultado fica salvo para as próximas execuções.");

        ConsoleUi.Detalhe(programa is null
            ? $"Conversor: {ConversorDeLivros.ExtratorInterno} (markitdown não encontrado)."
            : $"Conversor: markitdown ({programa.Descricao}).");

        var resultados = await ConversorDeLivros.ConverterSistemaAsync(
            caminhos,
            sistema.Id,
            programa,
            livro => ConsoleUi.Detalhe($"  · convertendo {livro}... (pode levar alguns minutos)"),
            cancelamento);

        var algumPeloExtratorInterno = false;

        foreach (var resultado in resultados)
        {
            switch (resultado.Situacao)
            {
                case SituacaoDaConversao.Convertido:
                    algumPeloExtratorInterno |= resultado.Conversor == ConversorDeLivros.ExtratorInterno;
                    ConsoleUi.Sucesso($"  · {resultado.Livro} -> {resultado.CaminhoDoTexto}");

                    if (resultado.Detalhe is { Length: > 0 } aviso)
                    {
                        ConsoleUi.Detalhe($"    ({aviso}; o texto saiu pelo {resultado.Conversor})");
                    }

                    break;

                case SituacaoDaConversao.JaEstavaPronto:
                    ConsoleUi.Detalhe($"  · {resultado.Livro} — texto já em dia");
                    break;

                default:
                    ConsoleUi.Aviso($"  · {resultado.Livro} — não deu para converter: {resultado.Detalhe}");
                    ConsoleUi.Detalhe(
                        "    O agente vai tentar ler o PDF deste livro, o que custa bem mais cota e " +
                        "depende do poppler (pdftoppm) instalado.");
                    break;
            }
        }

        // O markitdown lida melhor com tabela e estrutura, e um livro de regras é cheio das duas.
        // Só vale sugerir depois de a conversão ter funcionado: como um convite a melhorar, não
        // como pré-requisito de algo que acabou de acontecer sem ele.
        if (algumPeloExtratorInterno && programa is null)
        {
            ConsoleUi.Info("");
            ConsoleUi.Detalhe(ConversorDeLivros.ComoInstalar);
        }

        return resultados.Count > 0 && resultados.All(r => r.Situacao != SituacaoDaConversao.Falhou);
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
    /// Decide entre continuar, recomeçar e as saídas que não chamam o agente. Só pergunta quando
    /// mais de uma opção faz sentido — num sistema nunca processado não há o que continuar.
    /// </summary>
    private static OpcaoDeModo? EscolherModo(
        EstadoDoProcessamento estado,
        ModoDoConfigurador? sugerido,
        IReadOnlyList<string> fontesSemConhecimento)
    {
        if (!estado.TemHistorico)
        {
            return new OpcaoDeModo("", ModoDoConfigurador.Completo);
        }

        AvisarSobreFontesSemConhecimento(fontesSemConhecimento);

        // Sistema inteiro pronto: processar de novo só relê livro e regrava arquivo pronto. A
        // única saída que continua fazendo sentido é a que o usuário pede de propósito — mais a
        // releitura de uma fonte que consta como lida sem ter gerado nada.
        if (estado.EstaCompleto)
        {
            return SoRecomecarDoZero(estado, fontesSemConhecimento);
        }

        if (sugerido == ModoDoConfigurador.Expansao && estado.LivrosPendentes.Count > 0)
        {
            return new OpcaoDeModo("", ModoDoConfigurador.Expansao);
        }

        var opcoes = new List<OpcaoDeModo>
        {
            new($"Continuar de onde parou ({estado.Resumo()})", ModoDoConfigurador.Retomada),
            new("Recomeçar do zero (relê os livros e regera tudo — caro)", ModoDoConfigurador.Completo),
        };

        if (fontesSemConhecimento.Count > 0)
        {
            opcoes.Insert(0, OpcaoDeReler(fontesSemConhecimento));
        }

        if (estado.LivrosPendentes.Count > 0)
        {
            opcoes.Insert(0, new OpcaoDeModo(
                "Ler só os livros ainda não incorporados: " +
                string.Join(", ", estado.LivrosPendentes.Select(livro => livro.Arquivo)),
                ModoDoConfigurador.Expansao));
        }

        // Plano inteiro gerado e livro ainda marcado como pendente é ambíguo: tanto pode ser um
        // compêndio recém-adicionado (ler) quanto uma execução que gerou tudo e morreu antes de
        // fechar o registro (marcar). Quem sabe qual dos dois é o usuário, então a escolha é dele
        // — e a segunda saída não custa cota nenhuma.
        if (estado.LivrosPendentes.Count > 0 && estado.Pendentes.Count == 0)
        {
            opcoes.Insert(1, new OpcaoDeModo(
                $"Marcar esses {estado.LivrosPendentes.Count} livro(s) como já incorporados — a base " +
                "já cobre o conteúdo deles (não gasta cota)",
                ModoDoConfigurador.Retomada,
                AcaoDoProcessamento.MarcarLivrosComoIncorporados));
        }

        return ConsoleUi.Escolher("Como processar?", opcoes, opcao => opcao.Rotulo);
    }

    /// <summary>
    /// A tela de um sistema que não tem mais nada pendente. Ela existe para dizer não: o menu
    /// anterior oferecia "continuar de onde parou" mesmo sem haver onde parar, e aceitar isso
    /// mandava o agente reler os livros para reescrever a base que já estava pronta.
    /// </summary>
    private static OpcaoDeModo? SoRecomecarDoZero(
        EstadoDoProcessamento estado,
        IReadOnlyList<string> fontesSemConhecimento)
    {
        ConsoleUi.Info("");
        ConsoleUi.Sucesso($"Nada a processar: {estado.Resumo()}, e todos os livros já foram incorporados.");
        ConsoleUi.Info("Rodar o Configurador agora só faria ele reler os livros e regravar o que já existe.");
        ConsoleUi.Detalhe("Para acrescentar conteúdo, use a opção 3 do menu (adicionar livro ao sistema).");

        var opcoes = new List<OpcaoDeModo>
        {
            new("Recomeçar do zero mesmo assim (apaga o registro, relê os livros — caro)",
                ModoDoConfigurador.Completo),
        };

        if (fontesSemConhecimento.Count > 0)
        {
            opcoes.Insert(0, OpcaoDeReler(fontesSemConhecimento));
        }

        return ConsoleUi.Escolher("Mesmo assim?", opcoes, opcao => opcao.Rotulo);
    }

    /// <summary>
    /// Uma fonte consta como lida e não tem nada em <c>Knowledge/</c>. É o rastro de um livro que
    /// o agente não conseguiu abrir — em geral um PDF que o <c>Read</c> do Claude Code não
    /// rasterizou por falta do poppler. Também é o que se vê depois de mover um livro de fonte,
    /// e aí não há o que fazer; por isso o aplicativo aponta e deixa a decisão com quem sabe.
    /// </summary>
    private static void AvisarSobreFontesSemConhecimento(IReadOnlyList<string> fontes)
    {
        if (fontes.Count == 0)
        {
            return;
        }

        ConsoleUi.Info("");
        ConsoleUi.Aviso(
            $"Fonte(s) marcada(s) como lida(s), mas sem nada em Knowledge/: {string.Join(", ", fontes)}.");
        ConsoleUi.Info("Ou o livro não pôde ser lido na execução anterior, ou ele só mudou de pasta e o");
        ConsoleUi.Info("conteúdo dele continua na fonte antiga — nesse segundo caso, não há o que fazer.");
    }

    private static OpcaoDeModo OpcaoDeReler(IReadOnlyList<string> fontes) => new(
        $"Reler os livros de {string.Join(", ", fontes)} — a base não tem nada dessa(s) fonte(s)",
        ModoDoConfigurador.Expansao,
        AcaoDoProcessamento.RelerFontesSemConhecimento);

    /// <summary>
    /// Acerta o registro sem chamar agente: os livros passam a constar como já incorporados à
    /// base. É a contrapartida de o registro só ser fechado no fim de uma execução — uma que
    /// gerou tudo e morreu no último passo deixa livro pendente que já foi lido de fato.
    /// </summary>
    private static void MarcarLivrosComoIncorporados(EstadoDoProcessamento estado)
    {
        var livros = estado.LivrosPendentes.Select(livro => livro.Arquivo).ToList();

        estado.MarcarLivrosConcluidos();
        estado.Salvar();

        ConsoleUi.Info("");
        ConsoleUi.Sucesso($"{livros.Count} livro(s) marcado(s) como já incorporados:");

        foreach (var livro in livros)
        {
            ConsoleUi.Detalhe($"  · {livro}");
        }

        ConsoleUi.Info("Nenhum token gasto. Se faltar conteúdo de algum deles, apague os .md correspondentes");
        ConsoleUi.Info("em Knowledge/ ou recomece o sistema do zero.");
    }

    /// <summary>O que fazer com o sistema — nem toda escolha do menu chama o agente.</summary>
    private enum AcaoDoProcessamento
    {
        Processar,
        MarcarLivrosComoIncorporados,
        RelerFontesSemConhecimento,
    }

    private sealed record OpcaoDeModo(
        string Rotulo,
        ModoDoConfigurador Modo,
        AcaoDoProcessamento Acao = AcaoDoProcessamento.Processar);

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
    private static void Concluir(
        CaminhosDoProjeto caminhos,
        SistemaRpg sistema,
        IReadOnlyList<string> livrosLidos,
        IReadOnlyDictionary<string, int> conhecimentoAntes)
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

        // Livro só conta como lido quando o agente terminou o turno sem deixar pendência **e** a
        // fonte dele ganhou conteúdo nesta execução.
        //
        // A segunda condição existe porque a primeira, sozinha, já mentiu: com o plano antigo
        // completo, um compêndio que o agente não conseguiu nem abrir (PDF sem o poppler que o
        // Read do Claude Code exige) foi marcado como lido sem ter gerado um único arquivo. O
        // sistema ficava "pronto" com uma expansão que ninguém leu.
        if (estado.Pendentes.Count == 0)
        {
            estado.MarcarLivrosConcluidos(LivrosQueRenderamConteudo(estado, livrosLidos, conhecimentoAntes, caminhos, sistema));
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

    /// <summary>
    /// Dos livros que esta execução ia ler, quais deixaram rastro: os de uma fonte cuja pasta em
    /// <c>Knowledge/</c> tem mais arquivos agora do que tinha antes de o agente começar.
    ///
    /// <para>Contar por fonte, e não por arquivo, é o que dá para afirmar sem depender de o modelo
    /// dizer de qual livro veio cada gravação. Erra para o lado seguro: quando dois livros da
    /// mesma fonte são lidos juntos, os dois contam; quando nada saiu, nenhum conta.</para>
    /// </summary>
    private static IReadOnlyList<string> LivrosQueRenderamConteudo(
        EstadoDoProcessamento estado,
        IReadOnlyList<string> livrosLidos,
        IReadOnlyDictionary<string, int> conhecimentoAntes,
        CaminhosDoProjeto caminhos,
        SistemaRpg sistema)
    {
        var depois = ContarConhecimentoPorFonte(caminhos, sistema);

        return
        [
            .. livrosLidos.Where(nome =>
            {
                var fonte = estado.Livros
                    .FirstOrDefault(livro => livro.Arquivo.Equals(nome, StringComparison.OrdinalIgnoreCase))
                    ?.Fonte ?? FonteDoSistema.IdDaBase;

                return depois.GetValueOrDefault(fonte) > conhecimentoAntes.GetValueOrDefault(fonte);
            }),
        ];
    }

    /// <summary>Quantos arquivos de conhecimento cada fonte tem agora (o índice não conta).</summary>
    private static IReadOnlyDictionary<string, int> ContarConhecimentoPorFonte(
        CaminhosDoProjeto caminhos,
        SistemaRpg sistema)
    {
        var raiz = sistema.DiretorioConhecimento(caminhos);

        if (!Directory.Exists(raiz))
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        return Directory
            .EnumerateDirectories(raiz)
            .ToDictionary(
                pasta => new DirectoryInfo(pasta).Name,
                pasta => Directory
                    .EnumerateFiles(pasta, "*.md", SearchOption.AllDirectories)
                    .Count(arquivo => !Path.GetFileName(arquivo)
                        .Equals(IndiceDeConhecimento.NomeDoArquivo, StringComparison.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);
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
