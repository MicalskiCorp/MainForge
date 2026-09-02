using System.Text;
using MainForge.Core;

namespace MainForge.Tools;

/// <summary>Por que um valor do personagem não passa nas regras do sistema.</summary>
public enum TipoDaViolacao
{
    /// <summary>Campo obrigatório sem valor. O personagem está incompleto, não errado.</summary>
    CampoVazio,

    /// <summary>O campo não existe na ficha em branco do sistema — a ficha é de outra edição.</summary>
    CampoDesconhecido,

    /// <summary>Onde a regra pede número veio outra coisa.</summary>
    NaoEhNumero,

    /// <summary>O número está fora da faixa que o sistema permite.</summary>
    ForaDaFaixa,

    /// <summary>O valor não é nenhum dos que a regra lista (classe, raça, tendência).</summary>
    ValorNaoPrevisto,

    /// <summary>Caixa de marcação com um valor que não é nem marcado nem desmarcado.</summary>
    MarcacaoInvalida,

    /// <summary>
    /// Uma magia escrita sem o nome em inglês, num sistema que não é em português. Ver
    /// <see cref="NomesDeMagia"/>.
    /// </summary>
    MagiaSemNomeEmIngles,
}

/// <summary>Um valor do personagem que não bate com as regras do sistema.</summary>
/// <param name="Campo">O nome do campo no PDF.</param>
/// <param name="Rotulo">Como aquele campo se chama na ficha — é o que o usuário reconhece.</param>
/// <param name="Tipo">O que está errado.</param>
/// <param name="Valor">O que estava escrito ali.</param>
/// <param name="Detalhe">A explicação pronta, já em português.</param>
public sealed record ViolacaoDaFicha(
    string Campo,
    string Rotulo,
    TipoDaViolacao Tipo,
    string Valor,
    string Detalhe)
{
    /// <summary>Como a violação aparece numa lista, para o usuário ou para o agente.</summary>
    public string Descrever()
    {
        var nome = Rotulo is { Length: > 0 } ? $"{Rotulo} ({Campo})" : Campo;

        return $"{nome}: {Detalhe}";
    }
}

/// <summary>O que a validação achou.</summary>
/// <param name="Violacoes">Os valores fora da regra. Vazio é aprovação.</param>
/// <param name="Regras">As regras aplicadas, ou <c>null</c> quando o sistema não tem nenhuma.</param>
public sealed record ResultadoDaValidacao(
    IReadOnlyList<ViolacaoDaFicha> Violacoes,
    RegrasDaFicha? Regras)
{
    /// <summary>Nenhum valor divergiu das regras.</summary>
    public bool Aprovado => Violacoes.Count == 0;

    /// <summary>
    /// O sistema não tem regras gravadas: não houve validação nenhuma, e dizer "aprovado" seria
    /// mentira por omissão. Quem mostra o resultado precisa saber diferenciar as duas coisas.
    /// </summary>
    public bool SemRegras => Regras is null;

    /// <summary>Uma frase dizendo em que pé ficou, pronta para ir à tela ou ao agente.</summary>
    public string Resumir(string sistema) => (SemRegras, Aprovado) switch
    {
        (true, _) =>
            $"O sistema '{sistema}' ainda não tem regras de validação " +
            $"({SistemaRpg.NomeDaValidacaoDaFicha}) — nada foi conferido.",

        (_, true) =>
            $"Ficha válida em '{sistema}': os {Regras!.Campos.Count} campo(s) com regra estão de acordo.",

        _ => $"{Violacoes.Count} ponto(s) fora das regras de '{sistema}'.",
    };

    /// <summary>O resumo mais a lista, um por linha. É o texto que vai inteiro ao usuário.</summary>
    public string Relatorio(string sistema)
    {
        var texto = new StringBuilder(Resumir(sistema));

        foreach (var violacao in Violacoes)
        {
            texto.AppendLine().Append("  · ").Append(violacao.Descrever());
        }

        return texto.ToString();
    }
}

