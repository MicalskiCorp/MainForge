using System.Text.Json;
using System.Text.Json.Serialization;
using MainForge.Core;

namespace MainForge.Tools;

/// <summary>Se um item já foi produzido ou ainda falta.</summary>
public enum EstadoDoItem
{
    Pendente,
    Concluido,
}

/// <summary>Um arquivo que o Configurador se comprometeu a gerar.</summary>
public sealed record ItemDoPlano
{
    /// <summary>Caminho do .md relativo a <c>Knowledge/&lt;sistema&gt;/</c>.</summary>
    public required string Caminho { get; init; }

    /// <summary>Uma linha dizendo o que vai no arquivo — vira a descrição dele no índice.</summary>
    public string Descricao { get; init; } = "";

    public EstadoDoItem Estado { get; init; } = EstadoDoItem.Pendente;

    /// <summary>Livro que originou o item, quando veio de um compêndio/expansão.</summary>
    public string? Livro { get; init; }
}

/// <summary>Um livro do sistema e se o conteúdo dele já entrou na base.</summary>
public sealed record LivroDoSistema
{
    /// <summary>Nome do arquivo dentro de <c>Systems/&lt;sistema&gt;/&lt;fonte&gt;/</c>.</summary>
    public required string Arquivo { get; init; }

    /// <summary>
    /// A fonte a que o livro pertence — <c>base</c> ou o nome da expansão. É o que diz ao
    /// Configurador em qual pasta de <c>Knowledge/</c> o conteúdo dele deve cair.
    /// </summary>
    public string Fonte { get; init; } = FonteDoSistema.IdDaBase;

    public long Tamanho { get; init; }

    /// <summary>Data de modificação em ticks UTC — junto do tamanho, detecta troca do arquivo.</summary>
    public long ModificadoEm { get; init; }

    public EstadoDoItem Estado { get; init; } = EstadoDoItem.Pendente;
}

/// <summary>
/// O que já foi feito e o que falta no processamento de um sistema, gravado em
/// <c>Knowledge/&lt;sistema&gt;/_estado-do-processamento.json</c>.
///
/// <para><b>Por que existe.</b> Ler um livro inteiro é a operação mais cara do aplicativo e a
/// que mais chance tem de ser interrompida (cota da assinatura esgotada, Ctrl+C, máquina
/// desligada). Sem registro do que já saiu, a única saída seria recomeçar do zero e pagar tudo
/// de novo. Com ele, a próxima execução pergunta "onde eu parei?" e continua dali.</para>
///
/// <para><b>Quem escreve.</b> Só o C#, nunca o agente diretamente: o arquivo é atualizado no
/// servidor MCP a cada gravação bem-sucedida. Assim o registro não depende de o modelo lembrar
/// de anotar o próprio progresso.</para>
///
/// <para>O disco continua sendo a verdade final — <see cref="SincronizarComDisco"/> reconcilia
/// o registro com os arquivos que existem de fato, para que apagar um .md na mão baste para
/// mandar regerá-lo.</para>
/// </summary>
public sealed class EstadoDoProcessamento
{
    public const string NomeDoArquivo = "_estado-do-processamento.json";

    private static readonly JsonSerializerOptions Formato = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        // Sem isto, "Aventura&Cia" vira "Aventura&Cia" e todo acento do português vira escape: o arquivo
        // continua válido, mas deixa de ser legível por quem for conferir o que ficou faltando.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private CaminhosDoProjeto? _caminhos;

    public string Sistema { get; set; } = "";

    public DateTimeOffset AtualizadoEm { get; set; }

    public List<LivroDoSistema> Livros { get; set; } = [];

    public List<ItemDoPlano> Plano { get; set; } = [];

    [JsonIgnore]
    public IReadOnlyList<ItemDoPlano> Pendentes =>
        [.. Plano.Where(item => item.Estado == EstadoDoItem.Pendente)];

    [JsonIgnore]
    public IReadOnlyList<ItemDoPlano> Concluidos =>
        [.. Plano.Where(item => item.Estado == EstadoDoItem.Concluido)];

    [JsonIgnore]
    public IReadOnlyList<LivroDoSistema> LivrosPendentes =>
        [.. Livros.Where(livro => livro.Estado == EstadoDoItem.Pendente)];

