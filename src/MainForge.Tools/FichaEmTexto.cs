using System.Text;
using System.Text.RegularExpressions;
using MainForge.Core;

namespace MainForge.Tools;

/// <summary>
/// Desenha a ficha de um personagem em texto corrido, a partir do
/// <c>Ficha-ModeloEmTexto.md</c> do sistema e dos valores guardados no dossiê dele.
///
/// <para><b>Por que em C#.</b> É exatamente o desenho que o Dungeon Master mostra na conversa
/// antes de gerar o PDF — só que ali ele custa cota: o agente lê o modelo, substitui marcador por
/// marcador e escreve o resultado inteiro num turno. Depois que o personagem está pronto, os
/// valores já estão no <c>personagem.json</c> e o modelo já está no disco; refazer a substituição
/// aqui é a mesma ficha por zero token. É a regra 11 da estrutura do projeto no sentido inverso:
/// o que o C# consegue montar de graça não vira conversa.</para>
///
/// <para><b>O que ele não faz.</b> Não interpreta nada e não calcula nada — o que sai é o que
/// está gravado nos campos, exatamente como iria para o PDF. Fórmula de valor derivado é assunto
/// do <c>Ficha-Mapeamento.md</c>, e quem a aplica é o agente, na hora de decidir o valor.</para>
/// </summary>
public static class FichaEmTexto
{
    /// <summary>
    /// Um marcador do modelo. O nome vai como está entre as chaves, inclusive espaços: há campo
    /// de AcroForm chamado <c>"Race "</c>, com espaço no fim, e é esse o nome que casa com o
    /// dossiê.
    /// </summary>
    private static readonly Regex Marcador = new(@"\{\{([^{}]*)\}\}", RegexOptions.Compiled);

    /// <summary>Onde mora o modelo em texto de um sistema.</summary>
    public static string CaminhoDoModelo(CaminhosDoProjeto caminhos, string sistema) =>
        Path.Combine(
            CaminhosDoProjeto.ResolverDentroDe(caminhos.Conhecimento, sistema),
            SistemaRpg.NomeDoModeloEmTexto);

    /// <summary>
    /// A ficha do personagem desenhada com os valores dele, ou <c>null</c> quando o sistema não
    /// tem modelo em texto — situação real: sistema processado por uma versão antiga do
    /// Configurador, ou que chegou por pacote incompleto.
    /// </summary>
    public static string? Montar(CaminhosDoProjeto caminhos, Personagem personagem)
    {
        var caminho = CaminhoDoModelo(caminhos, personagem.Sistema);

        if (!File.Exists(caminho))
        {
            return null;
        }

        string modelo;

        try
        {
            modelo = File.ReadAllText(caminho);
        }
        catch (IOException)
        {
            return null;
        }

        return Desenhar(
            modelo,
            personagem.Campos,
            PreenchedorDeFicha.ListarCamposDeMarcacao(caminhos, personagem.Sistema));
    }

    /// <summary>
    /// Substitui os marcadores do modelo pelos valores informados.
    /// </summary>
    /// <param name="camposDeMarcacao">
    /// Os campos que são caixa de marcação na ficha em PDF. Podem vir vazios: aí todo valor é
    /// tratado como texto.
    /// </param>
    public static string Desenhar(
        string modelo,
        IReadOnlyDictionary<string, string> campos,
        IReadOnlySet<string> camposDeMarcacao)
    {
        var desenhos = Desenhos(modelo);

        if (desenhos.Count == 0)
        {
            return "";
        }

        var valores = Indexar(campos);
        var marcacoes = camposDeMarcacao.Select(Achatar).ToHashSet(StringComparer.Ordinal);

        return string.Join(
            "\n\n",
            desenhos.Select(desenho => PreencherDesenho(desenho, valores, marcacoes)));
    }

