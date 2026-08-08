using System.Text;
using MainForge.Core;
using MainForge.Mcp;

namespace MainForge.Cli;

/// <summary>
/// O aplicativo rodando como servidor MCP, quando lançado com <c>--mcp &lt;raiz&gt;</c>.
///
/// <para>Quem chama é o Claude Code, a partir do arquivo de configuração que
/// <c>ConfiguracaoDoServidorMcp</c> gera — nunca uma pessoa. É a única passagem em que a
/// interface toca em <c>MainForge.Mcp</c>, e ela existe para o aplicativo caber em um
/// executável só: distribuído como binário self-contained, um segundo executável significaria
/// um segundo runtime .NET embutido e duas versões que podem divergir.</para>
///
/// <para>Regra do transporte stdio: <b>nada além de JSON-RPC pode sair na saída padrão</b>. Por
/// isso este modo não imprime nada — nem título, nem aviso — e a saída de erro fica para
/// diagnóstico.</para>
/// </summary>
internal static class ModoServidorMcp
{
    public static async Task<int> RodarAsync(string? raiz)
    {
        var diretorio = string.IsNullOrWhiteSpace(raiz) ? Directory.GetCurrentDirectory() : raiz;

        // Sem UTF-8 explícito, acentos do português corrompem as mensagens JSON-RPC nos dois
        // sentidos.
        var utf8 = new UTF8Encoding(false);
        Console.InputEncoding = utf8;
        Console.OutputEncoding = utf8;

        // As fontes que a mesa usa chegam pelo ambiente, posto pelo arquivo de configuração que
        // o Claude Code usou para lançar este processo. Ausente significa "sem limite de fonte".
        var restricao = EscopoDaSessao.Ler(
            Environment.GetEnvironmentVariable(EscopoDaSessao.VariavelDeAmbiente));

        var servidor = new ServidorMcp(new CatalogoDeFerramentas(new CaminhosDoProjeto(diretorio), restricao));

        using var cancelamento = new CancellationTokenSource();

        Console.CancelKeyPress += (_, evento) =>
        {
            evento.Cancel = true;
            cancelamento.Cancel();
        };

        try
        {
            await servidor.RodarAsync(Console.In, Console.Out, cancelamento.Token);
        }
        catch (OperationCanceledException)
        {
            // Encerramento normal: o Claude Code fecha a stdin quando termina a sessão.
        }

        return 0;
    }
}