/// <summary>
/// Confere os valores de um personagem contra as regras do sistema
/// (<see cref="RegrasDaFicha"/>), sem chamar agente nenhum.
///
/// <para><b>Por que existe.</b> Até aqui, a única coisa que sabia se um personagem era válido era
/// o Dungeon Master, dentro de uma conversa. Isso deixava dois buracos: o personagem
/// <b>importado</b> de uma ficha em PDF entrava sem que nada fosse conferido — e a conferência
/// com o agente é opcional e paga —, e o personagem <b>criado</b> dependia de o modelo lembrar de
/// checar cada faixa e cada lista fechada, coisa que ele faz na maior parte das vezes e não em
/// todas. O que dá para conferir por máquina é conferido por máquina, de graça, nos dois
/// caminhos.</para>
///
/// <para><b>Ela aponta, não impede.</b> Nenhuma violação bloqueia a importação nem a geração da
/// ficha, e isso é escolha, não descuido: a ficha que o usuário trouxe é a que está valendo na
/// mesa dele, e o que parece erro pode ser um combinado do grupo — a mesma razão pela qual o
/// prompt do Dungeon Master manda perguntar antes de corrigir. O que a validação faz é garantir
/// que ninguém descubra o problema tarde demais.</para>
///
/// <para><b>Ela não sabe de regra que precise de julgamento.</b> "Este personagem pode ter esta
/// subclasse?" depende de ler o livro; aqui só entra o que uma máquina decide sozinha — campo
/// vazio, número fora da faixa, valor fora da lista. O resto continua sendo conversa.</para>
/// </summary>
public static class ValidacaoDePersonagem
{
    /// <summary>
    /// Confere os campos de um personagem contra as regras do sistema dele.
    ///
    /// <para>Sistema sem regras devolve um resultado <see cref="ResultadoDaValidacao.SemRegras"/>
    /// em vez de erro: um sistema mapeado por uma versão anterior do aplicativo continua
    /// funcionando, só não é conferido.</para>
    /// </summary>
    public static ResultadoDaValidacao Validar(CaminhosDoProjeto caminhos, Personagem personagem) =>
        Validar(RegrasDaFicha.CarregarOuGerar(caminhos, personagem.Sistema), personagem.Campos);

    /// <summary>
    /// Confere um conjunto de valores por nome de campo — a forma que a importação e o
    /// preenchimento da ficha têm em mãos, antes de existir dossiê.
    /// </summary>
    public static ResultadoDaValidacao Validar(
        CaminhosDoProjeto caminhos,
        string sistema,
        IReadOnlyDictionary<string, string> campos) =>
        Validar(RegrasDaFicha.CarregarOuGerar(caminhos, sistema), campos);

