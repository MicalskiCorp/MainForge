using MainBuild.Tools;

namespace MainBuild.Cli;

/// <summary>
/// Traz um sistema de RPG novo para dentro do aplicativo: o usuário informa o nome, os PDFs
/// dos livros e a ficha de personagem editável, e o programa valida e copia tudo para
/// Systems/ e Templates/. É o passo que antecede o Agente Configurador — sem os arquivos
/// aqui dentro, não há o que mapear.
/// </summary>
internal static class FluxoDeImportacao
{
    public static async Task ExecutarAsync(ContextoDoAplicativo contexto, CancellationToken cancelamento)
    {
        ConsoleUi.Titulo("Importar um sistema de RPG");
        ConsoleUi.Detalhe("Arraste os arquivos para a janela do console para colar o caminho. Enter vazio cancela.");

        var nome = ConsoleUi.LerLinha("\nNome do sistema (ex.: Aventura&Cia): ");

        if (nome.Length == 0)
        {
            return;
        }

        var livros = LerCaminhosDosLivros();

        if (livros.Count == 0)
        {
            return;
        }

        var ficha = LerCaminhoDaFicha();

        if (ficha is null)
        {
            return;
        }

        ConsoleUi.Titulo($"Confirmar importação de '{nome}'");
        ConsoleUi.Info($"  Livros  -> Systems/{nome}/");

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
        ConsoleUi.Info($"  {resultado.Livros.Count} livro(s) e a ficha '{resultado.Ficha}'.");
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

    private static List<string> LerCaminhosDosLivros()
    {
        ConsoleUi.Info("");
        ConsoleUi.Info("Caminho do PDF de cada livro do sistema, um por linha.");
        ConsoleUi.Detalhe("Enter numa linha vazia encerra a lista.");

        var livros = new List<string>();

        while (true)
        {
            var caminho = LimparCaminho(ConsoleUi.LerLinha($"  Livro {livros.Count + 1}: "));

            if (caminho.Length == 0)
            {
                if (livros.Count == 0)
                {
                    ConsoleUi.Aviso("Nenhum livro informado — importação cancelada.");
                }

                return livros;
            }

            if (!File.Exists(caminho))
            {
                ConsoleUi.Erro($"Não encontrei '{caminho}'.");
                continue;
            }

            livros.Add(caminho);
        }
    }

    private static string? LerCaminhoDaFicha()
    {
        ConsoleUi.Info("");
        ConsoleUi.Info("Caminho do PDF da ficha de personagem (precisa ser editável/preenchível).");

        while (true)
        {
            var caminho = LimparCaminho(ConsoleUi.LerLinha("  Ficha: "));

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

    /// <summary>
    /// Arrastar um arquivo para o console cola o caminho entre aspas quando ele tem espaço;
    /// tirar as aspas evita um "arquivo não encontrado" que confundiria o usuário.
    /// </summary>
    private static string LimparCaminho(string digitado) => digitado.Trim().Trim('"');
}
