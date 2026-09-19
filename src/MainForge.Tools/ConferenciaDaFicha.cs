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
/// <param name="LinhaNoModelo">
/// Em que linha do arquivo do modelo (a partir de zero) a divergência está. É o que deixa
/// <see cref="ConferenciaDaFicha.Corrigir"/> trocar o campo só naquela linha.
/// </param>
public sealed record DivergenciaDaFicha(
    string Campo,
    TipoDeDivergencia Tipo,
    string RotuloNoModelo,
    string? RotuloNaFicha,
    string? CampoEsperado = null,
    int? LinhaNaFicha = null,
    int? LinhaNoModelo = null)
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

/// <summary>O que <see cref="ConferenciaDaFicha.Corrigir"/> fez com o modelo em texto.</summary>
/// <param name="Antes">A conferência como o modelo estava.</param>
/// <param name="Trocados">Quantos marcadores foram trocados pelo campo certo. Zero é "nada mudou".</param>
/// <param name="Depois">A conferência do modelo já corrigido — o que sobrou para uma pessoa olhar.</param>
public sealed record CorrecaoDaFicha(ResultadoDaConferencia Antes, int Trocados, ResultadoDaConferencia Depois);

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
/// nada que o confirme, é deixado passar. Ficha e modelo em idiomas diferentes também não são
/// julgados linha a linha: nada casa por construção, e sai um aviso só com a causa.</para>
///
/// <para>As linhas <b>sem rótulo</b> nenhum (listas de magias) são conferidas por outra chave, a
/// coluna em que o campo está impresso — ver <see cref="ConferirColunas"/>.</para>
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
    /// Abaixo desta fração de campos conferíveis em acordo, o modelo foi montado pelo nome dos
    /// campos, e não pela linha impressa — e isso vira um aviso com a causa, além das divergências.
    /// </summary>
    private const double AcordoMinimo = 0.5;

    /// <summary>
    /// Quantos campos conferíveis são precisos para a medida acima significar alguma coisa. Com
    /// poucos, uma divergência isolada derrubaria a fração e o aviso acusaria o modelo inteiro.
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
            .BlocosDoDesenho(File.ReadAllText(caminhoDoModelo))
            .SelectMany((bloco, numero) => bloco.Select(linha => Analisar(linha.Indice, numero, linha.Texto)))
            .Where(linha => linha is not null)
            .Select(linha => linha!)
            .ToList();

        var avisos = Avisar(daFicha, linhasDoModelo, out var idioma, out var idiomasDiferentes);
        var confirmados = RotulosConfirmadosPorLinha(daFicha);

        // Só idioma diferente cala o julgamento linha a linha — aí nada casa por construção, e o
        // aviso já diz a causa. Desacordo alto no MESMO idioma é o contrário de ruído: é o modelo
        // pareado pelo nome dos campos, e calar ali aprovava justamente o pior modelo possível.
        var divergencias = Conferir(linhasDoModelo, porNome, daFicha, !idiomasDiferentes, confirmados);

        divergencias.AddRange(ConferirColunas(linhasDoModelo, porNome, daFicha, divergencias));
        divergencias.AddRange(ConferirFileiras(linhasDoModelo, porNome, daFicha, confirmados, divergencias));

        if (!idiomasDiferentes && DesacordoGeral(linhasDoModelo, porNome, daFicha, confirmados))
        {
            avisos = [.. avisos,
                $"Mais da metade das linhas conferíveis do {SistemaRpg.NomeDoModeloEmTexto} aponta para o " +
                "campo de outra linha: o desenho foi montado pelo NOME dos campos, e não pela linha " +
                "impressa na ficha."];
        }

        return new ResultadoDaConferencia(divergencias, avisos, idioma);
    }

    /// <summary>
    /// Confere e, havendo linha com o campo trocado cuja correção é certa, <b>corrige o
    /// modelo em texto</b> no próprio arquivo.
    ///
    /// <para><b>Por que corrigir sozinho.</b> Conferir só na criação do sistema não bastou: uma
    /// base mapeada antes desta conferência existir — ou chegada por pacote de outra instalação —
    /// continua com o de-para quebrado para sempre, porque nada volta a olhar para ela. E o agente
    /// que monta a ficha do personagem segue o modelo ao pé da letra: com ele errado, 13 perícias
    /// e os truques saíam na linha de outro campo, geração após geração. Quando a conferência sabe
    /// qual campo pertence à linha, deixar para uma pessoa trocar à mão é só adiar o erro.</para>
    ///
    /// <para><b>Só troca o que é certo, e nunca piora.</b> Entra só divergência com
    /// <see cref="DivergenciaDaFicha.CampoEsperado"/> — a conferência já só o preenche quando um
    /// candidato vence os outros. E se a conferência depois da troca achar mais problemas que
    /// antes, o arquivo volta ao que era.</para>
    ///
    /// <para>Grava direto no arquivo, e não pelo <see cref="EscritorDeConhecimento"/>: aquele é o
    /// ponto que confina o <b>agente</b> e mantém o índice, e aqui quem escreve é o próprio C#,
    /// trocando campos num arquivo que já existe — nada no índice muda.</para>
    /// </summary>
    public static CorrecaoDaFicha Corrigir(CaminhosDoProjeto caminhos, string sistema)
    {
        var antes = Conferir(caminhos, sistema);
        var atual = new CorrecaoDaFicha(antes, 0, antes);

        // Mais de uma rodada porque uma correção destrava a seguinte: as caixas de uma fileira só
        // são conferidas depois que a primeira delas, a que tem rótulo, está no lugar certo.
        for (var rodada = 0; rodada < 3; rodada++)
        {
            var passo = CorrigirUmaVez(caminhos, sistema, atual.Depois);

            if (passo.Trocados == 0)
            {
                break;
            }

            atual = new CorrecaoDaFicha(antes, atual.Trocados + passo.Trocados, passo.Depois);
        }

        return atual;
    }

    private static CorrecaoDaFicha CorrigirUmaVez(CaminhosDoProjeto caminhos, string sistema, ResultadoDaConferencia antes)
    {
        var trocasPorLinha = antes.Divergencias
            .Where(divergencia => divergencia is
            {
                Tipo: TipoDeDivergencia.CampoDeOutraLinha,
                CampoEsperado: not null,
                LinhaNoModelo: not null,
            })
            .GroupBy(divergencia => divergencia.LinhaNoModelo!.Value)
            .ToDictionary(
                grupo => grupo.Key,
                grupo => grupo
                    .GroupBy(divergencia => divergencia.Campo, StringComparer.Ordinal)
                    .ToDictionary(campo => campo.Key, campo => campo.First().CampoEsperado!, StringComparer.Ordinal));

        if (trocasPorLinha.Count == 0)
        {
            return new CorrecaoDaFicha(antes, 0, antes);
        }

        var caminhoDoModelo = FichaEmTexto.CaminhoDoModelo(caminhos, sistema);
        var original = File.ReadAllText(caminhoDoModelo);
        var quebra = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var linhas = original.ReplaceLineEndings("\n").Split('\n');
        var trocados = 0;

        foreach (var (indice, trocas) in trocasPorLinha)
        {
            // Cada linha é reescrita de uma vez, a partir do texto original dela: trocar marcador
            // por marcador em sequência faria Arcana→Animal e depois Animal→Nature na mesma linha.
            linhas[indice] = TrocarMantendoALargura(linhas[indice], trocas, ref trocados);
        }

        File.WriteAllText(caminhoDoModelo, string.Join(quebra, linhas));

        var depois = Conferir(caminhos, sistema);

        if (depois.Divergencias.Count >= antes.Divergencias.Count)
        {
            File.WriteAllText(caminhoDoModelo, original);
            return new CorrecaoDaFicha(antes, 0, antes);
        }

        return new CorrecaoDaFicha(antes, trocados, depois);
    }

    /// <summary>
    /// Troca os marcadores de uma linha sem desalinhar o desenho.
    ///
    /// <para>O desenho é arte de texto, e as colunas dele se sustentam pela largura de cada
    /// marcador: trocar <c>{{Arcana}}</c> por <c>{{Animal}}</c> sem compensar empurrava o rótulo
    /// daquela linha um caractere para a direita, e a coluna de perícias saía em zigue-zague. A
    /// diferença sai dos espaços logo depois do marcador — tirando, quando o nome novo é maior,
    /// sem nunca deixar menos de um; pondo, quando é menor. Marcador colado em outra coisa (a
    /// caixa entre colchetes) fica como está: ali não há espaço para compensar.</para>
    /// </summary>
    private static string TrocarMantendoALargura(string linha, IReadOnlyDictionary<string, string> trocas, ref int trocados)
    {
        var resultado = new System.Text.StringBuilder();
        var posicao = 0;

        foreach (Match marcador in Marcador.Matches(linha))
        {
            if (marcador.Index < posicao)
            {
                continue;
            }

            resultado.Append(linha, posicao, marcador.Index - posicao);
            posicao = marcador.Index + marcador.Length;

            if (!trocas.TryGetValue(marcador.Groups[1].Value, out var certo))
            {
                resultado.Append(marcador.Value);
                continue;
            }

            trocados++;

            var novo = "{{" + certo + "}}";
            var diferenca = novo.Length - marcador.Length;
            resultado.Append(novo);

            var espacos = 0;

            while (posicao + espacos < linha.Length && linha[posicao + espacos] == ' ')
            {
                espacos++;
            }

            if (espacos == 0)
            {
                continue;
            }

            var ficam = Math.Max(1, espacos - diferenca);
            resultado.Append(' ', ficam);
            posicao += espacos;
        }

        resultado.Append(linha, posicao, linha.Length - posicao);

        return resultado.ToString();
    }

    /// <summary>Uma linha do desenho que tem campo: o texto que a rotula e os campos dela.</summary>
    /// <param name="Indice">A posição da linha no arquivo do modelo, a partir de zero.</param>
    /// <param name="Bloco">Em qual bloco de desenho a linha está.</param>
    private sealed record LinhaDoModelo(
        int Indice,
        int Bloco,
        string Rotulo,
        HashSet<string> Palavras,
        IReadOnlyList<string> Campos);

    private static LinhaDoModelo? Analisar(int indice, int bloco, string linha)
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
            indice,
            bloco,
            rotulo,
            Palavras(rotulo),
            [.. marcadores.Select(marcador => marcador.Groups[1].Value)]);
    }

    /// <summary>
    /// O desacordo é geral? Mede quantos campos conferíveis têm as duas chaves de acordo.
    ///
    /// <para>Antes, esta medida decidia se as linhas eram julgadas: abaixo da metade, a conferência
    /// supunha "ficha trocada ou idioma diferente" e se calava. Só que o modelo pareado pelo nome
    /// dos campos — o erro que ela existe para pegar — erra 13 de 18 perícias e as caixas de todas
    /// elas, e caía exatamente abaixo da metade: saía <b>aprovado, sem nenhum aviso</b>. Agora ela
    /// só acrescenta um aviso com a causa provável; as linhas continuam sendo julgadas.</para>
    /// </summary>
    private static bool DesacordoGeral(
        List<LinhaDoModelo> linhas,
        Dictionary<string, CampoDaFicha> porNome,
        IReadOnlyList<CampoDaFicha> daFicha,
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

                if (CasaComALinha(campo, linha, daFicha, confirmados))
                {
                    deAcordo++;
                }
            }
        }

        return conferiveis >= MinimoParaMedirAcordo
            && (double)deAcordo / conferiveis < AcordoMinimo;
    }

    /// <summary>
    /// Quantas linhas seguidas sem rótulo formam um bloco que vale conferir pela coluna. Duas
    /// linhas soltas podem ser qualquer coisa; uma lista de truques ou de magias tem dezenas.
    /// </summary>
    private const int MinimoDeLinhasNaColuna = 3;

    /// <summary>
    /// Quanto dois campos podem diferir na horizontal (início e largura) e ainda serem a mesma
    /// coluna da ficha. Na ficha de D&amp;D 5e, o bloco de truques começa em 40 pt e o de 1º nível
    /// em 41 pt.
    /// </summary>
    private const double ToleranciaDaColuna = 3.0;

    /// <summary>
    /// Confere os blocos de linhas <b>sem rótulo</b> — a lista de truques, as magias de cada
    /// nível — pela coluna em que cada campo está impresso.
    ///
    /// <para><b>Por que é preciso.</b> Nessas linhas não há texto impresso ao lado do campo, então
    /// a segunda chave (o rótulo) não existe, e o modelo valia só pelo nome. E o nome engana do
    /// mesmo jeito que nas perícias: na ficha de D&amp;D 5e, <c>Spells 1015</c> não é o segundo
    /// truque, é a <b>primeira linha do 1º nível</b>, e o oitavo truque é <c>Spells 1022</c>. Um
    /// modelo numerado em sequência punha o segundo truque no bloco de 1º nível e uma magia de
    /// 1º nível no fim da lista de truques, com um buraco no meio.</para>
    ///
    /// <para><b>A chave aqui é a geometria.</b> Os campos de uma coluna ficam um embaixo do outro,
    /// com o mesmo início e a mesma largura, a um passo regular; um salto bem maior que o passo é
    /// o fim do bloco. Um bloco do desenho é comparado com a coluna impressa em que está a maior
    /// parte dos campos dele, posição a posição. Como no resto da conferência, só fala com
    /// certeza: coluna de tamanho diferente do bloco, ou sem maioria clara, fica sem julgamento.</para>
    /// </summary>
    private static IEnumerable<DivergenciaDaFicha> ConferirColunas(
        List<LinhaDoModelo> linhas,
        Dictionary<string, CampoDaFicha> porNome,
        IReadOnlyList<CampoDaFicha> daFicha,
        IReadOnlyList<DivergenciaDaFicha> jaAcusadas)
    {
        var acusados = jaAcusadas.Select(divergencia => (divergencia.LinhaNoModelo, divergencia.Campo)).ToHashSet();

        foreach (var bloco in BlocosSemRotulo(linhas, porNome))
        {
            var campos = bloco.Select(linha => porNome[linha.Campo]).ToList();

            var coluna = campos
                .Select(campo => ColunaDe(campo, daFicha))
                .GroupBy(col => col[0].Nome, StringComparer.Ordinal)
                .Select(grupo => (Coluna: grupo.First(), Votos: grupo.Count()))
                .OrderByDescending(candidata => candidata.Votos)
                .ToList();

            if (coluna.Count == 0
                || coluna[0].Votos * 2 <= campos.Count
                || (coluna.Count > 1 && coluna[1].Votos == coluna[0].Votos)
                || coluna[0].Coluna.Count != campos.Count)
            {
                continue;
            }

            var impressa = coluna[0].Coluna;

            for (var i = 0; i < campos.Count; i++)
            {
                var nome = campos[i].Nome;
                var ondeNoBloco = $"{i + 1}ª linha do bloco que começa em '{impressa[0].Nome}'";

                if (nome != impressa[i].Nome && !acusados.Contains((bloco[i].Indice, nome)))
                {
                    var posicaoReal = impressa.FindIndex(campo => campo.Nome == nome);

                    yield return new DivergenciaDaFicha(
                        nome,
                        TipoDeDivergencia.CampoDeOutraLinha,
                        ondeNoBloco,
                        posicaoReal >= 0
                            ? $"{posicaoReal + 1}ª linha desse bloco"
                            : "outro bloco da ficha",
                        impressa[i].Nome,
                        campos[i].Linha,
                        bloco[i].Indice);
                }

                // A caixa da linha ("magia preparada") vai com o campo que a linha de fato é: a que
                // está impressa logo à esquerda dele, na mesma linha da página. Corrigir só o nome
                // da magia deixaria a caixa de uma linha marcando a preparação da vizinha.
                var caixaCerta = CaixaAEsquerda(impressa[i], daFicha);

                if (caixaCerta is null)
                {
                    continue;
                }

                foreach (var caixa in linhas
                    .First(linha => linha.Indice == bloco[i].Indice)
                    .Campos
                    .Where(campo => porNome[campo].DeMarcacao && campo != caixaCerta.Nome))
                {
                    if (acusados.Contains((bloco[i].Indice, caixa)))
                    {
                        continue;
                    }

                    yield return new DivergenciaDaFicha(
                        caixa,
                        TipoDeDivergencia.CampoDeOutraLinha,
                        $"caixa da {ondeNoBloco}",
                        porNome[caixa].Rotulo,
                        caixaCerta.Nome,
                        porNome[caixa].Linha,
                        bloco[i].Indice);
                }
            }
        }
    }

    /// <summary>
    /// Até quantos pontos à esquerda de um campo uma caixa de marcação ainda é a caixa daquela
    /// linha. Na ficha de D&amp;D 5e a caixa de "preparada" fica a 9 pt do nome da magia.
    /// </summary>
    private const double AlcanceDaCaixa = 30.0;

    /// <summary>A única caixa de marcação colada à esquerda de um campo, na mesma linha impressa.</summary>
    private static CampoDaFicha? CaixaAEsquerda(CampoDaFicha campo, IReadOnlyList<CampoDaFicha> daFicha)
    {
        var caixas = daFicha
            .Where(outro => outro.DeMarcacao
                && outro.Pagina == campo.Pagina
                && outro.Linha == campo.Linha
                && outro.Esquerda < campo.Esquerda
                && campo.Esquerda - (outro.Esquerda + outro.Largura) <= AlcanceDaCaixa)
            .ToList();

        return caixas.Count == 1 ? caixas[0] : null;
    }

    /// <summary>
    /// Confere as caixas <b>sem rótulo</b> que continuam uma fileira: no desenho, uma linha
    /// rotulada com uma caixa ("SUCESSOS [ ]") seguida de linhas só com uma caixa cada; na ficha,
    /// as caixas lado a lado na mesma linha impressa, a primeira colada no rótulo.
    ///
    /// <para>É o mesmo problema das listas de magias, em outra forma. As caixas de uma fileira não
    /// têm texto próprio — o rótulo é da fileira, e está colado só na primeira —, então o modelo
    /// valia pelo nome, e nome de caixa (<c>Check Box 35</c>) não diz nada. Quando a primeira
    /// linha do desenho confere com a ficha, as seguintes são as caixas vizinhas dela, na ordem
    /// em que estão impressas; e só se o número de caixas bater exatamente.</para>
    /// </summary>
    private static IEnumerable<DivergenciaDaFicha> ConferirFileiras(
        List<LinhaDoModelo> linhas,
        Dictionary<string, CampoDaFicha> porNome,
        IReadOnlyList<CampoDaFicha> daFicha,
        IReadOnlySet<(int Pagina, int Linha, string Rotulo)> confirmados,
        IReadOnlyList<DivergenciaDaFicha> jaAcusadas)
    {
        var acusados = jaAcusadas.Select(divergencia => (divergencia.LinhaNoModelo, divergencia.Campo)).ToHashSet();

        for (var i = 0; i < linhas.Count; i++)
        {
            var cabeca = linhas[i];

            if (cabeca.Campos.Count != 1
                || cabeca.Palavras.Count == 0
                || !porNome.TryGetValue(cabeca.Campos[0], out var primeira)
                || !primeira.DeMarcacao
                || !PodeJulgar(primeira, confirmados)
                || !CasaComALinha(primeira, cabeca, daFicha, confirmados))
            {
                continue;
            }

            var seguintes = new List<LinhaDoModelo>();

            for (var j = i + 1;
                 j < linhas.Count
                 && linhas[j].Indice == cabeca.Indice + seguintes.Count + 1
                 && linhas[j].Campos.Count == 1
                 && linhas[j].Palavras.Count == 0
                 && porNome.TryGetValue(linhas[j].Campos[0], out var caixa)
                 && caixa.DeMarcacao;
                 j++)
            {
                seguintes.Add(linhas[j]);
            }

            if (seguintes.Count == 0)
            {
                continue;
            }

            var fileira = daFicha
                .Where(outro => outro.DeMarcacao && outro.Pagina == primeira.Pagina && outro.Linha == primeira.Linha)
                .OrderBy(outro => outro.Esquerda)
                .SkipWhile(outro => outro.Nome != primeira.Nome)
                .ToList();

            if (fileira.Count != seguintes.Count + 1)
            {
                continue;
            }

            for (var k = 0; k < seguintes.Count; k++)
            {
                var nome = seguintes[k].Campos[0];
                var certo = fileira[k + 1];

                if (nome == certo.Nome || acusados.Contains((seguintes[k].Indice, nome)))
                {
                    continue;
                }

                yield return new DivergenciaDaFicha(
                    nome,
                    TipoDeDivergencia.CampoDeOutraLinha,
                    $"{k + 2}ª caixa da fileira \"{cabeca.Rotulo}\"",
                    porNome[nome].Rotulo,
                    certo.Nome,
                    porNome[nome].Linha,
                    seguintes[k].Indice);
            }
        }
    }

    /// <summary>
    /// As sequências de linhas do desenho que têm um só campo de texto, sem rótulo nem no desenho
    /// nem na ficha. Linha vizinha no arquivo é o que as encadeia: um cabeçalho no meio
    /// ("NIVEL 1 ...") ou uma linha em branco separa um bloco do outro.
    /// </summary>
    private static List<List<(int Indice, string Campo)>> BlocosSemRotulo(
        List<LinhaDoModelo> linhas,
        Dictionary<string, CampoDaFicha> porNome)
    {
        var blocos = new List<List<(int Indice, string Campo)>>();
        List<(int Indice, string Campo)>? atual = null;
        var anterior = -2;

        foreach (var linha in linhas)
        {
            var deTexto = linha.Campos
                .Where(nome => porNome.TryGetValue(nome, out var campo) && !campo.DeMarcacao)
                .ToList();

            var muda = linha.Palavras.Count == 0
                && linha.Campos.All(porNome.ContainsKey)
                && deTexto.Count == 1
                && !porNome[deTexto[0]].RotuloDeConfianca;

            if (!muda || linha.Indice != anterior + 1)
            {
                if (atual is { Count: >= MinimoDeLinhasNaColuna })
                {
                    blocos.Add(atual);
                }

                atual = null;
            }

            if (muda)
            {
                atual ??= [];
                atual.Add((linha.Indice, deTexto[0]));
            }

            anterior = linha.Indice;
        }

        if (atual is { Count: >= MinimoDeLinhasNaColuna })
        {
            blocos.Add(atual);
        }

        return blocos;
    }

    /// <summary>
    /// A coluna impressa a que um campo de texto pertence: os campos da mesma página com o mesmo
    /// início e a mesma largura, de cima para baixo, cortados onde o espaço entre dois vizinhos
    /// passa bem do passo normal — é ali que um bloco termina e o próximo começa.
    /// </summary>
    private static List<CampoDaFicha> ColunaDe(CampoDaFicha campo, IReadOnlyList<CampoDaFicha> daFicha)
    {
        var alinhados = daFicha
            .Where(outro => !outro.DeMarcacao
                && outro.Pagina == campo.Pagina
                && Math.Abs(outro.Esquerda - campo.Esquerda) <= ToleranciaDaColuna
                && Math.Abs(outro.Largura - campo.Largura) <= ToleranciaDaColuna)
            .OrderByDescending(outro => outro.Base)
            .ToList();

        if (alinhados.Count < 2)
        {
            return alinhados;
        }

        var passos = alinhados.Zip(alinhados.Skip(1), (acima, abaixo) => acima.Base - abaixo.Base).ToList();
        var passoNormal = passos.Order().ElementAt(passos.Count / 2);
        var posicao = alinhados.FindIndex(outro => outro.Nome == campo.Nome);

        var inicio = posicao;

        while (inicio > 0 && passos[inicio - 1] <= passoNormal * 1.5)
        {
            inicio--;
        }

        var fim = posicao;

        while (fim < passos.Count && passos[fim] <= passoNormal * 1.5)
        {
            fim++;
        }

        return alinhados.GetRange(inicio, fim - inicio + 1);
    }

    private static IReadOnlyList<string> Avisar(
        IReadOnlyList<CampoDaFicha> daFicha,
        List<LinhaDoModelo> linhas,
        out IdiomaDetectado idiomaDaFicha,
        out bool idiomasDiferentes)
    {
        var avisos = new List<string>();

        var rotulosImpressos = daFicha
            .Where(campo => campo.Rotulo is { Length: > 0 })
            .Select(campo => campo.Rotulo!)
            .ToList();

        idiomaDaFicha = IdiomaDaFicha.Detectar(rotulosImpressos);
        var idiomaDoModelo = IdiomaDaFicha.Detectar(linhas.Select(linha => linha.Rotulo));

        idiomasDiferentes = idiomaDaFicha is not IdiomaDetectado.Indeterminado
            && idiomaDoModelo is not IdiomaDetectado.Indeterminado
            && idiomaDaFicha != idiomaDoModelo;

        if (idiomaDaFicha is not IdiomaDetectado.Portugues and not IdiomaDetectado.Indeterminado)
        {
            avisos.Add(
                $"A ficha em branco deste sistema está em {IdiomaDaFicha.Nome(idiomaDaFicha)}, não em " +
                "português. A base de conhecimento precisa acompanhar o idioma da ficha e dos livros — " +
                "é o que faz os rótulos casarem na hora de preencher. A conversa com o usuário continua " +
                "sempre em português, independentemente disso.");
        }

        if (idiomasDiferentes)
        {
            avisos.Add(
                $"A ficha está em {IdiomaDaFicha.Nome(idiomaDaFicha)} e o {SistemaRpg.NomeDoModeloEmTexto} " +
                $"está em {IdiomaDaFicha.Nome(idiomaDoModelo)}. Nenhum rótulo casa assim, e a conferência " +
                "campo a campo fica sem sentido: refaça o modelo no idioma da ficha.");
        }

        return avisos;
    }

    private static List<DivergenciaDaFicha> Conferir(
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
                        nome, TipoDeDivergencia.CampoInexistente, linha.Rotulo, null, LinhaNoModelo: linha.Indice));

                    continue;
                }

                // Só a partir da segunda vez: a repetição é a divergência, e conferir o rótulo das
                // duas linhas apontaria o dedo para a certa tanto quanto para a errada.
                if (!jaVistos.Add(nome))
                {
                    divergencias.Add(new DivergenciaDaFicha(
                        nome, TipoDeDivergencia.CampoRepetido, linha.Rotulo, campo.Rotulo, LinhaNoModelo: linha.Indice));

                    continue;
                }

                if (!julgarPorRotulo || linha.Palavras.Count == 0)
                {
                    continue;
                }

                if (!PodeJulgar(campo, confirmados))
                {
                    // O campo nomeado não tem rótulo que dê para conferir — mas a linha pode ter
                    // dono: outro campo do mesmo tipo com esse rótulo inteiro colado nele. Era o
                    // caso das caixas de teste de resistência trocadas pelas de teste contra a
                    // morte, cujo rótulo fica acima delas: nada acusava, porque o campo errado
                    // era justamente um dos que não se deixam julgar.
                    if (campo.DeMarcacao
                        && DonoDaLinha(campo, linha, daFicha, confirmados) is { } dono
                        && dono != nome)
                    {
                        divergencias.Add(new DivergenciaDaFicha(
                            nome,
                            TipoDeDivergencia.CampoDeOutraLinha,
                            linha.Rotulo,
                            campo.Rotulo,
                            dono,
                            campo.Linha,
                            linha.Indice));
                    }

                    continue;
                }

                // Segunda chave: a posição impressa. Concordando com o nome, está resolvido.
                if (CasaComALinha(campo, linha, daFicha, confirmados))
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
        var candidatos = Candidatos(campo, linha, daFicha, confirmados);

        if (candidatos.Count == 0)
        {
            return new DivergenciaDaFicha(
                nome, TipoDeDivergencia.LinhaSemCorrespondencia, linha.Rotulo, campo.Rotulo, LinhaNoModelo: linha.Indice);
        }

        // Só sugere quando um campo casa melhor que todos os outros; no empate, apontar um deles
        // seria chutar, e um chute com cara de correção é pior que nenhuma sugestão.
        var melhor = candidatos[0];
        var unico = candidatos.Count == 1 || Supera(melhor, candidatos[1]);

        return new DivergenciaDaFicha(
            nome,
            TipoDeDivergencia.CampoDeOutraLinha,
            linha.Rotulo,
            campo.Rotulo,
            unico ? melhor.Nome : null,
            campo.Linha,
            linha.Indice);
    }

    /// <summary>
    /// O campo do mesmo tipo que é, sem dúvida, o desta linha: o rótulo dele (colado, ou confirmado
    /// pela linha impressa) está <b>inteiro</b> dentro do texto da linha, e ele vence todos os
    /// outros candidatos.
    ///
    /// <para>Só para caixas de marcação. Num campo de texto, o rótulo de um vizinho cabe inteiro
    /// em linhas que não são dele — "Total" (o dos dados de vida) em "Espaços total", "Força" (o do
    /// teste de resistência) na linha do valor de Força —, e a linha seria dada ao campo errado.
    /// Caixa não tem esse problema: o que a rotula é o nome da perícia ou do teste, que só aparece
    /// na linha dela.</para>
    /// Mais exigente que <see cref="Candidatos"/> de propósito: aqui não há o campo nomeado para
    /// confirmar nada, e uma palavra solta em comum ("Sab") não basta para dar a linha a alguém.
    /// </summary>
    private static string? DonoDaLinha(
        CampoDaFicha campo,
        LinhaDoModelo linha,
        IReadOnlyList<CampoDaFicha> daFicha,
        IReadOnlySet<(int Pagina, int Linha, string Rotulo)> confirmados)
    {
        var inteiros = Candidatos(campo, linha, daFicha, confirmados)
            .Where(candidato => candidato.Peso == Palavras(daFicha.First(outro => outro.Nome == candidato.Nome).Rotulo!).Count)
            .ToList();

        return inteiros.Count == 1 || (inteiros.Count > 1 && Supera(inteiros[0], inteiros[1]))
            ? inteiros[0].Nome
            : null;
    }

    /// <summary>
    /// O campo que o modelo põe nesta linha é o que <b>melhor</b> casa com ela?
    ///
    /// <para><b>Por que não basta dividir uma palavra.</b> Metade das linhas de perícia termina
    /// com o atributo — "(Sab)", "(Int)" —, e essa palavra sozinha fazia qualquer perícia do mesmo
    /// atributo passar por certa: o campo impresso em "Medicina (Sab)" era aceito na linha
    /// "Percepção (Sab)". O modelo errado sobrevivia exatamente nas linhas em que o erro trocava
    /// perícias do mesmo atributo, e a correção automática, ao consertar a linha vizinha, deixava
    /// o campo usado duas vezes.</para>
    /// </summary>
    private static bool CasaComALinha(
        CampoDaFicha campo,
        LinhaDoModelo linha,
        IReadOnlyList<CampoDaFicha> daFicha,
        IReadOnlySet<(int Pagina, int Linha, string Rotulo)> confirmados)
    {
        var candidatos = Candidatos(campo, linha, daFicha, confirmados);
        var proprio = candidatos.FindIndex(candidato => candidato.Nome == campo.Nome);

        return proprio >= 0
            && !candidatos.Any(outro => outro.Nome != campo.Nome && Supera(outro, candidatos[proprio]));
    }

    /// <summary>
    /// Os campos do mesmo tipo que podem estar impressos nesta linha, do que mais casa ao que
    /// menos casa. O peso é quantas palavras o rótulo impresso de cada um divide com a linha.
    ///
    /// <para>Contar, e não só perguntar se divide alguma, é o que separa o campo certo dos
    /// vizinhos: metade das perícias tem "(Int)" no rótulo, e por uma palavra dessas todas
    /// empatariam. Só entram candidatos do mesmo tipo: numa linha "[caixa] [valor] Rótulo" os
    /// dois casam com o rótulo igualmente bem, e sugerir o valor para consertar uma caixa de
    /// marcação estragaria o desenho. E a régua é <see cref="PodeJulgar"/>, não a estrita: numa
    /// linha dessas a caixa certa também tem o rótulo distante.</para>
    /// </summary>
    private static List<(string Nome, int Peso, bool Colado)> Candidatos(
        CampoDaFicha campo,
        LinhaDoModelo linha,
        IReadOnlyList<CampoDaFicha> daFicha,
        IReadOnlySet<(int Pagina, int Linha, string Rotulo)> confirmados) =>
        daFicha
            .Where(outro => outro.DeMarcacao == campo.DeMarcacao)
            .Where(outro => PodeJulgar(outro, confirmados))
            .Select(outro => (outro.Nome, Peso: Palavras(outro.Rotulo!).Count(linha.Palavras.Contains), Colado: RotuloConfiavel(outro)))
            .Where(candidato => candidato.Peso > 0)
            .GroupBy(candidato => candidato.Nome, StringComparer.Ordinal)
            .Select(grupo => (Nome: grupo.Key, Peso: grupo.Max(candidato => candidato.Peso), Colado: grupo.Any(candidato => candidato.Colado)))
            .OrderByDescending(candidato => candidato.Peso)
            .ThenByDescending(candidato => candidato.Colado)
            .ToList();

    /// <summary>
    /// Um candidato vence o outro: casa com mais palavras da linha, ou casa com as mesmas e tem o
    /// rótulo <b>colado</b> enquanto o outro só o tem por confirmação.
    ///
    /// <para>O desempate existe por causa das fileiras de caixas, como os testes contra a morte:
    /// "SUCESSOS" está colado na primeira caixa e, na mesma linha, as outras duas o exibem de
    /// longe. Pelas palavras as três empatam, e a linha "SUCESSOS [caixa]" do desenho é da
    /// primeira — as outras são as linhas sem rótulo logo abaixo dela.</para>
    /// </summary>
    private static bool Supera((string Nome, int Peso, bool Colado) um, (string Nome, int Peso, bool Colado) outro) =>
        um.Peso > outro.Peso || (um.Peso == outro.Peso && um.Colado && !outro.Colado);

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