    /// <summary>
    /// O mesmo, com as regras já em mãos. É por aqui que os testes entram, e é o que evita reler
    /// o JSON quando quem chama acabou de carregá-lo.
    /// </summary>
    public static ResultadoDaValidacao Validar(
        RegrasDaFicha? regras,
        IReadOnlyDictionary<string, string> campos)
    {
        if (regras is null)
        {
            return new ResultadoDaValidacao([], null);
        }

        var violacoes = new List<ViolacaoDaFicha>();

        // Pelo nome exato e pela forma achatada, como <see cref="Valor"/> faz na ida. Só pelo nome
        // exato, um campo de AcroForm com espaço sobrando no fim ("Race ") era encontrado na hora
        // de conferir o valor dele e acusado como desconhecido logo abaixo: a mesma diferença
        // tolerada num sentido e não no outro.
        var conhecidos = regras.Campos
            .SelectMany(regra => new[] { regra.Campo, Achatar(regra.Campo) })
            .ToHashSet(StringComparer.Ordinal);

        foreach (var regra in regras.Campos)
        {
            var valor = Valor(campos, regra.Campo);

            if (valor.Length == 0)
            {
                if (regra.Obrigatorio)
                {
                    violacoes.Add(new ViolacaoDaFicha(
                        regra.Campo,
                        regra.Rotulo,
                        TipoDaViolacao.CampoVazio,
                        "",
                        Com("está vazio, e o sistema exige um valor aqui", regra)));
                }

                // Campo vazio não tem tipo, faixa nem lista a conferir: só a obrigatoriedade.
                continue;
            }

            Conferir(regra, valor, violacoes);
        }

        // O campo que a ficha não tem não reprova nada por si — a ficha trazida pode ser de outra
        // edição, e o projeto inteiro já trata isso deixando o valor de fora do PDF. O que ele
        // precisa é aparecer, porque é a explicação de um valor que "sumiu" na hora de gerar.
        foreach (var (campo, valor) in campos.Where(par =>
                     !conhecidos.Contains(par.Key) && !conhecidos.Contains(Achatar(par.Key))))
        {
            violacoes.Add(new ViolacaoDaFicha(
                campo,
                "",
                TipoDaViolacao.CampoDesconhecido,
                valor,
                "não existe na ficha em branco deste sistema — o valor fica no dossiê, mas fora do PDF"));
        }

        violacoes.AddRange(ViolacoesDeMagia(regras, campos));

        return new ResultadoDaValidacao(violacoes, regras);
    }

    private static void Conferir(RegraDeCampo regra, string valor, List<ViolacaoDaFicha> violacoes)
    {
        switch (regra.Tipo)
        {
            case TipoDoCampo.Inteiro:
                ConferirNumero(regra, valor, violacoes);
                break;

            case TipoDoCampo.Marcacao when !EhMarcacaoConhecida(valor):
                violacoes.Add(new ViolacaoDaFicha(
                    regra.Campo,
                    regra.Rotulo,
                    TipoDaViolacao.MarcacaoInvalida,
                    valor,
                    $"é caixa de marcação e recebeu '{Curto(valor)}' — use true/false, ou deixe vazio para desmarcar"));
                break;
        }

        if (regra.Valores.Count > 0 && !regra.Valores.Any(previsto => Iguais(previsto, valor)))
        {
            violacoes.Add(new ViolacaoDaFicha(
                regra.Campo,
                regra.Rotulo,
                TipoDaViolacao.ValorNaoPrevisto,
                valor,
                Com($"tem '{Curto(valor)}', que não é um dos valores deste sistema: {Listar(regra.Valores)}", regra)));
        }
    }

    private static void ConferirNumero(RegraDeCampo regra, string valor, List<ViolacaoDaFicha> violacoes)
    {
        // O sinal vai junto de propósito: metade dos números de uma ficha é modificador, e "+2" é
        // como o próprio Ficha-Mapeamento.md manda escrevê-lo.
        if (!int.TryParse(valor.Trim().TrimStart('+'), out var numero))
        {
            violacoes.Add(new ViolacaoDaFicha(
                regra.Campo,
                regra.Rotulo,
                TipoDaViolacao.NaoEhNumero,
                valor,
                Com($"deveria ser um número e tem '{Curto(valor)}'", regra)));

            return;
        }

        if (regra.Minimo is { } minimo && numero < minimo)
        {
            violacoes.Add(new ViolacaoDaFicha(
                regra.Campo,
                regra.Rotulo,
                TipoDaViolacao.ForaDaFaixa,
                valor,
                Com($"é {numero}, e o mínimo deste sistema é {minimo}", regra)));
        }

        if (regra.Maximo is { } maximo && numero > maximo)
        {
            violacoes.Add(new ViolacaoDaFicha(
                regra.Campo,
                regra.Rotulo,
                TipoDaViolacao.ForaDaFaixa,
                valor,
                Com($"é {numero}, e o máximo deste sistema é {maximo}", regra)));
        }
    }

