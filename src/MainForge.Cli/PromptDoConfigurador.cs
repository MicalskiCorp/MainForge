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
/// </summary>
internal static class PromptDoConfigurador
{
    public static string Montar(
        ModoDoConfigurador modo,
        SistemaRpg sistema,
        EstadoDoProcessamento estado,
        IReadOnlyList<string> livrosNovos) => modo switch
        {
            ModoDoConfigurador.Retomada => Retomada(sistema, estado),
            ModoDoConfigurador.Expansao => Expansao(sistema, estado, livrosNovos),
            _ => Completo(sistema),
        };

    private static string Completo(SistemaRpg sistema) => new StringBuilder()
        .AppendLine($"Processe o sistema '{sistema.Id}' do zero.")
        .AppendLine()
        .AppendLine($"1. Leia o(s) livro(s) em Systems/{sistema.Id}/ e a ficha em branco em Templates/{sistema.Id}/.")
        .AppendLine("2. Registre o plano de arquivos com registrar_plano_de_conhecimento antes de gravar o primeiro.")
        .AppendLine($"3. Gere a base completa em Knowledge/{sistema.Id}/, incluindo Ficha-Mapeamento.md e")
        .AppendLine("   Ficha-ModeloEmTexto.md, e descreva cada pasta com descrever_pasta_de_conhecimento.")
        .AppendLine()
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
        .AppendLine("Passos:")
        .AppendLine($"1. Comece pelos index.md em Knowledge/{sistema.Id}/ para ver o que a base já cobre.")
        .AppendLine("2. Gere os arquivos pendentes listados acima.")
        .AppendLine("3. Se perceber que falta algo que não está no plano, acrescente com")
        .AppendLine("   registrar_plano_de_conhecimento e gere também — inclusive Ficha-Mapeamento.md e")
        .AppendLine("   Ficha-ModeloEmTexto.md, se ainda não existirem.")
        .AppendLine()
        .AppendLine("Ao terminar, resuma o que completou e o que ainda ficou faltando.")
        .ToString();

    private static string Expansao(
        SistemaRpg sistema,
        EstadoDoProcessamento estado,
        IReadOnlyList<string> livrosNovos)
    {
        var texto = new StringBuilder()
            .AppendLine($"O sistema '{sistema.Id}' já tem base de conhecimento, e há livros dele que ainda")
            .AppendLine("não foram incorporados a ela. Some o conteúdo desses livros à base que já existe.")
            .AppendLine()
            .AppendLine("Pode ser um compêndio/expansão recém-adicionado, ou um livro básico cuja leitura")
            .AppendLine("foi interrompida antes do fim. Nos dois casos o trabalho é o mesmo: descobrir o que")
            .AppendLine("a base ainda não cobre e preencher só isso.")
            .AppendLine()
            .AppendLine("Livros a ler agora, e somente eles:");

        foreach (var livro in livrosNovos)
        {
            texto.AppendLine($"- Systems/{sistema.Id}/{livro}");
        }

        return texto
            .AppendLine()
            .AppendLine("Regras desta operação:")
            .AppendLine($"- Comece pelos index.md de Knowledge/{sistema.Id}/: eles dizem o que a base já cobre.")
            .AppendLine("  Leia dos livros só as partes que preenchem as lacunas que você identificar.")
            .AppendLine("- NÃO releia os livros marcados como já lidos e NÃO regenere a base do zero.")
            .AppendLine("- Conteúdo que a base ainda não tem vira arquivo novo, na pasta que já corresponde")
            .AppendLine("  àquele tipo de conteúdo.")
            .AppendLine("- Conteúdo que altera algo existente entra no arquivo existente, marcado com a")
            .AppendLine("  origem quando vier de um compêndio (ex.: \"(Compêndio X)\"), sem apagar o que já")
            .AppendLine("  estava lá. Leia o arquivo antes de sobrescrevê-lo — a gravação substitui o")
            .AppendLine("  conteúdo inteiro.")
            .AppendLine("- Se aparecer um tipo de conteúdo que a base ainda não tem, crie a pasta")
            .AppendLine("  correspondente e descreva-a com descrever_pasta_de_conhecimento.")
            .AppendLine("- Registre antes, com registrar_plano_de_conhecimento, os arquivos que pretende")
            .AppendLine("  criar ou atualizar por causa desses livros.")
            .AppendLine("- Se Ficha-Mapeamento.md ou Ficha-ModeloEmTexto.md ainda não existirem, gere-os.")
            .AppendLine()
            .AppendLine("Situação registrada:")
            .AppendLine()
            .AppendLine(estado.DescreverParaOAgente())
            .AppendLine("Ao terminar, resuma o que foi acrescentado e o que foi alterado.")
            .ToString();
    }
}