    /// <summary>
    /// Os blocos de código do modelo que formam o desenho da ficha.
    ///
    /// <para><b>Por que parar no primeiro <c>##</c>.</b> O desenho é o corpo do arquivo, e o que
    /// vem depois da primeira seção é sempre outra coisa: o "exemplo preenchido" com valores
    /// fictícios, a "versão compacta" para personagem de 1º nível, as observações de uso.
    /// Concatenar tudo mostraria a ficha do Bran de Pedravale embaixo da ficha do personagem de
    /// quem está olhando.</para>
    ///
    /// <para>Um modelo que não siga essa forma cai na regra de reserva: valem os blocos que têm
    /// marcador dentro, porque só o desenho os tem.</para>
    ///
    /// <para>É público porque <see cref="ConferenciaDaFicha"/> precisa conferir exatamente os
    /// blocos que vão virar ficha, e não o arquivo inteiro: repetir a regra lá deixaria a
    /// conferência opinando sobre um trecho que ninguém desenha.</para>
    /// </summary>
    public static IReadOnlyList<string> Desenhos(string modelo) =>
        [.. BlocosDoDesenho(modelo).Select(bloco => string.Join('\n', bloco.Select(linha => linha.Texto)).TrimEnd('\n'))];

    /// <summary>
    /// Os mesmos blocos de <see cref="Desenhos"/>, linha a linha e cada uma com a posição dela no
    /// arquivo (a partir de zero). Existe para quem precisa <b>corrigir</b> o modelo, e não só
    /// lê-lo: <see cref="ConferenciaDaFicha"/> troca o campo de uma linha errada no próprio
    /// arquivo, e para isso precisa saber qual linha do arquivo ela é.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<(int Indice, string Texto)>> BlocosDoDesenho(string modelo)
    {
        var doCorpo = BlocosDeCodigo(modelo, pararNaPrimeiraSecao: true);

        if (doCorpo.Count > 0)
        {
            return doCorpo;
        }

        return [.. BlocosDeCodigo(modelo, pararNaPrimeiraSecao: false)
            .Where(bloco => bloco.Any(linha => Marcador.IsMatch(linha.Texto)))];
    }

    private static List<IReadOnlyList<(int Indice, string Texto)>> BlocosDeCodigo(string modelo, bool pararNaPrimeiraSecao)
    {
        var blocos = new List<IReadOnlyList<(int, string)>>();
        List<(int, string)>? dentroDoBloco = null;
        var linhas = modelo.ReplaceLineEndings("\n").Split('\n');

        for (var indice = 0; indice < linhas.Length; indice++)
        {
            var linha = linhas[indice];

            if (linha.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (dentroDoBloco is null)
                {
                    dentroDoBloco = [];
                }
                else
                {
                    blocos.Add(dentroDoBloco);
                    dentroDoBloco = null;
                }

                continue;
            }

            if (dentroDoBloco is not null)
            {
                dentroDoBloco.Add((indice, linha));
                continue;
            }

            if (pararNaPrimeiraSecao && linha.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }
        }

        // Cerca não fechada é modelo malformado, e o que já foi lido continua sendo o desenho:
        // descartá-lo trocaria uma ficha torta por nenhuma ficha.
        if (dentroDoBloco is not null)
        {
            blocos.Add(dentroDoBloco);
        }

        return blocos;
    }

    /// <summary>
    /// Troca os marcadores de um bloco <b>sem desmontar o desenho</b>.
    ///
    /// <para>O modelo é arte de texto: as bordas, as colunas e os pontilhados só se sustentam
    /// enquanto cada valor ocupa a largura do marcador que ele substitui. Valor curto é completado
    /// com espaços; valor longo come os espaços que vêm logo depois dele, deixando ao menos um
    /// para não colar no que vem em seguida. Quando nem isso basta, a linha cresce — texto
    /// completo com a borda deslocada é melhor que valor cortado.</para>
    /// </summary>
    private static string PreencherDesenho(
        string desenho,
        IReadOnlyDictionary<string, string> valores,
        IReadOnlySet<string> marcacoes)
    {
        var linhas = desenho.Split('\n').Select(linha => PreencherLinha(linha, valores, marcacoes));

        return string.Join("\n", linhas);
    }

