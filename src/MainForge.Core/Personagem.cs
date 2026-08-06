namespace MainForge.Core;

/// <summary>Em que pé está um personagem.</summary>
public enum StatusDoPersonagem
{
    /// <summary>A criação começou e não terminou — é o que a próxima sessão retoma.</summary>
    Desenvolvendo,

    /// <summary>A ficha em PDF saiu. Continua podendo evoluir (subir de nível, mudar equipamento).</summary>
    Concluido,

    /// <summary>Abandonado de propósito pelo usuário. Some das listas de trabalho, mas não do disco.</summary>
    Descontinuado,
}

/// <summary>Uma linha do histórico do personagem: quando algo aconteceu e o que foi.</summary>
public sealed class AnotacaoDoPersonagem
{
    public DateTimeOffset Em { get; set; }

    public string Texto { get; set; } = "";
}

/// <summary>
/// O dossiê de um personagem: quem ele é, em que pé está e qual conversa o produziu. Fica em
/// <c>Personagens/&lt;Sistema&gt;/&lt;Id&gt;/personagem.json</c>, ao lado de um
/// <c>ficha.md</c> escrito pelo agente com o estado completo dele.
///
/// <para><b>Por que existe.</b> Sem isto, fechar a janela no meio de uma criação jogava fora
/// tudo que já tinha sido decidido: a conversa vivia só dentro do processo do Claude Code e
/// ninguém sabia sequer que ela tinha existido. Com o dossiê, a sessão é retomável e o
/// personagem pronto continua sendo um objeto do aplicativo — dá para subir de nível, trocar
/// inventário e gerar a ficha de novo meses depois.</para>
///
/// <para>É uma classe com propriedades graváveis, e não um <c>record</c>, porque ela é
/// serializada em JSON e lida de volta — o mesmo motivo de
/// <see cref="System.Text.Json.JsonSerializer"/> que já vale no registro de processamento.</para>
/// </summary>
public sealed class Personagem
{
    /// <summary>Nome da pasta do personagem — derivado do nome, validado como nome de pasta.</summary>
    public string Id { get; set; } = "";

    public string Sistema { get; set; } = "";

    /// <summary>Nome do personagem como o usuário o chama. Vazio enquanto ele não tiver um.</summary>
    public string Nome { get; set; } = "";

    public StatusDoPersonagem Status { get; set; } = StatusDoPersonagem.Desenvolvendo;

    /// <summary>
    /// As fontes que a mesa deste personagem usa. Ficam gravadas porque uma sessão de evolução
    /// precisa valer as mesmas regras da criação: subir de nível com uma expansão que a mesa
    /// não usava produziria um personagem que ninguém pode jogar.
    /// </summary>
    public List<string> Fontes { get; set; } = [];

    /// <summary>
    /// A conversa no Claude Code que produziu este personagem. Retomá-la traz de volta todo o
    /// contexto já pago; quando ela não existe mais, o <c>ficha.md</c> é o que reconstrói o
    /// personagem sem reler a base inteira.
    /// </summary>
    public string? IdDaSessao { get; set; }

    public DateTimeOffset CriadoEm { get; set; }

    public DateTimeOffset AtualizadoEm { get; set; }

    /// <summary>Uma linha dizendo quem é o personagem — é o que a lista do menu mostra.</summary>
    public string Resumo { get; set; } = "";

    /// <summary>Caminho da ficha em PDF já gerada, relativo à raiz do projeto.</summary>
    public string? FichaGerada { get; set; }

    /// <summary>
    /// Os valores com que a ficha foi preenchida da última vez, por nome de campo do PDF. É o
    /// ponto de partida de uma evolução: subir de nível muda alguns campos, não todos.
    /// </summary>
    public Dictionary<string, string> Campos { get; set; } = [];

    public List<AnotacaoDoPersonagem> Historico { get; set; } = [];

    /// <summary>O nome de exibição: o do personagem, ou o identificador enquanto ele não tem nome.</summary>
    public string Rotulo => Nome.Length > 0 ? Nome : Id;

    public void Anotar(string texto)
    {
        Historico.Add(new AnotacaoDoPersonagem { Em = DateTimeOffset.Now, Texto = texto });
    }

    /// <summary>Como o status aparece na interface.</summary>
    public static string DescreverStatus(StatusDoPersonagem status) => status switch
    {
        StatusDoPersonagem.Desenvolvendo => "desenvolvendo",
        StatusDoPersonagem.Concluido => "concluído",
        _ => "descontinuado",
    };
}
