using MainForge.Agents;
using MainForge.ClaudeCode;

namespace MainForge.Cli;

/// <summary>
/// Mostra a situação da instalação do Claude Code e, se o usuário quiser, faz um teste real
/// de ponta a ponta. Ocupa o lugar da antiga tela de configuração da API key: não há mais
/// nada a configurar, mas há o que verificar — e é melhor descobrir que a assinatura expirou
/// aqui do que no meio do processamento de um livro.
/// </summary>
internal static class FluxoDoClaudeCode
{
    public static async Task ExecutarAsync(ContextoDoAplicativo contexto, CancellationToken cancelamento)
    {
        ConsoleUi.Titulo("Claude Code");

        if (contexto.Opcoes is not { } opcoes)
        {
            contexto.ExigirClaudeCode();
            return;
        }

        ConsoleUi.Info($"Executável: {opcoes.CaminhoExecutavel}");
        ConsoleUi.Info($"Modelo:     {opcoes.Modelo}");
        ConsoleUi.Detalhe("Autenticação: da própria instalação do Claude Code (a sua assinatura).");
        ConsoleUi.Detalhe("O MainForge não guarda nem lê credencial nenhuma.");

        MostrarPermissoes();

        if (!ConsoleUi.Confirmar("\nFazer um teste real agora? (consome pouquíssimos tokens)"))
        {
            return;
        }

        ConsoleUi.Detalhe("    · verificando...");

        var resultado = await DiagnosticoDoClaudeCode.ExecutarAsync(opcoes, cancelamento);

        if (resultado.Versao is not null)
        {
            ConsoleUi.Info($"\nVersão: {resultado.Versao}");
        }

        if (resultado.Autenticado)
        {
            ConsoleUi.Sucesso("Tudo certo — o Claude Code respondeu e a assinatura está ativa.");
            return;
        }

        if (resultado.Limite is { } limite)
        {
            ConsoleUi.Aviso($"A instalação está certa, mas a cota da assinatura acabou: {limite.Mensagem}");

            ConsoleUi.Info(limite.Liberacao is { } liberacao
                ? $"A próxima janela abre por volta de {liberacao:dd/MM HH:mm}."
                : "O Claude Code não informou quando a próxima janela abre.");

            ConsoleUi.Detalhe("Nas operações longas o aplicativo espera essa virada sozinho e continua de onde parou.");
            return;
        }

        ConsoleUi.Erro($"O teste falhou: {resultado.Detalhe ?? "motivo não informado"}");
        ConsoleUi.Info("Rode 'claude' num terminal e confira se ele pede login.");
    }

    /// <summary>
    /// O allowlist por agente é o guardrail central do produto — vale poder olhar para ele sem
    /// abrir o código.
    /// </summary>
    private static void MostrarPermissoes()
    {
        ConsoleUi.Info("\nFerramentas por agente:");

        foreach (var agente in new[] { DefinicaoDeAgente.Configurador, DefinicaoDeAgente.DungeonMaster })
        {
            ConsoleUi.Info($"  {agente.Nome}");
            ConsoleUi.Detalhe($"    permitidas: {string.Join(", ", agente.FerramentasPermitidas())}");
            ConsoleUi.Detalhe($"    negadas:    {string.Join(", ", agente.FerramentasNegadas())}");
        }
    }
}
