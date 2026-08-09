using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Cli;

/// <summary>
/// Mostra a ficha de um personagem pronto em texto — o mesmo desenho que o Dungeon Master exibe
/// na conversa antes de gerar o PDF — e garante, no mesmo gesto, que o PDF dela está em
/// <c>Output/Personagens/</c>.
///
/// <para><b>Por que as duas coisas juntas.</b> São a mesma pergunta: "como está a ficha deste
/// personagem?". Quem abre esta tela quer ver o personagem e quer o arquivo para levar à mesa; e
/// é justamente aqui que dá para descobrir de graça que o PDF sumiu — o dossiê sobrevive a
/// <c>Output/</c> ser esvaziada, e até agora a única forma de refazer o arquivo era abrir uma
/// conversa e pagar cota para o agente reescrever valores que já estavam gravados no disco.</para>
///
/// <para><b>Nada disto consome cota.</b> O desenho vem do <c>Ficha-ModeloEmTexto.md</c> do
/// sistema e o PDF, da ficha em branco em <c>Templates/</c>; os valores são os que o dossiê já
/// guarda. Nenhum agente é chamado, e por isso a tela também funciona com a assinatura esgotada
/// ou sem o Claude Code instalado.</para>
/// </summary>
internal static class FluxoDeFichaDoPersonagem
{
    public static void Executar(ContextoDoAplicativo contexto, IReadOnlyList<Personagem> personagens)
    {
        var prontos = personagens
            .Where(personagem => personagem.Status == StatusDoPersonagem.Concluido)
            .ToList();

        if (prontos.Count == 0)
        {
            ConsoleUi.Info("Nenhum personagem concluído ainda.");
            ConsoleUi.Detalhe("Um personagem passa a 'concluído' quando a ficha em PDF dele é gerada.");
            return;
        }

        var escolhido = ConsoleUi.Escolher("Ver a ficha de qual personagem?", prontos, MenuDePersonagens.Descrever);

        if (escolhido is null)
        {
            return;
        }

        MostrarDesenho(contexto.Caminhos, escolhido);
        GarantirPdf(contexto.Caminhos, escolhido);
    }

    /// <summary>
    /// A ficha desenhada com os valores do personagem. Sem modelo em texto no sistema, ou sem
    /// campos guardados, o que sobra é o dossiê que o agente escreveu — pior que o desenho, mas
    /// é o estado do personagem, que é o que se veio ver.
    /// </summary>
    private static void MostrarDesenho(CaminhosDoProjeto caminhos, Personagem personagem)
    {
        if (personagem.Campos.Count == 0)
        {
            ConsoleUi.Info("");
            ConsoleUi.Aviso("O dossiê deste personagem não tem os valores dos campos da ficha.");
            ConsoleUi.Detalhe("Ele foi concluído por uma versão do aplicativo que não os guardava, ou por um");
            ConsoleUi.Detalhe("caminho que não passou pela geração do PDF. O que existe dele está abaixo.");
            MostrarDossie(caminhos, personagem);
            return;
        }

        var desenho = FichaEmTexto.Montar(caminhos, personagem);

        if (desenho is not { Length: > 0 })
        {
            ConsoleUi.Info("");
            ConsoleUi.Aviso($"{personagem.Sistema} não tem {SistemaRpg.NomeDoModeloEmTexto} — sem ele não há");
            ConsoleUi.Aviso("desenho da ficha para exibir. Reprocessar o sistema no menu 'Sistemas' o produz.");
            MostrarDossie(caminhos, personagem);
            return;
        }

        ConsoleUi.Titulo($"Ficha de {personagem.Rotulo} ({personagem.Sistema})");
        ConsoleUi.Info("");

        foreach (var linha in desenho.Split('\n'))
        {
            ConsoleUi.Info(linha);
        }
    }

    private static void MostrarDossie(CaminhosDoProjeto caminhos, Personagem personagem)
    {
        var texto = RepositorioDePersonagens.LerFichaEmTexto(caminhos, personagem.Sistema, personagem.Id);

        if (texto.Length == 0)
        {
            return;
        }

        ConsoleUi.Titulo($"Dossiê de {personagem.Rotulo}");
        ConsoleUi.Info("");

        foreach (var linha in texto.ReplaceLineEndings("\n").Split('\n'))
        {
            ConsoleUi.Detalhe(linha);
        }
    }

    /// <summary>
    /// Confere se a ficha em PDF está em <c>Output/</c> e a gera se não estiver. Só avisa quando
    /// há novidade: encontrar o arquivo onde ele deveria estar é o caso normal, e anunciá-lo a
    /// cada visualização ensinaria o usuário a ignorar as mensagens desta tela.
    /// </summary>
    private static void GarantirPdf(CaminhosDoProjeto caminhos, Personagem personagem)
    {
        var resultado = GeradorDeFichaEmPdf.Garantir(caminhos, personagem);

        ConsoleUi.Info("");

        switch (resultado.Situacao)
        {
            case SituacaoDaFichaEmPdf.JaExistia:
                ConsoleUi.Detalhe($"Ficha em PDF: {resultado.Caminho}");
                break;

            case SituacaoDaFichaEmPdf.Gerada:
                ConsoleUi.Sucesso($"A ficha em PDF não estava em Output/ — gerei uma agora: {resultado.Caminho}");
                break;

            default:
                ConsoleUi.Aviso("Não há ficha em PDF deste personagem em Output/, e não consegui gerar uma:");
                ConsoleUi.Detalhe($"  {resultado.Motivo}");
                break;
        }

        if (resultado.CamposIgnorados.Count > 0)
        {
            ConsoleUi.Detalhe(
                $"{resultado.CamposIgnorados.Count} campo(s) do dossiê não existem na ficha em branco de " +
                $"{personagem.Sistema} e ficaram fora do PDF: {string.Join(", ", resultado.CamposIgnorados.Take(10))}");
        }
    }
}
