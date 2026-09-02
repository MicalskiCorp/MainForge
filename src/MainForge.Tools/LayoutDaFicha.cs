using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace MainForge.Tools;

/// <summary>Onde está o texto impresso em relação ao campo que ele rotula.</summary>
public enum DirecaoDoRotulo
{
    /// <summary>Não há texto impresso perto o bastante para rotular o campo.</summary>
    Nenhum,

    /// <summary>Na mesma linha, à direita — é como a ficha de D&amp;D rotula perícias e testes.</summary>
    Direita,

    /// <summary>Na mesma linha, à esquerda.</summary>
    Esquerda,

    /// <summary>Acima do campo, como o nome de um quadro.</summary>
    Acima,

    /// <summary>Abaixo do campo, que é onde ficam as legendas dos quadros do cabeçalho.</summary>
    Abaixo,
}

/// <summary>
/// Um campo preenchível da ficha, com o lugar em que ele fica impresso.
/// </summary>
/// <param name="Nome">O nome exato do campo, como <c>preencher_ficha_personagem</c> o exige.</param>
/// <param name="Pagina">A página em que ele aparece, começando em 1.</param>
/// <param name="Linha">
/// A linha da página em que ele está, contada de cima para baixo. Campos que dividem a linha
/// têm o mesmo número — é o que mostra que uma caixa de marcação e um campo de texto formam
/// uma só entrada da ficha.
/// </param>
/// <param name="Ordem">A posição dele na leitura da página: 1 é o primeiro de cima.</param>
/// <param name="Rotulo">
/// O texto impresso mais próximo, que é o que diz para que serve o campo. <c>null</c> quando não
/// há nenhum perto o bastante.
/// </param>
/// <param name="Direcao">De que lado do campo <paramref name="Rotulo"/> está.</param>
/// <param name="DistanciaDoRotulo">
/// Quantos pontos separam o campo do rótulo. Vai junto de propósito: perto de zero o pareamento
/// é certo, e um valor alto avisa que o texto pode ser de outra coluna — melhor do que entregar
/// um palpite com cara de fato.
/// </param>
/// <param name="DeMarcacao">É caixa de marcação, e não campo de texto.</param>
/// <param name="DeVariasLinhas">
/// É um campo de texto que aceita várias linhas (<c>/Ff</c> com o bit de multilinha). Vai junto
/// porque é o que separa "cabe um nome" de "cabe uma descrição": uma ficha que traz as magias por
/// extenso tem um quadro desses, e uma que só tem espaço para a lista de nomes não tem nenhum.
/// </param>
/// <param name="Altura">
/// A altura do campo em pontos. Com <paramref name="DeVariasLinhas"/>, é o que diz quantas linhas
/// de texto cabem ali de verdade — um campo multilinha de 12 pt é uma linha só com a marca errada.
/// </param>
public sealed record CampoDaFicha(
    string Nome,
    int Pagina,
    int Linha,
    int Ordem,
    string? Rotulo,
    DirecaoDoRotulo Direcao,
    double DistanciaDoRotulo,
    bool DeMarcacao,
    bool DeVariasLinhas = false,
    double Altura = 0);

/// <summary>
/// Lê a ficha em branco de um sistema e devolve os campos preenchíveis <b>na ordem em que estão
/// impressos</b>, cada um com o rótulo que aparece na mesma linha.
///
/// <para><b>Por que a lista de nomes não bastava.</b> O nome de um campo de AcroForm não diz onde
/// ele fica nem para que serve, e num formulário grande os dois costumam divergir: caixas de
/// marcação chamadas <c>Check Box 11</c> a <c>Check Box 40</c> não estão nessa ordem na página,
/// e uma ficha traduzida pode ter os rótulos reordenados no idioma novo sem que os campos tenham
/// saído do lugar. Quem só recebe os nomes precisa adivinhar o pareamento — e o palpite entra no
/// <c>Ficha-Mapeamento.md</c> como se fosse fato, para ser descoberto muito depois, num PDF em
/// que Percepção mostra o valor de Persuasão.</para>
///
/// <para>Responder a pergunta inteira aqui é a regra 11 da estrutura do projeto: o que o C#
/// consegue ler de graça não deve virar turno de conversa nem, pior, virar dedução do modelo.</para>
/// </summary>
public static class LayoutDaFicha
{
    // Ler um campo de texto de AcroForm já exige fonte: o PdfSharp reconstrói a aparência dele
    // a partir do /DA. Sem isto, só abrir a ficha lança "No appropriate font found".
    static LayoutDaFicha()
    {
        GlobalFontSettings.FontResolver ??= new ResolvedorDeFontesDoWindows();
    }

