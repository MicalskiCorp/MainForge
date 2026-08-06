using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using MainForge.Core;

namespace MainForge.Tools;

/// <summary>O que um pacote declara sobre si mesmo, lido antes de qualquer arquivo ser extraído.</summary>
public sealed class ManifestoDoPacote
{
    /// <summary>Versão do formato. Um pacote de formato desconhecido é recusado em vez de adivinhado.</summary>
    public int Formato { get; set; } = PacoteDeSistema.FormatoAtual;

    public string Sistema { get; set; } = "";

    public DateTimeOffset GeradoEm { get; set; }

    /// <summary>As fontes que vêm dentro: <c>base</c> e as expansões já processadas.</summary>
    public List<string> Fontes { get; set; } = [];

    public int ArquivosDeConhecimento { get; set; }

    public List<string> Fichas { get; set; } = [];
}

/// <summary>Resultado de uma exportação, para a interface poder dizer o que saiu.</summary>
public sealed record ResultadoDaExportacao(
    string Arquivo,
    ManifestoDoPacote Manifesto,
    long Bytes);

/// <summary>Resultado de uma importação, para a interface poder dizer o que entrou.</summary>
public sealed record ResultadoDaImportacaoDePacote(
    SistemaRpg Sistema,
    ManifestoDoPacote Manifesto,
    int ArquivosDeConhecimento,
    IReadOnlyList<string> Fichas);

/// <summary>
/// Empacota um sistema já mapeado — a base de conhecimento em Markdown mais a ficha em PDF —
/// num arquivo único, e o traz de volta em outra instalação.
///
/// <para><b>Por que só isso vai dentro.</b> Mapear um sistema é a operação cara do aplicativo:
/// gasta a cota da assinatura lendo os livros inteiros. O resultado desse gasto é o que vale a
/// pena carregar. Os PDFs dos livros ficam de fora de propósito — são dezenas de MB e, sendo
/// obra comercial, não são do usuário para redistribuir; a base destilada e a ficha em branco
/// são o suficiente para o Dungeon Master trabalhar, que é o objetivo.</para>
///
/// <para>Consequência de não levar os livros: o sistema importado chega <b>completo e sem
/// pendência</b> — não há livro por ler. Quem quiser reprocessá-lo depois precisa trazer os
/// PDFs por conta própria, e é o que a interface avisa.</para>
///
/// <para><b>O manifesto é lido antes de extrair qualquer coisa</b>, e todo caminho de dentro do
/// pacote passa por <see cref="CaminhosDoProjeto.ResolverDentroDe"/>: um <c>.zip</c> vem de
/// fora e pode carregar caminhos como <c>../../</c> — o clássico "zip slip". Aqui é a mesma
/// regra que vale para caminho vindo do modelo: quem resolve é o C#, e o que escapa é
/// recusado.</para>
/// </summary>
public static class PacoteDeSistema
{
    public const int FormatoAtual = 1;

    public const string NomeDoManifesto = "pacote.json";

    public const string Extensao = ".mainforge.zip";

    private const string PastaDoConhecimento = "conhecimento/";
    private const string PastaDaFicha = "ficha/";

    private static readonly JsonSerializerOptions Formato = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Nome de arquivo sugerido para o pacote de um sistema.</summary>
    public static string NomeSugerido(string sistema) => $"{sistema}{Extensao}";

    /// <summary>
    /// Escreve o pacote do sistema em <paramref name="caminhoDoPacote"/>.
    ///
    /// <para>Recusa antes de escrever um byte quando o sistema não está pronto para viajar: sem
    /// os dois arquivos da ficha o destinatário receberia uma base que não consegue preencher
    /// PDF nenhum, e descobrir isso do outro lado é tarde demais.</para>
    /// </summary>
    public static ResultadoDaExportacao Exportar(
        CaminhosDoProjeto caminhos,
        string nomeDoSistema,
        string caminhoDoPacote)
    {
        var sistema = new SistemaRpg(NomeDePasta.Validar(nomeDoSistema, "sistema", nameof(nomeDoSistema)));
        var diretorioConhecimento = sistema.DiretorioConhecimento(caminhos);

        if (!sistema.TemConhecimento(caminhos))
        {
            throw new InvalidOperationException(
                $"O sistema '{sistema.Id}' não tem base de conhecimento em Sistemas/ — processe-o antes de exportar.");
        }

        var faltando = SistemaRpg.ArquivosDaFicha
            .Where(nome => !File.Exists(Path.Combine(diretorioConhecimento, nome)))
            .ToList();

        if (faltando.Count > 0)
        {
            throw new InvalidOperationException(
                $"O mapeamento da ficha de '{sistema.Id}' está incompleto: falta {string.Join(" e ", faltando)}. " +
                "Reprocesse o sistema antes de exportá-lo — sem esses arquivos o destinatário não consegue gerar ficha.");
        }

        var fichas = PdfsDaFicha(sistema.DiretorioModelo(caminhos));

        if (fichas.Count == 0)
        {
            throw new InvalidOperationException(
                $"Não há ficha em PDF em Templates/{sistema.Id}/ — é ela que o pacote leva junto com a base.");
        }

        var conhecimento = ArquivosDeConhecimento(diretorioConhecimento);

        var manifesto = new ManifestoDoPacote
        {
            Formato = FormatoAtual,
            Sistema = sistema.Id,
            GeradoEm = DateTimeOffset.Now,
            Fontes = [.. sistema.DescobrirFontesComConhecimento(caminhos).Select(fonte => fonte.Id)],
            ArquivosDeConhecimento = conhecimento.Count,
            Fichas = [.. fichas.Select(Path.GetFileName).OfType<string>()],
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(caminhoDoPacote))!);

