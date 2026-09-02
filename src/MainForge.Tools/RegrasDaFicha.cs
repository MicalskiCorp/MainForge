using System.Text.Json;
using System.Text.Json.Serialization;
using MainForge.Core;

namespace MainForge.Tools;

/// <summary>Que tipo de valor um campo da ficha aceita.</summary>
public enum TipoDoCampo
{
    /// <summary>Texto livre — o padrão, e o que não dá para validar além de "está preenchido".</summary>
    Texto,

    /// <summary>Número inteiro, com faixa opcional. É o que a maior parte de uma ficha é.</summary>
    Inteiro,

    /// <summary>Caixa de marcação: marcada ou desmarcada, nada no meio.</summary>
    Marcacao,
}

/// <summary>
/// O que vale num campo da ficha deste sistema.
///
/// <para>Cada regra é uma afirmação que dá para conferir sem interpretar nada: o campo está
/// preenchido, o número está na faixa, o texto é um dos valores da lista. Regra que dependa de
/// julgamento ("a magia cabe no espaço da classe") não entra aqui — ela é conversa com o Dungeon
/// Master, e escrevê-la como se fosse mecânica produziria reprovação falsa.</para>
/// </summary>
public sealed class RegraDeCampo
{
    /// <summary>O nome exato do campo no PDF, como <c>preencher_ficha_personagem</c> o exige.</summary>
    public string Campo { get; set; } = "";

    /// <summary>
    /// O que o campo é, em palavras — o rótulo impresso ao lado dele. Vai nas mensagens ao
    /// usuário: "Animal fora da faixa" não diz nada, "Arcanismo fora da faixa" diz.
    /// </summary>
    public string Rotulo { get; set; } = "";

    /// <summary>O personagem não está pronto enquanto este campo estiver vazio.</summary>
    public bool Obrigatorio { get; set; }

    public TipoDoCampo Tipo { get; set; } = TipoDoCampo.Texto;

    /// <summary>Menor valor aceito, para <see cref="TipoDoCampo.Inteiro"/>.</summary>
    public int? Minimo { get; set; }

    /// <summary>Maior valor aceito, para <see cref="TipoDoCampo.Inteiro"/>.</summary>
    public int? Maximo { get; set; }

    /// <summary>
    /// Os únicos valores aceitos, quando o campo é uma escolha fechada (classe, raça, tendência).
    /// Vazio significa texto livre. A comparação ignora acento e maiúscula: a lista existe para
    /// pegar "Guerreirо" escrito errado, não para brigar com "guerreiro" em minúscula.
    /// </summary>
    public List<string> Valores { get; set; } = [];

    /// <summary>Por que a regra é essa, para a mensagem poder citar a fonte.</summary>
    public string? Observacao { get; set; }
}

/// <summary>
/// As regras que dizem se um personagem é válido neste sistema, mais o que o aplicativo precisa
/// saber sobre a ficha em PDF dele. Mora em
/// <c>Sistemas/&lt;Sistema&gt;/Ficha-Validacao.json</c>, ao lado dos dois arquivos da ficha e
/// pela mesma razão: vale para o sistema inteiro, com qualquer expansão selecionada.
///
/// <para><b>Por que JSON e não Markdown.</b> Os outros dois arquivos da ficha são para o agente
/// ler; este é para o C# executar. A validação roda na criação, na importação e toda vez que a
/// ficha é gerada — três momentos em que abrir uma conversa custaria cota para responder uma
/// pergunta que não precisa de julgamento nenhum. É a regra 11 da estrutura do projeto: o que o
/// C# consegue decidir de graça não vira turno.</para>
///
/// <para><b>Ele nasce na importação do sistema e é refinado no mapeamento.</b> A importação
/// consegue montar o esqueleto sozinha, lendo o AcroForm da ficha em branco: todo campo listado,
/// com o tipo que o PDF declara e nenhuma exigência. O Configurador, que leu os livros, é quem
/// sabe o que é obrigatório, que faixa vale e quais são as escolhas fechadas — e sobrescreve o
/// esqueleto por <c>registrar_validacao_da_ficha</c>. Nascer cedo importa: um sistema que chegou
/// por pacote antigo, ou que ninguém reprocessou, continua tendo validação de tipo e de campo
/// inexistente em vez de nenhuma.</para>
/// </summary>
public sealed class RegrasDaFicha
{
    /// <summary>Versão do formato. Um arquivo mais novo que esta versão é ignorado, não adivinhado.</summary>
    public int Formato { get; set; } = FormatoAtual;

    public const int FormatoAtual = 1;

    public string Sistema { get; set; } = "";

    public DateTimeOffset GeradoEm { get; set; }

