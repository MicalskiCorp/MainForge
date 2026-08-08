using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using MainForge.Core;

namespace MainForge.Tools;

/// <summary>O que um pacote de personagem declara sobre si mesmo, antes de qualquer arquivo sair de dentro dele.</summary>
public sealed class ManifestoDoPersonagem
{
    /// <summary>Versão do formato. Um pacote de formato desconhecido é recusado em vez de adivinhado.</summary>
    public int Formato { get; set; } = PacoteDePersonagem.FormatoAtual;

    public string Sistema { get; set; } = "";

    /// <summary>O identificador (nome da pasta) que o personagem tinha na instalação de origem.</summary>
    public string Personagem { get; set; } = "";

    public string Nome { get; set; } = "";

    public string Resumo { get; set; } = "";

    public DateTimeOffset GeradoEm { get; set; }

    /// <summary>As fontes da mesa dele — é o que diz o que ele precisa do outro lado.</summary>
    public List<string> Fontes { get; set; } = [];

    /// <summary>Os níveis que vêm no histórico. <c>null</c> na lista é a ficha sem nível informado.</summary>
    public List<int?> Niveis { get; set; } = [];

    public bool TemFichaAtual { get; set; }
}

/// <summary>Resultado de uma exportação de personagem, para a interface poder dizer o que saiu.</summary>
public sealed record ResultadoDaExportacaoDePersonagem(
    string Arquivo,
    ManifestoDoPersonagem Manifesto,
    long Bytes);

/// <summary>Resultado de uma importação de personagem, para a interface poder dizer o que entrou.</summary>
public sealed record ResultadoDaImportacaoDePacoteDePersonagem(
    Personagem Personagem,
    ManifestoDoPersonagem Manifesto,
    int FichasDoHistorico,
    bool SistemaPresente);

/// <summary>
/// Leva um personagem inteiro de uma instalação para outra: o dossiê, o estado em texto e o
/// histórico de fichas por nível, num arquivo só.
///
/// <para><b>Por que ele existe separado do pacote de sistema.</b> São coisas de donos
/// diferentes. O pacote de sistema é o resultado de ler os livros — igual para todo mundo que
/// tem aqueles livros, e caro de refazer. O personagem é de quem joga: muda de máquina com a
/// pessoa, não com a mesa. Juntá-los faria exportar um sistema vazar os personagens de quem o
/// exportou.</para>
///
/// <para><b>Por que o histórico vai junto.</b> A ficha de cada nível é o que não se refaz. Um
/// personagem que chega sem ela chega sem passado: dá para continuar jogando, mas o nível 3 dele
/// deixou de existir. Levar só o PDF atual — que é o que a importação de uma ficha solta faz — é
/// a opção de quem só tem o PDF; quando o dossiê existe, ele viaja inteiro.</para>
///
/// <para><b>O manifesto é lido antes de extrair qualquer coisa</b>, e todo caminho de dentro do
/// pacote passa por <see cref="CaminhosDoProjeto.ResolverDentroDe"/>: um <c>.zip</c> vem de fora
/// e pode carregar <c>../../</c> tanto quanto um caminho vindo do modelo.</para>
/// </summary>
public static class PacoteDePersonagem
{
    public const int FormatoAtual = 1;

    public const string NomeDoManifesto = "pacote.json";

    public const string Extensao = ".mainforge-personagem.zip";

    private const string PastaDoDossie = "dossie/";
    private const string PastaDasFichas = "fichas/";
    private const string NomeDaFichaAtual = "ficha-atual.pdf";

    private static readonly JsonSerializerOptions Formato = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Nome de arquivo sugerido: leva o sistema junto porque o nome do personagem não é único.</summary>
    public static string NomeSugerido(Personagem personagem) =>
        $"{personagem.Sistema}-{personagem.Id}{Extensao}";

    /// <summary>
    /// Escreve o pacote do personagem em <paramref name="caminhoDoPacote"/>.
    /// </summary>
    public static ResultadoDaExportacaoDePersonagem Exportar(
        CaminhosDoProjeto caminhos,
        Personagem personagem,
        string caminhoDoPacote)
    {
        var diretorio = RepositorioDePersonagens.DiretorioDoPersonagem(caminhos, personagem.Sistema, personagem.Id);

        if (!Directory.Exists(diretorio))
        {
            throw new InvalidOperationException(
                $"Não há dossiê em Personagens/{personagem.Sistema}/{personagem.Id}/ para exportar.");
        }

        var fichas = FichasEmDisco(caminhos, personagem);
        var fichaAtual = CaminhoDaFichaAtual(caminhos, personagem);

        var manifesto = new ManifestoDoPersonagem
        {
            Formato = FormatoAtual,
            Sistema = personagem.Sistema,
            Personagem = personagem.Id,
            Nome = personagem.Nome,
            Resumo = personagem.Resumo,
            GeradoEm = DateTimeOffset.Now,
            Fontes = [.. personagem.Fontes],
            Niveis = [.. fichas.Select(par => par.Registro.Nivel)],
            TemFichaAtual = fichaAtual is not null,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(caminhoDoPacote))!);