    private static string PreencherLinha(
        string linha,
        IReadOnlyDictionary<string, string> valores,
        IReadOnlySet<string> marcacoes)
    {
        var achados = Marcador.Matches(linha);

        if (achados.Count == 0)
        {
            return linha.TrimEnd();
        }

        var resultado = new StringBuilder(linha.Length);
        var posicao = 0;

        foreach (Match achado in achados)
        {
            resultado.Append(linha, posicao, achado.Index - posicao);
            posicao = achado.Index + achado.Length;

            var nome = achado.Groups[1].Value;

            // Caixa de marcação não é completada com espaços: o marcador tem o comprimento do
            // nome do campo ("{{Check Box 12}}"), e completar até ele transformaria o "[X]" do
            // desenho num quadro de dezoito casas.
            if (marcacoes.Contains(Achatar(nome)))
            {
                resultado.Append(Marcado(Buscar(nome, valores)) ? 'X' : ' ');
                continue;
            }

            var valor = UmaLinhaSo(Buscar(nome, valores));
            resultado.Append(valor);

            var folga = achado.Length - valor.Length;

            if (folga > 0)
            {
                resultado.Append(' ', folga);
            }
            else if (folga < 0)
            {
                posicao += EspacosAConsumir(linha, posicao, -folga);
            }
        }

        resultado.Append(linha, posicao, linha.Length - posicao);

        return resultado.ToString().TrimEnd();
    }

    /// <summary>
    /// Quantos espaços logo depois do marcador dá para engolir sem colar o valor no que vem
    /// adiante. Se só há espaço até o fim da linha, todos servem — não há nada em que colar.
    /// </summary>
    private static int EspacosAConsumir(string linha, int inicio, int quanto)
    {
        var fim = inicio;

        while (fim < linha.Length && linha[fim] == ' ')
        {
            fim++;
        }

        var disponiveis = fim - inicio;

        if (fim < linha.Length)
        {
            disponiveis = Math.Max(0, disponiveis - 1);
        }

        return Math.Min(quanto, disponiveis);
    }

    /// <summary>
    /// O valor de um campo. A busca é pelo nome exato primeiro, porque é ele que o PDF usa; a
    /// forma achatada é a rede de segurança para a diferença que não muda o campo — o espaço
    /// sobrando no fim do nome, a maiúscula trocada — e que sozinha faria o marcador sair vazio.
    /// </summary>
    private static string Buscar(string nome, IReadOnlyDictionary<string, string> valores) =>
        valores.TryGetValue(nome, out var exato) ? exato
            : valores.TryGetValue(Achatar(nome), out var achatado) ? achatado
            : "";

    /// <summary>
    /// Os valores por nome exato e, junto, por nome achatado. Nome achatado repetido fica com o
    /// primeiro: são dois campos diferentes no PDF, e escolher entre eles pelo palpite seria
    /// pior que só acertar um.
    /// </summary>
    private static Dictionary<string, string> Indexar(IReadOnlyDictionary<string, string> campos)
    {
        var indice = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (campo, valor) in campos)
        {
            indice[campo] = valor;
        }

        foreach (var (campo, valor) in campos)
        {
            indice.TryAdd(Achatar(campo), valor);
        }

        return indice;
    }

    /// <summary>
    /// Quebra de linha não cabe numa linha de arte de texto: ela viraria uma linha solta fora do
    /// quadro, quebrando o desenho a partir dali. Vira espaço, como já acontece num campo de uma
    /// linha só do próprio PDF (<see cref="PreenchedorDeFicha"/>).
    /// </summary>
    private static string UmaLinhaSo(string valor) => valor.ReplaceLineEndings(" ").Trim();

    /// <summary>
    /// A caixa está marcada. Vale o vocabulário que <see cref="PreenchedorDeFicha"/> aceita na
    /// ida — <c>true</c>, <c>Yes</c>, o nome do estado no PDF — porque é dele que estes valores
    /// vieram: o que não é reconhecidamente "desmarcado" está marcado.
    /// </summary>
    private static bool Marcado(string valor)
    {
        var texto = valor.Trim().TrimStart('/');

        return texto.Length > 0 && Achatar(texto) switch
        {
            "false" or "0" or "nao" or "no" or "off" or "desmarcado" or "-" => false,
            _ => true,
        };
    }

    private static string Achatar(string texto) =>
        TextoNormalizado.SemAcento(texto).Trim().ToLowerInvariant();
}
