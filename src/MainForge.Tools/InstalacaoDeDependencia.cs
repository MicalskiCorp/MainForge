using System.Diagnostics;
using System.Text;

namespace MainForge.Tools;

/// <summary>Um comando de instalação, pronto para ser mostrado ao usuário antes de rodar.</summary>
/// <param name="Programa">Executável a lançar (ex.: <c>winget</c>).</param>
/// <param name="Argumentos">Argumentos, um a um — nunca uma linha de comando montada à mão.</param>
public sealed record ComandoDeInstalacao(string Programa, IReadOnlyList<string> Argumentos)
{
    /// <summary>O comando como o usuário o digitaria. É o que aparece no pedido de confirmação.</summary>
    public override string ToString() =>
        string.Join(" ", new[] { Programa }.Concat(Argumentos.Select(Citar)));

    private static string Citar(string argumento) =>
        argumento.Contains(' ') ? $"\"{argumento}\"" : argumento;
}

/// <summary>Como a instalação terminou.</summary>
public sealed record ResultadoDaInstalacao(bool Sucesso, string Saida);

/// <summary>
/// Roda um comando de instalação mostrando a saída conforme ela sai.
///
/// <para><b>Por que isto nunca roda sozinho.</b> Instalar programa é mexer na máquina de outra
/// pessoa, e o aplicativo faz isso só depois de mostrar o comando exato e receber um "sim". A
/// alternativa — instalar em silêncio para "funcionar em qualquer máquina" — troca um aviso
/// chato por uma surpresa desagradável, e é exatamente o comportamento que ninguém espera de um
/// programa de criar ficha de RPG.</para>
///
/// <para>Só entram aqui comandos que o próprio aplicativo montou (<see cref="ComandoDeInstalacao"/>
/// vem de uma lista fixa em código): nada de linha de comando vinda de arquivo de configuração,
/// de argumento ou do modelo.</para>
/// </summary>
public static class InstalacaoDeDependencia
{
    public static async Task<ResultadoDaInstalacao> ExecutarAsync(
        ComandoDeInstalacao comando,
        Action<string>? aoSair = null,
        CancellationToken cancelamento = default)
    {
        var inicio = new ProcessStartInfo
        {
            FileName = comando.Programa,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        foreach (var argumento in comando.Argumentos)
        {
            inicio.ArgumentList.Add(argumento);
        }

        var registro = new StringBuilder();

        try
        {
            using var processo = Process.Start(inicio);

            if (processo is null)
            {
                return new ResultadoDaInstalacao(false, $"não foi possível lançar '{comando.Programa}'");
            }

            var erros = processo.StandardError.ReadToEndAsync(cancelamento);

            while (await processo.StandardOutput.ReadLineAsync(cancelamento) is { } linha)
            {
                registro.AppendLine(linha);
                aoSair?.Invoke(linha);
            }

            await processo.WaitForExitAsync(cancelamento);

            var saidaDeErro = (await erros).Trim();

            if (saidaDeErro.Length > 0)
            {
                registro.AppendLine(saidaDeErro);
            }

            return new ResultadoDaInstalacao(processo.ExitCode == 0, registro.ToString().Trim());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception excecao) when (excecao is IOException or SystemException)
        {
            return new ResultadoDaInstalacao(false, excecao.Message);
        }
    }
}
