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
    /// <param name="podeAbrirPdf">
    /// Se o <c>Read</c> consegue abrir PDF nesta máquina (depende do poppler). Quando não
    /// consegue, o prompt diz isso: sem o aviso, o agente gasta um turno por livro descobrindo
    /// sozinho que o caminho não existe.
    /// </param>
    public static string Montar(
        ModoDoConfigurador modo,
        SistemaRpg sistema,
        EstadoDoProcessamento estado,
        CaminhosDoProjeto caminhos,
        bool podeAbrirPdf = true)
    {
        var mensagem = modo switch
        {
            ModoDoConfigurador.Retomada => Retomada(sistema, estado, caminhos),
            ModoDoConfigurador.Expansao => Expansao(sistema, estado, caminhos),
            _ => Completo(sistema, estado, caminhos),
        };

        return podeAbrirPdf ? mensagem : mensagem + SemLeituraDePdf();
    }

    /// <summary>
    /// O aviso de que o PDF está fora de alcance nesta execução. Vem no fim da mensagem, depois
    /// das instruções, porque é uma restrição do ambiente e não parte da tarefa.
    /// </summary>
    private static string SemLeituraDePdf() => new StringBuilder()
        .AppendLine()
        .AppendLine("IMPORTANTE — nesta máquina o Read NÃO abre PDF: falta o programa que o Claude Code usa")
        .AppendLine("para rasterizar as páginas (pdftoppm, do poppler). A leitura dos PDFs de Input/ está")
        .AppendLine("negada por isso, e vai ser recusada se você tentar.")
        .AppendLine()
        .AppendLine("O texto convertido é o caminho completo, não um atalho: trabalhe só por ele. Se uma")
        .AppendLine("tabela vier embaralhada na conversão, reconstitua o que der pelo texto ao redor e diga")
        .AppendLine("no resumo final o que ficou duvidoso — não é para tentar o PDF, e não é para inventar")
        .AppendLine("a regra que faltou.")
        .ToString();

    private static string Completo(SistemaRpg sistema, EstadoDoProcessamento estado, CaminhosDoProjeto caminhos) =>
        new StringBuilder()
        .AppendLine($"Processe o sistema '{sistema.Id}' do zero.")
        .AppendLine()
        .AppendLine($"1. Leia os livros do sistema (lista abaixo) e a ficha em branco em Templates/{sistema.Id}/.")
        .AppendLine("2. Registre o plano de arquivos com registrar_plano_de_conhecimento antes de gravar o primeiro.")
        .AppendLine($"3. Gere a base em Sistemas/{sistema.Id}/, respeitando a separação por fonte descrita abaixo,")
        .AppendLine("   e descreva cada pasta com descrever_pasta_de_conhecimento.")
        .AppendLine()
        .Append(OndeLerCadaLivro(sistema, estado, caminhos))
        .Append(SeparacaoPorFonte(sistema, estado))
        .AppendLine("Ao terminar, resuma o que criou e o que ficou de fora.")
        .ToString();

    private static string Retomada(SistemaRpg sistema, EstadoDoProcessamento estado, CaminhosDoProjeto caminhos) =>
        new StringBuilder()
        .AppendLine($"Continue o processamento do sistema '{sistema.Id}', que foi interrompido.")
        .AppendLine()
        .AppendLine("NÃO recomece do zero e NÃO regrave arquivos que já estão prontos: reescrever o que já")
        .AppendLine("existe gasta a cota da assinatura à toa e pode piorar um conteúdo que já estava certo.")
        .AppendLine("Leia dos livros apenas as partes que faltam para produzir os arquivos pendentes.")
        .AppendLine()
        .AppendLine("Situação registrada:")
        .AppendLine()
        .AppendLine(estado.DescreverParaOAgente())
        .Append(OndeLerCadaLivro(sistema, estado, caminhos))
        .Append(SeparacaoPorFonte(sistema, estado))
        .AppendLine("Passos:")
        .AppendLine($"1. Comece pelos index.md em Sistemas/{sistema.Id}/ para ver o que a base já cobre.")
        .AppendLine("2. Gere os arquivos pendentes listados acima.")
        .AppendLine("3. Se perceber que falta algo que não está no plano, acrescente com")
        .AppendLine("   registrar_plano_de_conhecimento e gere também — inclusive os arquivos da ficha,")
        .AppendLine("   se ainda não existirem.")
        .AppendLine()
        .AppendLine("Ao terminar, resuma o que completou e o que ainda ficou faltando.")
        .ToString();

    private static string Expansao(SistemaRpg sistema, EstadoDoProcessamento estado, CaminhosDoProjeto caminhos)
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
            texto.AppendLine($"  {fonte.Rotulo} — o conteúdo vai para Sistemas/{sistema.Id}/{fonte.Id}/");

            foreach (var livro in livros)
            {
                texto.AppendLine($"    - {CaminhoDeLeitura(sistema, fonte.Id, livro, caminhos)}");
            }
        }

        return texto
            .AppendLine()
            .Append(OndeLerCadaLivro(sistema, estado, caminhos))
            .Append(SeparacaoPorFonte(sistema, estado))
            .AppendLine("Regras desta operação:")
            .AppendLine($"- Comece pelos index.md de Sistemas/{sistema.Id}/: eles dizem o que a base já cobre.")
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
    /// Diz, livro a livro, qual arquivo abrir: o Markdown extraído do PDF quando ele existe, o
    /// próprio PDF quando não.
    ///
    /// <para>É o bloco que faz a conversão valer alguma coisa. O agente não tem como adivinhar
    /// que existe uma versão em texto do livro — <c>Glob</c> em <c>*.pdf</c> não a mostra — e ler
    /// o PDF quando há Markdown ao lado é pagar em cota por páginas rasterizadas para chegar ao
    /// mesmo conteúdo.</para>
    ///
    /// <para>A lista vem do registro de livros, e não do disco, porque é ela que o resto do prompt
    /// usa: livro que o progresso não conhece não seria lido de qualquer forma.</para>
    /// </summary>
    private static string OndeLerCadaLivro(
        SistemaRpg sistema,
        EstadoDoProcessamento estado,
        CaminhosDoProjeto caminhos)
    {
        if (estado.Livros.Count == 0)
        {
            return "";
        }

        var texto = new StringBuilder()
            .AppendLine("Onde ler cada livro:")
            .AppendLine();

        var algumTexto = false;

        foreach (var livro in estado.Livros)
        {
            var caminho = CaminhoDeLeitura(sistema, livro.Fonte, livro.Arquivo, caminhos);

            algumTexto |= caminho.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
            texto.AppendLine($"- [{livro.Fonte}] {caminho}");
        }

        texto.AppendLine();

        if (algumTexto)
        {
            // O porquê de ler o .md e como navegar nele estão no prompt de sistema, que é cacheado.
            // Aqui fica só o lembrete curto, porque a lista de caminhos acima é o gatilho dele.
            texto
                .AppendLine("Os .md acima são o texto extraído dos PDFs — leia-os, não os PDFs. Comece por")
                .AppendLine("estrutura_do_livro para ter o sumário, e use procurar_no_texto_dos_livros com")
                .AppendLine("'contexto' para receber o trecho sem uma leitura a mais.")
                .AppendLine();
        }

        return texto.ToString();
    }

    /// <summary>
    /// O caminho que o agente deve abrir para um livro: o texto convertido, se estiver em dia com
    /// o PDF, ou o PDF.
    /// </summary>
    private static string CaminhoDeLeitura(
        SistemaRpg sistema,
        string fonte,
        string livro,
        CaminhosDoProjeto caminhos)
    {
        var pdf = Path.Combine(sistema.DiretorioEntrada(caminhos), fonte, livro);
        var convertido = ConversorDeLivros.TextoAtualizadoDe(pdf);

        return convertido is null
            ? $"Input/{sistema.Id}/{fonte}/{livro}"
            : Path.GetRelativePath(caminhos.Raiz, convertido).Replace('\\', '/');
    }

    /// <summary>
    /// O mapa de pastas deste sistema: de qual pasta de <c>Input/</c> sai o conteúdo de qual
    /// pasta de <c>Sistemas/</c>.
    ///
    /// <para><b>Só o mapa, e não a regra.</b> Por que a separação por fonte existe e o que
    /// acontece ao quebrá-la está escrito no <c>Agents/Configurador.md</c>, que é o prompt de
    /// sistema — enviado uma vez e reaproveitado pelo cache em todos os turnos. Repetir a
    /// explicação aqui, na mensagem do usuário, era pagar por ela de novo a cada execução para
    /// dizer o que o agente já tinha lido. O que precisa estar aqui é o que muda de sistema para
    /// sistema: a lista concreta de fontes.</para>
    /// </summary>
    private static string SeparacaoPorFonte(SistemaRpg sistema, EstadoDoProcessamento estado)
    {
        var texto = new StringBuilder()
            .AppendLine("Separação por fonte (obrigatória) — o destino do conteúdo de cada pasta:")
            .AppendLine();

        var fontes = estado.Livros
            .Select(livro => livro.Fonte)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => new FonteDoSistema(id));

        foreach (var fonte in FonteDoSistema.Ordenar(fontes))
        {
            texto.AppendLine($"- Input/{sistema.Id}/{fonte.Id}/  ->  Sistemas/{sistema.Id}/{fonte.Id}/");
        }

        return texto
            .AppendLine()
            .AppendLine($"Exceção: os arquivos da ficha ficam na raiz de Sistemas/{sistema.Id}/ —")
            .AppendLine($"{string.Join(", ", SistemaRpg.ArquivosDaFicha)} e {SistemaRpg.NomeDaValidacaoDaFicha}")
            .AppendLine($"(este último não é gravado por você: use registrar_validacao_da_ficha).")
            .AppendLine()
            .ToString();
    }
}
