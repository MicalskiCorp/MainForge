using MainForge.Core;

namespace MainForge.Tools;

/// <summary>
/// As fichas em PDF de um personagem: a atual, em <c>Output/Personagens/</c>, e o histórico por
/// nível, em <c>Personagens/&lt;Sistema&gt;/&lt;Id&gt;/Fichas/</c>.
///
/// <para><b>Por que os dois lugares.</b> <c>Output/</c> é entrega, e entrega não é arquivo morto:
/// quem abre a pasta quer a ficha do personagem, não escolher entre <c>Thoradin.pdf</c>,
/// <c>Thoradin-nivel3.pdf</c> e <c>Thoradin-importada.pdf</c> qual é a que vale hoje. Por isso é
/// **uma por personagem**, sempre a mais recente, sempre no mesmo nome.</para>
///
/// <para>O histórico é o oposto: cada nível concluído é um estado que não se refaz. Subir de
/// nível reescreve o PDF, e sem cópia o personagem de ontem some. Ele mora junto do dossiê,
/// porque é dado de trabalho do aplicativo — a mesma razão de <c>Personagens/</c> não ser
/// <c>Output/</c>.</para>
///
/// <para><b>Um registro por nível, não um por geração.</b> Corrigir a ficha do nível 3 três vezes
/// deixa uma ficha do nível 3: a última, que é a que ficou valendo. Guardar cada geração
/// encheria a pasta de versões que ninguém sabe distinguir.</para>
/// </summary>
public static class FichasDoPersonagem
{
    /// <summary>Nome da pasta de histórico dentro do dossiê.</summary>
    public const string NomeDaPasta = "Fichas";

    public static string Diretorio(CaminhosDoProjeto caminhos, string sistema, string id) =>
        Path.Combine(RepositorioDePersonagens.DiretorioDoPersonagem(caminhos, sistema, id), NomeDaPasta);

    /// <summary>
    /// O nome do arquivo desta personagem em <c>Output/Personagens/</c> — estável entre gerações,
    /// que é o que faz a próxima ficha substituir a anterior em vez de se somar a ela.
    ///
    /// <para>Sai do identificador do personagem, que já é nome de pasta válido e único dentro do
    /// sistema. Quando dois sistemas têm um personagem de mesmo nome, o segundo ganha o nome do
    /// sistema na frente: sem isso, um apagaria a ficha do outro — e a colisão só apareceria no
    /// dia em que alguém procurasse o PDF que sumiu.</para>
    /// </summary>
    public static string NomeNaSaida(CaminhosDoProjeto caminhos, Personagem personagem)
    {
        var simples = $"{personagem.Id}.pdf";
        var caminhoSimples = $"Output/Personagens/{simples}";

        var dono = RepositorioDePersonagens
            .Listar(caminhos)
            .FirstOrDefault(outro => string.Equals(outro.FichaGerada, caminhoSimples, StringComparison.OrdinalIgnoreCase));

        return dono is null || EhOMesmo(dono, personagem)
            ? simples
            : $"{personagem.Sistema}-{personagem.Id}.pdf";
    }

    /// <summary>
    /// Guarda no histórico a ficha que acabou de ficar pronta e devolve o registro criado.
    ///
    /// <para>O arquivo é copiado, não movido: o que está em <c>Output/</c> continua sendo a ficha
    /// atual do personagem. Um nível que já tinha registro é substituído — a ficha daquele nível
    /// é a última que ficou valendo nele.</para>
    /// </summary>
    /// <param name="nivel">
    /// O nível em que a ficha foi concluída. <c>null</c> quando ninguém informou: o registro
    /// existe do mesmo jeito, num único lugar reservado para isso, em vez de o histórico ganhar
    /// um arquivo novo a cada geração sem nível.
    /// </param>
    /// <returns>
    /// O registro criado, ou <c>null</c> quando não há PDF para guardar. Ficha inexistente não
    /// vira registro apontando para o vazio, e também não derruba o fechamento do dossiê: o
    /// personagem ficar "em desenvolvimento" com a ficha pronta é o pior dos dois problemas.
    /// </returns>
    public static FichaDeNivel? Arquivar(
        CaminhosDoProjeto caminhos,
        Personagem personagem,
        string caminhoDaFichaAtual,
        int? nivel)
    {
        if (!File.Exists(caminhoDaFichaAtual))
        {
            return null;
        }

        var diretorio = Diretorio(caminhos, personagem.Sistema, personagem.Id);
        Directory.CreateDirectory(diretorio);

        var destino = Path.Combine(diretorio, NomeDoRegistro(nivel));
        File.Copy(caminhoDaFichaAtual, destino, overwrite: true);

        var registro = new FichaDeNivel(
            nivel,
            Path.GetRelativePath(caminhos.Raiz, destino).Replace('\\', '/'),
            DateTimeOffset.Now);

        personagem.Fichas.RemoveAll(ficha => ficha.Nivel == nivel);
        personagem.Fichas.Add(registro);
        personagem.Fichas.Sort((primeira, segunda) => (primeira.Nivel ?? 0).CompareTo(segunda.Nivel ?? 0));

        return registro;
    }