    /// <summary>Há algo registrado de uma execução anterior a que valha a pena voltar.</summary>
    [JsonIgnore]
    public bool TemHistorico => Plano.Count > 0 || Livros.Any(livro => livro.Estado == EstadoDoItem.Concluido);

    /// <summary>
    /// Os livros ainda não lidos agrupados pela fonte a que pertencem, base primeiro. O
    /// Configurador precisa deste recorte: o destino do conteúdo em <c>Knowledge/</c> depende
    /// da fonte do livro, não do livro em si.
    /// </summary>
    public IReadOnlyList<(FonteDoSistema Fonte, IReadOnlyList<string> Livros)> PendentesPorFonte()
    {
        var porFonte = LivrosPendentes
            .GroupBy(livro => livro.Fonte, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(grupo => new FonteDoSistema(grupo.Key), grupo => (IReadOnlyList<string>)[.. grupo.Select(livro => livro.Arquivo)]);

        return [.. FonteDoSistema.Ordenar(porFonte.Keys).Select(fonte => (fonte, porFonte[fonte]))];
    }

    public static string CaminhoDoArquivo(CaminhosDoProjeto caminhos, string sistema) =>
        Path.Combine(CaminhosDoProjeto.ResolverDentroDe(caminhos.Conhecimento, sistema), NomeDoArquivo);

    /// <summary>
    /// Lê o estado gravado, ou devolve um estado novo quando o sistema nunca foi processado.
    /// Um arquivo corrompido também vira estado novo: perder o registro custa uma reconstrução
    /// a partir do disco, enquanto derrubar o aplicativo custa a execução inteira.
    /// </summary>
    public static EstadoDoProcessamento Carregar(CaminhosDoProjeto caminhos, string sistema)
    {
        var caminho = CaminhoDoArquivo(caminhos, sistema);
        EstadoDoProcessamento? estado = null;

        if (File.Exists(caminho))
        {
            try
            {
                estado = JsonSerializer.Deserialize<EstadoDoProcessamento>(File.ReadAllText(caminho), Formato);
            }
            catch (Exception excecao) when (excecao is JsonException or IOException)
            {
                estado = null;
            }
        }

        estado ??= new EstadoDoProcessamento();
        estado.Sistema = sistema;
        estado._caminhos = caminhos;

        return estado;
    }

    public void Salvar()
    {
        var caminhos = ExigirCaminhos();
        var caminho = CaminhoDoArquivo(caminhos, Sistema);

        AtualizadoEm = DateTimeOffset.Now;
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, JsonSerializer.Serialize(this, Formato));
    }

    /// <summary>Esquece tudo — é o "recomeçar do zero" pedido pelo usuário.</summary>
    public void Limpar()
    {
        Plano.Clear();
        Livros.Clear();

        var caminho = CaminhoDoArquivo(ExigirCaminhos(), Sistema);

        if (File.Exists(caminho))
        {
            File.Delete(caminho);
        }
    }

    /// <summary>
    /// Reconcilia o registro com o que está em disco: item cujo arquivo existe vira concluído,
    /// item cujo arquivo sumiu volta a pendente, arquivo .md fora do plano entra como
    /// concluído (é o caso das bases geradas antes de este registro existir) e os livros de
    /// <c>Systems/</c> são recenseados — um livro novo ou trocado entra como pendente.
    /// </summary>
    public void SincronizarComDisco()
    {
        var caminhos = ExigirCaminhos();
        var raizDoSistema = CaminhosDoProjeto.ResolverDentroDe(caminhos.Conhecimento, Sistema);

        for (var indice = 0; indice < Plano.Count; indice++)
        {
            var item = Plano[indice];
            var existe = File.Exists(Path.Combine(raizDoSistema, item.Caminho.Replace('/', Path.DirectorySeparatorChar)));

            Plano[indice] = item with { Estado = existe ? EstadoDoItem.Concluido : EstadoDoItem.Pendente };
        }

        AdotarArquivosSoltos(raizDoSistema);
        RecensearLivros(caminhos);
    }