    /// <summary>
    /// As magias escritas na ficha, conferidas contra a regra do nome em inglês. Só vale nos
    /// sistemas que não são em português — ver <see cref="NomesDeMagia"/>.
    /// </summary>
    private static IEnumerable<ViolacaoDaFicha> ViolacoesDeMagia(
        RegrasDaFicha regras,
        IReadOnlyDictionary<string, string> campos)
    {
        if (regras.EmPortugues || regras.CamposDeMagia.Count == 0)
        {
            yield break;
        }

        foreach (var campo in regras.CamposDeMagia)
        {
            var valor = Valor(campos, campo);

            if (valor.Length == 0)
            {
                continue;
            }

            var semIngles = NomesDeMagia.SemNomeEmIngles(valor).ToList();

            if (semIngles.Count == 0)
            {
                continue;
            }

            yield return new ViolacaoDaFicha(
                campo,
                regras.Regra(campo)?.Rotulo ?? "",
                TipoDaViolacao.MagiaSemNomeEmIngles,
                valor,
                $"{semIngles.Count} magia(s) sem o nome em inglês: {Listar(semIngles)}. " +
                NomesDeMagia.ComoEscrever);
        }
    }

    /// <summary>
    /// O valor de um campo. Procura pelo nome exato — é ele que o PDF usa — e cai para a forma
    /// achatada como rede de segurança, pela mesma razão que <see cref="FichaEmTexto"/>: há campo
    /// de AcroForm com espaço sobrando no fim do nome.
    /// </summary>
    private static string Valor(IReadOnlyDictionary<string, string> campos, string nome)
    {
        if (campos.TryGetValue(nome, out var exato))
        {
            return exato.Trim();
        }

        foreach (var (campo, valor) in campos)
        {
            if (Iguais(campo, nome))
            {
                return valor.Trim();
            }
        }

        return "";
    }

    /// <summary>
    /// O vocabulário de marcação que <see cref="PreenchedorDeFicha"/> aceita na ida. Recusar aqui
    /// o que ele aceita lá produziria uma violação por um valor que gera a ficha sem problema
    /// nenhum — e o nome do estado no PDF (<c>Yes</c>, <c>Off</c>) é justamente o que o
    /// <c>Ficha-Mapeamento.md</c> costuma trazer.
    /// </summary>
    private static bool EhMarcacaoConhecida(string valor)
    {
        var texto = TextoNormalizado.SemAcento(valor.Trim().TrimStart('/')).ToLowerInvariant();

        return texto is "true" or "1" or "sim" or "yes" or "on" or "x" or "marcado"
            or "false" or "0" or "nao" or "no" or "off" or "desmarcado";
    }

    /// <summary>A observação da regra vira parte da frase: é ela que cita a fonte da exigência.</summary>
    private static string Com(string frase, RegraDeCampo regra) =>
        regra.Observacao is { Length: > 0 } observacao ? $"{frase} ({observacao})" : frase;

    private static bool Iguais(string um, string outro) =>
        Achatar(um).Equals(Achatar(outro), StringComparison.Ordinal);

    /// <summary>
    /// A forma em que duas escritas do mesmo nome se encontram: sem acento, sem espaço nas pontas
    /// e em minúscula. É a mesma tolerância que <see cref="FichaEmTexto"/> aplica aos marcadores.
    /// </summary>
    private static string Achatar(string texto) =>
        TextoNormalizado.SemAcento(texto).Trim().ToLowerInvariant();

    /// <summary>
    /// Uma lista fechada pode ter as trinta magias de um círculo. Mostrar as primeiras e contar o
    /// resto é o que faz a mensagem caber numa tela sem deixar de ser útil.
    /// </summary>
    private static string Listar(IReadOnlyList<string> valores)
    {
        const int quantos = 8;

        return valores.Count <= quantos
            ? string.Join(", ", valores)
            : $"{string.Join(", ", valores.Take(quantos))} e mais {valores.Count - quantos}";
    }

    private static string Curto(string valor)
    {
        var linha = valor.ReplaceLineEndings(" ").Trim();

        return linha.Length <= 40 ? linha : string.Concat(linha.AsSpan(0, 37), "...");
    }
}
