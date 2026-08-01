using MainBuild.Claude;

namespace MainBuild.Cli;

/// <summary>
/// Tela de configuração da API key: o usuário informa a chave aqui, no momento de usar o
/// aplicativo. A chave nunca é ecoada na tela, nunca é gravada em texto puro e nunca vem do
/// código-fonte — fica cifrada com DPAPI no perfil do usuário
/// (<see cref="ArmazenamentoDeChaveApi"/>).
/// </summary>
internal static class FluxoDaChaveApi
{
    public static void Executar(ContextoDoAplicativo contexto)
    {
        while (true)
        {
            ConsoleUi.Titulo("Chave da Claude API");
            ConsoleUi.Info($"Situação atual: {contexto.DescreverOrigemDaChave()}");

            if (contexto.OrigemDaChave == OrigemDaChaveApi.VariavelDeAmbiente)
            {
                ConsoleUi.Aviso(
                    "A variável de ambiente tem prioridade sobre a chave guardada aqui. " +
                    "Para usar a chave do aplicativo, apague a variável ANTHROPIC_API_KEY do ambiente.");
            }

            var acoes = new List<string> { "Informar / substituir a chave" };

            if (contexto.Armazenamento.Existe)
            {
                acoes.Add("Apagar a chave guardada");
            }

            var escolha = ConsoleUi.Escolher("O que deseja fazer?", acoes, acao => acao);

            switch (escolha)
            {
                case null:
                    return;

                case var acao when acao.StartsWith("Informar"):
                    Informar(contexto);
                    break;

                default:
                    Apagar(contexto);
                    break;
            }
        }
    }

    private static void Informar(ContextoDoAplicativo contexto)
    {
        ConsoleUi.Info("");
        ConsoleUi.Detalhe("A chave está no Console da Anthropic, em API Keys. Nada é exibido enquanto você digita.");

        var chave = ConsoleUi.LerSegredo("Cole a chave (Esc cancela): ");

        if (chave.Length == 0)
        {
            ConsoleUi.Aviso("Cancelado — nada foi alterado.");
            return;
        }

        if (!chave.StartsWith("sk-ant-", StringComparison.Ordinal))
        {
            ConsoleUi.Aviso("Essa chave não começa com \"sk-ant-\", o formato usual das chaves da Anthropic.");

            if (!ConsoleUi.Confirmar("Guardar mesmo assim?"))
            {
                return;
            }
        }

        contexto.Armazenamento.Salvar(chave);
        contexto.RecarregarChave();

        ConsoleUi.Sucesso($"Chave guardada cifrada em {contexto.Armazenamento.CaminhoDoArquivo}");
        ConsoleUi.Detalhe("Só a sua conta de usuário deste Windows consegue decifrá-la.");
    }

    private static void Apagar(ContextoDoAplicativo contexto)
    {
        if (!ConsoleUi.Confirmar("Apagar a chave guardada?"))
        {
            return;
        }

        contexto.Armazenamento.Apagar();
        contexto.RecarregarChave();
        ConsoleUi.Sucesso("Chave apagada.");
    }
}
