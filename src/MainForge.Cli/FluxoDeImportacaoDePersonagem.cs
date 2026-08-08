using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Cli;

/// <summary>
/// Traz para o aplicativo um personagem que já existe numa ficha em PDF preenchida: o usuário
/// informa o caminho do arquivo e ele vira um personagem igual aos outros — com dossiê, com
/// fontes de mesa, e pronto para evoluir, corrigir e gerar ficha de novo.
///
/// <para><b>Por que esta porta existe.</b> Quem chega ao MainForge já joga, e já tem personagem
/// preenchido no PDF editável do sistema. Sem ela, usar o aplicativo com esse personagem
/// significava ditá-lo inteiro numa conversa — cota gasta para o agente redescobrir escolhas que
/// já estavam decididas, com o risco de sair diferente do que está na mesa.</para>
///
/// <para><b>A leitura do PDF não custa cota nenhuma</b> (é AcroForm, em C#), e a conversa de
/// conferência que vem depois é opcional e separada: importar sempre funciona, mesmo sem Claude
/// Code instalado ou com a cota esgotada.</para>
/// </summary>
internal static class FluxoDeImportacaoDePersonagem
{
    public static async Task ExecutarAsync(ContextoDoAplicativo contexto, CancellationToken cancelamento)
    {
        var caminhos = contexto.Caminhos;

        ConsoleUi.Titulo("Importar personagem de uma ficha em PDF");
        ConsoleUi.Info("A ficha precisa ser o PDF editável do sistema, com os campos preenchidos —");
        ConsoleUi.Info("é de lá que os valores são lidos. PDF digitalizado ou achatado não serve.");

        var ficha = LerFicha();

        if (ficha is null)
        {
            return;
        }

        ConsoleUi.Info("");
        ConsoleUi.Sucesso($"Li {ficha.Valores.Count} campo(s) preenchido(s) de {ficha.TotalDeCampos} na ficha.");
        MostrarAmostra(ficha);

        var sistema = EscolherSistema(caminhos, ficha);

        if (sistema is null)
        {
            return;
        }

        var fontes = FluxoDePersonagem.PerguntarFontes(caminhos, sistema);

        if (fontes is null)
        {
            return;
        }

        var nome = PerguntarNome(ficha);

        if (nome.Length == 0)
        {
            return;
        }

        ResultadoDaImportacaoDePersonagem resultado;

        try
        {
            resultado = ImportadorDePersonagem.Importar(
                caminhos,
                sistema,
                nome,
                [.. fontes.Escolhidas.Select(fonte => fonte.Id)],
                ficha);
        }
        catch (Exception excecao) when (excecao is IOException or UnauthorizedAccessException)
        {
            ConsoleUi.Erro($"Não consegui gravar o personagem: {excecao.Message}");
            return;
        }

        Relatar(caminhos, sistema, resultado);

        await OferecerConferenciaAsync(contexto, resultado.Personagem, sistema, cancelamento);
    }

    /// <summary>
    /// Pede o caminho até vir um PDF que abra e tenha formulário. Insiste em vez de voltar ao
    /// menu porque o erro mais comum aqui é de digitação, e porque a diferença entre "não achei o
    /// arquivo" e "este PDF não tem campos" muda o que o usuário faz a seguir.
    /// </summary>
    private static FichaPreenchida? LerFicha()
    {
        while (true)
        {
            ConsoleUi.Info("");
            ConsoleUi.Detalhe("Enter numa linha vazia volta ao menu.");
            var caminho = EntradaDeArquivos.Limpar(ConsoleUi.LerLinha("Caminho do PDF da ficha: "));

            if (caminho.Length == 0)
            {
                return null;
            }

            try
            {
                return LeitorDeFichaPreenchida.Ler(caminho);
            }
            catch (Exception excecao) when (excecao is FileNotFoundException or InvalidOperationException or IOException)
            {
                ConsoleUi.Erro(excecao.Message);
            }
        }
    }

