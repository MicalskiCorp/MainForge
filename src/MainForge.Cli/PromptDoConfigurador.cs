using System.Text;
using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Cli;

/// <summary>Em que situação o Configurador está sendo chamado.</summary>
internal enum ModoDoConfigurador
{
    /// <summary>Sistema novo (ou recomeçado do zero): ler tudo e gerar a base inteira.</summary>
    Completo,

    /// <summary>Já existe base, interrompida no meio: continuar do ponto em que parou.</summary>
    Retomada,

    /// <summary>
    /// Base já existente e livros ainda não incorporados a ela — um compêndio recém-adicionado
    /// ou um livro básico cuja leitura parou no meio. Nos dois casos: ler só esses livros e
    /// somar ao que já está pronto.
    /// </summary>
    Expansao,
}

/// <summary>
/// Monta a mensagem que abre o turno do Configurador. As três situações pedem instruções
/// bem diferentes, e a diferença entre elas é justamente o que evita o desperdício: numa
/// retomada, mandar "gere a base do sistema" faria o agente reler o livro e reescrever tudo
/// que já estava pronto.
///
/// <para>O estado do processamento vai dentro da mensagem, e não só pela ferramenta
/// <c>consultar_progresso</c>, porque o agente precisa saber o que fazer <em>antes</em> de
/// decidir chamar ferramenta nenhuma.</para>
///
/// <para>Em todos os modos a mensagem diz, livro a livro, em que pasta de fonte o conteúdo
/// deve cair. Essa correspondência não é decorativa: é ela que permite ao usuário, depois,
/// dizer que aquela mesa não usa determinado compêndio.</para>
/// </summary>
internal static class PromptDoConfigurador
{
    public static string Montar(
        ModoDoConfigurador modo,
        SistemaRpg sistema,
        EstadoDoProcessamento estado) => modo switch
        {
            ModoDoConfigurador.Retomada => Retomada(sistema, estado),
            ModoDoConfigurador.Expansao => Expansao(sistema, estado),
            _ => Completo(sistema, estado),
        };

    private static string Completo(SistemaRpg sistema, EstadoDoProcessamento estado) => new StringBuilder()
        .AppendLine($"Processe o sistema '{sistema.Id}' do zero.")
        .AppendLine()
        .AppendLine($"1. Leia os livros em Systems/{sistema.Id}/ e a ficha em branco em Templates/{sistema.Id}/.")
        .AppendLine("2. Registre o plano de arquivos com registrar_plano_de_conhecimento antes de gravar o primeiro.")
        .AppendLine($"3. Gere a base em Knowledge/{sistema.Id}/, respeitando a separação por fonte descrita abaixo,")
        .AppendLine("   e descreva cada pasta com descrever_pasta_de_conhecimento.")
        .AppendLine()
        .Append(SeparacaoPorFonte(sistema, estado))
        .AppendLine("Ao terminar, resuma o que criou e o que ficou de fora.")
        .ToString();

    private static string Retomada(SistemaRpg sistema, EstadoDoProcessamento estado) => new StringBuilder()
        .AppendLine($"Continue o processamento do sistema '{sistema.Id}', que foi interrompido.")
        .AppendLine()
        .AppendLine("NÃO recomece do zero e NÃO regrave arquivos que já estão prontos: reescrever o que já")
        .AppendLine("existe gasta a cota da assinatura à toa e pode piorar um conteúdo que já estava certo.")
        .AppendLine("Leia dos livros apenas as partes que faltam para produzir os arquivos pendentes.")
        .AppendLine()
        .AppendLine("Situação registrada:")
        .AppendLine()
        .AppendLine(estado.DescreverParaOAgente())
        .Append(SeparacaoPorFonte(sistema, estado))
        .AppendLine("Passos:")
        .AppendLine($"1. Comece pelos index.md em Knowledge/{sistema.Id}/ para ver o que a base já cobre.")
        .AppendLine("2. Gere os arquivos pendentes listados acima.")
        .AppendLine("3. Se perceber que falta algo que não está no plano, acrescente com")
        .AppendLine("   registrar_plano_de_conhecimento e gere também — inclusive os arquivos da ficha,")
        .AppendLine("   se ainda não existirem.")
        .AppendLine()
        .AppendLine("Ao terminar, resuma o que completou e o que ainda ficou faltando.")
        .ToString();

