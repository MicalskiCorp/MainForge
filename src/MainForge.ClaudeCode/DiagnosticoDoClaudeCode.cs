using System.Diagnostics;
using System.Text;

namespace MainForge.ClaudeCode;

/// <summary>Resultado de um diagnóstico da instalação do Claude Code.</summary>
/// <param name="Versao">Versão relatada pelo CLI, ou <c>null</c> se nem isso respondeu.</param>
/// <param name="Autenticado">Se um turno trivial completou — é o que prova que a assinatura está ativa.</param>
/// <param name="Detalhe">Mensagem de erro quando algo falhou.</param>
public sealed record ResultadoDoDiagnostico(string? Versao, bool Autenticado, string? Detalhe);

/// <summary>
/// Confere se o Claude Code está instalado e autenticado. Vale a pena existir porque as duas
/// falhas têm remédios diferentes — "não instalado" e "instalado mas deslogado" apareceriam
/// como o mesmo erro genérico no meio de uma operação cara.
/// </summary>
public static class DiagnosticoDoClaudeCode
{
    public static async Task<ResultadoDoDiagnostico> ExecutarAsync(
        OpcoesDoClaudeCode opcoes,
        CancellationToken cancelamento = default)
    {
        var versao = await LerVersaoAsync(opcoes, cancelamento);

        if (versao is null)
        {
            return new ResultadoDoDiagnostico(null, false,
                $"'{opcoes.CaminhoExecutavel}' não respondeu a --version.");
        }

        // Um turno mínimo, sem ferramenta nenhuma: se a assinatura não estiver ativa, é aqui
        // que aparece, e por poucos tokens.
        var caminhoPrompt = Path.Combine(Path.GetTempPath(), $"mainforge-diag-{Guid.NewGuid():N}.md");

        try
        {
            await File.WriteAllTextAsync(
                caminhoPrompt,
                "Você é um verificador de instalação. Responda exatamente 'ok' e nada mais.",
                cancelamento);

            var pedido = new PedidoDeTurno
            {
                Mensagem = "Responda apenas: ok",
                DiretorioDeTrabalho = Directory.GetCurrentDirectory(),
                CaminhoPromptDeSistema = caminhoPrompt,
                FerramentasNegadas = ["Bash", "Read", "Write", "Edit", "Glob", "Grep", "Task", "WebFetch", "WebSearch"],
            };

            await foreach (var evento in new ProcessoDoClaudeCode(opcoes).ExecutarAsync(pedido, cancelamento))
            {
                if (evento is TurnoConcluido conclusao)
                {
                    return new ResultadoDoDiagnostico(versao, !conclusao.Falhou, conclusao.MotivoDaFalha);
                }
            }

            return new ResultadoDoDiagnostico(versao, false, "O Claude Code não devolveu resposta.");
        }
        finally
        {
            Apagar(caminhoPrompt);
        }
    }

    private static void Apagar(string caminho)
    {
        try
        {
            File.Delete(caminho);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task<string?> LerVersaoAsync(OpcoesDoClaudeCode opcoes, CancellationToken cancelamento)
    {
        var inicio = new ProcessStartInfo
        {
            FileName = opcoes.CaminhoExecutavel,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        inicio.ArgumentList.Add("--version");

        try
        {
            using var processo = Process.Start(inicio);

            if (processo is null)
            {
                return null;
            }

            var saida = await processo.StandardOutput.ReadToEndAsync(cancelamento);
            await processo.WaitForExitAsync(cancelamento);

            var texto = saida.Trim();
            return texto.Length > 0 ? texto : null;
        }
        catch (SystemException)
        {
            return null;
        }
    }
}
