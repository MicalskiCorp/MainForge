using Anthropic;

namespace MainBuild.Claude;

/// <summary>
/// Fábrica fina em torno do SDK oficial da Anthropic para .NET, para que o resto do app
/// nunca construa <see cref="AnthropicClient"/> diretamente e todo ponto de chamada
/// compartilhe a mesma resolução de chave.
/// </summary>
public static class FabricaClienteClaude
{
    public static AnthropicClient Criar(OpcoesClienteClaude opcoes) => new()
    {
        ApiKey = opcoes.ChaveApi,
    };
}
