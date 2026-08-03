namespace MainForge.Cli;

/// <summary>
/// Leitura de caminhos de arquivo digitados (ou arrastados) no console. Fica separada dos
/// fluxos porque tanto a importação de um sistema novo quanto a adição de um compêndio pedem
/// a mesma coisa da mesma forma.
/// </summary>
internal static class EntradaDeArquivos
{
    /// <summary>
    /// Lê uma lista de PDFs, um por linha, até uma linha vazia. Devolve lista vazia quando o
    /// usuário desiste sem informar nenhum.
    /// </summary>
    public static List<string> LerPdfs(string oQueE)
    {
        ConsoleUi.Info("");
        ConsoleUi.Info($"Caminho do PDF de cada {oQueE}, um por linha.");
        ConsoleUi.Detalhe("Enter numa linha vazia encerra a lista.");

        var arquivos = new List<string>();

        while (true)
        {
            var caminho = Limpar(ConsoleUi.LerLinha($"  {char.ToUpperInvariant(oQueE[0])}{oQueE[1..]} {arquivos.Count + 1}: "));

            if (caminho.Length == 0)
            {
                return arquivos;
            }

            if (!File.Exists(caminho))
            {
                ConsoleUi.Erro($"Não encontrei '{caminho}'.");
                continue;
            }

            arquivos.Add(caminho);
        }
    }

    /// <summary>
    /// Arrastar um arquivo para o console cola o caminho entre aspas quando ele tem espaço;
    /// tirar as aspas evita um "arquivo não encontrado" que confundiria o usuário.
    /// </summary>
    public static string Limpar(string digitado) => digitado.Trim().Trim('"');
}
