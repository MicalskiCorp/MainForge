using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Cli;

/// <summary>
/// Acrescenta livros a um sistema que já existe: compêndios, expansões, suplementos — tudo
/// que soma conteúdo a um sistema sem substituí-lo.
///
/// <para>É um fluxo separado da importação porque o que se faz depois é diferente. Importar um
/// sistema novo manda o agente ler tudo e montar a base do zero; acrescentar um compêndio
/// manda ler <em>só</em> o livro novo e encaixá-lo no que já está lá. Fazer o segundo caso
/// pela porta do primeiro significaria reler o livro básico inteiro, que é exatamente a conta
/// que este fluxo evita.</para>
/// </summary>
internal static class FluxoDeAdicaoDeLivro
{
    public static async Task ExecutarAsync(ContextoDoAplicativo contexto, CancellationToken cancelamento)
    {
        var caminhos = contexto.Caminhos;
        var sistemas = SistemaRpg.DescobrirImportados(caminhos);

        if (sistemas.Count == 0)
        {
            ConsoleUi.Aviso("Nenhum sistema importado ainda.");
            ConsoleUi.Info("Um compêndio precisa de um sistema para entrar — importe o livro básico primeiro.");
            return;
        }

        ConsoleUi.Titulo("Adicionar livro a um sistema");
        ConsoleUi.Detalhe("Para compêndios, expansões e suplementos de um sistema que já existe aqui.");

        var escolhido = ConsoleUi.Escolher(
            "A qual sistema?",
            sistemas,
            sistema => sistema.TemConhecimento(caminhos)
                ? $"{sistema.Id}  (base de conhecimento pronta)"
                : $"{sistema.Id}  (ainda não processado)");

        if (escolhido is null)
        {
            return;
        }

        var livros = EntradaDeArquivos.LerPdfs("compêndio/expansão");

        if (livros.Count == 0)
        {
            ConsoleUi.Aviso("Nenhum livro informado — operação cancelada.");
            return;
        }

        ConsoleUi.Titulo($"Confirmar adição a '{escolhido.Id}'");
        ConsoleUi.Info($"  Livros -> Systems/{escolhido.Id}/");

        foreach (var livro in livros)
        {
            ConsoleUi.Detalhe($"    · {Path.GetFileName(livro)}");
        }

        if (!ConsoleUi.Confirmar("\nCopiar esses arquivos para o projeto?"))
        {
            return;
        }

        IReadOnlyList<string> copiados;

        try
        {
            copiados = ImportadorDeSistema.AdicionarLivros(caminhos, escolhido.Id, livros);
        }
        catch (Exception excecao) when (excecao is ArgumentException or InvalidOperationException or IOException)
        {
            ConsoleUi.Erro($"Adição cancelada: {excecao.Message}");
            return;
        }

        // Recensear já marca os arquivos novos como pendentes — é isso que o Configurador lê
        // depois para saber que só eles precisam ser lidos.
        var estado = EstadoDoProcessamento.Carregar(caminhos, escolhido.Id);
        estado.SincronizarComDisco();
        estado.Salvar();

        ConsoleUi.Sucesso($"\n{copiados.Count} livro(s) adicionado(s) a '{escolhido.Id}'.");

        if (!escolhido.TemConhecimento(caminhos))
        {
            ConsoleUi.Info("");
            ConsoleUi.Info("Este sistema ainda não tem base de conhecimento. O processamento vai ler todos");
            ConsoleUi.Info("os livros, inclusive estes.");
        }
        else
        {
            ConsoleUi.Info("");
            ConsoleUi.Info("O próximo passo é o Configurador ler só esses livros e somá-los à base que já");
            ConsoleUi.Info("existe, sem regerar o que já está pronto.");
        }

        if (ConsoleUi.Confirmar("Processar agora?"))
        {
            await FluxoDoConfigurador.ExecutarAsync(
                contexto,
                cancelamento,
                escolhido,
                ModoDoConfigurador.Expansao);
        }
    }
}