        using (var arquivo = new FileStream(caminhoDoPacote, FileMode.Create, FileAccess.Write))
        using (var pacote = new ZipArchive(arquivo, ZipArchiveMode.Create))
        {
            Gravar(pacote, NomeDoManifesto, JsonSerializer.Serialize(manifesto, Formato));

            var dossie = Path.Combine(diretorio, RepositorioDePersonagens.NomeDoArquivo);

            if (File.Exists(dossie))
            {
                pacote.CreateEntryFromFile(dossie, PastaDoDossie + RepositorioDePersonagens.NomeDoArquivo);
            }

            var emTexto = Path.Combine(diretorio, RepositorioDePersonagens.NomeDaFichaEmTexto);

            if (File.Exists(emTexto))
            {
                pacote.CreateEntryFromFile(emTexto, PastaDoDossie + RepositorioDePersonagens.NomeDaFichaEmTexto);
            }

            foreach (var (_, caminho) in fichas)
            {
                pacote.CreateEntryFromFile(caminho, PastaDasFichas + Path.GetFileName(caminho));
            }

            // A ficha atual vai junto mesmo sendo, quase sempre, cópia do registro do nível mais
            // alto. "Quase sempre" é o problema: quando o histórico não pôde ser gravado, ela é a
            // única ficha que existe — e é ela que o destinatário espera achar em Output/.
            if (fichaAtual is not null)
            {
                pacote.CreateEntryFromFile(fichaAtual, NomeDaFichaAtual);
            }
        }

