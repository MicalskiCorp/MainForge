using System.Text.RegularExpressions;
using MainForge.Core;

namespace MainForge.Tools;

/// <summary>Por que um campo do modelo em texto não confere com a ficha em PDF.</summary>
public enum TipoDeDivergencia
{
    /// <summary>O modelo usa um campo que não existe no formulário da ficha.</summary>
    CampoInexistente,

    /// <summary>
    /// O mesmo campo aparece em duas linhas do desenho. Uma delas está errada por definição —
    /// um campo do PDF só pode receber um valor —, e é o rastro típico de quem pareou o desenho
    /// pelo nome do campo: uma perícia rouba o campo da outra e as duas passam a apontar para ele.
    /// </summary>
    CampoRepetido,

    /// <summary>
    /// As duas chaves discordam: o campo que o modelo nomeia está impresso <b>em outra linha</b>
    /// da ficha, e a linha que o modelo desenha pertence a outro campo. É o erro que faz o valor
    /// de uma perícia sair na linha de outra.
    /// </summary>
    CampoDeOutraLinha,

    /// <summary>
    /// A linha do desenho não corresponde a nenhuma linha impressa da ficha: nem o campo que ela
    /// nomeia está ali, nem existe campo cujo rótulo seja esse. Ou o desenho inventou uma linha,
    /// ou a ficha em branco não é a que o modelo descreve.
    /// </summary>
    LinhaSemCorrespondencia,
}

/// <summary>Um desacordo entre o modelo em texto do sistema e a ficha em PDF dele.</summary>
/// <param name="Campo">O nome do campo, como o modelo o escreve.</param>
/// <param name="Tipo">O que está errado.</param>
/// <param name="RotuloNoModelo">O que o desenho em texto diz que aquela linha é.</param>
/// <param name="RotuloNaFicha">O que está impresso ao lado do campo no PDF.</param>
/// <param name="CampoEsperado">
/// O campo que está mesmo impresso na linha que o modelo desenhou — a correção pronta, quando dá
/// para identificá-la sem ambiguidade.
/// </param>
/// <param name="LinhaNaFicha">Em que linha impressa da página o campo nomeado está, se está.</param>
public sealed record DivergenciaDaFicha(
    string Campo,
    TipoDeDivergencia Tipo,
    string RotuloNoModelo,
    string? RotuloNaFicha,
    string? CampoEsperado = null,
    int? LinhaNaFicha = null)
{
    public string Descrever() => Tipo switch
    {
        TipoDeDivergencia.CampoInexistente =>
            $"'{Campo}' (na linha \"{RotuloNoModelo}\") não existe no formulário da ficha.",

        TipoDeDivergencia.CampoRepetido =>
            $"'{Campo}' é usado em mais de uma linha do desenho — a segunda é \"{RotuloNoModelo}\". " +
            $"Na ficha ele está impresso em \"{RotuloNaFicha ?? "linha não identificada"}\", e só " +
            "essa linha pode usá-lo.",

        TipoDeDivergencia.LinhaSemCorrespondencia =>
            $"a linha \"{RotuloNoModelo}\" do desenho não existe na ficha: '{Campo}' está impresso " +
            $"em \"{RotuloNaFicha}\" e nenhum campo da ficha tem esse rótulo.",

        _ =>
            $"'{Campo}' está impresso na linha \"{RotuloNaFicha}\"" +
            (LinhaNaFicha is { } linha ? $" (linha {linha} da página)" : "") +
            $", mas o modelo o usa na linha \"{RotuloNoModelo}\"" +
            (CampoEsperado is { } certo ? $" — quem está impresso ali é '{certo}'." : "."),
    };
}

/// <summary>O que a conferência achou.</summary>
/// <param name="Divergencias">Os desacordos campo a campo. Vazio é aprovação.</param>
/// <param name="Avisos">
/// Observações sobre a ficha como um todo, já escritas em português para irem ao usuário —
/// idioma da ficha, desencontro de idioma entre ficha e modelo.
/// </param>
/// <param name="IdiomaDaFichaEmBranco">O idioma dos rótulos impressos no PDF.</param>
public sealed record ResultadoDaConferencia(
    IReadOnlyList<DivergenciaDaFicha> Divergencias,
    IReadOnlyList<string> Avisos,
    IdiomaDetectado IdiomaDaFichaEmBranco)
{
    /// <summary>Nenhum campo divergiu. Pode haver aviso mesmo assim.</summary>
    public bool Aprovada => Divergencias.Count == 0;
}