    private static string Expansao(SistemaRpg sistema, EstadoDoProcessamento estado)
    {
        var texto = new StringBuilder()
            .AppendLine($"O sistema '{sistema.Id}' já tem base de conhecimento, e há livros dele que ainda")
            .AppendLine("não foram incorporados a ela. Some o conteúdo desses livros à base que já existe.")
            .AppendLine()
            .AppendLine("Pode ser um compêndio/expansão recém-adicionado, ou um livro básico cuja leitura")
            .AppendLine("foi interrompida antes do fim. Nos dois casos o trabalho é o mesmo: descobrir o que")
            .AppendLine("a base ainda não cobre e preencher só isso.")
            .AppendLine()
            .AppendLine("Livros a ler agora, e somente eles, com o destino do que sair de cada um:");

        foreach (var (fonte, livros) in estado.PendentesPorFonte())
        {
            texto.AppendLine();
            texto.AppendLine($"  {fonte.Rotulo} — o conteúdo vai para Knowledge/{sistema.Id}/{fonte.Id}/");

            foreach (var livro in livros)
            {
                texto.AppendLine($"    - Systems/{sistema.Id}/{fonte.Id}/{livro}");
            }
        }

        return texto
            .AppendLine()
            .Append(SeparacaoPorFonte(sistema, estado))
            .AppendLine("Regras desta operação:")
            .AppendLine($"- Comece pelos index.md de Knowledge/{sistema.Id}/: eles dizem o que a base já cobre.")
            .AppendLine("  Leia dos livros só as partes que preenchem as lacunas que você identificar.")
            .AppendLine("- NÃO releia os livros marcados como já lidos e NÃO regenere a base do zero.")
            .AppendLine("- Conteúdo de expansão NUNCA entra num arquivo de outra fonte. Se o compêndio muda")
            .AppendLine("  uma regra que já existe na base, o arquivo novo, dentro da pasta da expansão, diz")
            .AppendLine("  o que muda e cita o arquivo da base — não reescreva o arquivo da base para isso.")
            .AppendLine("- Se a expansão trouxer um tipo de conteúdo que ela ainda não tem, crie a pasta")
            .AppendLine("  correspondente dentro dela e descreva-a com descrever_pasta_de_conhecimento.")
            .AppendLine("- Registre antes, com registrar_plano_de_conhecimento, os arquivos que pretende criar.")
            .AppendLine("- Se os arquivos da ficha ainda não existirem, gere-os.")
            .AppendLine()
            .AppendLine("Situação registrada:")
            .AppendLine()
            .AppendLine(estado.DescreverParaOAgente())
            .AppendLine("Ao terminar, resuma o que foi acrescentado e de qual fonte veio cada coisa.")
            .ToString();
    }

    /// <summary>
    /// O bloco que explica o mapa de pastas. Repetido nos três modos de propósito: é a regra
    /// que o agente mais tem chance de quebrar, porque a estrutura por fonte não se deduz do
    /// conteúdo dos livros — ela vem de como o usuário importou cada um.
    /// </summary>
    private static string SeparacaoPorFonte(SistemaRpg sistema, EstadoDoProcessamento estado)
    {
        var texto = new StringBuilder()
            .AppendLine("Separação por fonte (obrigatória):")
            .AppendLine()
            .AppendLine($"Cada pasta em Systems/{sistema.Id}/ é uma fonte — o jogo base ou uma expansão — e o")
            .AppendLine($"conteúdo que sair dos livros dela vai para a pasta de mesmo nome em Knowledge/{sistema.Id}/.")
            .AppendLine("O usuário escolhe, na hora de criar um personagem, quais expansões aquela mesa usa; o")
            .AppendLine("que estiver na pasta errada vai valer numa mesa que não deveria, ou sumir de uma que")
            .AppendLine("deveria. Um arquivo de uma fonte pode citar outro de outra fonte, mas nunca copiar o")
            .AppendLine("conteúdo dele.")
            .AppendLine();

        var fontes = estado.Livros
            .Select(livro => livro.Fonte)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => new FonteDoSistema(id));

        foreach (var fonte in FonteDoSistema.Ordenar(fontes))
        {
            texto.AppendLine($"- Systems/{sistema.Id}/{fonte.Id}/  ->  Knowledge/{sistema.Id}/{fonte.Id}/");
        }

        return texto
            .AppendLine()
            .AppendLine($"As duas exceções são os arquivos da ficha, que valem para o sistema inteiro e ficam na")
            .AppendLine($"raiz de Knowledge/{sistema.Id}/, fora de qualquer pasta de fonte:")
            .AppendLine($"{string.Join(" e ", SistemaRpg.ArquivosDaFicha)}.")
            .AppendLine()
            .ToString();
    }
}
