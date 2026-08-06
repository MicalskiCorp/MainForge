using System.Text;
using MainForge.Core;
using MainForge.Mcp;

// Servidor MCP do MainForge. Não é feito para ser rodado à mão: quem o lança é o Claude Code,
// a partir do arquivo de configuração que MainForge.Agents gera, recebendo a raiz do projeto
// como primeiro argumento.
//
//   MainForge.Mcp.exe <raiz-do-projeto>

var raiz = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
    ? args[0]
    : Directory.GetCurrentDirectory();

// stdio é o transporte do protocolo: sem UTF-8 explícito, acentos do português corrompem as
// mensagens JSON-RPC nos dois sentidos.
var utf8 = new UTF8Encoding(false);
Console.InputEncoding = utf8;
Console.OutputEncoding = utf8;

// As fontes que a mesa usa chegam pelo ambiente, posto pelo arquivo de configuração que o
// Claude Code usou para lançar este processo. Ausente significa "sem limite de fonte", que é o
// caso do Configurador.
var restricao = RestricaoDeFontes.Ler(Environment.GetEnvironmentVariable(RestricaoDeFontes.VariavelDeAmbiente));

var servidor = new ServidorMcp(new CatalogoDeFerramentas(new CaminhosDoProjeto(raiz), restricao));

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