    /// <summary>
    /// Apaga a ficha anterior do personagem em <c>Output/</c> quando a nova tem outro nome.
    ///
    /// <para>Acontece quando o personagem foi importado (a cópia da ficha trazida), quando o nome
    /// dele mudou ou quando a ficha veio de uma versão do aplicativo em que o agente escolhia o
    /// nome do arquivo. Sem isto, "uma ficha por personagem" duraria até a primeira evolução.</para>
    ///
    /// <para>Só apaga dentro de <c>Output/Personagens/</c> e só o arquivo que o próprio dossiê
    /// aponta: é a única coisa que o aplicativo sabe que é dele.</para>
    /// </summary>
    public static void ApagarAnterior(CaminhosDoProjeto caminhos, string? fichaAnterior, string caminhoDaFichaAtual)
    {
        if (fichaAnterior is not { Length: > 0 })
        {
            return;
        }

        try
        {
            var anterior = CaminhosDoProjeto.ResolverDentroDe(caminhos.Raiz, fichaAnterior);

            var dentroDaSaida = anterior.StartsWith(
                caminhos.SaidaPersonagens + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);

            if (!dentroDaSaida || PathIguais(anterior, caminhoDaFichaAtual) || !File.Exists(anterior))
            {
                return;
            }

            File.Delete(anterior);
        }
        catch (Exception excecao) when (excecao is IOException or UnauthorizedAccessException)
        {
            // Ficha aberta no leitor de PDF é o caso comum. Deixar o arquivo para trás é pior que
            // nada, mas muito melhor que derrubar a geração da ficha nova por causa da limpeza.
        }
    }

    private static string NomeDoRegistro(int? nivel) =>
        nivel is { } numero ? $"nivel-{numero:00}.pdf" : "sem-nivel.pdf";

    private static bool EhOMesmo(Personagem umPersonagem, Personagem outro) =>
        umPersonagem.Id.Equals(outro.Id, StringComparison.OrdinalIgnoreCase) &&
        umPersonagem.Sistema.Equals(outro.Sistema, StringComparison.OrdinalIgnoreCase);

    private static bool PathIguais(string um, string outro) =>
        Path.GetFullPath(um).Equals(Path.GetFullPath(outro), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// O nível do personagem lido dos campos da ficha, quando algum deles diz isso sem margem
    /// para dúvida.
    ///
    /// <para><b>Por que só os nomes exatos.</b> Uma ficha de RPG tem vários campos com "nível" no
    /// nome — nível de magia, nível de conjurador, nível da classe secundária. Aceitar
    /// "contém nível" arquivaria a ficha do personagem de nível 5 como se fosse do nível 1 (o
    /// espaço de magias). Errar aqui é pior que não saber: o registro fica no lugar errado e
    /// substitui o do nível certo.</para>
    /// </summary>
    public static int? DeduzirNivel(IReadOnlyDictionary<string, string> campos)
    {
        string[] nomes =
        [
            "nivel", "level", "nivelpersonagem", "niveldopersonagem", "nivelatual",
            "characterlevel", "totallevel",
        ];

        foreach (var (campo, valor) in campos)
        {
            if (!nomes.Contains(Achatar(campo)))
            {
                continue;
            }

            if (int.TryParse(valor.Trim(), out var nivel) && nivel is > 0 and < 100)
            {
                return nivel;
            }
        }

        return null;
    }

    private static string Achatar(string nomeDoCampo) =>
        new(TextoNormalizado.SemAcento(nomeDoCampo).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
