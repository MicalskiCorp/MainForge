using System.Text.Json;
using System.Text.Json.Serialization;
using MainForge.Core;

namespace MainForge.Tools;

/// <summary>
/// As escolhas do usuário sobre como o aplicativo gasta a cota dele, gravadas em
/// <c>_preferencias.json</c> na raiz do projeto.
///
/// <para><b>Por que um arquivo, e não uma variável de ambiente.</b> As variáveis que o aplicativo
/// já lê (<c>MAINFORGE_RAIZ</c>, <c>MAINFORGE_CLAUDE_CODE</c>) apontam onde as coisas estão numa
/// máquina — quem as define é quem instalou. Isto é outra coisa: é uma decisão tomada dentro do
/// aplicativo, num menu, e que precisa continuar valendo no próximo uso sem o usuário lembrar de
/// nada.</para>
///
/// <para>O underscore no nome marca o arquivo como derivado e local, como em
/// <c>_estado-do-processamento.json</c> e <c>_texto/</c>: ele é de uma instalação, não do
/// projeto, e por isso não é versionado nem entra em pacote exportado.</para>
///
/// <para>Arquivo ausente ou corrompido devolve o padrão em vez de falhar. Perder uma preferência
/// custa o usuário reescolhê-la; derrubar a abertura do aplicativo custa tudo.</para>
/// </summary>
public sealed class PreferenciasDoUsuario
{
    public const string NomeDoArquivo = "_preferencias.json";

    private static readonly JsonSerializerOptions Formato = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Quanto gastar por quanta qualidade. Traduzido em modelo e esforço por agente.</summary>
    public PerfilDeExecucao Perfil { get; set; } = PerfilDeExecucao.Equilibrado;

    /// <summary>
    /// Teto de gasto por execução de agente, em dólares. <c>null</c> significa sem teto — que é o
    /// padrão, porque numa assinatura o limite real é a janela de uso, não o dinheiro.
    /// </summary>
    public decimal? TetoDeGastoUsd { get; set; }

    /// <summary>
    /// Um modelo específico, quando o usuário quiser mandar nisso em vez de deixar o perfil
    /// decidir. Não tem opção de menu: existe para quem sabe o que está fazendo editar o arquivo.
    /// </summary>
    public string? ModeloForcado { get; set; }

    public static string CaminhoDoArquivo(CaminhosDoProjeto caminhos) =>
        Path.Combine(caminhos.Raiz, NomeDoArquivo);

    public static PreferenciasDoUsuario Carregar(CaminhosDoProjeto caminhos)
    {
        var caminho = CaminhoDoArquivo(caminhos);

        if (!File.Exists(caminho))
        {
            return new PreferenciasDoUsuario();
        }

        try
        {
            return JsonSerializer.Deserialize<PreferenciasDoUsuario>(File.ReadAllText(caminho), Formato)
                   ?? new PreferenciasDoUsuario();
        }
        catch (Exception excecao) when (excecao is JsonException or IOException)
        {
            return new PreferenciasDoUsuario();
        }
    }

    public void Salvar(CaminhosDoProjeto caminhos) =>
        File.WriteAllText(CaminhoDoArquivo(caminhos), JsonSerializer.Serialize(this, Formato));
}
