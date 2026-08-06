using MainForge.ClaudeCode;
using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Cli;

/// <summary>Quanto falta se o programa não estiver na máquina.</summary>
internal enum PesoDaDependencia
{
    /// <summary>Sem isto o aplicativo não faz o que promete.</summary>
    Obrigatoria,

    /// <summary>Sem isto uma parte deixa de funcionar, mas o resto anda.</summary>
    Recomendada,

    /// <summary>Sem isto tudo funciona; com isto funciona melhor.</summary>
    Opcional,
}

/// <summary>Um programa externo de que o aplicativo depende, e como resolvê-lo.</summary>
/// <param name="ParaQue">O que deixa de funcionar sem ele — é o que decide se vale instalar.</param>
/// <param name="Comando">Como instalar nesta máquina, quando há um jeito automático.</param>
/// <param name="Instrucao">O que dizer quando não há.</param>
/// <param name="ReiniciarDepois">
/// A instalação mexe no PATH, e processo já em execução não enxerga PATH novo.
/// </param>
internal sealed record Dependencia(
    string Nome,
    PesoDaDependencia Peso,
    string ParaQue,
    Func<string?> Localizar,
    Func<ComandoDeInstalacao?> Comando,
    string Instrucao,
    bool ReiniciarDepois = false);

/// <summary>
/// A tela que responde "por que isto não funciona nesta máquina?".
///
/// <para><b>Por que existe.</b> O aplicativo é baixado como um binário só e roda em máquina que
/// ninguém preparou. O runtime .NET vem embutido no download e as bibliotecas de PDF são
/// compiladas junto — mas três coisas continuam sendo instalação de quem usa: o Claude Code
/// (que é a conta e a assinatura da pessoa), o poppler e o markitdown. Descobrir isso no meio de
/// um processamento de livro, por uma mensagem de erro em inglês vinda de um subprocesso, é o
/// pior momento possível.</para>
///
/// <para><b>Por que não instala sozinho.</b> Instalar programa é mexer na máquina de outra
/// pessoa. O aplicativo mostra o comando exato, pergunta, e só então roda — e nunca para o
/// Claude Code, cuja instalação oficial é baixar e executar um script da internet. Guiar é a
/// fronteira certa ali.</para>
/// </summary>
internal static class FluxoDeDependencias
{
    /// <summary>
    /// Confere antes de uma operação cara e só fala se houver o que dizer. Devolve <c>false</c>
    /// quando falta algo obrigatório e o usuário não quis resolver — aí não vale começar.
    /// </summary>
    public static async Task<bool> GarantirAsync(CancellationToken cancelamento)
    {
        var faltando = Catalogo().Where(dependencia => dependencia.Localizar() is null).ToList();

        if (faltando.Count == 0)
        {
            return true;
        }

        ConsoleUi.Titulo("Dependências");
        ConsoleUi.Info("Algumas coisas que este processamento usaria não estão nesta máquina:");

        foreach (var dependencia in faltando)
        {
            await OferecerAsync(dependencia, cancelamento);
        }

        var obrigatoriasQueFaltam = faltando
            .Where(dependencia => dependencia.Peso == PesoDaDependencia.Obrigatoria)
            .Where(dependencia => dependencia.Localizar() is null)
            .ToList();

        if (obrigatoriasQueFaltam.Count == 0)
        {
            return true;
        }

        ConsoleUi.Erro(
            $"Sem {string.Join(" e ", obrigatoriasQueFaltam.Select(dependencia => dependencia.Nome))}, " +
            "o processamento não tem como acontecer.");

        return false;
    }

    /// <summary>A tela completa do menu: mostra tudo, inclusive o que já está resolvido.</summary>
    public static async Task ExecutarAsync(ContextoDoAplicativo contexto, CancellationToken cancelamento)
    {
        ConsoleUi.Titulo("Dependências do aplicativo");
        ConsoleUi.Detalhe($"Projeto: {contexto.Caminhos.Raiz}");
        ConsoleUi.Info("");
        ConsoleUi.Sucesso($"  [ok]     .NET {Environment.Version.Major} — embutido no aplicativo, nada a instalar");
        ConsoleUi.Sucesso("  [ok]     leitura e preenchimento de PDF — compilados junto (PdfPig, PDFsharp)");

        var dependencias = Catalogo();

        foreach (var dependencia in dependencias)
        {
            var onde = dependencia.Localizar();

            if (onde is not null)
            {
                ConsoleUi.Sucesso($"  [ok]     {dependencia.Nome} — {Encurtar(onde)}");
                continue;
            }

            var rotulo = dependencia.Peso == PesoDaDependencia.Obrigatoria ? "[FALTA]  " : "[falta]  ";
            ConsoleUi.Aviso($"  {rotulo}{dependencia.Nome} — {dependencia.ParaQue}");
        }

        var faltando = dependencias.Where(dependencia => dependencia.Localizar() is null).ToList();

        if (faltando.Count == 0)
        {
            ConsoleUi.Info("");
            ConsoleUi.Sucesso("Está tudo no lugar.");
            return;
        }

        foreach (var dependencia in faltando)
        {
            await OferecerAsync(dependencia, cancelamento);
        }
    }

