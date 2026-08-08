using MainForge.ClaudeCode;
using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Cli;

/// <summary>
/// Estado compartilhado entre os fluxos do menu: os caminhos do projeto e a instalação do
/// Claude Code que executa os agentes.
///
/// Repare no que sumiu daqui: chave de API, armazenamento cifrado, origem da chave. O
/// aplicativo não tem mais credencial nenhuma — quem está autenticado é o Claude Code, com a
/// assinatura do usuário, e nós só o executamos.
/// </summary>
internal sealed class ContextoDoAplicativo
{
    public ContextoDoAplicativo(CaminhosDoProjeto caminhos)
    {
        Caminhos = caminhos;
        Preferencias = PreferenciasDoUsuario.Carregar(caminhos);
        Opcoes = ComPreferencias(OpcoesDoClaudeCode.Resolver());
    }

    public CaminhosDoProjeto Caminhos { get; }

    public OpcoesDoClaudeCode? Opcoes { get; private set; }

    /// <summary>
    /// As escolhas do usuário sobre gasto de cota. Ficam no contexto porque valem para todos os
    /// fluxos e podem mudar no meio da sessão, pelo menu Ambiente.
    /// </summary>
    public PreferenciasDoUsuario Preferencias { get; private set; }

    public bool TemClaudeCode => Opcoes is not null;

    /// <summary>
    /// Troca as preferências e reaplica-as às opções de execução, para que a próxima operação já
    /// rode com o perfil novo sem o usuário precisar reabrir o aplicativo.
    /// </summary>
    public void AtualizarPreferencias(PreferenciasDoUsuario preferencias)
    {
        Preferencias = preferencias;
        preferencias.Salvar(Caminhos);
        Opcoes = ComPreferencias(Opcoes);
    }

    private OpcoesDoClaudeCode? ComPreferencias(OpcoesDoClaudeCode? opcoes) => opcoes is null
        ? null
        : opcoes with
        {
            Perfil = Preferencias.Perfil,
            ModeloForcado = Preferencias.ModeloForcado,
            TetoDeGastoUsd = Preferencias.TetoDeGastoUsd,
        };

    /// <summary>
    /// Devolve as opções de execução, ou <c>null</c> (explicando ao usuário) quando não há
    /// Claude Code instalado. Todo fluxo que vai rodar um agente passa por aqui.
    /// </summary>
    public OpcoesDoClaudeCode? ExigirClaudeCode()
    {
        if (Opcoes is not null)
        {
            return Opcoes;
        }

        ConsoleUi.Erro("Claude Code não encontrado nesta máquina.");
        ConsoleUi.Info("O MainForge roda os agentes através dele, usando a sua assinatura — não há API key.");
        ConsoleUi.Info("Instale em https://claude.com/product/claude-code e rode 'claude' uma vez para entrar.");
        ConsoleUi.Detalhe($"Se já estiver instalado em local não padrão, aponte {LocalizadorDoClaudeCode.VariavelDeAmbiente}.");
        return null;
    }

    public string DescreverClaudeCode() =>
        Opcoes is null ? "não encontrado" : Opcoes.CaminhoExecutavel;
}
