namespace MainForge.ClaudeCode;

/// <summary>
/// Encontra o executável do Claude Code na máquina. É a única dependência externa do
/// aplicativo: em vez de uma API key, o que precisa existir é uma instalação do Claude Code
/// já autenticada (a autenticação é dele, não nossa — nunca vemos credencial nenhuma).
/// </summary>
public static class LocalizadorDoClaudeCode
{
    /// <summary>
    /// Variável de ambiente para apontar uma instalação em local não padrão, sem precisar
    /// mexer no PATH.
    /// </summary>
    public const string VariavelDeAmbiente = "MAINFORGE_CLAUDE_CODE";

    /// <summary>
    /// Procura, em ordem: a variável de ambiente, o PATH, e os diretórios onde os
    /// instaladores oficiais colocam o binário no Windows. Devolve <c>null</c> se não achar —
    /// cabe à interface explicar ao usuário como instalar, não a esta classe.
    /// </summary>
    public static string? Localizar()
    {
        var doAmbiente = Environment.GetEnvironmentVariable(VariavelDeAmbiente);

        if (!string.IsNullOrWhiteSpace(doAmbiente) && File.Exists(doAmbiente))
        {
            return Path.GetFullPath(doAmbiente);
        }

        foreach (var candidato in Candidatos())
        {
            if (File.Exists(candidato))
            {
                return Path.GetFullPath(candidato);
            }
        }

        return null;
    }

    private static IEnumerable<string> Candidatos()
    {
        var perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appDataLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDataRoaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        // Instalador nativo (o mais comum hoje).
        yield return Path.Combine(perfil, ".local", "bin", NomeDoExecutavel);

        // Instalação via npm global.
        yield return Path.Combine(appDataRoaming, "npm", NomeDoExecutavel);
        yield return Path.Combine(appDataLocal, "npm", NomeDoExecutavel);

        foreach (var doPath in NoPath())
        {
            yield return doPath;
        }
    }

    private static IEnumerable<string> NoPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        foreach (var diretorio in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var limpo = diretorio.Trim('"');

            if (limpo.Length == 0)
            {
                continue;
            }

            // Path.Combine explode com caracteres inválidos, que aparecem em PATH mal formado.
            string candidato;

            try
            {
                candidato = Path.Combine(limpo, NomeDoExecutavel);
            }
            catch (ArgumentException)
            {
                continue;
            }

            yield return candidato;
        }
    }

    private static string NomeDoExecutavel =>
        OperatingSystem.IsWindows() ? "claude.exe" : "claude";
}