    /// <summary>
    /// Mostra alguns valores lidos. É a conferência que custa um segundo e evita a pior saída
    /// desta tela: importar a ficha errada — a do outro personagem, a versão antiga do arquivo —
    /// e só descobrir isso no meio de uma conversa, depois de gastar cota.
    /// </summary>
    private static void MostrarAmostra(FichaPreenchida ficha)
    {
        var amostra = ficha.Valores
            .Where(par => !par.Value.Contains('\n'))
            .Take(8)
            .ToList();

        if (amostra.Count == 0)
        {
            return;
        }

        ConsoleUi.Info("");

        foreach (var (campo, valor) in amostra)
        {
            ConsoleUi.Detalhe($"  {Encurtar(campo, 28),-28} {Encurtar(valor, 44)}");
        }

        if (ficha.Valores.Count > amostra.Count)
        {
            ConsoleUi.Detalhe($"  ... e mais {ficha.Valores.Count - amostra.Count} campo(s).");
        }
    }

    /// <summary>
    /// Deixa o usuário escolher o sistema, com os mais parecidos com a ficha primeiro.
    ///
    /// <para>A ficha diz de que sistema é: os nomes dos campos de um AcroForm são os da ficha em
    /// branco de onde ele saiu. Sugerir é útil e decidir sozinho não seria — uma ficha adaptada,
    /// ou dois sistemas que compartilham o modelo, dariam um palpite errado que o usuário não
    /// teria como corrigir.</para>
    /// </summary>
    private static SistemaRpg? EscolherSistema(CaminhosDoProjeto caminhos, FichaPreenchida ficha)
    {
        var candidatos = ImportadorDePersonagem.Ranquear(caminhos, ficha);

        if (candidatos.Count == 0)
        {
            ConsoleUi.Info("");
            ConsoleUi.Aviso("Nenhum sistema importado ainda.");
            ConsoleUi.Info("Importe o sistema desta ficha no menu 'Sistemas' antes — é a base de conhecimento");
            ConsoleUi.Info("dele que o Dungeon Master usa para evoluir o personagem depois.");
            return null;
        }

        if (candidatos[0].Reconhecida)
        {
            ConsoleUi.Info("");
            ConsoleUi.Sucesso($"Esta ficha parece ser a de '{candidatos[0].Sistema.Id}'.");
        }

        var escolhido = ConsoleUi.Escolher(
            "De qual sistema é este personagem?",
            candidatos,
            candidato => Descrever(caminhos, candidato));

        return escolhido?.Sistema;
    }

    private static string Descrever(CaminhosDoProjeto caminhos, SistemaCompativel candidato)
    {
        var detalhes = new List<string>();

        if (candidato.CamposDoModelo == 0)
        {
            detalhes.Add("sem ficha em branco em Templates/ — não dá para gerar o PDF depois");
        }
        else
        {
            detalhes.Add($"{candidato.CamposEmComum} de {candidato.CamposDoModelo} campos da ficha em branco");
        }

        if (!candidato.Sistema.TemConhecimento(caminhos))
        {
            detalhes.Add("sem base de conhecimento — processe os livros para poder evoluir");
        }

        return $"{candidato.Sistema.Id}  ({string.Join("; ", detalhes)})";
    }

    private static string PerguntarNome(FichaPreenchida ficha)
    {
        var sugestao = ImportadorDePersonagem.SugerirNome(ficha);

        ConsoleUi.Info("");
        ConsoleUi.Detalhe("O nome vira a pasta do personagem e pode mudar depois, na conversa.");

        var rotulo = sugestao.Length > 0
            ? $"Nome do personagem [{sugestao}]: "
            : "Nome (ou apelido) do personagem: ";

        var digitado = ConsoleUi.LerLinha(rotulo);

        if (digitado.Length > 0)
        {
            return digitado;
        }

        if (sugestao.Length > 0)
        {
            return sugestao;
        }

        ConsoleUi.Aviso("Sem nome não dá para criar a pasta do personagem.");
        return "";
    }

