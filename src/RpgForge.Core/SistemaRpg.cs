namespace RpgForge.Core;

/// <summary>
/// Um sistema de RPG identificado pelo nome da sua pasta (ex.: "D&amp;D5e"), que deve ser o
/// mesmo em Systems/, Templates/ e Knowledge/.
/// </summary>
public sealed record SistemaRpg(string Id)
{
    /// <summary>Sistemas com livros já importados em Systems/, prontos ou não para uso.</summary>
    public static IReadOnlyList<SistemaRpg> DescobrirImportados(CaminhosDoProjeto caminhos)
    {
        if (!Directory.Exists(caminhos.Sistemas))
        {
            return [];
        }

        return Directory.EnumerateDirectories(caminhos.Sistemas)
            .Select(d => new SistemaRpg(Path.GetFileName(d)))
            .OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Sistemas já processados pelo Configurador, com base de conhecimento pronta.</summary>
    public static IReadOnlyList<SistemaRpg> DescobrirProntos(CaminhosDoProjeto caminhos)
    {
        if (!Directory.Exists(caminhos.Conhecimento))
        {
            return [];
        }

        return Directory.EnumerateDirectories(caminhos.Conhecimento)
            .Select(d => new SistemaRpg(Path.GetFileName(d)))
            .OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string DiretorioSistemas(CaminhosDoProjeto caminhos) => Path.Combine(caminhos.Sistemas, Id);
    public string DiretorioModelo(CaminhosDoProjeto caminhos) => Path.Combine(caminhos.Modelos, Id);
    public string DiretorioConhecimento(CaminhosDoProjeto caminhos) => Path.Combine(caminhos.Conhecimento, Id);

    public bool TemConhecimento(CaminhosDoProjeto caminhos) => Directory.Exists(DiretorioConhecimento(caminhos));
}
