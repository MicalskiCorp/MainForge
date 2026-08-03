namespace MainForge.Core;

/// <summary>
/// Um sistema de RPG identificado pelo nome da sua pasta (ex.: "D&amp;D5e"), que deve ser o
/// mesmo em Systems/, Templates/ e Knowledge/.
/// </summary>
public sealed record SistemaRpg(string Id)
{
    /// <summary>
    /// Sistema fictício de um livro só, usado pelo harness de validação ponta a ponta
    /// (<c>tools/ValidacaoPontaAPonta</c>) para exercitar o fluxo inteiro consumindo pouca cota.
    /// </summary>
    public const string IdDoSistemaDeTeste = "SistemaTeste";

    /// <summary>
    /// Sistema que só existe para o desenvolvimento do aplicativo. Não aparece na interface:
    /// quem abre o programa não tem o que fazer com ele, e vê-lo na lista ao lado dos sistemas
    /// de verdade só levantaria a dúvida de se é para usar. O harness continua alcançando-o
    /// pelo nome, que é como ele o referencia.
    /// </summary>
    public bool EhDeTesteInterno => Id.Equals(IdDoSistemaDeTeste, StringComparison.OrdinalIgnoreCase);

    /// <summary>Sistemas com livros já importados em Systems/, prontos ou não para uso.</summary>
    public static IReadOnlyList<SistemaRpg> DescobrirImportados(CaminhosDoProjeto caminhos)
    {
        if (!Directory.Exists(caminhos.Sistemas))
        {
            return [];
        }

        return Directory.EnumerateDirectories(caminhos.Sistemas)
            .Select(d => new SistemaRpg(Path.GetFileName(d)))
            .Where(s => !s.EhDeTesteInterno)
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
            .Where(s => !s.EhDeTesteInterno)
            .Where(s => s.TemConhecimento(caminhos))
            .OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string DiretorioSistemas(CaminhosDoProjeto caminhos) => Path.Combine(caminhos.Sistemas, Id);
    public string DiretorioModelo(CaminhosDoProjeto caminhos) => Path.Combine(caminhos.Modelos, Id);
    public string DiretorioConhecimento(CaminhosDoProjeto caminhos) => Path.Combine(caminhos.Conhecimento, Id);

    /// <summary>
    /// Existe conteúdo em Markdown para o Dungeon Master usar. A pasta sozinha não basta: um
    /// processamento interrompido antes do primeiro arquivo deixa lá só o registro de
    /// progresso, e um sistema nessa situação não está pronto para criar personagem.
    /// </summary>
    public bool TemConhecimento(CaminhosDoProjeto caminhos)
    {
        var diretorio = DiretorioConhecimento(caminhos);

        return Directory.Exists(diretorio) &&
               Directory.EnumerateFiles(diretorio, "*.md", SearchOption.AllDirectories).Any();
    }
}