    /// <summary>
    /// Quanto dois elementos podem diferir na vertical e ainda estarem na mesma linha da ficha.
    /// O retângulo de uma caixa de marcação e o do campo de texto ao lado dela raramente têm a
    /// mesma base — na ficha de D&amp;D a diferença chega a 4 pt na mesma linha.
    /// </summary>
    private const double ToleranciaDaLinha = 6.0;

    /// <summary>
    /// Quanto o <b>meio</b> de um texto impresso pode ficar acima ou abaixo do meio de um campo e
    /// os dois ainda serem a mesma linha da ficha.
    ///
    /// <para><b>Por que o meio, e não a base.</b> Comparar as bases parece a mesma coisa e não é:
    /// a base de um texto é a linha em que as letras se apoiam, e a de um campo é a borda de baixo
    /// do quadro, que desce mais quanto mais alto for o campo. Num campo de 8 pt (uma perícia) as
    /// duas quase coincidem; num de 17 pt (o bônus de proficiência, a sabedoria passiva, as
    /// moedas) o rótulo fica 6 a 7 pt acima da base do quadro — pouco mais que a tolerância —, e o
    /// campo era dado como <b>sem rótulo nenhum</b>.
    ///
    /// Isso não errava um pareamento: apagava a possibilidade de conferir. Na ficha de D&amp;D 5e
    /// em português eram sete campos, e os sete são exatamente os que ninguém confere de olho —
    /// a sabedoria passiva, o bônus de proficiência e as cinco caixas de moeda, em que o nome
    /// interno é a abreviação em inglês e o rótulo impresso é a em português (<c>SP</c>, de
    /// <i>silver</i>, impresso ao lado de "PP"; <c>PP</c>, de <i>platinum</i>, ao lado de "PL").
    /// Sem rótulo, <see cref="ConferenciaDaFicha"/> se cala e o mapeamento fica valendo por
    /// palpite — que é a única coisa que este arquivo existe para não deixar acontecer.</para>
    ///
    /// <para>O meio resolve os dois lados sem afrouxar nada: num campo alto de verdade (o quadro
    /// de "Características e Habilidades", 370 pt) o meio fica longe de qualquer legenda, e o
    /// rótulo continua vindo da busca vertical, acima ou abaixo — como sempre veio.</para>
    /// </summary>
    private const double ToleranciaDoMeio = 6.0;

    /// <summary>
    /// Lacuna horizontal a partir da qual duas palavras deixam de ser o mesmo rótulo. Menor que
    /// isso é espaço entre palavras ("Lidar com Animais"); maior é outra coluna da ficha.
    /// </summary>
    private const double LacunaEntreRotulos = 12.0;

    /// <summary>
    /// Até onde, na mesma linha, um texto ainda pode ser o rótulo do campo. Passando disso ele
    /// quase certamente é de outra coluna: numa ficha em três colunas, "mesma linha" não quer
    /// dizer "mesma coisa".
    /// </summary>
    private const double AlcanceNaLinha = 40.0;

    /// <summary>
    /// Até que distância acima ou abaixo do campo ainda vale procurar um título, quando não há
    /// nada na mesma linha. É o caso do quadro rotulado por fora, como "NOME DO PERSONAGEM"
    /// (abaixo) e "FORÇA" (acima).
    /// </summary>
    private const double AlcanceVertical = 30.0;

