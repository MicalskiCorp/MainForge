using MainForge.Agents;
using MainForge.Core;

namespace MainForge.Cli;

/// <summary>
/// Roda o Agente Configurador sobre um sistema importado em Systems/, gerando a base de
/// conhecimento em Knowledge/. É a operação mais cara em tokens do aplicativo (o agente lê o
/// livro inteiro), por isso pede confirmação explícita antes de começar.
/// </summary>
internal static class FluxoDoConfigurador
{
    /// <param name="sistemaEscolhido">
    /// Já vem preenchido quando o fluxo é chamado logo após uma importação — nesse caso não
    /// faz sentido perguntar de novo qual sistema processar.
    /// </param>
    public static async Task ExecutarAsync(
        ContextoDoAplicativo contexto,
        CancellationToken cancelamento,
        SistemaRpg? sistemaEscolhido = null)
    {
        var sistemas = SistemaRpg.DescobrirImportados(contexto.Caminhos);

        if (sistemas.Count == 0)
        {
            ConsoleUi.Aviso("Nenhum sistema importado ainda.");
            ConsoleUi.Info("Use a opção \"Importar um sistema de RPG\" no menu principal.");
            return;
        }

        var escolhido = sistemaEscolhido ?? ConsoleUi.Escolher(
            "Qual sistema processar?",
            sistemas,
            sistema => sistema.TemConhecimento(contexto.Caminhos)
                ? $"{sistema.Id}  (já processado — será reprocessado)"
                : sistema.Id);

        if (escolhido is null)
        {
            return;
        }

        var pdfs = Directory
            .EnumerateFiles(escolhido.DiretorioSistemas(contexto.Caminhos), "*.pdf", SearchOption.AllDirectories)
            .ToList();

        if (pdfs.Count == 0)
        {
            ConsoleUi.Erro($"Nenhum PDF em {escolhido.DiretorioSistemas(contexto.Caminhos)}.");
            return;
        }

        ConsoleUi.Titulo($"Processar '{escolhido.Id}'");
        ConsoleUi.Info($"{pdfs.Count} PDF(s) a ler:");

        foreach (var pdf in pdfs)
        {
            ConsoleUi.Detalhe($"  · {Path.GetFileName(pdf)} ({new FileInfo(pdf).Length / 1024} KB)");
        }

        ConsoleUi.Aviso(
            "O agente vai ler esses PDFs pela Claude API. Livros grandes consomem muitos tokens " +
            "(e portanto custam) — vale começar por um sistema pequeno.");

        if (!ConsoleUi.Confirmar("Começar o processamento?"))
        {
            return;
        }

        var mensagens = contexto.ObterServicoDeMensagens();

        if (mensagens is null)
        {
            return;
        }

        var sessao = new SessaoDeAgente(mensagens, DefinicaoDeAgente.Configurador, contexto.Caminhos, contexto.Opcoes!);

        ConsoleUi.Titulo("Configurador trabalhando");
        ProgressoDoAgente.Pensando("O Configurador");

        string resposta;

        try
        {
            resposta = await sessao.EnviarAsync(
                $"Processe o sistema '{escolhido.Id}': leia o(s) livro(s) em PDF e também a ficha " +
                $"em branco de Templates/{escolhido.Id}/, e gere a base de conhecimento completa em " +
                $"Knowledge/{escolhido.Id}/, incluindo os arquivos Ficha-Mapeamento.md e " +
                "Ficha-ModeloEmTexto.md. Ao terminar, resuma o que criou.",
                ProgressoDoAgente.Impressora(),
                cancelamento);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            ConsoleUi.Erro($"Falha ao processar '{escolhido.Id}': {excecao.Message}");
            return;
        }

        ConsoleUi.Info("");
        ConsoleUi.Info(resposta);

        var diretorioConhecimento = escolhido.DiretorioConhecimento(contexto.Caminhos);

        if (!Directory.Exists(diretorioConhecimento))
        {
            ConsoleUi.Erro($"O agente terminou mas não criou {diretorioConhecimento}.");
            return;
        }

        var gerados = Directory.EnumerateFiles(diretorioConhecimento, "*.md", SearchOption.AllDirectories).ToList();

        ConsoleUi.Sucesso($"\n{gerados.Count} arquivo(s) de conhecimento em Knowledge/{escolhido.Id}/:");

        foreach (var arquivo in gerados)
        {
            var relativo = Path.GetRelativePath(diretorioConhecimento, arquivo);
            ConsoleUi.Detalhe($"  · {relativo} ({new FileInfo(arquivo).Length / 1024.0:0.0} KB)");
        }

        AvisarSobreArquivosDaFicha(diretorioConhecimento);

        if (!Directory.Exists(escolhido.DiretorioModelo(contexto.Caminhos)))
        {
            ConsoleUi.Aviso(
                $"Falta a ficha editável em Templates/{escolhido.Id}/ — sem ela o Dungeon Master " +
                "consegue criar o personagem, mas não gerar o PDF final.");
        }
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
