using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MainForge.Core;

namespace MainForge.Tools;

/// <summary>
/// Guarda e recupera os dossiês de personagem em <c>Personagens/&lt;Sistema&gt;/&lt;Id&gt;/</c>.
///
/// <para><b>Por que existe.</b> Antes disto, uma criação de personagem só existia dentro da
/// conversa: fechar a janela no meio jogava fora todas as escolhas já feitas, e um personagem
/// pronto era apenas um PDF em <c>Output/</c>, sem nada que dissesse de que sistema ele era,
/// com quais expansões foi montado ou como chegar ao estado dele para subir de nível.</para>
///
/// <para>São dois arquivos por personagem, com donos diferentes de propósito:</para>
/// <list type="bullet">
///   <item><c>personagem.json</c> — do C#: status, fontes da mesa, id da sessão, campos da
///   última ficha gerada. É o que a interface lê para montar as listas.</item>
///   <item><c>ficha.md</c> — do agente, gravado por <c>registrar_personagem</c>: o estado
///   completo do personagem em texto. É o que reconstrói o personagem numa sessão nova quando
///   a conversa original não existe mais no Claude Code.</item>
/// </list>
/// </summary>
public static class RepositorioDePersonagens
{
    public const string NomeDoArquivo = "personagem.json";

    /// <summary>O estado do personagem em texto, escrito pelo agente. Nome fixo: o prompt cita.</summary>
    public const string NomeDaFichaEmTexto = "ficha.md";

    private static readonly JsonSerializerOptions Formato = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        // Sem isto todo acento do português vira escape e o arquivo deixa de ser legível por
        // quem abrir para conferir — o mesmo motivo do registro de processamento.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DiretorioDoSistema(CaminhosDoProjeto caminhos, string sistema) =>
        CaminhosDoProjeto.ResolverDentroDe(caminhos.Personagens, sistema);

    public static string DiretorioDoPersonagem(CaminhosDoProjeto caminhos, string sistema, string id) =>
        CaminhosDoProjeto.ResolverDentroDe(DiretorioDoSistema(caminhos, sistema), id);

    /// <summary>Caminho do <c>ficha.md</c>, relativo à raiz — é assim que o prompt o cita.</summary>
    public static string CaminhoDaFichaEmTexto(CaminhosDoProjeto caminhos, string sistema, string id) =>
        Path.Combine(DiretorioDoPersonagem(caminhos, sistema, id), NomeDaFichaEmTexto);

    /// <summary>Todos os personagens de todos os sistemas, do mais recente ao mais antigo.</summary>
    public static IReadOnlyList<Personagem> Listar(CaminhosDoProjeto caminhos)
    {
        if (!Directory.Exists(caminhos.Personagens))
        {
            return [];
        }

        return
        [
            .. Directory
                .EnumerateDirectories(caminhos.Personagens)
                .SelectMany(diretorio => Listar(caminhos, Path.GetFileName(diretorio)))
                .OrderByDescending(personagem => personagem.AtualizadoEm),
        ];
    }

    /// <summary>Os personagens de um sistema, do mais recente ao mais antigo.</summary>
    public static IReadOnlyList<Personagem> Listar(CaminhosDoProjeto caminhos, string sistema)
    {
        var diretorio = DiretorioDoSistema(caminhos, sistema);

        if (!Directory.Exists(diretorio))
        {
            return [];
        }

        return
        [
            .. Directory
                .EnumerateDirectories(diretorio)
                .Select(pasta => Carregar(caminhos, sistema, Path.GetFileName(pasta)))
                .OfType<Personagem>()
                .OrderByDescending(personagem => personagem.AtualizadoEm),
        ];
    }

