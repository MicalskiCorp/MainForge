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
        ConsoleUi.Detalhe("O MainForge não guarda, não lê e não copia credencial nenhuma — o login mora");
        ConsoleUi.Detalhe("no Claude Code desta máquina, e é por isso que ele não viaja junto com o binário.");

        MostrarPermissoes();

        if (ConsoleUi.Confirmar("\nEntrar na sua conta Claude agora (ou trocar de conta)?"))
        {
            EntrarNaConta(opcoes);
            return;
        }

        if (!ConsoleUi.Confirmar("Fazer um teste real agora? (consome pouquíssimos tokens)"))
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
        ConsoleUi.Info("Use a opção de entrar na conta, aqui mesmo, e tente de novo.");
    }

    /// <summary>
    /// Abre o Claude Code numa janela própria para o usuário fazer login.
    ///
    /// <para><b>Por que assim, e não uma tela de login do aplicativo.</b> O login é uma conversa
    /// entre a pessoa e a Anthropic — navegador, código de verificação, sessão gravada pelo
    /// próprio Claude Code. Um formulário de usuário e senha aqui dentro pediria a credencial da
    /// conta dela para um programa que não tem por que vê-la, e é exatamente a forma de um golpe
    /// de phishing. O aplicativo abre a ferramenta certa e sai da frente.</para>
    ///
    /// <para>A janela é separada de propósito: este console está com a entrada tomada pelo menu, e
    /// o login precisa de um terminal interativo de verdade.</para>
    /// </summary>
    private static void EntrarNaConta(OpcoesDoClaudeCode opcoes)
    {
        ConsoleUi.Info("");
        ConsoleUi.Info("Vai abrir uma janela do Claude Code. Nela:");
        ConsoleUi.Info("  1. digite  /login  e confirme;");
        ConsoleUi.Info("  2. termine o login no navegador que abrir;");
        ConsoleUi.Info("  3. digite  /exit  para fechar a janela.");
        ConsoleUi.Detalhe("O login fica guardado pelo Claude Code, nesta máquina. O MainForge não o vê.");

        try
        {
            using var processo = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = opcoes.CaminhoExecutavel,
                // Janela própria: é o que dá um terminal interativo ao login.
                UseShellExecute = true,
            });

            ConsoleUi.Info("");
            ConsoleUi.Sucesso("Janela aberta. Quando terminar, volte aqui e faça o teste real.");
        }
        catch (Exception excecao) when (excecao is IOException or SystemException)
        {
            ConsoleUi.Erro($"Não deu para abrir o Claude Code: {excecao.Message}");
            ConsoleUi.Info($"Abra um terminal, rode '{opcoes.CaminhoExecutavel}' e digite /login.");
        }
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
