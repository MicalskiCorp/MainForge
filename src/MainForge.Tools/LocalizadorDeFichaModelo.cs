using MainForge.Core;

namespace MainForge.Tools;

/// <summary>
/// Descobre qual PDF de Templates/&lt;sistema&gt;/ é a ficha a usar. Fica separado porque os dois
/// usos precisam concordar: o Configurador lista os campos da ficha para mapeá-la e o Dungeon
/// Master a preenche (ambos via <see cref="PreenchedorDeFicha"/>) — se as regras divergissem,
/// o agente mapearia uma ficha e preencheria outra.
/// </summary>
internal static class LocalizadorDeFichaModelo
{
    public static string Resolver(string diretorioModelo, string? arquivoModelo)
    {
        if (arquivoModelo is not null)
        {
            var caminho = CaminhosDoProjeto.ResolverDentroDe(diretorioModelo, arquivoModelo);

            return File.Exists(caminho)
                ? caminho
                : throw new ErroDeFerramenta($"Template '{arquivoModelo}' não encontrado em '{diretorioModelo}'.");
        }

        if (!Directory.Exists(diretorioModelo))
        {
            throw new ErroDeFerramenta($"Não há templates em '{diretorioModelo}'.");
        }

        var pdfs = Directory.EnumerateFiles(diretorioModelo, "*.pdf", SearchOption.TopDirectoryOnly).ToList();

        return pdfs.Count switch
        {
            0 => throw new ErroDeFerramenta($"Nenhum template PDF encontrado em '{diretorioModelo}'."),
            1 => pdfs[0],
            _ => throw new ErroDeFerramenta(
                $"Há mais de um template PDF em '{diretorioModelo}' — informe 'arquivoModelo'. " +
                $"Opções: {string.Join(", ", pdfs.Select(Path.GetFileName))}."),
        };
    }
}
