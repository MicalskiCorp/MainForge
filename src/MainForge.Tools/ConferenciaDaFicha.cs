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
/// <para><b>Só fala quando tem certeza.</b> Um campo é julgado quando o texto impresso está
/// <b>na mesma linha</b> dele e colado (ver <see cref="RotuloConfiavel"/>) — ou quando está longe
/// mas <b>outro campo da mesma linha exibe o mesmo texto de perto</b>, que é confirmação e não
/// palpite (ver <see cref="PodeJulgar"/>). Rótulo ausente, acima/abaixo do campo, ou distante sem
/// nada que o confirme, é deixado passar. E antes de julgar campo por campo, a
/// conferência mede se os dois artefatos se falam: se quase nada casa, o problema é sistêmico —
/// ficha e modelo em idiomas diferentes, ou ficha trocada — e sai um aviso explicando isso, no
/// lugar de uma divergência por linha que não ajudaria ninguém.</para>
/// </summary>
public static class ConferenciaDaFicha
{
    // A régua geométrica de "este texto rotula este campo" mora em CampoDaFicha
    // (RotuloDeConfianca), junto dos dados que ela mede. Aqui sobra a parte que é desta
    // conferência: o rótulo também precisa ter palavra que distinga alguma coisa.

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
        var confirmados = RotulosConfirmadosPorLinha(daFicha);

        return new ResultadoDaConferencia(
            Conferir(
                linhasDoModelo,
                porNome,
                daFicha,
                JulgarPorRotulo(linhasDoModelo, porNome, confirmados),
                confirmados),
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
        Dictionary<string, CampoDaFicha> porNome,
        IReadOnlySet<(int Pagina, int Linha, string Rotulo)> confirmados)
    {
        int conferiveis = 0, deAcordo = 0;

        foreach (var linha in linhas)
        {
            foreach (var nome in linha.Campos)
            {
                if (linha.Palavras.Count == 0
                    || !porNome.TryGetValue(nome, out var campo)
                    || !PodeJulgar(campo, confirmados))
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
        bool julgarPorRotulo,
        IReadOnlySet<(int Pagina, int Linha, string Rotulo)> confirmados)
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

                if (!julgarPorRotulo || linha.Palavras.Count == 0 || !PodeJulgar(campo, confirmados))
                {
                    continue;
                }

                // Segunda chave: a posição impressa. Concordando com o nome, está resolvido.
                if (Palavras(campo.Rotulo!).Overlaps(linha.Palavras))
                {
                    continue;
                }

                divergencias.Add(Discordancia(nome, campo, linha, daFicha, confirmados));
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
        IReadOnlyList<CampoDaFicha> daFicha,
        IReadOnlySet<(int Pagina, int Linha, string Rotulo)> confirmados)
    {
        // Quantas palavras cada campo divide com a linha do desenho. Contar, e não só perguntar
        // se divide alguma, é o que separa o campo certo dos vizinhos: metade das perícias tem
        // "(Int)" no rótulo, e por uma palavra dessas todas empatariam.
        //
        // Só entram candidatos do mesmo tipo do campo acusado. Numa linha "[caixa] [valor] Rótulo"
        // os dois casam com o rótulo igualmente bem, e sugerir o valor para consertar uma caixa de
        // marcação seria uma correção que estraga o desenho — quem a seguisse ao pé da letra
        // trocaria a caixa por um número.
        //
        // E a régua aqui é a mesma de lá (PodeJulgar), não a estrita: numa linha
        // "[caixa] [valor] Rótulo" a caixa certa também tem o rótulo distante. Procurá-la só entre
        // os rótulos colados a deixaria de fora — e a conferência diria "nenhum campo da ficha tem
        // esse rótulo" a respeito de um campo que está bem ali.
        var candidatos = daFicha
            .Where(outro => outro.DeMarcacao == campo.DeMarcacao)
            .Where(outro => PodeJulgar(outro, confirmados))
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
    /// <para><b>E o rótulo precisa dizer alguma coisa.</b> Um rótulo cujas palavras são todas
    /// curtas demais para distinguir — as caixas de moeda da ficha de D&amp;D são rotuladas "PC",
    /// "PP", "PE", "PO", "PL" — não sobrevive a <see cref="Palavras"/>, e comparar um conjunto
    /// vazio com qualquer coisa dá "não casa". Sem esta condição, todo campo assim seria acusado
    /// de estar na linha errada por não haver como conferir que está na certa, que é o oposto do
    /// que esta conferência promete fazer.</para>
    /// <summary>
    /// Os rótulos que uma linha impressa da ficha tem <b>confirmados</b>: aqueles que algum campo
    /// daquela linha exibe de forma confiável (na mesma linha, colado). Cada entrada é
    /// "nesta página, nesta linha, este texto é o rótulo".
    /// </summary>
    private static IReadOnlySet<(int Pagina, int Linha, string Rotulo)> RotulosConfirmadosPorLinha(
        IReadOnlyList<CampoDaFicha> daFicha) =>
        daFicha
            .Where(RotuloConfiavel)
            .Select(campo => (campo.Pagina, campo.Linha, campo.Rotulo!))
            .ToHashSet();

    /// <summary>
    /// Dá para julgar este campo pelo rótulo dele?
    ///
    /// <para>Sim quando o rótulo é confiável por si (ver <see cref="RotuloConfiavel"/>) — e
    /// também quando ele está longe mas <b>outro campo da mesma linha exibe exatamente o mesmo
    /// texto de perto</b>. Aí não há palpite: a identidade daquela linha já foi estabelecida por
    /// quem tinha o rótulo colado, e o campo distante só está confirmando o que ela diz.</para>
    ///
    /// <para><b>Por que isso faltava.</b> O corte de distância existe porque um rótulo longe pode
    /// ser de outra coluna. Só que na ficha de D&amp;D 5e a linha de cada perícia e de cada teste
    /// de resistência é <c>[caixa de marcação] [valor] Rótulo</c>: o valor fica a 4 pt do texto e a
    /// caixa, a 23,6 pt. O valor era conferido e a caixa não — e a caixa é justamente quem diz
    /// <b>em que o personagem é proficiente</b>. Eram 24 campos mudos (6 testes de resistência e
    /// 18 perícias), num bloco longo e repetitivo que ninguém confere de olho, que é exatamente o
    /// que esta conferência existe para cobrir. Um pareamento trocado ali dá um personagem
    /// proficiente nas coisas erradas, sem nada acusando.</para>
    ///
    /// <para>A confirmação exige o <b>mesmo texto</b>, e não só uma linha em comum: as linhas são
    /// detectadas na largura da página inteira, então "mesma linha" junta colunas que nada têm a
    /// ver uma com a outra — a caixa de teste contra a morte cai na mesma linha da perícia
    /// Acrobacia. Exigir o texto idêntico é o que separa confirmar de adivinhar.</para>
    /// </summary>
    private static bool PodeJulgar(
        CampoDaFicha campo,
        IReadOnlySet<(int Pagina, int Linha, string Rotulo)> confirmados) =>
        RotuloConfiavel(campo)
        || (campo.Rotulo is { Length: > 0 } rotulo
            && campo.Direcao is DirecaoDoRotulo.Direita or DirecaoDoRotulo.Esquerda
            && Palavras(rotulo).Count > 0
            && confirmados.Contains((campo.Pagina, campo.Linha, rotulo)));

    private static bool RotuloConfiavel(CampoDaFicha campo) =>
        campo.RotuloDeConfianca && Palavras(campo.Rotulo!).Count > 0;

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