    /// <summary>
    /// Os campos da ficha em branco de <paramref name="caminhoDaFicha"/>, em ordem de leitura.
    /// </summary>
    /// <exception cref="ErroDeFerramenta">A ficha não abre ou não tem formulário.</exception>
    public static IReadOnlyList<CampoDaFicha> Ler(string caminhoDaFicha)
    {
        var rotulos = LerRotulos(caminhoDaFicha);
        var widgets = LerWidgets(caminhoDaFicha);
        var campos = new List<CampoDaFicha>();

        foreach (var pagina in widgets.Keys.Order())
        {
            var daPagina = rotulos.GetValueOrDefault(pagina, []);
            var legendados = JaLegendam(daPagina, widgets[pagina]);
            var ordem = 0;
            var numeroDaLinha = 0;

            foreach (var linha in EmLinhas(widgets[pagina]))
            {
                numeroDaLinha++;

                foreach (var widget in linha)
                {
                    var (rotulo, direcao, distancia) = RotuloDe(widget, daPagina, legendados);

                    campos.Add(new CampoDaFicha(
                        widget.Nome,
                        pagina,
                        numeroDaLinha,
                        ++ordem,
                        rotulo,
                        direcao,
                        distancia,
                        widget.DeMarcacao,
                        widget.DeVariasLinhas,
                        widget.Altura));
                }
            }
        }

        return campos;
    }

    private sealed record Widget(
        string Nome,
        double Base,
        double Esquerda,
        double Largura,
        double Altura,
        bool DeMarcacao,
        bool DeVariasLinhas = false)
    {
        public double Meio => Base + (Altura / 2);

        public double Direita => Esquerda + Largura;
    }

    private sealed record Rotulo(string Texto, double Base, double Topo, double Esquerda, double Direita)
    {
        public double Meio => (Base + Topo) / 2;
    }

    /// <summary>
    /// Agrupa os campos como alguém lê a página: de cima para baixo e, dentro da mesma linha, da
    /// esquerda para a direita. A linha é uma faixa, não uma coordenada — ver
    /// <see cref="ToleranciaDaLinha"/>, porque o retângulo de uma caixa de marcação e o do campo
    /// de texto ao lado dela raramente têm a mesma base.
    /// </summary>
    private static IEnumerable<List<Widget>> EmLinhas(List<Widget> daPagina)
    {
        var restantes = daPagina.OrderByDescending(w => w.Meio).ToList();

        for (var i = 0; i < restantes.Count;)
        {
            var j = i + 1;

            while (j < restantes.Count && Math.Abs(restantes[j].Meio - restantes[i].Meio) <= ToleranciaDaLinha)
            {
                j++;
            }

            yield return [.. restantes[i..j].OrderBy(w => w.Esquerda)];

            i = j;
        }
    }

    /// <summary>
    /// O rótulo de um campo: o texto impresso mais próximo dele.
    ///
    /// <para>A ficha ora põe o nome depois da caixa (perícias, testes de resistência), ora antes,
    /// ora fora do quadro — acima ("FORÇA") ou abaixo ("NOME DO PERSONAGEM"). Por isso as quatro
    /// direções são consideradas e vence a mais próxima, com a distância indo junto na resposta:
    /// numa ficha de três colunas, "mesma linha" não significa "mesma coisa", e quem lê precisa
    /// poder desconfiar de um rótulo distante.</para>
    /// </summary>
    private static (string? Texto, DirecaoDoRotulo Direcao, double Distancia) RotuloDe(
        Widget widget,
        List<Rotulo> daPagina,
        IReadOnlyDictionary<Rotulo, double> legendados)
    {
        var candidatos = new List<(Rotulo Rotulo, DirecaoDoRotulo Direcao, double Distancia)>();

        foreach (var rotulo in daPagina)
        {
            if (Math.Abs(rotulo.Meio - widget.Meio) <= ToleranciaDoMeio)
            {
                var lacuna = rotulo.Esquerda >= widget.Direita ? rotulo.Esquerda - widget.Direita
                    : rotulo.Direita <= widget.Esquerda ? widget.Esquerda - rotulo.Direita
                    : 0;

                // Texto que já é a legenda de outro campo, e mais perto dele do que daqui, não
                // está disponível: "IDADE" fica logo abaixo da caixa de idade e logo à direita da
                // caixa larga do nome do personagem, e sem esta regra a segunda o tomava da
                // primeira — passando a chamar de "idade" o campo do nome.
                var deOutro = legendados.TryGetValue(rotulo, out var doDono) && doDono < lacuna;

                if (lacuna <= AlcanceNaLinha && !deOutro)
                {
                    candidatos.Add((
                        rotulo,
                        rotulo.Esquerda >= widget.Direita ? DirecaoDoRotulo.Direita : DirecaoDoRotulo.Esquerda,
                        lacuna));
                }

                continue;
            }

            // Fora da linha, só vale o texto que passa por cima ou por baixo do campo.
            if (rotulo.Direita <= widget.Esquerda || rotulo.Esquerda >= widget.Direita)
            {
                continue;
            }

            var acima = rotulo.Base > widget.Base;
            var vao = acima ? rotulo.Base - (widget.Base + widget.Altura) : widget.Base - rotulo.Base;

            if (vao <= AlcanceVertical)
            {
                candidatos.Add((
                    rotulo,
                    acima ? DirecaoDoRotulo.Acima : DirecaoDoRotulo.Abaixo,
                    Math.Max(0, vao)));
            }
        }

        if (candidatos.Count == 0)
        {
            return (null, DirecaoDoRotulo.Nenhum, 0);
        }

        var melhor = candidatos.MinBy(candidato => candidato.Distancia);

        return (melhor.Rotulo.Texto, melhor.Direcao, Math.Round(melhor.Distancia, 1));
    }

