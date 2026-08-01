using Anthropic.Services.Beta;
using MainBuild.Claude;
using MainBuild.Core;

namespace MainBuild.Cli;

/// <summary>
/// Estado compartilhado entre os fluxos do menu: caminhos do projeto, armazenamento da API
/// key e o cliente da Claude API. O cliente é criado sob demanda — abrir o aplicativo, ver
/// os sistemas disponíveis ou configurar a chave não deve exigir chave nenhuma.
/// </summary>
internal sealed class ContextoDoAplicativo
{
    private IMessageService? _mensagens;

    public ContextoDoAplicativo(CaminhosDoProjeto caminhos, ArmazenamentoDeChaveApi armazenamento)
    {
        Caminhos = caminhos;
        Armazenamento = armazenamento;
        RecarregarChave();
    }

    public CaminhosDoProjeto Caminhos { get; }

    public ArmazenamentoDeChaveApi Armazenamento { get; }

    public OrigemDaChaveApi OrigemDaChave { get; private set; }

    public OpcoesClienteClaude? Opcoes { get; private set; }

    public bool TemChave => Opcoes is not null;

    /// <summary>
    /// Relê a chave (variável de ambiente tem prioridade sobre o arquivo cifrado) e descarta
    /// o cliente atual, para a próxima operação usar a chave nova. Chamado no início e sempre
    /// que o usuário mexe na chave pelo menu.
    /// </summary>
    public void RecarregarChave()
    {
        (OrigemDaChave, Opcoes) = OpcoesClienteClaude.Resolver(Armazenamento);
        _mensagens = null;
    }

    /// <summary>
    /// Devolve o serviço de mensagens da Claude API, criando o cliente na primeira vez.
    /// Devolve <c>null</c> (e explica ao usuário) quando ainda não há chave configurada.
    /// </summary>
    public IMessageService? ObterServicoDeMensagens()
    {
        if (Opcoes is null)
        {
            ConsoleUi.Erro("Nenhuma chave da Claude API configurada.");
            ConsoleUi.Info("Use a opção \"Configurar a chave da Claude API\" no menu principal.");
            return null;
        }

        _mensagens ??= FabricaClienteClaude.Criar(Opcoes).Beta.Messages;
        return _mensagens;
    }

    public string DescreverOrigemDaChave() => OrigemDaChave switch
    {
        OrigemDaChaveApi.VariavelDeAmbiente => "variável de ambiente ANTHROPIC_API_KEY",
        OrigemDaChaveApi.ArquivoProtegido => $"arquivo cifrado ({Armazenamento.CaminhoDoArquivo})",
        _ => "não configurada",
    };
}
