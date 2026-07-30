namespace RpgForge.Claude;

/// <summary>
/// Configuração necessária para falar com a Claude API. Este projeto só suporta uma API
/// key padrão da Anthropic (cobrança por token) — veja o README do projeto para entender
/// por que a autenticação via Claude Agent SDK / assinatura Pro-Max foi descartada para um
/// app em C#.
/// </summary>
public sealed class OpcoesClienteClaude
{
    /// <summary>
    /// Modelo usado por todo agente, a menos que ele sobrescreva explicitamente. "Melhor
    /// modelo disponível", segundo a especificação do produto, hoje significa Claude Opus 5.
    /// </summary>
    public const string ModeloPadrao = "claude-opus-5";

    public required string ChaveApi { get; init; }

    public string Modelo { get; init; } = ModeloPadrao;

    /// <summary>
    /// Resolve as opções a partir da variável de ambiente ANTHROPIC_API_KEY. Lança exceção
    /// com mensagem clara se ela não estiver definida — a tela de configurações do WPF é
    /// responsável por pedir a chave ao usuário e guardá-la (ex.: via DPAPI) antes de cair
    /// aqui, não esta classe.
    /// </summary>
    public static OpcoesClienteClaude DoAmbiente()
    {
        var chaveApi = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

        if (string.IsNullOrWhiteSpace(chaveApi))
        {
            throw new InvalidOperationException(
                "ANTHROPIC_API_KEY não está definida. Configure sua API key da Anthropic " +
                "(Console > API Keys) como variável de ambiente ou informe-a nas " +
                "configurações do aplicativo.");
        }

        return new OpcoesClienteClaude { ChaveApi = chaveApi };
    }
}
