using RpgForge.Agents;
using RpgForge.Core;

namespace RpgForge.Cli;

/// <summary>
/// Roda o Agente Configurador sobre um sistema importado em Systems/, gerando a base de
/// conhecimento em Knowledge/. É a operação mais cara em tokens do aplicativo (o agente lê o
/// livro inteiro), por isso pede confirmação explícita antes de começar.
/// </summary>
internal static class FluxoDoConfigurador
{
    public static async Task ExecutarAsync(ContextoDoAplicativo contexto, CancellationToken cancelamento)
    {
        var sistemas = SistemaRpg.DescobrirImportados(contexto.Caminhos);

        if (sistemas.Count == 0)
        {
            ConsoleUi.Aviso("Nenhum sistema encontrado em Systems/.");
            ConsoleUi.Info($"Crie uma pasta por sistema em {contexto.Caminhos.Sistemas} e coloque o PDF do livro dentro dela.");
            return;
        }

        var escolhido = ConsoleUi.Escolher(
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
                $"Processe o sistema '{escolhido.Id}': leia o(s) PDF(s) dele e gere a base de " +
                $"conhecimento completa em Knowledge/{escolhido.Id}/. Ao terminar, resuma o que criou.",
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

        if (!Directory.Exists(escolhido.DiretorioModelo(contexto.Caminhos)))
        {
            ConsoleUi.Aviso(
                $"Falta a ficha editável em Templates/{escolhido.Id}/ — sem ela o Dungeon Master " +
                "consegue criar o personagem, mas não gerar o PDF final.");
        }
    }
}