    /// <summary>
    /// Quem escreveu estas regras: a importação do sistema (esqueleto) ou o Configurador
    /// (regras de verdade). É o que permite à interface dizer "este sistema ainda não tem
    /// validação das regras, só a estrutura da ficha".
    /// </summary>
    public OrigemDasRegras Origem { get; set; } = OrigemDasRegras.EstruturaDaFicha;

    /// <summary>
    /// O idioma dos rótulos impressos na ficha em branco, detectado uma vez na importação.
    ///
    /// <para>Fica gravado aqui porque respondê-lo custa abrir o PDF, e a resposta é pedida em
    /// lugares que não têm por que fazer isso — a regra do nome das magias, por exemplo, precisa
    /// saber se este é um sistema em português a cada arquivo de conhecimento gravado.</para>
    /// </summary>
    public IdiomaDetectado Idioma { get; set; } = IdiomaDetectado.Indeterminado;

    /// <summary>Este sistema tem magias (ou o equivalente dele: poderes, invocações, artes).</summary>
    public bool UsaMagias { get; set; }

    /// <summary>
    /// Os campos da ficha em que as magias do personagem são escritas. Servem a duas coisas: saber
    /// de onde tirar a lista de magias para a folha extra, e conferir a grafia dos nomes.
    /// </summary>
    public List<string> CamposDeMagia { get; set; } = [];

    /// <summary>
    /// A ficha em PDF tem espaço para a <b>descrição completa</b> de cada magia, e não só para a
    /// lista de nomes. <c>null</c> quando ninguém declarou — aí quem responde é o próprio PDF,
    /// por <see cref="EspacoDeMagiasNaFicha"/>.
    ///
    /// <para>É o que decide se o personagem precisa da folha extra de magias: uma ficha com um
    /// quadro grande de "Magias conhecidas" já traz tudo, e uma com trinta linhas de uma linha
    /// cada só cabe o nome.</para>
    /// </summary>
    public bool? MagiasPorExtenso { get; set; }

    public List<RegraDeCampo> Campos { get; set; } = [];

    private static readonly JsonSerializerOptions Serializacao = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        // Sem isto todo acento do português vira escape e o arquivo deixa de ser legível por quem
        // abrir para conferir — o mesmo motivo do dossiê e do registro de processamento.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Onde mora o arquivo de regras de um sistema.</summary>
    public static string Caminho(CaminhosDoProjeto caminhos, string sistema) =>
        Path.Combine(
            CaminhosDoProjeto.ResolverDentroDe(caminhos.Conhecimento, sistema),
            SistemaRpg.NomeDaValidacaoDaFicha);