/// <summary>
/// Confere o <c>Ficha-ModeloEmTexto.md</c> de um sistema contra a ficha em PDF dele, resolvendo
/// cada campo por <b>duas chaves independentes</b>: o <b>nome</b> escrito no marcador e a
/// <b>posição impressa</b> — a linha da ficha cujo rótulo é o daquela linha do desenho. Enquanto
/// as duas apontam para o mesmo campo, o de-para está de pé; quando discordam, uma delas está
/// errada e a conferência diz qual campo pertence àquela linha.
///
/// <para><b>Por que duas chaves.</b> O preenchimento sempre foi resolvido só pelo nome: o
/// marcador <c>{{Animal}}</c> escreve no campo de AcroForm <c>Animal</c>, e pronto. Nome é uma
/// chave que ninguém consegue conferir de olho — numa ficha traduzida ele nem é o nome da coisa
/// (<c>Animal</c> está impresso na linha "Arcanismo"). Sozinha, ela deixou 13 perícias em 18
/// sendo escritas na linha de outra perícia, no PDF, sem nenhum sinal. A segunda chave é o que
/// dá para conferir: o texto impresso ao lado do campo.</para>
///
/// <para><b>Só fala quando tem certeza.</b> Um campo só é julgado quando o texto impresso está
/// <b>na mesma linha</b> dele e colado (ver <see cref="RotuloConfiavel"/>). Rótulo distante,
/// ausente ou acima/abaixo do campo é deixado passar. E antes de julgar campo por campo, a
/// conferência mede se os dois artefatos se falam: se quase nada casa, o problema é sistêmico —
/// ficha e modelo em idiomas diferentes, ou ficha trocada — e sai um aviso explicando isso, no
/// lugar de uma divergência por linha que não ajudaria ninguém.</para>
/// </summary>
public static class ConferenciaDaFicha
{
    /// <summary>
    /// Até que distância, em pontos, o texto impresso ao lado de um campo é seguramente o rótulo
    /// dele. Acima disso pode ser de outra coluna, e a conferência se cala.
    /// </summary>
    private const double DistanciaConfiavel = 10.0;

    /// <summary>
    /// Palavra curta demais não distingue nada ("de", "do", "PV") e casaria por acaso entre
    /// rótulos diferentes.
    /// </summary>
    private const int TamanhoMinimoDaPalavra = 3;

    /// <summary>
    /// Abaixo desta fração de campos conferíveis em acordo, o desencontro deixou de ser erro de
    /// linha e passou a ser de arquivo: a ficha não é a que o modelo descreve, ou os dois estão
    /// em idiomas diferentes. Listar cada linha nesse caso esconderia a causa dentro do sintoma.
    /// </summary>
    private const double AcordoMinimo = 0.5;

    /// <summary>
    /// Quantos campos conferíveis são precisos para a medida acima significar alguma coisa. Com
    /// poucos, uma divergência real derrubaria a fração e viraria "aviso" em vez de erro.
    /// </summary>
    private const int MinimoParaMedirAcordo = 8;

    private static readonly Regex Marcador = new(@"\{\{([^{}]*)\}\}", RegexOptions.Compiled);

