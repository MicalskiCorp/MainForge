using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Cli;

/// <summary>
/// Traz um sistema de RPG novo para dentro do aplicativo: o usuário informa o nome, os PDFs
/// dos livros e a ficha de personagem editável, e o programa valida e copia tudo para
/// Systems/ e Templates/. É o passo que antecede o Agente Configurador — sem os arquivos
/// aqui dentro, não há o que mapear.
///
/// <para>Aqui não se pergunta "base ou expansão?": importar um sistema <em>é</em> trazer o jogo
/// base dele, e os livros vão para <c>Systems/&lt;Sistema&gt;/base/</c>. Expansão precisa de um
/// sistema já existente para se somar, e entra pela opção de adicionar livro.</para>
/// </summary>
internal static class FluxoDeImportacao
{
    public static async Task ExecutarAsync(ContextoDoAplicativo contexto, CancellationToken cancelamento)
    {
        ConsoleUi.Titulo("Importar um sistema de RPG");
        ConsoleUi.Detalhe("Arraste os arquivos para a janela do console para colar o caminho. Enter vazio cancela.");
        ConsoleUi.Detalhe("São os livros do jogo base; compêndios e expansões entram depois, pela opção 3.");

        var nome = ConsoleUi.LerLinha("\nNome do sistema (ex.: Aventura&Cia): ");

        if (nome.Length == 0)
        {
            return;
        }

        var livros = EntradaDeArquivos.LerPdfs("livro do jogo base");

        if (livros.Count == 0)
        {
            ConsoleUi.Aviso("Nenhum livro informado — importação cancelada.");
            return;
        }

        var ficha = LerCaminhoDaFicha();

        if (ficha is null)
        {
            return;
        }

        ConsoleUi.Titulo($"Confirmar importação de '{nome}'");
        ConsoleUi.Info($"  Livros  -> Systems/{nome}/{FonteDoSistema.IdDaBase}/   (jogo base)");

        foreach (var livro in livros)
        {
            ConsoleUi.Detalhe($"    · {Path.GetFileName(livro)}");
        }

        ConsoleUi.Info($"  Ficha   -> Templates/{nome}/");
        ConsoleUi.Detalhe($"    · {Path.GetFileName(ficha)}");

        if (!ConsoleUi.Confirmar("\nCopiar esses arquivos para o projeto?"))
        {
            return;
        }

        ResultadoDaImportacao resultado;

        try
        {
            resultado = ImportadorDeSistema.Importar(contexto.Caminhos, nome, livros, ficha);
        }
        catch (Exception excecao) when (excecao is ArgumentException or InvalidOperationException or IOException)
        {
            ConsoleUi.Erro($"Importação cancelada: {excecao.Message}");
            return;
        }

        ConsoleUi.Sucesso($"\nSistema '{resultado.Sistema.Id}' importado.");
        ConsoleUi.Info($"  {resultado.Livros.Count} livro(s) no {resultado.Fonte.Rotulo} e a ficha '{resultado.Ficha}'.");
        ConsoleUi.Info($"  {resultado.CamposDaFicha.Count} campo(s) preenchível(is) na ficha:");
        ConsoleUi.Detalhe($"    {string.Join(", ", resultado.CamposDaFicha)}");

        ConsoleUi.Info("");
        ConsoleUi.Info("O próximo passo é o Agente Configurador ler esses livros e a ficha para");
        ConsoleUi.Info("montar a base de conhecimento do sistema.");

        if (ConsoleUi.Confirmar("Processar agora?"))
        {
            await FluxoDoConfigurador.ExecutarAsync(contexto, cancelamento, resultado.Sistema);
        }
    }

    private static string? LerCaminhoDaFicha()
    {
        ConsoleUi.Info("");
        ConsoleUi.Info("Caminho do PDF da ficha de personagem (precisa ser editável/preenchível).");

        while (true)
        {
            var caminho = EntradaDeArquivos.Limpar(ConsoleUi.LerLinha("  Ficha: "));

            if (caminho.Length == 0)
            {
                return null;
            }

            if (!File.Exists(caminho))
            {
                ConsoleUi.Erro($"Não encontrei '{caminho}'.");
                continue;
            }

            // Valida os campos já aqui: assim o usuário descobre que a ficha não é
            // preenchível antes de confirmar a importação inteira.
            try
            {
                var campos = ImportadorDeSistema.LerCamposDaFicha(caminho);
                ConsoleUi.Sucesso($"  {campos.Count} campo(s) preenchível(is) encontrado(s).");
                return caminho;
            }
            catch (Exception excecao)
            {
                ConsoleUi.Erro($"  {excecao.Message}");
            }
        }
    }
}