    /// <summary>
    /// Quão perto, na vertical, um texto precisa estar de uma caixa para ser <b>a legenda dela</b>
    /// — e não apenas um texto que passa por perto. Legenda vem colada; o alcance generoso de
    /// <see cref="AlcanceVertical"/> serve para <em>achar</em> um rótulo quando não há nenhum
    /// outro, que é uma afirmação bem mais fraca do que <em>reservar</em> um.
    /// </summary>
    private const double VaoDeLegenda = 10.0;

    /// <summary>
    /// Quanto um texto pode transbordar a largura da caixa e ainda ser legenda dela. Legenda é
    /// alinhada com o que ela nomeia; texto que atravessa a caixa e segue adiante está falando de
    /// outra coisa — na ficha de D&amp;D, "SABEDORIA PASSIVA (PERCEPÇÃO)" passa por cima do quadro
    /// de proficiências e sai 50 pt fora dele, e não legenda coisa nenhuma ali.
    /// </summary>
    private const double TransbordoDeLegenda = 4.0;

    /// <summary>
    /// Para cada texto impresso que é <b>legenda</b> de algum campo — colado acima ou abaixo dele
    /// e alinhado com ele —, a menor distância a que ele legenda alguém.
    ///
    /// <para><b>Para que serve.</b> Legenda é a forma menos ambígua de rotular: fica grudada na
    /// caixa e contida na largura dela. Um texto assim não é candidato a rotular a caixa <em>ao
    /// lado</em>, por mais perto que ela esteja — e sem essa reserva um campo largo alcança
    /// lateralmente a legenda do vizinho, que foi o que passou a acontecer quando a régua da mesma
    /// linha deixou de comparar bases e passou a comparar meios: a caixa do nome do personagem, com
    /// 208 pt de largura, tomou o "IDADE" da caixa de idade que fica 9,8 pt à direita dela.</para>
    ///
    /// <para>Só a distância menor reserva: um texto que legenda uma caixa a 8 pt continua
    /// disponível para o campo que o tem a 5 pt do lado, porque aí a legenda é que era o palpite
    /// pior.</para>
    /// </summary>
    private static Dictionary<Rotulo, double> JaLegendam(List<Rotulo> daPagina, List<Widget> widgets)
    {
        var legendas = new Dictionary<Rotulo, double>();

        foreach (var rotulo in daPagina)
        {
            foreach (var widget in widgets)
            {
                // Contido na largura da caixa, e não só encostando nela.
                if (rotulo.Esquerda < widget.Esquerda - TransbordoDeLegenda
                    || rotulo.Direita > widget.Direita + TransbordoDeLegenda)
                {
                    continue;
                }

                var acima = rotulo.Base > widget.Base;
                var vao = acima ? rotulo.Base - (widget.Base + widget.Altura) : widget.Base - rotulo.Topo;

                if (vao < 0 || vao > VaoDeLegenda)
                {
                    continue;
                }

                if (!legendas.TryGetValue(rotulo, out var menor) || vao < menor)
                {
                    legendas[rotulo] = vao;
                }
            }
        }

        return legendas;
    }

