using Anthropic.Helpers.Beta;
using MainBuild.Core;

namespace MainBuild.Tools;

/// <summary>
/// Descobre qual PDF de Templates/&lt;sistema&gt;/ é a ficha a usar. Fica separado porque duas
/// ferramentas dependem disso e precisam concordar: o Configurador lê a ficha para mapeá-la
/// (<see cref="FerramentaLerFichaModelo"/>) e o Dungeon Master a preenche
/// (<see cref="FerramentaPreencherFichaPersonagem"/>) — se as regras divergissem, o agente
/// mapearia uma ficha e preencheria outra.
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
                : throw new BetaToolError($"Template '{arquivoModelo}' não encontrado em '{diretorioModelo}'.");
        }

        if (!Directory.Exists(diretorioModelo))
        {
            throw new BetaToolError($"Não há templates em '{diretorioModelo}'.");
        }

        var pdfs = Directory.EnumerateFiles(diretorioModelo, "*.pdf", SearchOption.TopDirectoryOnly).ToList();

        return pdfs.Count switch
        {
            0 => throw new BetaToolError($"Nenhum template PDF encontrado em '{diretorioModelo}'."),
            1 => pdfs[0],
            _ => throw new BetaToolError(
                $"Há mais de um template PDF em '{diretorioModelo}' — informe 'arquivoModelo'. " +
                $"Opções: {string.Join(", ", pdfs.Select(Path.GetFileName))}."),
        };
    }
}
