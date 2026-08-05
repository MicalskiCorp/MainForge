namespace MainForge.Core;

/// <summary>
/// Acha um executável no PATH do sistema.
///
/// <para>Existe porque o aplicativo depende de programas que <em>podem</em> estar na máquina —
/// o markitdown, o poppler — e precisa saber quais estão antes de contar com eles. Mora em
/// <see cref="MainForge.Core"/> por ser usado dos dois lados da árvore de projetos: pela
/// conversão dos livros e pelo diagnóstico do que o Claude Code consegue fazer.</para>
/// </summary>
public static class ProgramaDoSistema
{
    /// <summary>
    /// Caminho completo do programa, ou <c>null</c> se ele não está no PATH.
    ///
    /// <para>Respeita o PATHEXT do Windows: sem isso, um programa instalado como <c>.exe</c> ou
    /// <c>.cmd</c> — o caso do pip — não seria encontrado.</para>
    /// </summary>
    public static string? Localizar(string programa)
    {
        var extensoes = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';')
            : [""];

        var diretorios = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var diretorio in diretorios)
        {
            foreach (var extensao in extensoes)
            {
                string candidato;

                try
                {
                    candidato = Path.Combine(diretorio, programa + extensao.Trim());
                }
                catch (ArgumentException)
                {
                    break; // entrada inválida no PATH; as outras continuam valendo
                }

                if (File.Exists(candidato))
                {
                    return candidato;
                }
            }
        }

        return null;
    }
}