    /// <summary>
    /// As regras de um sistema, ou <c>null</c> quando ele não tem nenhuma — sistema mapeado por
    /// uma versão anterior do aplicativo, ou chegado por pacote antigo. Arquivo ilegível também
    /// vira <c>null</c>: ficar sem validação é ruim, derrubar a criação do personagem por causa
    /// dela é pior.
    /// </summary>
    public static RegrasDaFicha? Carregar(CaminhosDoProjeto caminhos, string sistema)
    {
        string caminho;

        try
        {
            caminho = Caminho(caminhos, sistema);
        }
        catch (Exception excecao) when (excecao is UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        if (!File.Exists(caminho))
        {
            return null;
        }

        try
        {
            var regras = JsonSerializer.Deserialize<RegrasDaFicha>(File.ReadAllText(caminho), Serializacao);

            if (regras is null || regras.Formato > FormatoAtual)
            {
                return null;
            }

            regras.Sistema = sistema;

            return regras;
        }
        catch (Exception excecao) when (excecao is JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// As regras do sistema, gerando o esqueleto a partir da ficha em branco quando ainda não há
    /// nenhuma — e gravando-o, para que o custo de abrir o PDF se pague uma vez só.
    ///
    /// <para><b>Por que gerar em vez de devolver nada.</b> O arquivo nasce na importação do
    /// sistema, mas todo sistema mapeado antes desta versão existe sem ele: são justamente os que
    /// já têm personagem, e ficariam para sempre sem conferência nenhuma até alguém reprocessar os
    /// livros — a operação mais cara do aplicativo, pedida para produzir um arquivo que a ficha em
    /// branco responde de graça. É a mesma regra que vale para o <c>index.md</c> e para a ficha em
    /// PDF que sumiu de <c>Output/</c>: coisa derivada que falta é refeita, não perguntada.</para>
    ///
    /// <para>Devolve <c>null</c> só quando não há de onde tirar o esqueleto — sistema sem
    /// <c>Templates/</c>, o que chegou por pacote antigo. Aí não há o que conferir mesmo.</para>
    /// </summary>
    public static RegrasDaFicha? CarregarOuGerar(CaminhosDoProjeto caminhos, string sistema)
    {
        if (Carregar(caminhos, sistema) is { } existentes)
        {
            return existentes;
        }

        try
        {
            var diretorio = CaminhosDoProjeto.ResolverDentroDe(caminhos.Modelos, sistema);

            if (!Directory.Exists(diretorio))
            {
                return null;
            }

            var ficha = LocalizadorDeFichaModelo.Resolver(diretorio, null);
            var regras = Esqueleto(LayoutDaFicha.Ler(ficha));

            Gravar(caminhos, sistema, regras);

            return regras;
        }
        catch (Exception excecao) when (excecao is not OutOfMemoryException)
        {
            // Ficha ilegível, pasta sem PDF, disco somente-leitura: sem validação, como antes.
            // Derrubar a criação de personagem por não conseguir gerar a conferência seria trocar
            // a falta de uma checagem pela perda do que ela ia checar.
            return null;
        }
    }

    /// <summary>Grava as regras, criando a pasta do sistema se ela ainda não existir.</summary>
    public static string Gravar(CaminhosDoProjeto caminhos, string sistema, RegrasDaFicha regras)
    {
        var caminho = Caminho(caminhos, sistema);

        regras.Sistema = sistema;
        regras.Formato = FormatoAtual;
        regras.GeradoEm = DateTimeOffset.Now;

        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, JsonSerializer.Serialize(regras, Serializacao));

        return Path.GetRelativePath(caminhos.Raiz, caminho);
    }

    /// <summary>
    /// O esqueleto que a importação do sistema consegue montar sozinha: um campo por campo do
    /// AcroForm, com o rótulo impresso ao lado, o tipo que o PDF declara e <b>nenhuma exigência</b>.
    ///
    /// <para>Não inventa obrigatoriedade nem faixa: os livros ainda não foram lidos, e uma regra
    /// chutada aqui reprovaria personagem legítimo. O que ele já garante é o que não depende de
    /// livro nenhum — que o campo existe na ficha e que uma caixa de marcação não recebe texto.</para>
    /// </summary>
    public static RegrasDaFicha Esqueleto(IReadOnlyList<CampoDaFicha> daFicha)
    {
        var deMagia = daFicha.Where(EhDeMagia).ToList();

        var regras = new RegrasDaFicha
        {
            Origem = OrigemDasRegras.EstruturaDaFicha,
            Idioma = IdiomaDaFicha.Detectar(
                daFicha.Where(campo => campo.Rotulo is { Length: > 0 }).Select(campo => campo.Rotulo!)),
            UsaMagias = deMagia.Count > 0,
            CamposDeMagia = [.. deMagia.Select(campo => campo.Nome).Distinct(StringComparer.Ordinal)],
            MagiasPorExtenso = deMagia.Count > 0 ? EspacoDeMagiasNaFicha(deMagia) : null,
        };

        foreach (var campo in daFicha.DistinctBy(campo => campo.Nome, StringComparer.Ordinal))
        {
            regras.Campos.Add(new RegraDeCampo
            {
                Campo = campo.Nome,

                // Só o rótulo em que dá para confiar. O rótulo daqui vai para a mensagem que o
                // usuário lê ("FORÇA (Forca): está vazio"), e um rótulo errado ali é pior que
                // nenhum: numa ficha de três colunas com o texto a 80 pt do seu campo, o rótulo da
                // coluna seguinte fica a 20 pt e vence — o campo de Força passaria a se chamar
                // "Destreza:" na cara do usuário. Sem ele, a mensagem usa o nome do campo, que é
                // feio e nunca mente. Quem preenche os que faltam é o Configurador, que leu a ficha.
                Rotulo = campo.RotuloDeConfianca ? campo.Rotulo! : "",
                Tipo = campo.DeMarcacao ? TipoDoCampo.Marcacao : TipoDoCampo.Texto,
            });
        }

        return regras;
    }

    /// <summary>
    /// Quantos pontos de altura um quadro precisa ter para caber a descrição das magias de um
    /// personagem, e não só a lista dos nomes.
    ///
    /// <para>O corte é alto de propósito. Errar para "tem espaço" tira do conjurador justamente a
    /// folha que ele precisa ter na mesa, e o erro é silencioso: nada falha, a folha só não sai.
    /// Errar para "não tem" produz um PDF a mais que talvez repita o que a ficha já traz — que é
    /// um incômodo, não uma perda. Entre os dois, esta medida erra para o lado do incômodo.</para>
    /// </summary>
    private const double AlturaParaAsDescricoes = 150.0;

    /// <summary>
    /// A ficha tem, entre os campos de magia, um quadro em que caibam as descrições completas.
    /// É a resposta do próprio PDF, usada quando ninguém declarou
    /// <see cref="MagiasPorExtenso"/>.
    /// </summary>
    public static bool EspacoDeMagiasNaFicha(IEnumerable<CampoDaFicha> camposDeMagia) =>
        camposDeMagia.Any(campo => campo.DeVariasLinhas && campo.Altura >= AlturaParaAsDescricoes);

    /// <summary>
    /// Este campo é onde as magias do personagem vão? Decidido pelo rótulo impresso primeiro e
    /// pelo nome do campo depois — a mesma ordem de confiança que o resto do projeto usa, porque
    /// numa ficha traduzida o nome do campo pode estar em qualquer idioma.
    ///
    /// <para><b>O que fica de fora, e por quê.</b> Numa ficha, "magia" nomeia duas coisas
    /// diferentes, e só uma delas é uma magia:</para>
    /// <list type="bullet">
    ///   <item>o quadro de <em>ataques</em>, que costuma ser de ataques <b>e</b> conjurações — na
    ///   ficha de D&amp;D 5e ele se chama <c>AttacksSpellcasting</c> e é rotulado "ATAQUES E
    ///   MAGIAS". É uma linha por golpe, com bônus e dano;</item>
    ///   <item>o <em>cabeçalho da conjuração</em>: classe conjuradora, habilidade-chave, CD do
    ///   teste de resistência, bônus de ataque mágico. Todos se chamam <c>Spell...</c> e nenhum é
    ///   uma magia — eles dizem <b>como</b> o personagem conjura, não <b>o que</b>.</item>
    /// </list>
    /// <para>Contar os dois fazia dois estragos de uma vez: dava ao sistema um "quadro grande de
    /// magias" que ele não tem — e com isso tirava do conjurador a folha extra — e mandava a folha
    /// ler nomes de magia de uma lista de ataques, produzindo "Sabedoria" e "Clérigo" no meio das
    /// magias do personagem.</para>
    ///
    /// <para>É uma lista, e lista se esquece. O que a segura é o Configurador poder declarar
    /// <c>camposDeMagia</c> por conta própria: aqui é o palpite de quem só tem o PDF, e lá é a
    /// resposta de quem leu o livro.</para>
    /// </summary>
    private static bool EhDeMagia(CampoDaFicha campo)
    {
        string[] termos = ["magia", "spell", "feitico", "encanto", "conjura", "truque", "cantrip"];

        string[] naoSao =
        [
            "ataque", "attack", "arma", "weapon", "golpe",
            "habilidade", "ability", "classe", "class", "save", "resistencia", "bonus", "atk",
        ];

        var rotulo = TextoNormalizado.SemAcento(campo.Rotulo ?? "").ToLowerInvariant();
        var nome = TextoNormalizado.SemAcento(campo.Nome).ToLowerInvariant();

        if (naoSao.Any(termo => rotulo.Contains(termo, StringComparison.Ordinal)
                                || nome.Contains(termo, StringComparison.Ordinal)))
        {
            return false;
        }

        return termos.Any(termo => rotulo.Contains(termo, StringComparison.Ordinal)
                                   || nome.Contains(termo, StringComparison.Ordinal));
    }

    /// <summary>
    /// A ficha deste sistema traz as magias por extenso? A declaração do Configurador manda; sem
    /// ela, vale o que o PDF mostrou na importação.
    /// </summary>
    public bool TrazMagiasPorExtenso => MagiasPorExtenso ?? false;

    /// <summary>
    /// O sistema é escrito em português. É o que dispensa a tradução dos nomes de magia: num
    /// sistema em português o nome da magia <em>é</em> o nome em português, e exigir um nome em
    /// inglês ao lado inventaria um original que os livros não têm.
    /// </summary>
    public bool EmPortugues => Idioma == IdiomaDetectado.Portugues;

    /// <summary>A regra do campo, ou <c>null</c> quando não há nenhuma para ele.</summary>
    public RegraDeCampo? Regra(string campo) =>
        Campos.FirstOrDefault(regra => regra.Campo.Equals(campo, StringComparison.Ordinal));
}

/// <summary>De onde vieram as regras de um sistema.</summary>
public enum OrigemDasRegras
{
    /// <summary>Do AcroForm da ficha em branco, na importação: estrutura, sem regra de jogo.</summary>
    EstruturaDaFicha,

    /// <summary>Do Configurador, depois de ler os livros: as regras do sistema de verdade.</summary>
    Configurador,
}