    /// <summary>
    /// Lê o dossiê, ou <c>null</c> se ele não existir. Um arquivo corrompido também vira
    /// <c>null</c>: perder um personagem da lista é ruim, derrubar o aplicativo ao abrir o menu
    /// é pior.
    /// </summary>
    public static Personagem? Carregar(CaminhosDoProjeto caminhos, string sistema, string id)
    {
        var caminho = Path.Combine(DiretorioDoPersonagem(caminhos, sistema, id), NomeDoArquivo);

        if (!File.Exists(caminho))
        {
            return null;
        }

        try
        {
            var personagem = JsonSerializer.Deserialize<Personagem>(File.ReadAllText(caminho), Formato);

            if (personagem is null)
            {
                return null;
            }

            // O disco manda: pasta renomeada na mão continua sendo o personagem dela.
            personagem.Id = id;
            personagem.Sistema = sistema;

            return personagem;
        }
        catch (Exception excecao) when (excecao is JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Abre um dossiê novo. O identificador sai do nome informado e ganha sufixo numérico
    /// enquanto colidir — dois personagens chamados "Thoradin" são dois personagens, e não uma
    /// sobrescrita silenciosa do primeiro.
    /// </summary>
    public static Personagem Criar(
        CaminhosDoProjeto caminhos,
        string sistema,
        string nome,
        IReadOnlyList<string> fontes)
    {
        var agora = DateTimeOffset.Now;

        var personagem = new Personagem
        {
            Id = IdDisponivel(caminhos, sistema, nome),
            Sistema = sistema,
            Nome = nome.Trim(),
            Status = StatusDoPersonagem.Desenvolvendo,
            Fontes = [.. fontes],
            CriadoEm = agora,
            AtualizadoEm = agora,
        };

        personagem.Anotar("Criação iniciada.");
        Salvar(caminhos, personagem);

        return personagem;
    }

    public static void Salvar(CaminhosDoProjeto caminhos, Personagem personagem)
    {
        var diretorio = DiretorioDoPersonagem(caminhos, personagem.Sistema, personagem.Id);
        Directory.CreateDirectory(diretorio);

        personagem.AtualizadoEm = DateTimeOffset.Now;

        File.WriteAllText(
            Path.Combine(diretorio, NomeDoArquivo),
            JsonSerializer.Serialize(personagem, Formato));
    }

    /// <summary>
    /// O que o agente registrou sobre o personagem no meio da conversa: o estado dele em texto
    /// e, quando já houver, os valores dos campos da ficha.
    ///
    /// <para>Isto é o que faz "desenvolvendo" significar alguma coisa. Sem uma gravação no meio
    /// do caminho, um personagem interrompido antes do PDF seria uma pasta vazia — e retomar
    /// dependeria inteiramente de a conversa ainda existir do lado do Claude Code.</para>
    /// </summary>
    public static Personagem Registrar(
        CaminhosDoProjeto caminhos,
        string sistema,
        string id,
        string? nome,
        string? resumo,
        string? fichaEmTexto,
        IReadOnlyDictionary<string, string>? campos)
    {
        var personagem = Carregar(caminhos, sistema, id)
            ?? throw new ErroDeFerramenta(
                $"Não há personagem '{id}' em Personagens/{sistema}/. " +
                "Use exatamente o identificador que a mensagem inicial da conversa informou.");

        if (personagem.Status == StatusDoPersonagem.Descontinuado)
        {
            throw new ErroDeFerramenta(
                $"O personagem '{id}' está descontinuado — o usuário precisa reativá-lo no menu antes.");
        }

        if (nome is { Length: > 0 })
        {
            personagem.Nome = nome.Trim();
        }

        if (resumo is { Length: > 0 })
        {
            personagem.Resumo = Encurtar(resumo);
        }

        if (campos is { Count: > 0 })
        {
            personagem.Campos = new Dictionary<string, string>(campos);
        }

        if (fichaEmTexto is { Length: > 0 })
        {
            var diretorio = DiretorioDoPersonagem(caminhos, sistema, id);
            Directory.CreateDirectory(diretorio);
            File.WriteAllText(Path.Combine(diretorio, NomeDaFichaEmTexto), fichaEmTexto);
        }

        Salvar(caminhos, personagem);

        return personagem;
    }

    /// <summary>
    /// Liga a ficha em PDF recém-gerada ao dossiê e dá o personagem por concluído.
    ///
    /// <para>Quem chama é o servidor MCP, logo depois de <c>preencher_ficha_personagem</c>: o
    /// PDF existir é o único sinal de conclusão que não depende de o modelo declarar que
    /// terminou.</para>
    /// </summary>
    public static void RegistrarFichaGerada(
        CaminhosDoProjeto caminhos,
        string sistema,
        string id,
        string caminhoDaFicha,
        IReadOnlyDictionary<string, string> campos)
    {
        var personagem = Carregar(caminhos, sistema, id)
            ?? throw new ErroDeFerramenta($"Não há personagem '{id}' em Personagens/{sistema}/.");

        var evolucao = personagem.Status == StatusDoPersonagem.Concluido;

        personagem.FichaGerada = caminhoDaFicha.Replace('\\', '/');
        personagem.Campos = new Dictionary<string, string>(campos);
        personagem.Status = StatusDoPersonagem.Concluido;
        personagem.Anotar(evolucao ? $"Ficha regerada: {personagem.FichaGerada}" : $"Ficha gerada: {personagem.FichaGerada}");

        Salvar(caminhos, personagem);
    }

    /// <summary>Muda o status por decisão do usuário (descontinuar, reabrir para evoluir).</summary>
    public static void MudarStatus(CaminhosDoProjeto caminhos, Personagem personagem, StatusDoPersonagem novo)
    {
        if (personagem.Status == novo)
        {
            return;
        }

        personagem.Anotar($"Status: {Personagem.DescreverStatus(personagem.Status)} -> {Personagem.DescreverStatus(novo)}.");
        personagem.Status = novo;

        Salvar(caminhos, personagem);
    }

    /// <summary>
    /// Guarda em qual conversa do Claude Code este personagem está sendo feito e soma o que o
    /// turno custou.
    /// </summary>
    /// <param name="consumoDoTurno">
    /// Só o gasto deste turno, não o da conversa inteira: quem chama é um laço que passa por aqui
    /// a cada resposta, e somar o acumulado da sessão contaria os primeiros turnos várias vezes.
    /// </param>
    public static void RegistrarSessao(
        CaminhosDoProjeto caminhos,
        Personagem personagem,
        string? idDaSessao,
        ConsumoDeTokens? consumoDoTurno = null)
    {
        var mudouSessao = idDaSessao is not null && personagem.IdDaSessao != idDaSessao;
        var houveGasto = consumoDoTurno is { Vazio: false };

        if (!mudouSessao && !houveGasto)
        {
            return;
        }

        if (mudouSessao)
        {
            personagem.IdDaSessao = idDaSessao;
        }

        if (consumoDoTurno is { Vazio: false } gasto)
        {
            personagem.Consumo += gasto;
        }

        Salvar(caminhos, personagem);
    }

    /// <summary>
    /// O texto que o agente gravou sobre o personagem, ou vazio se ele ainda não gravou nada.
    /// A interface usa isto para mostrar o que já existe antes de retomar uma criação.
    /// </summary>
    public static string LerFichaEmTexto(CaminhosDoProjeto caminhos, string sistema, string id)
    {
        var caminho = CaminhoDaFichaEmTexto(caminhos, sistema, id);

        try
        {
            return File.Exists(caminho) ? File.ReadAllText(caminho) : "";
        }
        catch (IOException)
        {
            return "";
        }
    }

    /// <summary>
    /// Um identificador de pasta a partir do nome digitado, livre de colisão. Caractere que não
    /// serve em nome de arquivo vira hífen em vez de derrubar a criação: o nome do personagem é
    /// texto livre do usuário, e "Aza'thor, o Bravo" é um nome legítimo.
    /// </summary>
    private static string IdDisponivel(CaminhosDoProjeto caminhos, string sistema, string nome)
    {
        var baseDoId = Higienizar(nome);
        var candidato = baseDoId;
        var sufixo = 2;

        while (Directory.Exists(DiretorioDoPersonagem(caminhos, sistema, candidato)))
        {
            candidato = $"{baseDoId}-{sufixo++}";
        }

        return candidato;
    }

    private static string Higienizar(string nome)
    {
        var proibidos = Path.GetInvalidFileNameChars();
        var limpo = new StringBuilder();

        foreach (var caractere in nome.Trim())
        {
            limpo.Append(proibidos.Contains(caractere) || caractere is '.' ? '-' : caractere);
        }

        var resultado = limpo.ToString().Trim('-', ' ');

        return resultado.Length > 0 ? resultado : "Personagem";
    }

    private static string Encurtar(string texto)
    {
        var linha = texto.ReplaceLineEndings(" ").Trim();

        return linha.Length <= 160 ? linha : string.Concat(linha.AsSpan(0, 160).TrimEnd(), "...");
    }
}
