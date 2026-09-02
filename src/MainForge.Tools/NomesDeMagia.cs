using System.Text.RegularExpressions;

namespace MainForge.Tools;

/// <summary>
/// A regra de como o nome de uma magia é escrito neste aplicativo: <b>o nome fica em inglês, e a
/// tradução vem ao lado, entre parênteses</b> — <c>Fireball (Bola de Fogo)</c>. Tudo o mais da
/// magia (escola, alcance, duração, o texto do efeito) é traduzido normalmente: é só o
/// <em>nome</em> que não muda de idioma.
///
/// <para><b>Por que o nome fica em inglês.</b> O nome da magia é o identificador dela na mesa. É
/// por ele que se procura a magia no livro, na errata, no fórum e no aplicativo do outro jogador —
/// e cada tradução escolhe uma palavra diferente para a mesma magia, de edição para edição e de
/// editora para editora. Traduzir o nome e mais nada custaria pouco; traduzir o nome e perder o
/// original transforma a base num dicionário particular, em que a magia que o mestre chamou de
/// "Bola de Fogo" pode ser <c>Fireball</c>, <c>Flaming Sphere</c> ou <c>Fire Bolt</c> conforme
/// quem traduziu. Manter os dois resolve as duas pontas: quem lê em português entende, e quem
/// precisa cruzar com qualquer outra fonte tem a chave certa.</para>
///
/// <para><b>A exceção, e ela é uma só: o sistema em português.</b> Num jogo escrito originalmente
/// em português, o nome da magia <em>é</em> o nome em português — não existe original em inglês
/// para preservar, e inventar um seria fabricar informação que os livros não têm. Nesses sistemas
/// a regra não vale, e nada muda. Quem responde "este sistema é em português?" é
/// <see cref="RegrasDaFicha.Idioma"/>, detectado da ficha em branco na importação.</para>
/// </summary>
public static class NomesDeMagia
{
    /// <summary>A instrução pronta, para ir junto de qualquer recusa ou aviso.</summary>
    public const string ComoEscrever =
        "Escreva o nome da magia em inglês com a tradução entre parênteses, assim: " +
        "\"Fireball (Bola de Fogo)\". Só o nome fica em inglês — o resto da magia vai traduzido.";

    /// <summary>
    /// As pastas em que um arquivo de conhecimento é um arquivo de magias. É o gatilho da regra:
    /// fora daqui ela não opina, porque um nome entre parênteses no meio de um texto de classe não
    /// é o nome de uma magia.
    /// </summary>
    private static readonly string[] PastasDeMagia =
        ["magias", "magia", "spells", "spell", "feiticos", "feitico", "encantamentos", "conjuracoes"];

    /// <summary>
    /// Um título de magia bem formado: um nome, e a tradução entre parênteses no fim. O que vem
    /// antes dos parênteses precisa ter letra — <c>## (Bola de Fogo)</c> não é nome nenhum.
    /// </summary>
    private static readonly Regex ComTraducao = new(
        @"^\s*\S.*\p{L}.*\s\(\s*[^()]*\p{L}[^()]*\s*\)\s*$",
        RegexOptions.Compiled);

    /// <summary>Um título de Markdown: o nível de <c>#</c> e o texto dele.</summary>
    private static readonly Regex Titulo = new(@"^(#{1,6})\s+(.*\S)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Este caminho de arquivo é de um arquivo de magias? Olha as pastas do caminho, e não o nome
    /// do arquivo: <c>base/Magias/Circulo-3.md</c> é de magias e <c>base/Classes/Mago.md</c> não é,
    /// mesmo citando magias por dentro.
    /// </summary>
    public static bool EhArquivoDeMagias(string caminhoRelativo)
    {
        var partes = caminhoRelativo.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        // O último elemento é o arquivo, e o nome dele não decide nada: um "Magias.md" solto dentro
        // de Classes/ é a tabela de magias daquela classe, não o catálogo.
        return partes
            .Take(Math.Max(0, partes.Length - 1))
            .Any(pasta => PastasDeMagia.Contains(
                TextoNormalizado.SemAcento(pasta).ToLowerInvariant(),
                StringComparer.Ordinal));
    }

    /// <summary>
    /// Os títulos de um arquivo de magias que não trazem o nome em inglês com a tradução ao lado.
    /// Lista vazia é aprovação.
    ///
    /// <para><b>O que ela deixa passar de propósito.</b> O título do arquivo (o primeiro
    /// <c>#</c>), que é o nome da seção e não de uma magia, e os títulos que claramente organizam
    /// em vez de nomear ("Magias de 3º Círculo", "Como ler estas magias"). Uma conferência que dá
    /// alarme falso é desligada, e aí não confere mais nada — a mesma razão pela qual
    /// <see cref="ConferenciaDaFicha"/> se cala diante de rótulo distante.</para>
    /// </summary>
    public static IReadOnlyList<string> TitulosSemNomeEmIngles(string conteudo)
    {
        var titulos = Titulo.Matches(conteudo)
            .Select(achado => achado.Groups[2].Value)
            .ToList();

        // O primeiro título é o do arquivo, e não o de uma magia — a menos que o arquivo tenha um
        // título só, e aí ele é a magia que o arquivo descreve.
        var primeiro = titulos.Count > 1 ? 1 : 0;

        return
        [
            .. titulos
                .Skip(primeiro)
                .Where(titulo => !EhOrganizador(titulo))
                .Where(titulo => !ComTraducao.IsMatch(titulo)),
        ];
    }

    /// <summary>
    /// Os nomes de magia escritos num campo da ficha que estão sem o nome em inglês.
    ///
    /// <para>O campo é lista: uma magia por linha, ou separadas por vírgula ou ponto e vírgula —
    /// é como uma ficha de RPG guarda "magias conhecidas". Item sem letra nenhuma (um traço, um
    /// número de espaços) é espaço em branco do formulário, não magia.</para>
    /// </summary>
    public static IReadOnlyList<string> SemNomeEmIngles(string valorDoCampo) =>
    [
        .. valorDoCampo
            .ReplaceLineEndings("\n")
            .Split(['\n', ',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(item => item.Any(char.IsLetter))
            .Where(item => !ComTraducao.IsMatch(item)),
    ];

    /// <summary>
    /// O título organiza a seção em vez de nomear uma magia. São os cabeçalhos que toda base tem
    /// e que não são magia nenhuma — cobrar tradução deles produziria "Magias de 3º Círculo
    /// (Magias de 3º Círculo)", que é pior que o problema.
    /// </summary>
    private static bool EhOrganizador(string texto)
    {
        string[] termos =
        [
            "circulo", "nivel", "level", "escola", "school", "lista", "list", "tabela", "table",
            "como", "sobre", "indice", "index", "resumo", "visao geral", "truques", "cantrips",
            "magias de", "spells of", "spell list", "referencia",
        ];

        var achatado = TextoNormalizado.SemAcento(texto).ToLowerInvariant();

        return termos.Any(termo => achatado.Contains(termo, StringComparison.Ordinal));
    }
}