    private static Dictionary<int, List<Widget>> LerWidgets(string caminhoDaFicha)
    {
        using var documento = PdfReader.Open(caminhoDaFicha, PdfDocumentOpenMode.Modify);

        var formulario = FormularioDeFicha.Obter(documento)
            ?? throw new ErroDeFerramenta(
                $"'{Path.GetFileName(caminhoDaFicha)}' não tem campos de formulário (AcroForm). " +
                "A ficha precisa ser um PDF editável/preenchível, não um PDF só de leitura ou digitalizado.");

        var paginaDe = new Dictionary<PdfObjectID, int>();

        for (var i = 0; i < documento.PageCount; i++)
        {
            if (documento.Pages[i].Elements.GetArray("/Annots") is not { } anotacoes)
            {
                continue;
            }

            for (var j = 0; j < anotacoes.Elements.Count; j++)
            {
                if (anotacoes.Elements.GetReference(j)?.ObjectID is { } identificador)
                {
                    paginaDe[identificador] = i + 1;
                }
            }
        }

        var porPagina = new Dictionary<int, List<Widget>>();

        void Percorrer(PdfAcroField campo)
        {
            // Campo com filhos é um grupo: quem tem posição na página é cada widget dele.
            if (campo.HasKids && campo.Fields.Count > 0)
            {
                for (var i = 0; i < campo.Fields.Count; i++)
                {
                    Percorrer(campo.Fields[i]);
                }

                return;
            }

            if (campo.Reference?.ObjectID is not { } identificador
                || !paginaDe.TryGetValue(identificador, out var pagina))
            {
                return;
            }

            var retangulo = campo.Elements.GetRectangle("/Rect");

            porPagina.TryAdd(pagina, []);
            porPagina[pagina].Add(new Widget(
                campo.Name ?? "",
                retangulo.Y1,
                retangulo.X1,
                retangulo.Width,
                retangulo.Height,
                campo is PdfCheckBoxField,
                campo is PdfTextField { MultiLine: true }));
        }

        for (var i = 0; i < formulario.Fields.Count; i++)
        {
            Percorrer(formulario.Fields[i]);
        }

        return porPagina;
    }

    private static Dictionary<int, List<Rotulo>> LerRotulos(string caminhoDaFicha)
    {
        var porPagina = new Dictionary<int, List<Rotulo>>();

        using var documento = UglyToad.PdfPig.PdfDocument.Open(caminhoDaFicha);

        foreach (var pagina in documento.GetPages())
        {
            porPagina[pagina.Number] = pagina.GetWords()
                .GroupBy(palavra => Math.Round(palavra.BoundingBox.Bottom / ToleranciaDaLinha))
                .SelectMany(linha => Agrupar([.. linha.OrderBy(p => p.BoundingBox.Left)]))
                .ToList();
        }

        return porPagina;
    }

    /// <summary>
    /// Junta as palavras vizinhas de uma linha num rótulo só, para que "Lidar com Animais" chegue
    /// inteiro e não como três candidatos disputando o mesmo campo.
    /// </summary>
    private static IEnumerable<Rotulo> Agrupar(List<Word> palavras)
    {
        var texto = new List<string>();
        double inicio = 0, baseDaLinha = 0, topoDaLinha = 0, fim = 0;

        foreach (var palavra in palavras)
        {
            if (texto.Count > 0 && palavra.BoundingBox.Left - fim > LacunaEntreRotulos)
            {
                yield return new Rotulo(string.Join(' ', texto), baseDaLinha, topoDaLinha, inicio, fim);
                texto.Clear();
            }

            if (texto.Count == 0)
            {
                inicio = palavra.BoundingBox.Left;
                baseDaLinha = palavra.BoundingBox.Bottom;
                topoDaLinha = palavra.BoundingBox.Top;
            }

            // O topo é o da palavra mais alta do grupo: num rótulo com maiúscula e parêntese
            // ("Sabedoria Passiva (Percepção)") as palavras não têm todas a mesma altura, e é a
            // caixa inteira que diz onde o texto está.
            topoDaLinha = Math.Max(topoDaLinha, palavra.BoundingBox.Top);
            baseDaLinha = Math.Min(baseDaLinha, palavra.BoundingBox.Bottom);

            texto.Add(palavra.Text);
            fim = palavra.BoundingBox.Right;
        }

        if (texto.Count > 0)
        {
            yield return new Rotulo(string.Join(' ', texto), baseDaLinha, topoDaLinha, inicio, fim);
        }
    }
}
