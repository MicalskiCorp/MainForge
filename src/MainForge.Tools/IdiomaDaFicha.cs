namespace MainForge.Tools;

/// <summary>Em que idioma um texto da ficha está escrito.</summary>
public enum IdiomaDetectado
{
    /// <summary>Não deu para dizer — texto curto demais, ou só números e siglas.</summary>
    Indeterminado,

    /// <summary>Português.</summary>
    Portugues,

    /// <summary>Inglês.</summary>
    Ingles,
}

/// <summary>
/// Descobre em que idioma estão os rótulos impressos numa ficha e os do desenho em texto que a
/// acompanha.
///
/// <para><b>Por que o aplicativo precisa saber disso.</b> O de-para entre o desenho em texto e a
/// ficha em PDF é feito casando o rótulo de cada linha com o texto impresso ao lado do campo. Se
/// os dois estiverem em idiomas diferentes — ficha em inglês, base de conhecimento em português —
/// nenhum rótulo casa, e a conferência acusaria a ficha inteira como errada quando o problema é
/// outro: os dois artefatos não se falam. Aqui a pergunta é respondida uma vez, e vira um aviso
/// que explica a causa em vez de uma enxurrada de divergências.</para>
///
/// <para><b>A regra do projeto.</b> A base de conhecimento nasce da leitura dos livros, então ela
/// sai no idioma deles e da ficha — é isso que faz os rótulos casarem. O que <em>não</em> muda de
/// idioma é a conversa com o usuário, sempre em português: o aviso desta classe é para o usuário,
/// e por isso é escrito em português mesmo quando a ficha está em inglês.</para>
/// </summary>
public static class IdiomaDaFicha
{
    /// <summary>
    /// Palavras que só aparecem numa ficha em português. São rótulos de ficha, não vocabulário
    /// geral: o que se procura é a língua em que a <em>ficha</em> foi impressa.
    /// </summary>
    private static readonly HashSet<string> Portuguesas = new(StringComparer.Ordinal)
    {
        "forca", "destreza", "constituicao", "inteligencia", "sabedoria", "carisma",
        "pericias", "personagem", "nivel", "antecedente", "tendencia", "resistencia",
        "deslocamento", "magias", "equipamento", "idiomas", "nome", "jogador", "raca",
        "proficiencia", "ataques", "tesouro", "aparencia", "historia", "vida", "dados",
    };

    /// <summary>As mesmas coisas, na ficha oficial em inglês.</summary>
    private static readonly HashSet<string> Inglesas = new(StringComparer.Ordinal)
    {
        "strength", "dexterity", "constitution", "intelligence", "wisdom", "charisma",
        "skills", "saving", "throws", "character", "level", "background", "alignment",
        "speed", "proficiency", "equipment", "features", "traits", "spells", "attacks",
        "treasure", "appearance", "backstory", "player", "race", "hit", "points",
    };

    /// <summary>
    /// Acentos e cedilha são o sinal mais barato de português, e o mais difícil de aparecer por
    /// acaso numa ficha em inglês.
    /// </summary>
    private const string LetrasDePortugues = "ãõçáéíóúâêô";

    /// <summary>
    /// Quantas palavras reconhecidas bastam para arriscar um palpite. Abaixo disso o texto não
    /// sustenta conclusão nenhuma, e o silêncio é a resposta certa.
    /// </summary>
    private const int MinimoDeIndicios = 3;

    /// <summary>
    /// O idioma em que <paramref name="textos"/> está escrito — tipicamente os rótulos impressos
    /// de uma ficha, ou os do desenho em texto dela.
    /// </summary>
    public static IdiomaDetectado Detectar(IEnumerable<string> textos)
    {
        var palavras = textos
            .SelectMany(texto => TextoNormalizado.SemAcento(texto).ToLowerInvariant().Split(
                [' ', '\t', '\n', '\r', '.', ',', ':', ';', '(', ')', '[', ']', '/', '-', '_'],
                StringSplitOptions.RemoveEmptyEntries))
            .ToList();

        var emPortugues = palavras.Count(Portuguesas.Contains);
        var emIngles = palavras.Count(Inglesas.Contains);

        // Acento é indício direto e não depende de a palavra estar na lista.
        if (textos.Any(texto => texto.Any(letra => LetrasDePortugues.Contains(char.ToLowerInvariant(letra)))))
        {
            emPortugues += MinimoDeIndicios;
        }

        if (Math.Max(emPortugues, emIngles) < MinimoDeIndicios || emPortugues == emIngles)
        {
            return IdiomaDetectado.Indeterminado;
        }

        return emPortugues > emIngles ? IdiomaDetectado.Portugues : IdiomaDetectado.Ingles;
    }

    /// <summary>O nome do idioma para escrever numa mensagem ao usuário.</summary>
    public static string Nome(IdiomaDetectado idioma) => idioma switch
    {
        IdiomaDetectado.Portugues => "português",
        IdiomaDetectado.Ingles => "inglês",
        _ => "idioma não identificado",
    };
}
