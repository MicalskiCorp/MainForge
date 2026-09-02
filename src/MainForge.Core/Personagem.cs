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

/// <summary>
/// A ficha que ficou pronta num nível: um registro por nível do personagem.
///
/// <para><b>Por que guardar.</b> <c>Output/</c> tem uma ficha por personagem — a atual —, porque
/// é entrega: quem abre a pasta procura "a ficha do Thoradin", não escolhe entre seis arquivos
/// para descobrir qual vale. Mas a ficha de cada nível é história que não se refaz: subir de
/// nível reescreve o PDF, e sem cópia o estado anterior some. Voltar um nível depois de uma
/// evolução errada, ou só rever como o personagem era, deixa de ser possível.</para>
/// </summary>
/// <param name="Nivel">O nível em que esta ficha foi concluída, ou <c>null</c> quando não foi informado.</param>
/// <param name="Arquivo">Caminho do PDF guardado, relativo à raiz do projeto.</param>
/// <param name="Em">Quando ela foi gerada.</param>
/// <param name="Magias">
/// A folha extra de magias daquele nível, quando o sistema precisou de uma. Vai junto porque ela é
/// parte do mesmo estado: um personagem que voltasse do histórico com a ficha do nível 3 e a folha
/// de magias do nível 7 estaria pior do que sem folha nenhuma.
/// </param>
public sealed record FichaDeNivel(int? Nivel, string Arquivo, DateTimeOffset Em, string? Magias = null)
{
    /// <summary>Como o nível aparece na interface — inclusive quando não se sabe qual é.</summary>
    public string Rotulo => Nivel is { } nivel ? $"nível {nivel}" : "nível não informado";
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
    /// Quando a ficha foi gerada da última vez.
    ///
    /// <para><b>Por que não basta <see cref="FichaGerada"/>.</b> É por este campo que a interface
    /// sabe que a geração acabou de acontecer e a conversa pode encerrar. O caminho não serve para
    /// isso: numa evolução, o agente costuma reusar o mesmo nome de arquivo, e uma ficha regerada
    /// por cima da anterior não muda uma letra dele — a conversa ficaria aberta depois de já ter
    /// entregado o que o usuário veio buscar.</para>
    /// </summary>
    public DateTimeOffset? FichaGeradaEm { get; set; }

    /// <summary>
    /// Caminho da folha extra com as magias por extenso, relativo à raiz do projeto, quando este
    /// personagem tem uma. <c>null</c> é o caso normal: o sistema não usa magias, a ficha dele já
    /// comporta as descrições, ou o personagem não é conjurador.
    ///
    /// <para>Fica no dossiê pelo mesmo motivo que <see cref="FichaGerada"/>: é uma entrega em
    /// <c>Output/</c>, e o dossiê é quem sabe o que ele produziu. Ver
    /// <c>MainForge.Tools.FolhaDeMagias</c>.</para>
    /// </summary>
    public string? FolhaDeMagias { get; set; }

    /// <summary>
    /// Os valores com que a ficha foi preenchida da última vez, por nome de campo do PDF. É o
    /// ponto de partida de uma evolução: subir de nível muda alguns campos, não todos.
    /// </summary>
    public Dictionary<string, string> Campos { get; set; } = [];

    /// <summary>
    /// As fichas guardadas, uma por nível, da mais antiga para a mais nova. A do nível atual é a
    /// última — e é a mesma que está em <see cref="FichaGerada"/>.
    /// </summary>
    public List<FichaDeNivel> Fichas { get; set; } = [];

    public List<AnotacaoDoPersonagem> Historico { get; set; } = [];

    /// <summary>
    /// O que este personagem já custou de cota, somando todas as conversas que o produziram.
    /// Acumula entre sessões: criar, continuar e evoluir vão todos para a mesma conta, que é
    /// como o usuário pensa no gasto ("quanto me custou este personagem?").
    /// </summary>
    public ConsumoDeTokens Consumo { get; set; } = ConsumoDeTokens.Zero;

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
