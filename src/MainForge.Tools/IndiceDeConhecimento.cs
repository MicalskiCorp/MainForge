using System.Text;
using MainForge.Core;

namespace MainForge.Tools;

/// <summary>Uma linha de índice: o nome do arquivo ou da pasta e o que há dentro dele.</summary>
public sealed record ItemDoIndice(string Nome, string Descricao);

/// <summary>
/// Mantém um <c>index.md</c> em cada nível de <c>Knowledge/</c>, descrevendo o que existe
/// naquele nível.
///
/// <para><b>Por que isso existe.</b> Sem índice, o único jeito de um agente descobrir onde
/// está uma regra é abrir arquivo por arquivo — e cada abertura custa tokens. Com um índice
/// por pasta, ele lê um arquivo pequeno, decide o que interessa e abre só isso. É a diferença
/// entre carregar a base inteira no contexto e carregar três arquivos dela.</para>
///
/// <para><b>Reconstrução em vez de remendo.</b> Toda gravação regenera os índices do sistema
/// a partir do que está em disco, preservando as descrições já registradas. Sai mais caro em
/// I/O (alguns milissegundos numa base de centenas de arquivos) e evita a classe inteira de
/// bugs em que o índice diverge do diretório — arquivo apagado que continua listado, arquivo
/// novo que nunca aparece.</para>
/// </summary>
public static class IndiceDeConhecimento
{
    public const string NomeDoArquivo = "index.md";

    private const int LimiteDaDescricao = 220;
    private const string SecaoDeArquivos = "## Arquivos";
    private const string SecaoDePastas = "## Subpastas";

    /// <summary>
    /// Regenera o <c>index.md</c> de todos os níveis de <c>Knowledge/&lt;sistema&gt;/</c> e o
    /// índice raiz de <c>Knowledge/</c>. Devolve os índices gravados, relativos à raiz do
    /// projeto.
    /// </summary>
    /// <param name="descricoes">
    /// Descrições novas, por caminho relativo a <c>Knowledge/&lt;sistema&gt;/</c> (com barras
    /// normais). Vence o que já estava registrado. Uma chave sem extensão descreve uma pasta;
    /// a chave vazia descreve a raiz do sistema.
    /// </param>
    public static IReadOnlyList<string> Reconstruir(
        CaminhosDoProjeto caminhos,
        string sistema,
        IReadOnlyDictionary<string, string>? descricoes = null)
    {
        var raizDoSistema = CaminhosDoProjeto.ResolverDentroDe(caminhos.Conhecimento, sistema);

        if (!Directory.Exists(raizDoSistema))
        {
            return [];
        }

        var gerados = new List<string>();

        ReconstruirDiretorio(
            caminhos,
            raizDoSistema,
            raizDoSistema,
            sistema,
            Normalizar(descricoes),
            gerados);

        AtualizarIndiceRaiz(caminhos);

        return gerados;
    }

    /// <summary>
    /// Regenera <c>Knowledge/index.md</c>, o índice de mais alto nível: um sistema por linha,
    /// com a descrição que o próprio índice do sistema declara. É por onde o Dungeon Master
    /// começa a navegar.
    /// </summary>
    public static void AtualizarIndiceRaiz(CaminhosDoProjeto caminhos)
    {
        if (!Directory.Exists(caminhos.Conhecimento))
        {
            return;
        }

        var sistemas = Directory
            .EnumerateDirectories(caminhos.Conhecimento)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(diretorio => new ItemDoIndice(
                Path.GetFileName(diretorio),
                LerIndice(Path.Combine(diretorio, NomeDoArquivo)).Descricao))
            .ToList();

        var texto = new StringBuilder()
            .AppendLine("# Indice — Knowledge")
            .AppendLine()
            .AppendLine("Bases de conhecimento geradas pelo Agente Configurador, uma por sistema de RPG.")
            .AppendLine("Abra o `index.md` do sistema desejado para ver a estrutura dele; cada pasta tem o")
            .AppendLine("seu próprio `index.md` descrevendo o que há naquele nível.")
            .AppendLine()
            .AppendLine("## Sistemas")
            .AppendLine()
            .Append(MontarTabela("Sistema", sistemas, item => $"[{item.Nome}]({Link(item.Nome)}/{NomeDoArquivo})"))
            .ToString();

        File.WriteAllText(Path.Combine(caminhos.Conhecimento, NomeDoArquivo), texto);
    }