    private static void Relatar(
        CaminhosDoProjeto caminhos,
        SistemaRpg sistema,
        ResultadoDaImportacaoDePersonagem resultado)
    {
        var personagem = resultado.Personagem;

        ConsoleUi.Info("");
        ConsoleUi.Sucesso($"'{personagem.Rotulo}' importado em {sistema.Id}.");
        ConsoleUi.Detalhe($"Dossiê: Personagens/{sistema.Id}/{personagem.Id}/");
        ConsoleUi.Detalhe($"Cópia da ficha: {resultado.FichaCopiada}");
        ConsoleUi.Detalhe($"{resultado.CamposAproveitados.Count} campo(s) guardados para a próxima geração da ficha.");

        if (resultado.CamposForaDoModelo.Count > 0)
        {
            ConsoleUi.Info("");
            ConsoleUi.Aviso($"{resultado.CamposForaDoModelo.Count} campo(s) preenchidos não existem na ficha em branco de {sistema.Id}:");
            ConsoleUi.Detalhe($"  {string.Join(", ", resultado.CamposForaDoModelo.Keys.Take(10))}");
            ConsoleUi.Detalhe("Eles ficam registrados no dossiê, mas fora da geração do PDF — a ficha trazida");
            ConsoleUi.Detalhe("pode ser de outra edição ou de uma versão adaptada.");
        }

        if (ImportadorDePersonagem.CamposDoModelo(caminhos, sistema).Count == 0)
        {
            ConsoleUi.Info("");
            ConsoleUi.Aviso($"{sistema.Id} não tem ficha em branco em Templates/ — o personagem existe, mas não há");
            ConsoleUi.Aviso("como gerar o PDF dele de novo até você importar a ficha do sistema.");
        }
    }

    /// <summary>
    /// Oferece a conversa que transforma valores transcritos em personagem conhecido: o agente
    /// lê o que veio do PDF, confere contra as regras da mesa e reescreve o dossiê.
    ///
    /// <para><b>Por que é oferta, e não parte da importação.</b> É a única etapa que gasta cota, e
    /// o valor dela depende do que o usuário vai fazer em seguida — quem importou só para guardar
    /// o personagem não precisa dela agora, e ela continua disponível depois em "evoluir ou
    /// alterar". Emendá-la automaticamente cobraria de todo mundo o preço de uma conferência que
    /// nem sempre é o próximo passo.</para>
    /// </summary>
    private static async Task OferecerConferenciaAsync(
        ContextoDoAplicativo contexto,
        Personagem personagem,
        SistemaRpg sistema,
        CancellationToken cancelamento)
    {
        ConsoleUi.Info("");

        if (!sistema.TemConhecimento(contexto.Caminhos))
        {
            ConsoleUi.Detalhe($"Assim que os livros de {sistema.Id} forem processados, dá para conferir esta ficha");
            ConsoleUi.Detalhe("contra as regras em 'Evoluir ou alterar um pronto'.");
            return;
        }

        ConsoleUi.Info("O que está no dossiê agora é a transcrição do PDF, sem nenhuma conferência.");
        ConsoleUi.Info("O Dungeon Master pode ler esses valores, conferi-los contra as regras da mesa e");
        ConsoleUi.Info("reescrever o dossiê — é o que faz as evoluções seguintes partirem de algo confiável.");
        ConsoleUi.Detalhe("Isso consome cota da sua assinatura. Dá para fazer depois, pelo menu.");
        ConsoleUi.Info("");

        if (!ConsoleUi.Confirmar("Conferir a ficha com o Dungeon Master agora?"))
        {
            return;
        }

        await FluxoDePersonagem.ExecutarAsync(
            contexto,
            personagem,
            ModoDaConversa.Conferencia,
            null,
            cancelamento);
    }

    private static string Encurtar(string texto, int largura)
    {
        var linha = texto.ReplaceLineEndings(" ").Trim();

        return linha.Length <= largura ? linha : string.Concat(linha.AsSpan(0, largura - 3), "...");
    }
}