    /// <summary>
    /// Acrescenta ao plano o que o agente prometeu gerar, preservando o estado do que já está
    /// lá — replanejar não pode apagar o progresso.
    /// </summary>
    public void RegistrarPlano(IEnumerable<ItemDoPlano> itens)
    {
        foreach (var item in itens)
        {
            var caminho = NormalizarCaminho(item.Caminho);
            var indice = IndiceDoItem(caminho);

            if (indice < 0)
            {
                Plano.Add(item with { Caminho = caminho });
                continue;
            }

            var existente = Plano[indice];

            Plano[indice] = existente with
            {
                Descricao = item.Descricao.Length > 0 ? item.Descricao : existente.Descricao,
                Livro = item.Livro ?? existente.Livro,
            };
        }
    }

    /// <summary>Registra um arquivo gravado, mesmo que ele não estivesse no plano.</summary>
    public void MarcarArquivo(string caminhoRelativo, string? descricao = null, string? livro = null)
    {
        var caminho = NormalizarCaminho(caminhoRelativo);
        var indice = IndiceDoItem(caminho);

        var item = indice < 0
            ? new ItemDoPlano { Caminho = caminho }
            : Plano[indice];

        item = item with
        {
            Estado = EstadoDoItem.Concluido,
            Descricao = string.IsNullOrWhiteSpace(descricao) ? item.Descricao : descricao.Trim(),
            Livro = livro ?? item.Livro,
        };

        if (indice < 0)
        {
            Plano.Add(item);
        }
        else
        {
            Plano[indice] = item;
        }
    }

    /// <summary>Marca livros como já incorporados à base. Sem argumento, marca todos.</summary>
    public void MarcarLivrosConcluidos(IEnumerable<string>? arquivos = null)
    {
        var alvos = arquivos is null
            ? null
            : new HashSet<string>(arquivos.Select(arquivo => Path.GetFileName(arquivo) ?? arquivo), StringComparer.OrdinalIgnoreCase);

        for (var indice = 0; indice < Livros.Count; indice++)
        {
            if (alvos is null || alvos.Contains(Livros[indice].Arquivo))
            {
                Livros[indice] = Livros[indice] with { Estado = EstadoDoItem.Concluido };
            }
        }
    }

    /// <summary>Resumo de uma linha para a interface: "12 de 40 arquivos, 1 livro pendente".</summary>
    public string Resumo()
    {
        var total = Plano.Count;
        var prontos = Concluidos.Count;
        var livros = LivrosPendentes.Count;

        var arquivos = total == 0
            ? $"{prontos} arquivo(s) gerado(s)"
            : $"{prontos} de {total} arquivo(s) planejado(s)";

        return livros == 0 ? arquivos : $"{arquivos}, {livros} livro(s) ainda não lido(s)";
    }

    /// <summary>
    /// O mesmo estado escrito para o agente ler. Vai no prompt de retomada e é o que a
    /// ferramenta <c>consultar_progresso</c> devolve.
    /// </summary>
    public string DescreverParaOAgente(int limiteDeItens = 80)
    {
        var texto = new System.Text.StringBuilder();

        texto.AppendLine($"Sistema: {Sistema}");
        texto.AppendLine($"Progresso: {Resumo()}.");

        texto.AppendLine();
        texto.AppendLine("Livros:");

        if (Livros.Count == 0)
        {
            texto.AppendLine("- (nenhum registrado)");
        }

        foreach (var livro in Livros)
        {
            var situacao = livro.Estado == EstadoDoItem.Concluido ? "ja lido" : "PENDENTE";
            texto.AppendLine($"- [{livro.Fonte}] {livro.Arquivo} — {situacao}");
        }

        texto.AppendLine();
        texto.AppendLine($"Arquivos ja gerados ({Concluidos.Count}):");

        if (Concluidos.Count == 0)
        {
            texto.AppendLine("- (nenhum)");
        }

        foreach (var item in Concluidos.Take(limiteDeItens))
        {
            texto.AppendLine($"- {item.Caminho}");
        }

        if (Concluidos.Count > limiteDeItens)
        {
            texto.AppendLine($"- ... e mais {Concluidos.Count - limiteDeItens}. Veja os index.md da base.");
        }

        texto.AppendLine();
        texto.AppendLine($"Arquivos planejados que ainda faltam ({Pendentes.Count}):");

        if (Pendentes.Count == 0)
        {
            texto.AppendLine("- (nenhum)");
        }

        foreach (var item in Pendentes.Take(limiteDeItens))
        {
            texto.AppendLine(item.Descricao.Length > 0
                ? $"- {item.Caminho} — {item.Descricao}"
                : $"- {item.Caminho}");
        }

        if (Pendentes.Count > limiteDeItens)
        {
            texto.AppendLine($"- ... e mais {Pendentes.Count - limiteDeItens}.");
        }

        return texto.ToString();
    }

