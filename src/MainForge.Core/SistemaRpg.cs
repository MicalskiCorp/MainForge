namespace MainForge.Core;

/// <summary>
/// Um sistema de RPG identificado pelo nome da sua pasta (ex.: "D&amp;D5e"), que deve ser o
/// mesmo em Input/, Templates/ e Sistemas/.
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

    /// <summary>Sistemas com livros já importados em Input/, prontos ou não para uso.</summary>
    public static IReadOnlyList<SistemaRpg> DescobrirImportados(CaminhosDoProjeto caminhos)
    {
        if (!Directory.Exists(caminhos.Entrada))
        {
            return [];
        }

        return Directory.EnumerateDirectories(caminhos.Entrada)
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

    /// <summary>
    /// Os dois arquivos da ficha, que valem para o sistema inteiro e por isso moram na raiz de
    /// <c>Sistemas/&lt;Sistema&gt;/</c>, fora das pastas de fonte. O nome é fixo porque o
    /// Dungeon Master procura exatamente por eles.
    /// </summary>
    public static readonly IReadOnlyList<string> ArquivosDaFicha =
        ["Ficha-Mapeamento.md", "Ficha-ModeloEmTexto.md"];

    public string DiretorioEntrada(CaminhosDoProjeto caminhos) => Path.Combine(caminhos.Entrada, Id);
    public string DiretorioModelo(CaminhosDoProjeto caminhos) => Path.Combine(caminhos.Modelos, Id);
    public string DiretorioConhecimento(CaminhosDoProjeto caminhos) => Path.Combine(caminhos.Conhecimento, Id);

    /// <summary>Onde ficam os PDFs de uma fonte: <c>Input/&lt;Sistema&gt;/&lt;fonte&gt;/</c>.</summary>
    public string DiretorioDaFonte(CaminhosDoProjeto caminhos, FonteDoSistema fonte) =>
        CaminhosDoProjeto.ResolverDentroDe(DiretorioEntrada(caminhos), fonte.Id);

    /// <summary>Onde fica o conhecimento de uma fonte: <c>Sistemas/&lt;Sistema&gt;/&lt;fonte&gt;/</c>.</summary>
    public string DiretorioConhecimentoDaFonte(CaminhosDoProjeto caminhos, FonteDoSistema fonte) =>
        CaminhosDoProjeto.ResolverDentroDe(DiretorioConhecimento(caminhos), fonte.Id);

    /// <summary>Fontes com livro importado, base primeiro. É o que a tela de sistemas mostra.</summary>
    public IReadOnlyList<FonteDoSistema> DescobrirFontes(CaminhosDoProjeto caminhos) =>
        FontesEm(DiretorioEntrada(caminhos), diretorio => Directory.EnumerateFiles(diretorio, "*.pdf", SearchOption.AllDirectories).Any());

    /// <summary>
    /// Fontes que já têm conhecimento gerado — as únicas que o Dungeon Master consegue usar.
    /// Uma expansão importada mas ainda não processada não aparece aqui: oferecê-la na criação
    /// de personagem prometeria um conteúdo que não existe em lugar nenhum.
    /// </summary>
    public IReadOnlyList<FonteDoSistema> DescobrirFontesComConhecimento(CaminhosDoProjeto caminhos) =>
        FontesEm(DiretorioConhecimento(caminhos), diretorio => Directory.EnumerateFiles(diretorio, "*.md", SearchOption.AllDirectories).Any());

    /// <summary>
    /// O sistema está no layout antigo, de antes de os livros serem separados por fonte: tem
    /// conteúdo, mas não tem a pasta <c>base/</c> em que esse conteúdo deveria estar.
    ///
    /// <para>Enquanto isso for verdade não dá para escolher expansão na criação de personagem —
    /// tudo está no mesmo saco. A tela de sistemas oferece a migração, que é só mover arquivo e
    /// não custa token nenhum.</para>
    /// </summary>
    public bool PrecisaMigrarParaFontes(CaminhosDoProjeto caminhos) =>
        ConteudoForaDaBase(DiretorioEntrada(caminhos), "*.pdf", []) ||
        ConteudoForaDaBase(DiretorioConhecimento(caminhos), "*.md", ArquivosDaFicha);

    private static IReadOnlyList<FonteDoSistema> FontesEm(string diretorioDoSistema, Func<string, bool> temConteudo)
    {
        if (!Directory.Exists(diretorioDoSistema))
        {
            return [];
        }

        return FonteDoSistema.Ordenar(Directory
            .EnumerateDirectories(diretorioDoSistema)
            .Where(temConteudo)
            .Select(diretorio => new FonteDoSistema(Path.GetFileName(diretorio))));
    }

    /// <summary>
    /// Há arquivo de conteúdo neste diretório sem que exista a pasta <c>base/</c> — sinal de
    /// que ele foi montado antes da separação por fonte. Os arquivos que valem para o sistema
    /// inteiro (o índice e os dois da ficha) não contam: eles ficam na raiz por definição.
    /// </summary>
    private static bool ConteudoForaDaBase(string diretorio, string padrao, IReadOnlyList<string> queFicamNaRaiz)
    {
        if (!Directory.Exists(diretorio) || Directory.Exists(Path.Combine(diretorio, FonteDoSistema.IdDaBase)))
        {
            return false;
        }

        return Directory
            .EnumerateFiles(diretorio, padrao, SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Any(nome => nome is not null &&
                         !nome.Equals(NomeDoIndice, StringComparison.OrdinalIgnoreCase) &&
                         !queFicamNaRaiz.Contains(nome, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// O índice é derivado do conteúdo, então nunca conta como conteúdo. O nome mora aqui, e
    /// não em <c>IndiceDeConhecimento</c>, porque MainForge.Core não conhece MainForge.Tools —
    /// é lá que ele é reexportado, para haver um só lugar onde essa string é escrita.
    /// </summary>
    public const string NomeDoIndice = "index.md";

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