        return new ResultadoDaExportacaoDePersonagem(
            caminhoDoPacote,
            manifesto,
            new FileInfo(caminhoDoPacote).Length);
    }

    /// <summary>
    /// Lê só o manifesto, sem extrair nada — é o que permite à interface mostrar quem está no
    /// pacote e perguntar antes de mexer no projeto.
    /// </summary>
    public static ManifestoDoPersonagem LerManifesto(string caminhoDoPacote)
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

        var manifesto = JsonSerializer.Deserialize<ManifestoDoPersonagem>(leitor.ReadToEnd(), Formato)
            ?? throw new InvalidOperationException($"O {NomeDoManifesto} do pacote está vazio ou ilegível.");

        if (manifesto.Formato > FormatoAtual)
        {
            throw new InvalidOperationException(
                $"O pacote foi gerado por uma versão mais nova do MainForge (formato {manifesto.Formato}, " +
                $"esta versão entende até o {FormatoAtual}). Atualize o aplicativo.");
        }

        return manifesto is { Sistema.Length: > 0, Personagem.Length: > 0 }
            ? manifesto
            : throw new InvalidOperationException(
                $"O {NomeDoManifesto} não diz de que sistema e de que personagem este pacote é. " +
                "Um pacote de sistema não serve aqui — ele se importa pelo menu Sistemas.");
    }

    /// <summary>
    /// Traz o personagem para dentro desta instalação: dossiê, estado em texto, histórico de
    /// fichas e a ficha atual em <c>Output/</c>.
    /// </summary>
    /// <param name="idAlternativo">
    /// Outro identificador, quando o do pacote já existe aqui e o usuário não quer substituí-lo.
    /// </param>
    /// <param name="substituir">
    /// Autoriza sobrescrever um personagem de mesmo identificador. Sem isto a importação é
    /// recusada: o dossiê que está lá pode ser outro personagem com o mesmo nome, e o histórico
    /// dele não volta.
    /// </param>
    public static ResultadoDaImportacaoDePacoteDePersonagem Importar(
        CaminhosDoProjeto caminhos,
        string caminhoDoPacote,
        string? idAlternativo = null,
        bool substituir = false)
    {
        var manifesto = LerManifesto(caminhoDoPacote);

        var sistema = new SistemaRpg(NomeDePasta.Validar(manifesto.Sistema, "sistema", nameof(caminhoDoPacote)));

        var id = NomeDePasta.Validar(
            string.IsNullOrWhiteSpace(idAlternativo) ? manifesto.Personagem : idAlternativo,
            "personagem",
            nameof(idAlternativo));

        var destino = RepositorioDePersonagens.DiretorioDoPersonagem(caminhos, sistema.Id, id);

        if (!substituir && Directory.Exists(destino))
        {
            throw new InvalidOperationException(
                $"Já existe um personagem em Personagens/{sistema.Id}/{id}/. Importe com outro identificador " +
                "ou confirme a substituição.");
        }

        var diretorioDasFichas = FichasDoPersonagem.Diretorio(caminhos, sistema.Id, id);

        Directory.CreateDirectory(destino);
        Directory.CreateDirectory(diretorioDasFichas);

        var fichaAtualExtraida = Path.Combine(destino, NomeDaFichaAtual);
        var doHistorico = new List<string>();

        using (var pacote = AbrirParaLeitura(caminhoDoPacote))
        {
            foreach (var entrada in pacote.Entries)
            {
                if (entrada.FullName.EndsWith('/') || entrada.Name.Length == 0)
                {
                    continue; // diretório
                }

                var caminhoDeDestino = DestinoDaEntrada(
                    entrada.FullName, destino, diretorioDasFichas, fichaAtualExtraida);

                if (caminhoDeDestino is null)
                {
                    continue; // o manifesto e qualquer coisa fora das pastas conhecidas
                }

                Directory.CreateDirectory(Path.GetDirectoryName(caminhoDeDestino)!);
                entrada.ExtractToFile(caminhoDeDestino, overwrite: true);

                if (caminhoDeDestino.StartsWith(diretorioDasFichas, StringComparison.OrdinalIgnoreCase))
                {
                    doHistorico.Add(caminhoDeDestino);
                }
            }
        }

        var personagem = RepositorioDePersonagens.Carregar(caminhos, sistema.Id, id)
            ?? throw new InvalidOperationException(
                $"O pacote não trouxe o {RepositorioDePersonagens.NomeDoArquivo} do personagem.");

        // Os caminhos de dentro do dossiê são da instalação de origem: eles carregam o
        // identificador e o sistema de lá. Reapontá-los para o que acabou de ser extraído é o que
        // faz o personagem importado com outro identificador continuar achando as próprias fichas.
        personagem.Fichas = ReapontarHistorico(caminhos, personagem, doHistorico);
        personagem.FichaGerada = RestaurarFichaAtual(caminhos, personagem, fichaAtualExtraida);
        personagem.Anotar($"Importado do pacote '{Path.GetFileName(caminhoDoPacote)}'.");

        File.Delete(fichaAtualExtraida);
        RepositorioDePersonagens.Salvar(caminhos, personagem);

        return new ResultadoDaImportacaoDePacoteDePersonagem(
            personagem,
            manifesto,
            personagem.Fichas.Count,
            sistema.TemConhecimento(caminhos));
    }

    /// <summary>
    /// Refaz a lista do histórico a partir dos PDFs que entraram, lendo o nível do nome de cada
    /// arquivo. A data de cada registro vem do manifesto quando ela existia — o que importa é a
    /// ordem por nível, que é como o histórico é lido.
    /// </summary>
    private static List<FichaDeNivel> ReapontarHistorico(
        CaminhosDoProjeto caminhos,
        Personagem personagem,
        IReadOnlyList<string> extraidas)
    {
        var refeitas = new List<FichaDeNivel>();

        foreach (var caminho in extraidas)
        {
            var nivel = NivelDoNomeDoArquivo(Path.GetFileName(caminho));

            var quando = personagem.Fichas.FirstOrDefault(ficha => ficha.Nivel == nivel)?.Em
                ?? File.GetLastWriteTime(caminho);

            refeitas.Add(new FichaDeNivel(
                nivel,
                Path.GetRelativePath(caminhos.Raiz, caminho).Replace('\\', '/'),
                quando));
        }

        refeitas.Sort((primeira, segunda) => (primeira.Nivel ?? 0).CompareTo(segunda.Nivel ?? 0));

        return refeitas;
    }

    /// <summary>
    /// Põe a ficha atual em <c>Output/Personagens/</c>, com o nome que este personagem tem
    /// <b>nesta</b> instalação — uma ficha por personagem também para quem chega de fora.
    ///
    /// <para>Quando o pacote não trouxe a ficha atual, o registro de nível mais alto ocupa o
    /// lugar dela: é a mesma ficha em todo caso normal, e é melhor que deixar o personagem
    /// concluído apontando para um arquivo que não existe.</para>
    /// </summary>
    private static string? RestaurarFichaAtual(
        CaminhosDoProjeto caminhos,
        Personagem personagem,
        string fichaAtualExtraida)
    {
        // O histórico já foi reapontado e ordenado por nível quando isto roda: o último registro
        // é o do nível mais alto, que é o estado atual do personagem.
        var doNivelMaisAlto = personagem.Fichas.LastOrDefault() is { } ultima
            ? Path.Combine(caminhos.Raiz, ultima.Arquivo)
            : null;

        var origem = File.Exists(fichaAtualExtraida) ? fichaAtualExtraida : doNivelMaisAlto;

        if (origem is null || !File.Exists(origem))
        {
            return null;
        }

        Directory.CreateDirectory(caminhos.SaidaPersonagens);

        var destino = CaminhosDoProjeto.ResolverDentroDe(
            caminhos.SaidaPersonagens,
            FichasDoPersonagem.NomeNaSaida(caminhos, personagem));

        File.Copy(origem, destino, overwrite: true);

        return Path.GetRelativePath(caminhos.Raiz, destino).Replace('\\', '/');
    }

    /// <summary>
    /// Para onde vai uma entrada do pacote, ou <c>null</c> se ela não pertence a nada que
    /// conhecemos.
    ///
    /// <para>É aqui que o "zip slip" morre: o caminho de dentro do arquivo é caminho vindo de
    /// fora, e <see cref="CaminhosDoProjeto.ResolverDentroDe"/> recusa o que escapar do
    /// destino.</para>
    /// </summary>
    private static string? DestinoDaEntrada(
        string entrada,
        string diretorioDoPersonagem,
        string diretorioDasFichas,
        string caminhoDaFichaAtual)
    {
        var normalizada = entrada.Replace('\\', '/');

        if (normalizada.Equals(NomeDaFichaAtual, StringComparison.OrdinalIgnoreCase))
        {
            return caminhoDaFichaAtual;
        }

        if (normalizada.StartsWith(PastaDoDossie, StringComparison.OrdinalIgnoreCase))
        {
            var relativo = normalizada[PastaDoDossie.Length..];

            return relativo.Equals(RepositorioDePersonagens.NomeDoArquivo, StringComparison.OrdinalIgnoreCase) ||
                   relativo.Equals(RepositorioDePersonagens.NomeDaFichaEmTexto, StringComparison.OrdinalIgnoreCase)
                ? CaminhosDoProjeto.ResolverDentroDe(diretorioDoPersonagem, relativo)
                : null;
        }

        if (normalizada.StartsWith(PastaDasFichas, StringComparison.OrdinalIgnoreCase))
        {
            var relativo = normalizada[PastaDasFichas.Length..];

            return relativo.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                ? CaminhosDoProjeto.ResolverDentroDe(diretorioDasFichas, relativo)
                : null;
        }

        return null;
    }

    /// <summary>Os PDFs do histórico que existem de verdade, com o registro de cada um.</summary>
    private static IReadOnlyList<(FichaDeNivel Registro, string Caminho)> FichasEmDisco(
        CaminhosDoProjeto caminhos,
        Personagem personagem)
    {
        var fichas = new List<(FichaDeNivel, string)>();

        foreach (var registro in personagem.Fichas)
        {
            var caminho = Path.Combine(caminhos.Raiz, registro.Arquivo);

            if (File.Exists(caminho))
            {
                fichas.Add((registro, caminho));
            }
        }

        return fichas;
    }

    private static string? CaminhoDaFichaAtual(CaminhosDoProjeto caminhos, Personagem personagem)
    {
        if (personagem.FichaGerada is not { Length: > 0 } relativo)
        {
            return null;
        }

        var caminho = Path.Combine(caminhos.Raiz, relativo);

        return File.Exists(caminho) ? caminho : null;
    }

    /// <summary>
    /// O nível a partir do nome do arquivo (<c>nivel-03.pdf</c>). O nome é a fonte da verdade
    /// porque é o que sobrevive à viagem: o dossiê pode ter vindo com caminhos de outra máquina,
    /// mas o arquivo extraído está aqui e diz a que nível pertence.
    /// </summary>
    private static int? NivelDoNomeDoArquivo(string nomeDoArquivo)
    {
        var semExtensao = Path.GetFileNameWithoutExtension(nomeDoArquivo);

        return semExtensao.StartsWith("nivel-", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(semExtensao["nivel-".Length..], out var nivel)
            ? nivel
            : null;
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
}