    private static readonly Regex Separadores = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);

    /// <summary>
    /// Confere o modelo em texto de <paramref name="sistema"/> contra a ficha em PDF dele.
    /// </summary>
    /// <exception cref="ErroDeFerramenta">
    /// O sistema não tem ficha em branco, não tem modelo em texto, ou a ficha não abre.
    /// </exception>
    public static ResultadoDaConferencia Conferir(CaminhosDoProjeto caminhos, string sistema)
    {
        var caminhoDoModelo = FichaEmTexto.CaminhoDoModelo(caminhos, sistema);

        if (!File.Exists(caminhoDoModelo))
        {
            throw new ErroDeFerramenta(
                $"O sistema '{sistema}' não tem {SistemaRpg.NomeDoModeloEmTexto} — não há o que conferir.");
        }

        var daFicha = PreenchedorDeFicha.ListarLayoutDaFicha(caminhos, sistema, null);

        var porNome = daFicha
            .GroupBy(campo => campo.Nome)
            .ToDictionary(grupo => grupo.Key, grupo => grupo.First(), StringComparer.Ordinal);

        var linhasDoModelo = FichaEmTexto
            .Desenhos(File.ReadAllText(caminhoDoModelo))
            .SelectMany(bloco => bloco.Split('\n'))
            .Select(Analisar)
            .Where(linha => linha is not null)
            .Select(linha => linha!)
            .ToList();

        var avisos = Avisar(daFicha, linhasDoModelo, out var idioma);

        return new ResultadoDaConferencia(
            Conferir(linhasDoModelo, porNome, daFicha, JulgarPorRotulo(linhasDoModelo, porNome)),
            avisos,
            idioma);
    }

    /// <summary>Uma linha do desenho que tem campo: o texto que a rotula e os campos dela.</summary>
    private sealed record LinhaDoModelo(string Rotulo, HashSet<string> Palavras, IReadOnlyList<string> Campos);

    private static LinhaDoModelo? Analisar(string linha)
    {
        var marcadores = Marcador.Matches(linha);

        if (marcadores.Count == 0)
        {
            return null;
        }

        // O rótulo da linha é o que sobra quando os marcadores saem: no desenho, é o texto que
        // acompanha os valores ("Acrobacia (Des)", "FORCA .........").
        var rotulo = Marcador.Replace(linha, " ").Trim();

        return new LinhaDoModelo(
            rotulo,
            Palavras(rotulo),
            [.. marcadores.Select(marcador => marcador.Groups[1].Value)]);
    }

    /// <summary>
    /// Vale julgar as linhas uma a uma, ou o desacordo é geral? Mede quantos campos conferíveis
    /// têm as duas chaves de acordo; se quase nenhum, julgar linha a linha só produziria ruído.
    /// </summary>
    private static bool JulgarPorRotulo(
        List<LinhaDoModelo> linhas,
        Dictionary<string, CampoDaFicha> porNome)
    {
        int conferiveis = 0, deAcordo = 0;

        foreach (var linha in linhas)
        {
            foreach (var nome in linha.Campos)
            {
                if (linha.Palavras.Count == 0
                    || !porNome.TryGetValue(nome, out var campo)
                    || !RotuloConfiavel(campo))
                {
                    continue;
                }

                conferiveis++;

                if (Palavras(campo.Rotulo!).Overlaps(linha.Palavras))
                {
                    deAcordo++;
                }
            }
        }

        return conferiveis < MinimoParaMedirAcordo
            || (double)deAcordo / conferiveis >= AcordoMinimo;
    }

    private static IReadOnlyList<string> Avisar(
        IReadOnlyList<CampoDaFicha> daFicha,
        List<LinhaDoModelo> linhas,
        out IdiomaDetectado idiomaDaFicha)
    {
        var avisos = new List<string>();

        var rotulosImpressos = daFicha
            .Where(campo => campo.Rotulo is { Length: > 0 })
            .Select(campo => campo.Rotulo!)
            .ToList();

        idiomaDaFicha = IdiomaDaFicha.Detectar(rotulosImpressos);
        var idiomaDoModelo = IdiomaDaFicha.Detectar(linhas.Select(linha => linha.Rotulo));

        if (idiomaDaFicha is not IdiomaDetectado.Portugues and not IdiomaDetectado.Indeterminado)
        {
            avisos.Add(
                $"A ficha em branco deste sistema está em {IdiomaDaFicha.Nome(idiomaDaFicha)}, não em " +
                "português. A base de conhecimento precisa acompanhar o idioma da ficha e dos livros — " +
                "é o que faz os rótulos casarem na hora de preencher. A conversa com o usuário continua " +
                "sempre em português, independentemente disso.");
        }

        if (idiomaDaFicha is not IdiomaDetectado.Indeterminado
            && idiomaDoModelo is not IdiomaDetectado.Indeterminado
            && idiomaDaFicha != idiomaDoModelo)
        {
            avisos.Add(
                $"A ficha está em {IdiomaDaFicha.Nome(idiomaDaFicha)} e o {SistemaRpg.NomeDoModeloEmTexto} " +
                $"está em {IdiomaDaFicha.Nome(idiomaDoModelo)}. Nenhum rótulo casa assim, e a conferência " +
                "campo a campo fica sem sentido: refaça o modelo no idioma da ficha.");
        }

        return avisos;
    }

    private static IReadOnlyList<DivergenciaDaFicha> Conferir(
        List<LinhaDoModelo> linhas,
        Dictionary<string, CampoDaFicha> porNome,
        IReadOnlyList<CampoDaFicha> daFicha,
        bool julgarPorRotulo)
    {
        var divergencias = new List<DivergenciaDaFicha>();
        var jaVistos = new HashSet<string>(StringComparer.Ordinal);

        foreach (var linha in linhas)
        {
            foreach (var nome in linha.Campos)
            {
                // Primeira chave: o nome. Vale sempre, e não depende de idioma nem de leiaute.
                if (!porNome.TryGetValue(nome, out var campo))
                {
                    divergencias.Add(new DivergenciaDaFicha(
                        nome, TipoDeDivergencia.CampoInexistente, linha.Rotulo, null));

                    continue;
                }

                // Só a partir da segunda vez: a repetição é a divergência, e conferir o rótulo das
                // duas linhas apontaria o dedo para a certa tanto quanto para a errada.
                if (!jaVistos.Add(nome))
                {
                    divergencias.Add(new DivergenciaDaFicha(
                        nome, TipoDeDivergencia.CampoRepetido, linha.Rotulo, campo.Rotulo));

                    continue;
                }

                if (!julgarPorRotulo || linha.Palavras.Count == 0 || !RotuloConfiavel(campo))
                {
                    continue;
                }

                // Segunda chave: a posição impressa. Concordando com o nome, está resolvido.
                if (Palavras(campo.Rotulo!).Overlaps(linha.Palavras))
                {
                    continue;
                }

                divergencias.Add(Discordancia(nome, campo, linha, daFicha));
            }
        }

        return divergencias;
    }

    /// <summary>
    /// As duas chaves discordaram. Procura quem está mesmo impresso na linha que o desenho fez:
    /// achando um só, a mensagem já traz a correção; não achando nenhum, a linha não existe na
    /// ficha, e é isso que precisa ser dito.
    /// </summary>
    private static DivergenciaDaFicha Discordancia(
        string nome,
        CampoDaFicha campo,
        LinhaDoModelo linha,
        IReadOnlyList<CampoDaFicha> daFicha)
    {
        // Quantas palavras cada campo divide com a linha do desenho. Contar, e não só perguntar
        // se divide alguma, é o que separa o campo certo dos vizinhos: metade das perícias tem
        // "(Int)" no rótulo, e por uma palavra dessas todas empatariam.
        var candidatos = daFicha
            .Where(RotuloConfiavel)
            .Select(outro => (outro.Nome, Peso: Palavras(outro.Rotulo!).Count(linha.Palavras.Contains)))
            .Where(candidato => candidato.Peso > 0)
            .GroupBy(candidato => candidato.Nome, StringComparer.Ordinal)
            .Select(grupo => (Nome: grupo.Key, Peso: grupo.Max(candidato => candidato.Peso)))
            .OrderByDescending(candidato => candidato.Peso)
            .ToList();

        if (candidatos.Count == 0)
        {
            return new DivergenciaDaFicha(
                nome, TipoDeDivergencia.LinhaSemCorrespondencia, linha.Rotulo, campo.Rotulo);
        }

        // Só sugere quando um campo casa melhor que todos os outros; no empate, apontar um deles
        // seria chutar, e um chute com cara de correção é pior que nenhuma sugestão.
        var melhor = candidatos[0];
        var unico = candidatos.Count == 1 || candidatos[1].Peso < melhor.Peso;

        return new DivergenciaDaFicha(
            nome,
            TipoDeDivergencia.CampoDeOutraLinha,
            linha.Rotulo,
            campo.Rotulo,
            unico ? melhor.Nome : null,
            campo.Linha);
    }

    /// <summary>
    /// Dá para julgar um campo por causa deste rótulo?
    ///
    /// <para>Só quando ele está <b>na mesma linha</b> do campo e perto. Rótulo acima ou abaixo é
    /// ambíguo por construção: esta mesma ficha põe o título em cima de uns quadros ("FORÇA",
    /// sobre o valor de Força) e embaixo de outros ("RAÇA", sob o campo da raça) — e uma legenda
    /// no vão entre duas caixas fica a poucos pontos das duas. Geometria não decide isso, então a
    /// conferência não opina: cabeçalho e quadros continuam sendo conferidos no olho, e o que ela
    /// garante é o que ninguém consegue conferir no olho — os blocos longos e repetitivos de
    /// perícias, testes e magias, que é onde o pareamento errado se esconde.</para>
    /// </summary>
    private static bool RotuloConfiavel(CampoDaFicha campo) =>
        campo.Rotulo is { Length: > 0 }
        && campo.Direcao is DirecaoDoRotulo.Direita or DirecaoDoRotulo.Esquerda
        && campo.DistanciaDoRotulo <= DistanciaConfiavel;

    /// <summary>
    /// As palavras que identificam um rótulo, sem acento, sem pontuação e sem as curtas demais.
    /// É assim que "Historia (Int)" do desenho casa com "História (Int)" impresso.
    /// </summary>
    private static HashSet<string> Palavras(string texto) =>
        Separadores
            .Split(TextoNormalizado.SemAcento(texto).ToLowerInvariant())
            .Where(palavra => palavra.Length >= TamanhoMinimoDaPalavra)
            .ToHashSet(StringComparer.Ordinal);
}