    /// <summary>
    /// Arquivos .md que existem em disco mas ninguém planejou. Entram como concluídos com a
    /// descrição que o índice já conhece — é o que permite retomar uma base gerada por uma
    /// versão anterior do aplicativo.
    /// </summary>
    private void AdotarArquivosSoltos(string raizDoSistema)
    {
        if (!Directory.Exists(raizDoSistema))
        {
            return;
        }

        foreach (var arquivo in Directory.EnumerateFiles(raizDoSistema, "*.md", SearchOption.AllDirectories))
        {
            var nome = Path.GetFileName(arquivo);

            if (nome.Equals(IndiceDeConhecimento.NomeDoArquivo, StringComparison.OrdinalIgnoreCase))
            {
                continue; // índice é derivado, não é conteúdo planejado
            }

            var relativo = NormalizarCaminho(Path.GetRelativePath(raizDoSistema, arquivo));

            if (IndiceDoItem(relativo) >= 0)
            {
                continue;
            }

            Plano.Add(new ItemDoPlano
            {
                Caminho = relativo,
                Estado = EstadoDoItem.Concluido,
                // O índice já sabe o que há em cada arquivo; repetir aqui evita um registro de
                // caminhos mudos, em que "o que já existe" não diz nada a quem for continuar.
                Descricao = IndiceDeConhecimento.DescricaoRegistrada(ExigirCaminhos(), Sistema, relativo),
            });
        }
    }

    private void RecensearLivros(CaminhosDoProjeto caminhos)
    {
        var diretorio = Path.Combine(caminhos.Sistemas, Sistema);

        if (!Directory.Exists(diretorio))
        {
            return;
        }

        foreach (var caminho in Directory.EnumerateFiles(diretorio, "*.pdf", SearchOption.AllDirectories))
        {
            var informacao = new FileInfo(caminho);
            var nome = informacao.Name;
            var indice = Livros.FindIndex(livro => livro.Arquivo.Equals(nome, StringComparison.OrdinalIgnoreCase));

            var atual = new LivroDoSistema
            {
                Arquivo = nome,
                Fonte = FonteDoLivro(diretorio, caminho),
                Tamanho = informacao.Length,
                ModificadoEm = informacao.LastWriteTimeUtc.Ticks,
            };

            if (indice < 0)
            {
                Livros.Add(atual);
                continue;
            }

            var registrado = Livros[indice];

            // Mesmo nome, conteúdo diferente: o livro foi trocado e precisa ser lido de novo.
            // A fonte é recensada sempre — mover o livro de pasta não deve obrigar a relê-lo.
            Livros[indice] = registrado.Tamanho == atual.Tamanho && registrado.ModificadoEm == atual.ModificadoEm
                ? registrado with { Fonte = atual.Fonte }
                : atual;
        }
    }

    /// <summary>
    /// A fonte é a primeira pasta abaixo de <c>Systems/&lt;sistema&gt;/</c>. Um PDF solto na
    /// raiz é do layout antigo e conta como jogo base — é o que a migração vai formalizar.
    /// </summary>
    private static string FonteDoLivro(string diretorioDoSistema, string caminhoDoLivro)
    {
        var relativo = Path.GetRelativePath(diretorioDoSistema, caminhoDoLivro).Replace('\\', '/');
        var barra = relativo.IndexOf('/');

        return barra <= 0 ? FonteDoSistema.IdDaBase : relativo[..barra];
    }

    private int IndiceDoItem(string caminho) =>
        Plano.FindIndex(item => NormalizarCaminho(item.Caminho).Equals(caminho, StringComparison.OrdinalIgnoreCase));

    private static string NormalizarCaminho(string caminho) => caminho.Replace('\\', '/').Trim('/');

    private CaminhosDoProjeto ExigirCaminhos() =>
        _caminhos ?? throw new InvalidOperationException(
            $"Estado carregado sem {nameof(CaminhosDoProjeto)} — use {nameof(Carregar)}.");
}