    /// <summary>
    /// Descrição registrada para um arquivo ou pasta, ou vazio se não houver índice ainda.
    /// Usado pelos testes e por quem precisa saber o que o índice já sabe.
    /// </summary>
    public static string DescricaoRegistrada(CaminhosDoProjeto caminhos, string sistema, string caminhoRelativo)
    {
        var raizDoSistema = CaminhosDoProjeto.ResolverDentroDe(caminhos.Conhecimento, sistema);
        var alvo = CaminhosDoProjeto.ResolverDentroDe(raizDoSistema, caminhoRelativo);
        var pai = Path.GetDirectoryName(alvo)!;
        var indice = LerIndice(Path.Combine(pai, NomeDoArquivo));
        var nome = Path.GetFileName(alvo);

        return indice.Arquivos.TryGetValue(nome, out var deArquivo)
            ? deArquivo
            : indice.Pastas.GetValueOrDefault(nome, "");
    }

    /// <summary>
    /// Reconstrói um diretório e, recursivamente, os que estão dentro dele. Devolve a descrição
    /// da pasta, para o índice do nível de cima poder repeti-la sem inventá-la.
    /// </summary>
    private static string ReconstruirDiretorio(
        CaminhosDoProjeto caminhos,
        string diretorio,
        string raizDoSistema,
        string sistema,
        IReadOnlyDictionary<string, string> descricoes,
        List<string> gerados)
    {
        var caminhoDoIndice = Path.Combine(diretorio, NomeDoArquivo);
        var anterior = LerIndice(caminhoDoIndice);
        var relativoDaPasta = RelativoNormalizado(raizDoSistema, diretorio);

        var descricaoDaPasta = Primeiro(
            descricoes.GetValueOrDefault(relativoDaPasta, ""),
            anterior.Descricao,
            DerivarDaPasta(diretorio));

        var pastas = new List<ItemDoIndice>();

        foreach (var subdiretorio in Directory.EnumerateDirectories(diretorio).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var nome = Path.GetFileName(subdiretorio);

            var descricao = Primeiro(
                ReconstruirDiretorio(caminhos, subdiretorio, raizDoSistema, sistema, descricoes, gerados),
                anterior.Pastas.GetValueOrDefault(nome, ""));

            pastas.Add(new ItemDoIndice(nome, descricao));
        }

        var arquivos = new List<ItemDoIndice>();

        foreach (var arquivo in Directory.EnumerateFiles(diretorio, "*.md").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var nome = Path.GetFileName(arquivo);

            if (nome.Equals(NomeDoArquivo, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var descricao = Primeiro(
                descricoes.GetValueOrDefault(Juntar(relativoDaPasta, nome), ""),
                anterior.Arquivos.GetValueOrDefault(nome, ""),
                DerivarDoArquivo(arquivo));

            arquivos.Add(new ItemDoIndice(nome, descricao));
        }

        File.WriteAllText(
            caminhoDoIndice,
            Montar(sistema, relativoDaPasta, descricaoDaPasta, arquivos, pastas));

        gerados.Add(Path.GetRelativePath(caminhos.Raiz, caminhoDoIndice));

        return descricaoDaPasta;
    }

    private static string Montar(
        string sistema,
        string relativoDaPasta,
        string descricao,
        IReadOnlyList<ItemDoIndice> arquivos,
        IReadOnlyList<ItemDoIndice> pastas)
    {
        var titulo = relativoDaPasta.Length == 0 ? sistema : relativoDaPasta;
        var caminho = relativoDaPasta.Length == 0 ? "" : relativoDaPasta + "/";

        var texto = new StringBuilder()
            .AppendLine($"# Indice — {titulo}")
            .AppendLine()
            .AppendLine($"Caminho: `Knowledge/{sistema}/{caminho}`")
            .AppendLine();

        if (descricao.Length > 0)
        {
            texto.AppendLine(descricao).AppendLine();
        }

        texto.AppendLine(SecaoDeArquivos).AppendLine();

        texto.Append(arquivos.Count == 0
            ? "Nenhum arquivo neste nível." + Environment.NewLine
            : MontarTabela("Arquivo", arquivos, item => $"[{item.Nome}]({Link(item.Nome)})"));

        texto.AppendLine().AppendLine(SecaoDePastas).AppendLine();

        texto.Append(pastas.Count == 0
            ? "Nenhuma subpasta." + Environment.NewLine
            : MontarTabela("Pasta", pastas, item => $"[{item.Nome}]({Link(item.Nome)}/{NomeDoArquivo})"));

        return texto.ToString();
    }

    private static string MontarTabela(
        string cabecalho,
        IReadOnlyList<ItemDoIndice> itens,
        Func<ItemDoIndice, string> ligacao)
    {
        var tabela = new StringBuilder()
            .AppendLine($"| {cabecalho} | Conteudo |")
            .AppendLine("| --- | --- |");

        foreach (var item in itens)
        {
            tabela.AppendLine($"| {ligacao(item)} | {Escapar(item.Descricao)} |");
        }

        return tabela.ToString();
    }

    /// <summary>O que um índice já existente declara, para não se perder na reconstrução.</summary>
    private sealed record ConteudoDoIndice(
        string Descricao,
        IReadOnlyDictionary<string, string> Arquivos,
        IReadOnlyDictionary<string, string> Pastas)
    {
        public static readonly ConteudoDoIndice Vazio = new(
            "",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static ConteudoDoIndice LerIndice(string caminho)
    {
        if (!File.Exists(caminho))
        {
            return ConteudoDoIndice.Vazio;
        }

        var arquivos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pastas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var descricao = new StringBuilder();

        var secao = "";

        foreach (var linha in File.ReadLines(caminho))
        {
            var texto = linha.Trim();

            if (texto.StartsWith("##", StringComparison.Ordinal))
            {
                secao = texto;
                continue;
            }

            if (texto.StartsWith('|'))
            {
                var item = LerLinhaDeTabela(texto);

                if (item is null)
                {
                    continue;
                }

                var destino = secao.StartsWith(SecaoDePastas, StringComparison.Ordinal) ? pastas : arquivos;
                destino[item.Nome] = item.Descricao;
                continue;
            }

            // Antes da primeira seção vem o cabeçalho (título e "Caminho:") e, depois dele, a
            // descrição da pasta escrita pelo agente — é ela que se preserva.
            if (secao.Length == 0 &&
                texto.Length > 0 &&
                !texto.StartsWith('#') &&
                !texto.StartsWith("Caminho:", StringComparison.Ordinal))
            {
                descricao.Append(descricao.Length > 0 ? " " : "").Append(texto);
            }
        }

        return new ConteudoDoIndice(descricao.ToString(), arquivos, pastas);
    }

    /// <summary>
    /// Lê uma linha <c>| [Nome](link) | descrição |</c>, ignorando cabeçalho e separador da
    /// tabela.
    /// </summary>
    private static ItemDoIndice? LerLinhaDeTabela(string linha)
    {
        var celulas = SepararCelulas(linha);

        if (celulas.Count < 2)
        {
            return null;
        }

        var primeira = celulas[0].Trim();
        var abre = primeira.IndexOf('[');
        var fecha = primeira.IndexOf(']');

        if (abre < 0 || fecha <= abre)
        {
            return null; // cabeçalho ("| Arquivo | Conteudo |") ou separador ("| --- | --- |")
        }

        var nome = primeira[(abre + 1)..fecha].Trim().TrimEnd('/');

        return nome.Length == 0
            ? null
            : new ItemDoIndice(nome, celulas[1].Trim());
    }

    /// <summary>
    /// Separa as células de uma linha de tabela respeitando o escape: uma barra vertical
    /// escapada faz parte da descrição, não é fronteira de coluna. Separar sem isso corta a
    /// descrição no primeiro caractere escapado, e o corte só apareceria na reconstrução
    /// seguinte — quando o texto perdido já não estaria em lugar nenhum.
    /// </summary>
    private static List<string> SepararCelulas(string linha)
    {
        var celulas = new List<string>();
        var atual = new StringBuilder();

        for (var indice = 0; indice < linha.Length; indice++)
        {
            var caractere = linha[indice];

            if (caractere == '\\' && indice + 1 < linha.Length && linha[indice + 1] == '|')
            {
                atual.Append('|');
                indice++;
                continue;
            }

            if (caractere == '|')
            {
                celulas.Add(atual.ToString());
                atual.Clear();
                continue;
            }

            atual.Append(caractere);
        }

        celulas.Add(atual.ToString());

        // A linha começa e termina com barra, então a primeira e a última célula são vazias.
        return [.. celulas.Skip(1).SkipLast(1)];
    }

    /// <summary>
    /// Descrição de emergência para um arquivo que nunca foi descrito: o primeiro parágrafo de
    /// texto, ou o título. Serve para indexar bases geradas antes de os índices existirem, sem
    /// gastar um token sequer.
    /// </summary>
    private static string DerivarDoArquivo(string caminho)
    {
        var titulo = "";
        var paragrafo = new StringBuilder();

        try
        {
            foreach (var linha in File.ReadLines(caminho).Take(40))
            {
                var texto = linha.Trim();

                // O parágrafo acaba na primeira linha em branco depois de ele ter começado.
                if (texto.Length == 0)
                {
                    if (paragrafo.Length > 0)
                    {
                        break;
                    }

                    continue;
                }

                if (texto.StartsWith('|') || texto.StartsWith("```", StringComparison.Ordinal))
                {
                    continue;
                }

                if (texto.StartsWith('#'))
                {
                    titulo = titulo.Length == 0 ? texto.TrimStart('#').Trim() : titulo;
                    continue;
                }

                paragrafo
                    .Append(paragrafo.Length > 0 ? " " : "")
                    .Append(texto.TrimStart('>', '-', '*', ' '));

                if (paragrafo.Length >= LimiteDaDescricao)
                {
                    break;
                }
            }
        }
        catch (IOException)
        {
            return "";
        }

        var derivada = paragrafo.Length > 0 ? paragrafo.ToString() : titulo;

        return Encurtar(derivada.Replace("**", ""));
    }

    /// <summary>
    /// Descrição de emergência para uma pasta que ninguém descreveu: o começo do README dela,
    /// se houver. Vale para bases geradas antes dos índices, em que o README era o único lugar
    /// onde alguém dizia para que servia aquele diretório.
    /// </summary>
    private static string DerivarDaPasta(string diretorio)
    {
        var leiame = Path.Combine(diretorio, "README.md");

        return File.Exists(leiame) ? DerivarDoArquivo(leiame) : "";
    }

    private static string Encurtar(string texto) =>
        texto.Length <= LimiteDaDescricao ? texto : string.Concat(texto.AsSpan(0, LimiteDaDescricao).TrimEnd(), "...");

    private static string Primeiro(params string[] candidatos) =>
        candidatos.FirstOrDefault(candidato => candidato.Length > 0) ?? "";

    /// <summary>Uma barra vertical crua partiria a tabela em duas colunas a mais.</summary>
    private static string Escapar(string descricao) =>
        descricao.ReplaceLineEndings(" ").Replace("|", "\\|").Trim();

    /// <summary>
    /// Codifica só o que quebra a sintaxe do link Markdown: espaço e parênteses.
    ///
    /// <para>O resto fica literal de propósito. O agente não abre estes links num navegador —
    /// ele os usa como caminho no <c>Read</c>. Uma codificação completa transformava o sistema
    /// "D&amp;D5e" em <c>D%26D5e</c>, um diretório que não existe em disco, e cada navegação
    /// pelo índice raiz começava com uma leitura recusada.</para>
    /// </summary>
    private static string Link(string nome) => nome
        .Replace("%", "%25")
        .Replace(" ", "%20")
        .Replace("(", "%28")
        .Replace(")", "%29");

    private static string RelativoNormalizado(string raiz, string caminho)
    {
        var relativo = Path.GetRelativePath(raiz, caminho).Replace('\\', '/');

        return relativo == "." ? "" : relativo;
    }

    private static string Juntar(string pasta, string nome) => pasta.Length == 0 ? nome : $"{pasta}/{nome}";

    private static IReadOnlyDictionary<string, string> Normalizar(IReadOnlyDictionary<string, string>? descricoes)
    {
        var normalizadas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (descricoes is null)
        {
            return normalizadas;
        }

        foreach (var (caminho, descricao) in descricoes)
        {
            var chave = caminho.Replace('\\', '/').Trim('/');
            normalizadas[chave] = descricao.Trim();
        }

        return normalizadas;
    }
}
