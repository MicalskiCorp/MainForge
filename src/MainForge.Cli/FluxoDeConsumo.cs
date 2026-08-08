using MainForge.ClaudeCode;
using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Cli;

/// <summary>
/// A tela de gasto de cota: quanto cada sistema e cada personagem já custaram, e a escolha de
/// quanto gastar daqui em diante.
///
/// <para><b>Por que as duas coisas juntas.</b> São a mesma pergunta em dois tempos — "para onde
/// foi minha cota?" e "quero que continue indo para lá?". O aplicativo inteiro é desenhado em
/// torno de gastar menos (converter os livros, indexar a base, retomar em vez de recomeçar), mas
/// até aqui o único sinal disso que o usuário via era a cota acabando no meio de um
/// processamento.</para>
/// </summary>
internal static class FluxoDeConsumo
{
    public static void Executar(ContextoDoAplicativo contexto)
    {
        MostrarGasto(contexto.Caminhos);
        EscolherPerfil(contexto);
    }

    private static void MostrarGasto(CaminhosDoProjeto caminhos)
    {
        ConsoleUi.Titulo("Cota consumida até agora");

        var total = ConsumoDeTokens.Zero;
        var algumaLinha = false;

        foreach (var sistema in SistemaRpg.DescobrirImportados(caminhos))
        {
            var consumo = EstadoDoProcessamento.Carregar(caminhos, sistema.Id).Consumo;

            if (consumo.Vazio)
            {
                continue;
            }

            algumaLinha = true;
            total += consumo;
            ConsoleUi.Info($"  sistema {sistema.Id}: {consumo.Descrever()}");
        }

        foreach (var personagem in RepositorioDePersonagens.Listar(caminhos))
        {
            if (personagem.Consumo.Vazio)
            {
                continue;
            }

            algumaLinha = true;
            total += personagem.Consumo;
            ConsoleUi.Detalhe($"  personagem {personagem.Rotulo} ({personagem.Sistema}): {personagem.Consumo.Descrever()}");
        }

        if (!algumaLinha)
        {
            ConsoleUi.Detalhe("Nada registrado ainda — a medição começa na próxima execução de agente.");
            return;
        }

        ConsoleUi.Info("");
        ConsoleUi.Sucesso($"Total: {total.Descrever()}.");

        if (total.CacheLido > 0)
        {
            ConsoleUi.Detalhe(
                $"{total.ProporcaoEmCache:P0} da entrada veio do cache do Claude Code — é o que retomar " +
                "uma conversa em vez de recomeçá-la economiza.");
        }

        ConsoleUi.Detalhe("O valor em dólares é o que a mesma conversa custaria pela API; na assinatura,");
        ConsoleUi.Detalhe("o que ele consome é a sua janela de uso.");
    }

    private static void EscolherPerfil(ContextoDoAplicativo contexto)
    {
        var preferencias = contexto.Preferencias;

        ConsoleUi.Titulo("Quanto gastar daqui em diante");
        ConsoleUi.Info($"Perfil atual: {AjusteDeExecucao.Descrever(preferencias.Perfil)}");
        ConsoleUi.Info("");
        MostrarOQueCadaAgenteUsa(preferencias.Perfil);

        var escolhido = ConsoleUi.Escolher(
            "Trocar o perfil?",
            new List<OpcaoDePerfil>
            {
                new(PerfilDeExecucao.Economico),
                new(PerfilDeExecucao.Equilibrado),
                new(PerfilDeExecucao.Qualidade),
            },
            opcao => AjusteDeExecucao.Descrever(opcao.Perfil));

        if (escolhido is null || escolhido.Perfil == preferencias.Perfil)
        {
            return;
        }

        preferencias.Perfil = escolhido.Perfil;
        contexto.AtualizarPreferencias(preferencias);

        ConsoleUi.Info("");
        ConsoleUi.Sucesso($"Perfil salvo em {PreferenciasDoUsuario.NomeDoArquivo}. Vale a partir da próxima operação.");
        MostrarOQueCadaAgenteUsa(escolhido.Perfil);
    }

    /// <summary>O menu escolhe entre objetos, e um enum não serve de tipo de referência.</summary>
    private sealed record OpcaoDePerfil(PerfilDeExecucao Perfil);

    /// <summary>
    /// O perfil traduzido em modelo e esforço, agente a agente. Sem isto "econômico" e
    /// "equilibrado" são adjetivos; com isto o usuário vê exatamente o que mudou.
    /// </summary>
    private static void MostrarOQueCadaAgenteUsa(PerfilDeExecucao perfil)
    {
        var extracao = AjusteDeExecucao.Resolver(perfil, NaturezaDoTrabalho.Extracao);
        var conversa = AjusteDeExecucao.Resolver(perfil, NaturezaDoTrabalho.Conversa);

        ConsoleUi.Detalhe($"  Configurador (lê os livros):  {extracao.Modelo}, esforço {extracao.Esforco}");
        ConsoleUi.Detalhe($"  Dungeon Master (conversa):    {conversa.Modelo}, esforço {conversa.Esforco}");
    }
}
