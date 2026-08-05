namespace MainForge.Core;

/// <summary>
/// Valida os nomes que o usuário digita e que viram nome de pasta em <c>Input/</c>,
/// <c>Templates/</c> e <c>Sistemas/</c> — o do sistema e o da expansão.
///
/// <para>Existe separado porque o mesmo nome é usado por três pastas diferentes: aceitar uma
/// barra ou um ".." aqui não é desarrumação, é caminho para escrever fora do projeto. A
/// validação acontece antes de o nome virar caminho, e não depois.</para>
/// </summary>
public static class NomeDePasta
{
    /// <summary>Devolve o nome já sem espaços nas pontas, ou lança se ele não servir de pasta.</summary>
    /// <param name="oQueE">Como chamar o valor na mensagem de erro (ex.: "sistema", "expansão").</param>
    /// <param name="parametro">Nome do parâmetro de quem chamou, para a exceção.</param>
    public static string Validar(string nome, string oQueE, string parametro)
    {
        var limpo = (nome ?? "").Trim();

        if (limpo.Length == 0)
        {
            throw new ArgumentException($"O nome da {oQueE} não pode ser vazio.", parametro);
        }

        if (limpo != Path.GetFileName(limpo) || limpo is "." or "..")
        {
            throw new ArgumentException(
                $"'{nome}' não serve como nome de pasta — use um nome simples, sem barras.",
                parametro);
        }

        var proibidos = Path.GetInvalidFileNameChars().Where(limpo.Contains).ToList();

        return proibidos.Count == 0
            ? limpo
            : throw new ArgumentException(
                $"O nome '{nome}' tem caractere(s) não permitido(s): {string.Join(" ", proibidos)}",
                parametro);
    }
}