    /// <summary>
    /// As três dependências externas, na ordem em que doem. O que <em>não</em> está aqui é tão
    /// importante quanto: o runtime .NET e as bibliotecas de PDF viajam dentro do binário.
    /// </summary>
    private static IReadOnlyList<Dependencia> Catalogo() =>
    [
        new(
            "Claude Code",
            PesoDaDependencia.Obrigatoria,
            "é ele que roda os agentes, com a sua conta e a sua assinatura",
            LocalizadorDoClaudeCode.Localizar,
            ComandoDoClaudeCode,
            "Instale de um destes jeitos e depois entre na sua conta por Ambiente > Claude Code:\n" +
            "  winget install --id Anthropic.ClaudeCode\n" +
            "  npm install -g @anthropic-ai/claude-code        (se você já usa Node)\n" +
            "  ou o instalador oficial: https://claude.com/product/claude-code\n" +
            "Se ele já estiver instalado em outro lugar, aponte " +
            $"{LocalizadorDoClaudeCode.VariavelDeAmbiente} para o executável.",
            ReiniciarDepois: true),

        new(
            "poppler (pdftoppm)",
            PesoDaDependencia.Recomendada,
            "sem ele o agente não abre PDF nenhum — inclusive a ficha em branco, que é lida pelo leiaute",
            () => ProgramaDoSistema.Localizar(LeituraDePdf.ProgramaNecessario),
            ComandoDoPoppler,
            LeituraDePdf.ComoInstalar,
            ReiniciarDepois: true),

        new(
            "markitdown",
            PesoDaDependencia.Opcional,
            "converte os livros melhor que o extrator interno, sobretudo tabelas e listas",
            () => ConversorDeLivros.Localizar()?.Descricao,
            ComandoDoMarkitdown,
            ConversorDeLivros.ComoInstalar),
    ];

    private static async Task OferecerAsync(Dependencia dependencia, CancellationToken cancelamento)
    {
        ConsoleUi.Info("");
        ConsoleUi.Aviso($"{dependencia.Nome} — {dependencia.ParaQue}.");

        var comando = dependencia.Comando();

        if (comando is null)
        {
            ConsoleUi.Detalhe(dependencia.Instrucao);
            return;
        }

        ConsoleUi.Info("Dá para instalar agora, com o gerenciador de pacotes que já existe aqui:");
        ConsoleUi.Detalhe($"  {comando}");

        if (!ConsoleUi.Confirmar("Rodar esse comando?"))
        {
            ConsoleUi.Detalhe(dependencia.Instrucao);
            return;
        }

        var resultado = await InstalacaoDeDependencia.ExecutarAsync(
            comando,
            linha => ConsoleUi.Detalhe($"    {linha}"),
            cancelamento);

        if (!resultado.Sucesso)
        {
            ConsoleUi.Erro($"A instalação não terminou bem. {PrimeirasLinhas(resultado.Saida)}");
            ConsoleUi.Detalhe(dependencia.Instrucao);
            return;
        }

        ConsoleUi.Sucesso($"{dependencia.Nome} instalado.");

        if (dependencia.ReiniciarDepois && dependencia.Localizar() is null)
        {
            ConsoleUi.Aviso(
                "Feche e abra o aplicativo para ele enxergar o programa novo: o PATH só chega a " +
                "processos iniciados depois da instalação.");
        }
    }

    /// <summary>
    /// Como instalar o Claude Code, na ordem do que dá menos trabalho a quem está do outro lado.
    ///
    /// <para>O winget vem com o Windows, então é o caminho que existe em máquina recém-formatada
    /// — e é o que faz a instalação ser uma resposta "sim" em vez de uma ida ao navegador. O npm
    /// é a segunda opção, para quem já tem Node.</para>
    ///
    /// <para>O que o aplicativo <b>não</b> oferece é o instalador oficial em script
    /// (<c>irm ... | iex</c>): baixar e executar um script remoto em nome do usuário é
    /// exatamente o tipo de coisa que um programa não deveria fazer por conta própria. Quem
    /// preferir esse caminho tem a instrução na tela.</para>
    /// </summary>
    private static ComandoDeInstalacao? ComandoDoClaudeCode()
    {
        if (ProgramaDoSistema.Localizar("winget") is { } winget)
        {
            return new ComandoDeInstalacao(winget,
                [
                    "install", "--id", "Anthropic.ClaudeCode",
                    "--source", "winget",
                    "--accept-package-agreements", "--accept-source-agreements",
                ]);
        }

        return ProgramaDoSistema.Localizar("npm") is { } npm
            ? new ComandoDeInstalacao(npm, ["install", "-g", "@anthropic-ai/claude-code"])
            : null;
    }

    private static ComandoDeInstalacao? ComandoDoPoppler() =>
        ProgramaDoSistema.Localizar("winget") is { } winget
            ? new ComandoDeInstalacao(winget,
                [
                    "install", "--id", "oschwartz10612.Poppler",
                    "--source", "winget",
                    "--accept-package-agreements", "--accept-source-agreements",
                ])
            : null;

    private static ComandoDeInstalacao? ComandoDoMarkitdown() =>
        ProgramaDoSistema.Localizar("pip") is { } pip
            ? new ComandoDeInstalacao(pip, ["install", "markitdown[pdf]"])
            : null;

    private static string Encurtar(string texto) =>
        texto.Length <= 70 ? texto : string.Concat(texto.AsSpan(0, 70), "...");

    private static string PrimeirasLinhas(string texto)
    {
        var linhas = texto
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join(" ", linhas.TakeLast(3));
    }
}