        using (var arquivo = new FileStream(caminhoDoPacote, FileMode.Create, FileAccess.Write))
        using (var pacote = new ZipArchive(arquivo, ZipArchiveMode.Create))
        {
            Gravar(pacote, NomeDoManifesto, JsonSerializer.Serialize(manifesto, Formato));

            foreach (var caminho in conhecimento)
            {
                var relativo = Relativo(diretorioConhecimento, caminho);
                pacote.CreateEntryFromFile(caminho, PastaDoConhecimento + relativo);
            }

            foreach (var ficha in fichas)
            {
                pacote.CreateEntryFromFile(ficha, PastaDaFicha + Path.GetFileName(ficha));
            }
        }

        return new ResultadoDaExportacao(caminhoDoPacote, manifesto, new FileInfo(caminhoDoPacote).Length);
    }

    /// <summary>
    /// Lê só o manifesto, sem extrair nada. É o que permite à interface mostrar o que há dentro
    /// e perguntar antes de mexer no projeto.
    /// </summary>
    public static ManifestoDoPacote LerManifesto(string caminhoDoPacote)
    {
        if (!File.Exists(caminhoDoPacote))
        {
            throw new FileNotFoundException($"Pacote não encontrado: '{caminhoDoPacote}'.", caminhoDoPacote);
        }

        using var pacote = AbrirParaLeitura(caminhoDoPacote);

        var entrada = pacote.GetEntry(NomeDoManifesto)
            ?? throw new InvalidOperationException(
                $"'{Path.GetFileName(caminhoDoPacote)}' não tem {NomeDoManifesto} — não é um pacote do MainForge.");

        using var fluxo = entrada.Open();
        using var leitor = new StreamReader(fluxo);

        var manifesto = JsonSerializer.Deserialize<ManifestoDoPacote>(leitor.ReadToEnd(), Formato)
            ?? throw new InvalidOperationException($"O {NomeDoManifesto} do pacote está vazio ou ilegível.");

        if (manifesto.Formato > FormatoAtual)
        {
            throw new InvalidOperationException(
                $"O pacote foi gerado por uma versão mais nova do MainForge (formato {manifesto.Formato}, " +
                $"esta versão entende até o {FormatoAtual}). Atualize o aplicativo.");
        }

        return manifesto.Sistema.Length > 0
            ? manifesto
            : throw new InvalidOperationException($"O {NomeDoManifesto} do pacote não diz de que sistema ele é.");
    }

    /// <summary>
    /// Traz o pacote para dentro do projeto, criando <c>Sistemas/&lt;Sistema&gt;/</c> e
    /// <c>Templates/&lt;Sistema&gt;/</c>, e deixando o índice e o registro de progresso coerentes
    /// com o que entrou.
    /// </summary>
    /// <param name="nomeAlternativo">
    /// Outro nome para o sistema, quando o do pacote já existe aqui e o usuário não quer
    /// substituí-lo.
    /// </param>
    /// <param name="substituir">
    /// Autoriza sobrescrever um sistema de mesmo nome. Sem isto a importação é recusada: a base
    /// existente pode ter custado horas de cota, e apagá-la sem perguntar é o pior erro possível.
    /// </param>
    public static ResultadoDaImportacaoDePacote Importar(
        CaminhosDoProjeto caminhos,
        string caminhoDoPacote,
        string? nomeAlternativo = null,
        bool substituir = false)
    {
        var manifesto = LerManifesto(caminhoDoPacote);

        var nome = NomeDePasta.Validar(
            string.IsNullOrWhiteSpace(nomeAlternativo) ? manifesto.Sistema : nomeAlternativo,
            "sistema",
            nameof(nomeAlternativo));

        var sistema = new SistemaRpg(nome);
        var destinoConhecimento = sistema.DiretorioConhecimento(caminhos);
        var destinoFicha = sistema.DiretorioModelo(caminhos);

        if (!substituir && sistema.TemConhecimento(caminhos))
        {
            throw new InvalidOperationException(
                $"Já existe uma base de conhecimento em Sistemas/{nome}/. Escolha outro nome para o sistema " +
                "importado, ou confirme a substituição.");
        }

        Directory.CreateDirectory(destinoConhecimento);
        Directory.CreateDirectory(destinoFicha);

        var conhecimento = 0;
        var fichas = new List<string>();

        using (var pacote = AbrirParaLeitura(caminhoDoPacote))
        {
            foreach (var entrada in pacote.Entries)
            {
                if (entrada.FullName.EndsWith('/') || entrada.Name.Length == 0)
                {
                    continue; // diretório
                }

                var destino = DestinoDaEntrada(entrada.FullName, destinoConhecimento, destinoFicha);

                if (destino is null)
                {
                    continue; // o manifesto e qualquer coisa fora das duas pastas conhecidas
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
                entrada.ExtractToFile(destino, overwrite: true);

                if (entrada.FullName.StartsWith(PastaDaFicha, StringComparison.OrdinalIgnoreCase))
                {
                    fichas.Add(Path.GetFileName(destino));
                }
                else
                {
                    conhecimento++;
                }
            }
        }

        if (conhecimento == 0)
        {
            throw new InvalidOperationException(
                $"O pacote não trouxe nenhum arquivo de conhecimento em '{PastaDoConhecimento}'.");
        }

        // Índice e registro saem do que está em disco: o pacote não carrega nenhum dos dois. O
        // registro nasce completo de propósito — sem os PDFs, não há livro pendente para ler.
        IndiceDeConhecimento.Reconstruir(caminhos, nome);

        var estado = EstadoDoProcessamento.Carregar(caminhos, nome);
        estado.SincronizarComDisco();
        estado.Salvar();

        return new ResultadoDaImportacaoDePacote(sistema, manifesto, conhecimento, fichas);
    }

    /// <summary>
    /// Para onde vai uma entrada do pacote, ou <c>null</c> se ela não pertence a nenhuma das
    /// duas pastas conhecidas.
    ///
    /// <para>É aqui que o "zip slip" morre: o caminho de dentro do arquivo é tratado como
    /// caminho vindo de fora, e <see cref="CaminhosDoProjeto.ResolverDentroDe"/> rejeita o que
    /// escapar da pasta de destino.</para>
    /// </summary>
    private static string? DestinoDaEntrada(string entrada, string destinoConhecimento, string destinoFicha)
    {
        var normalizada = entrada.Replace('\\', '/');

        if (normalizada.StartsWith(PastaDoConhecimento, StringComparison.OrdinalIgnoreCase))
        {
            var relativo = normalizada[PastaDoConhecimento.Length..];

            return relativo.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                ? CaminhosDoProjeto.ResolverDentroDe(destinoConhecimento, relativo)
                : null;
        }

        if (normalizada.StartsWith(PastaDaFicha, StringComparison.OrdinalIgnoreCase))
        {
            var relativo = normalizada[PastaDaFicha.Length..];

            return relativo.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                ? CaminhosDoProjeto.ResolverDentroDe(destinoFicha, relativo)
                : null;
        }

        return null;
    }

    private static ZipArchive AbrirParaLeitura(string caminho)
    {
        try
        {
            return ZipFile.OpenRead(caminho);
        }
        catch (InvalidDataException excecao)
        {
            throw new InvalidOperationException(
                $"'{Path.GetFileName(caminho)}' não é um arquivo .zip válido: {excecao.Message}", excecao);
        }
    }

    private static void Gravar(ZipArchive pacote, string nome, string conteudo)
    {
        using var fluxo = pacote.CreateEntry(nome).Open();
        using var escritor = new StreamWriter(fluxo);

        escritor.Write(conteudo);
    }

    /// <summary>Os .md da base, incluindo os index.md — quem recebe precisa navegar de imediato.</summary>
    private static IReadOnlyList<string> ArquivosDeConhecimento(string diretorio) =>
    [
        .. Directory.EnumerateFiles(diretorio, "*.md", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase),
    ];

    private static IReadOnlyList<string> PdfsDaFicha(string diretorio) =>
        Directory.Exists(diretorio)
            ? [.. Directory.EnumerateFiles(diretorio, "*.pdf", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase)]
            : [];

    private static string Relativo(string raiz, string caminho) =>
        Path.GetRelativePath(raiz, caminho).Replace('\\', '/');
}
