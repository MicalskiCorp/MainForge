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
    /// <summary>Caminho do .md relativo a <c>Sistemas/&lt;sistema&gt;/</c>.</summary>
    public required string Caminho { get; init; }

    /// <summary>Uma linha dizendo o que vai no arquivo — vira a descrição dele no índice.</summary>
    public string Descricao { get; init; } = "";

    public EstadoDoItem Estado { get; init; } = EstadoDoItem.Pendente;

    /// <summary>Livro que originou o item, quando veio de um compêndio/expansão.</summary>
    public string? Livro { get; init; }
}

/// <summary>
/// O que identifica um livro dentro de um sistema: a fonte a que ele pertence mais o nome do
/// arquivo.
///
/// <para><b>Por que a fonte faz parte da identidade.</b> O registro localizava livro só pelo nome
/// do arquivo, e PDFs baixados se chamam <c>Livro-do-Jogador.pdf</c> ou <c>Core-Rulebook.pdf</c> —
/// nomes que se repetem entre o jogo base e um compêndio. Com a chave só no nome, importar a
/// expansão fundia os dois num registro único: o livro da base passava a constar como sendo da
/// expansão e a marca de "já lido" vazava de um para o outro.</para>
/// </summary>
public readonly record struct ChaveDeLivro(string Fonte, string Arquivo)
{
    public bool Equals(ChaveDeLivro outra) =>
        string.Equals(Fonte, outra.Fonte, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Arquivo, outra.Arquivo, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(
        Fonte.ToUpperInvariant(),
        Arquivo.ToUpperInvariant());

    public override string ToString() => $"{Fonte}/{Arquivo}";
}

/// <summary>Um livro do sistema e se o conteúdo dele já entrou na base.</summary>
public sealed record LivroDoSistema
{
    /// <summary>Nome do arquivo dentro de <c>Input/&lt;sistema&gt;/&lt;fonte&gt;/</c>.</summary>
    public required string Arquivo { get; init; }

    /// <summary>
    /// A fonte a que o livro pertence — <c>base</c> ou o nome da expansão. É o que diz ao
    /// Configurador em qual pasta de <c>Sistemas/</c> o conteúdo dele deve cair.
    /// </summary>
    public string Fonte { get; init; } = FonteDoSistema.IdDaBase;

    public long Tamanho { get; init; }

    /// <summary>
    /// Data de modificação em ticks UTC. Serve para <em>evitar</em> trabalho: data igual dispensa
    /// abrir o arquivo. Sozinha ela não decide nada — veja <see cref="Hash"/>.
    /// </summary>
    public long ModificadoEm { get; init; }

    /// <summary>
    /// SHA-256 do arquivo, em hexadecimal. É o que diz se o livro é o mesmo.
    ///
    /// <para><b>Por que não basta tamanho e data.</b> Copiar a pasta, restaurar um backup ou
    /// deixar uma sincronização de nuvem passar por cima muda a data de modificação sem trocar
    /// uma vírgula do livro. Enquanto a data mandava, isso marcava o livro como "trocado" e a
    /// próxima execução oferecia relê-lo inteiro — a leitura mais cara do aplicativo, refeita
    /// para chegar exatamente ao mesmo conteúdo.</para>
    ///
    /// <para>Vazio nos registros gravados antes deste campo existir.</para>
    /// </summary>
    public string Hash { get; init; } = "";

    public EstadoDoItem Estado { get; init; } = EstadoDoItem.Pendente;

    /// <summary>Como este livro é localizado no registro.</summary>
    [JsonIgnore]
    public ChaveDeLivro Chave => new(Fonte, Arquivo);
}

/// <summary>
/// O que já foi feito e o que falta no processamento de um sistema, gravado em
/// <c>Sistemas/&lt;sistema&gt;/_estado-do-processamento.json</c>.
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

    /// <summary>
    /// O que este sistema já custou de cota, somando todas as execuções do Configurador — as que
    /// terminaram e as que foram interrompidas.
    ///
    /// <para>Acumular em vez de guardar só a última execução é o que responde à pergunta que o
    /// usuário faz de verdade: "quanto custou mapear este sistema?". Uma base gerada em cinco
    /// retomadas custou a soma das cinco, não a da última.</para>
    /// </summary>
    public ConsumoDeTokens Consumo { get; set; } = ConsumoDeTokens.Zero;

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
    /// Não sobrou nada a fazer: todo arquivo planejado existe em disco e todo livro já foi
    /// incorporado à base.
    ///
    /// <para>Serve para <em>impedir</em> um processamento, não para oferecer um: mandar o agente
    /// trabalhar nessa situação faz ele reler livro e regravar arquivo pronto, gastando a cota da
    /// assinatura para chegar ao mesmo lugar. Quem quiser refazer assim mesmo tem o "recomeçar do
    /// zero", que é uma escolha explícita.</para>
    /// </summary>
    [JsonIgnore]
    public bool EstaCompleto => TemHistorico && Pendentes.Count == 0 && LivrosPendentes.Count == 0;

    /// <summary>
    /// Os livros ainda não lidos agrupados pela fonte a que pertencem, base primeiro. O
    /// Configurador precisa deste recorte: o destino do conteúdo em <c>Sistemas/</c> depende
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

    /// <summary>
    /// Soma o que uma execução do Configurador custou ao total do sistema.
    ///
    /// <para>Chamada mesmo quando a execução falhou ou foi interrompida: a cota que ela queimou
    /// não volta, e um relatório que só conta os sucessos mentiria justamente na situação em que
    /// o usuário mais quer saber para onde foi o gasto.</para>
    /// </summary>
    public void RegistrarConsumo(ConsumoDeTokens consumo)
    {
        if (!consumo.Vazio)
        {
            Consumo += consumo;
        }
    }

    /// <summary>Esquece tudo — é o "recomeçar do zero" pedido pelo usuário.</summary>
    public void Limpar()
    {
        Plano.Clear();
        Livros.Clear();

        // O consumo não é zerado: o que já foi gasto foi gasto, e recomeçar do zero é
        // exatamente a hora em que o total acumulado importa.

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
    /// <c>Input/</c> são recenseados — um livro novo ou trocado entra como pendente.
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
    /// Fontes que têm livro marcado como incorporado mas nenhum conhecimento gerado — o conteúdo
    /// que aquele livro teria produzido não existe em lugar nenhum.
    ///
    /// <para><b>Por que isso acontece.</b> O Configurador não conseguiu abrir o PDF de um compêndio
    /// recém-adicionado (a leitura de PDF do Claude Code depende do <c>pdftoppm</c>, que pode não
    /// estar instalado), terminou o turno sem gerar arquivo algum e, como o plano antigo já estava
    /// completo, o livro foi registrado como lido. O sistema fica "pronto" com uma expansão que
    /// ninguém leu.</para>
    ///
    /// <para><b>Por que isto só aponta, em vez de corrigir.</b> Mover um livro de fonte produz a
    /// mesma imagem — fonte nova sem conhecimento — e nesse caso o certo é justamente <em>não</em>
    /// reler, porque o conteúdo já está na base, na pasta da fonte antiga. Só quem sabe qual dos
    /// dois casos é o seu é o usuário; ao aplicativo cabe mostrar a inconsistência em vez de
    /// escondê-la ou de decidir por ele.</para>
    /// </summary>
    public IReadOnlyList<string> FontesSemConhecimento(CaminhosDoProjeto caminhos)
    {
        var raizDoSistema = CaminhosDoProjeto.ResolverDentroDe(caminhos.Conhecimento, Sistema);

        return
        [
            .. Livros
                .Where(livro => livro.Estado == EstadoDoItem.Concluido)
                .Select(livro => livro.Fonte)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(fonte => !TemConhecimentoDaFonte(raizDoSistema, fonte))
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>Devolve os livros daquelas fontes ao estado de não lidos, para serem relidos.</summary>
    public IReadOnlyList<string> MarcarFontesComoPendentes(IReadOnlyList<string> fontes)
    {
        var afetados = new List<string>();

        for (var indice = 0; indice < Livros.Count; indice++)
        {
            if (!fontes.Contains(Livros[indice].Fonte, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            Livros[indice] = Livros[indice] with { Estado = EstadoDoItem.Pendente };
            afetados.Add(Livros[indice].Arquivo);
        }

        return afetados;
    }

    /// <summary>
    /// Numa base do layout antigo o conhecimento fica solto na raiz do sistema, sem pasta de
    /// fonte — e ele conta como conhecimento da base, senão a migração pendente pareceria uma
    /// base vazia.
    /// </summary>
    private static bool TemConhecimentoDaFonte(string raizDoSistema, string fonte)
    {
        if (!Directory.Exists(raizDoSistema))
        {
            return false;
        }

        var diretorio = Path.Combine(raizDoSistema, fonte);

        if (!Directory.Exists(diretorio))
        {
            diretorio = Directory.Exists(Path.Combine(raizDoSistema, FonteDoSistema.IdDaBase))
                ? diretorio
                : raizDoSistema; // layout antigo: tudo solto na raiz
        }

        return Directory.Exists(diretorio) &&
               Directory
                   .EnumerateFiles(diretorio, "*.md", SearchOption.AllDirectories)
                   .Any(arquivo => EhConteudo(Path.GetFileName(arquivo)));
    }

    /// <summary>O índice é derivado e os arquivos da ficha valem para o sistema inteiro.</summary>
    private static bool EhConteudo(string nome) =>
        !nome.Equals(IndiceDeConhecimento.NomeDoArquivo, StringComparison.OrdinalIgnoreCase) &&
        !SistemaRpg.ArquivosDaFicha.Contains(nome, StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// Marca livros como já incorporados à base. Sem argumento, marca todos.
    ///
    /// <para>Os alvos são identificados por fonte <em>e</em> nome, e não só pelo nome: dois
    /// livros homônimos em fontes diferentes — <c>Livro-do-Jogador.pdf</c> no jogo base e na
    /// expansão, que é como PDFs baixados costumam se chamar — são dois livros, e marcar um deles
    /// não pode marcar o outro.</para>
    /// </summary>
    public void MarcarLivrosConcluidos(IEnumerable<ChaveDeLivro>? alvos = null)
    {
        var procurados = alvos is null ? null : new HashSet<ChaveDeLivro>(alvos);

        for (var indice = 0; indice < Livros.Count; indice++)
        {
            if (procurados is null || procurados.Contains(Livros[indice].Chave))
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
        var diretorio = Path.Combine(caminhos.Entrada, Sistema);

        if (!Directory.Exists(diretorio))
        {
            return;
        }

        foreach (var caminho in Directory.EnumerateFiles(diretorio, "*.pdf", SearchOption.AllDirectories))
        {
            var informacao = new FileInfo(caminho);

            var atual = new LivroDoSistema
            {
                Arquivo = informacao.Name,
                Fonte = FonteDoLivro(diretorio, caminho),
                Tamanho = informacao.Length,
                ModificadoEm = informacao.LastWriteTimeUtc.Ticks,
            };

            var indice = IndiceDoLivro(atual.Chave, atual.Tamanho);

            if (indice < 0)
            {
                Livros.Add(atual with { Hash = HashDoArquivo(caminho) });
                continue;
            }

            var registrado = Livros[indice];

            // Nada mudou nem na data nem no tamanho: não há por que abrir um arquivo de 15 MB.
            // A fonte é recensada sempre — mover o livro de pasta não deve obrigar a relê-lo.
            if (registrado.ModificadoEm == atual.ModificadoEm && registrado.Tamanho == atual.Tamanho)
            {
                Livros[indice] = registrado with { Fonte = atual.Fonte };
                continue;
            }

            // Tamanho diferente é outro arquivo, e nem vale o hash. Igual, com data diferente, é
            // quase sempre uma cópia ou uma sincronização de nuvem: aí o conteúdo decide.
            var hash = "";
            var mesmoLivro = registrado.Tamanho == atual.Tamanho &&
                             ContinuaSendoOMesmoLivro(registrado, caminho, out hash);

            Livros[indice] = mesmoLivro
                ? registrado with { Fonte = atual.Fonte, ModificadoEm = atual.ModificadoEm, Hash = hash }
                : atual with { Hash = HashDoArquivo(caminho) };
        }
    }

    /// <summary>
    /// Onde este livro está no registro.
    ///
    /// <para>Procura primeiro pela chave inteira (fonte e nome). Não achando, aceita um registro
    /// de <em>outra</em> fonte com o mesmo nome e o mesmo tamanho: é o livro que o usuário moveu
    /// de pasta, e reaproveitar o registro dele é o que evita reler um livro só porque ele mudou
    /// de fonte — a leitura mais cara do aplicativo, refeita para chegar ao mesmo conteúdo.</para>
    ///
    /// <para>O tamanho é o que separa esse caso do outro: dois livros homônimos de fontes
    /// diferentes que <em>não</em> são o mesmo arquivo têm tamanhos diferentes, e aí cada um
    /// ganha o seu registro. Sem essa condição, importar o compêndio fundia os dois.</para>
    /// </summary>
    private int IndiceDoLivro(ChaveDeLivro chave, long tamanho)
    {
        var exato = Livros.FindIndex(livro => livro.Chave.Equals(chave));

        if (exato >= 0)
        {
            return exato;
        }

        return Livros.FindIndex(livro =>
            livro.Arquivo.Equals(chave.Arquivo, StringComparison.OrdinalIgnoreCase) &&
            livro.Tamanho == tamanho &&
            !ExisteEmDisco(livro.Chave));
    }

    /// <summary>
    /// O livro daquele registro ainda está no lugar onde ele foi registrado?
    ///
    /// <para>Se estiver, o registro é dele e não pode ser reaproveitado por um homônimo de outra
    /// fonte: os dois arquivos existem ao mesmo tempo, e são dois livros. Se não estiver, aquele
    /// registro ficou órfão — é o livro que mudou de pasta.</para>
    /// </summary>
    private bool ExisteEmDisco(ChaveDeLivro chave)
    {
        if (_caminhos is null)
        {
            return false;
        }

        return File.Exists(Path.Combine(_caminhos.Entrada, Sistema, chave.Fonte, chave.Arquivo));
    }

    /// <summary>
    /// O arquivo em disco ainda é o livro que foi lido?
    ///
    /// <para>Com hash registrado, a resposta é exata. Sem ele — registro gravado por uma versão
    /// anterior do aplicativo —, o tamanho idêntico já basta: um livro trocado por outra edição
    /// não tem o mesmo tamanho em bytes, e o custo de errar para o lado do "mudou" é reler o
    /// livro inteiro, que é a operação mais cara que existe aqui.</para>
    /// </summary>
    private static bool ContinuaSendoOMesmoLivro(LivroDoSistema registrado, string caminho, out string hash)
    {
        hash = HashDoArquivo(caminho);

        return registrado.Hash.Length == 0 || registrado.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// SHA-256 do arquivo. Um livro que não abre não derruba o recenseamento: sem hash, ele cai
    /// na regra do tamanho.
    /// </summary>
    private static string HashDoArquivo(string caminho)
    {
        try
        {
            using var fluxo = File.OpenRead(caminho);

            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fluxo));
        }
        catch (IOException)
        {
            return "";
        }
        catch (UnauthorizedAccessException)
        {
            return "";
        }
    }

    /// <summary>
    /// A fonte é a primeira pasta abaixo de <c>Input/&lt;sistema&gt;/</c>. Um PDF solto na
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
