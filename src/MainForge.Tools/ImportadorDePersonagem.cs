using System.Text;
using MainForge.Core;

namespace MainForge.Tools;

/// <summary>
/// Quanto a ficha que o usuário trouxe parece com a ficha em branco de um sistema.
/// </summary>
/// <param name="Sistema">O sistema comparado.</param>
/// <param name="CamposEmComum">Campos da ficha importada que existem no modelo do sistema.</param>
/// <param name="CamposDoModelo">Quantos campos o modelo tem ao todo.</param>
public sealed record SistemaCompativel(SistemaRpg Sistema, int CamposEmComum, int CamposDoModelo)
{
    /// <summary>Fração dos campos do modelo que a ficha importada reconhece, de 0 a 1.</summary>
    public double Cobertura => CamposDoModelo == 0 ? 0 : (double)CamposEmComum / CamposDoModelo;

    /// <summary>
    /// É a mesma ficha, e não outra que por acaso tem campos de nome parecido. O corte é alto de
    /// propósito: a consequência de acertar o sistema errado é um personagem que nunca vai gerar
    /// PDF, e o usuário escolhe na mão de qualquer forma — isto só decide o que aparece sugerido.
    /// </summary>
    public bool Reconhecida => Cobertura >= 0.5;
}

/// <summary>O que a importação produziu, para a interface poder contar ao usuário.</summary>
/// <param name="Personagem">O dossiê recém-criado.</param>
/// <param name="FichaCopiada">Caminho da cópia do PDF, relativo à raiz do projeto.</param>
/// <param name="CamposAproveitados">Valores que o modelo do sistema sabe reescrever.</param>
/// <param name="CamposForaDoModelo">
/// Valores lidos do PDF cujo campo não existe no modelo do sistema. Ficam registrados no
/// <c>ficha.md</c>, mas fora de <see cref="Core.Personagem.Campos"/>.
/// </param>
public sealed record ResultadoDaImportacaoDePersonagem(
    Personagem Personagem,
    string FichaCopiada,
    IReadOnlyDictionary<string, string> CamposAproveitados,
    IReadOnlyDictionary<string, string> CamposForaDoModelo);

/// <summary>
/// Traz para dentro do aplicativo um personagem que já existe numa ficha em PDF preenchida.
///
/// <para><b>Por que existe.</b> O aplicativo sabia fazer personagem do zero e não sabia receber
/// um pronto — e quem já joga tem os seus, preenchidos no PDF editável do sistema. A alternativa
/// era ditar a ficha inteira numa conversa: cota gasta para o agente redescobrir escolhas que já
/// estavam decididas, com o risco de sair diferente do que está na mesa.</para>
///
/// <para><b>O que ele entrega.</b> Um dossiê igual ao de qualquer personagem daqui — mesma pasta,
/// mesmo <c>personagem.json</c>, mesmo <c>ficha.md</c> —, para que evoluir, corrigir e gerar a
/// ficha de novo funcionem sem saber de onde ele veio. Por isso os valores são separados entre os
/// que o modelo do sistema sabe reescrever e os que não: <see cref="Core.Personagem.Campos"/> é o
/// ponto de partida do próximo <c>preencher_ficha_personagem</c>, e um campo que não existe no
/// modelo reprovaria a geração inteira.</para>
///
/// <para><b>O que ele não faz.</b> Não interpreta a ficha. O que sai daqui é o que estava escrito
/// no PDF, campo por campo, marcado como não conferido — validar contra as regras é conversa com
/// o Dungeon Master, e é ela que reescreve o <c>ficha.md</c> em cima deste.</para>
/// </summary>
public static class ImportadorDePersonagem
{
    /// <summary>
    /// Ordena os sistemas por quanto a ficha importada se parece com o modelo de cada um, do mais
    /// parecido para o menos.
    ///
    /// <para>Existe porque a pergunta "de que sistema é esta ficha?" tem resposta no próprio
    /// arquivo: os nomes dos campos de um AcroForm são os mesmos da ficha em branco de onde ele
    /// saiu. Deixar o usuário responder sozinho convida ao engano que mais custa aqui — importar
    /// no sistema errado produz um personagem que nunca gera PDF.</para>
    /// </summary>
    public static IReadOnlyList<SistemaCompativel> Ranquear(CaminhosDoProjeto caminhos, FichaPreenchida ficha)
    {
        var nomesLidos = ficha.Valores.Keys
            .Concat(ficha.CamposEmBranco)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. SistemaRpg.DescobrirTodos(caminhos)
                .Select(sistema =>
                {
                    var doModelo = CamposDoModelo(caminhos, sistema);

                    return new SistemaCompativel(
                        sistema,
                        doModelo.Count(campo => nomesLidos.Contains(campo)),
                        doModelo.Count);
                })
                .OrderByDescending(compativel => compativel.Cobertura)
                .ThenBy(compativel => compativel.Sistema.Id, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>
    /// Os nomes de campo da(s) ficha(s) em branco de um sistema. Um sistema sem
    /// <c>Templates/</c> — o que chegou por pacote, por exemplo — devolve lista vazia em vez de
    /// erro: dá para importar o personagem nele, só não dá para gerar o PDF depois.
    /// </summary>
    public static IReadOnlyList<string> CamposDoModelo(CaminhosDoProjeto caminhos, SistemaRpg sistema)
    {
        var diretorio = sistema.DiretorioModelo(caminhos);

        if (!Directory.Exists(diretorio))
        {
            return [];
        }

        var campos = new List<string>();

        foreach (var modelo in Directory.EnumerateFiles(diretorio, "*.pdf", SearchOption.TopDirectoryOnly))
        {
            try
            {
                campos.AddRange(ImportadorDeSistema.LerCamposDaFicha(modelo));
            }
            catch (Exception excecao) when (excecao is InvalidOperationException or IOException)
            {
                // Modelo ilegível não é assunto desta tela: quem cuida disso é a importação do
                // sistema. Aqui ele só deixa de contar como parecido com a ficha trazida.
            }
        }

        return [.. campos.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Cria o dossiê do personagem a partir da ficha preenchida: guarda os valores, copia o PDF
    /// para <c>Output/Personagens/</c> e escreve um <c>ficha.md</c> provisório com o que foi lido.
    ///
    /// <para>O personagem nasce <b>concluído</b>: ele tem ficha em PDF, que é o sinal de conclusão
    /// que o resto do aplicativo usa. É o que faz "evoluir" valer para ele desde o primeiro
    /// minuto — que é a razão de importar.</para>
    /// </summary>
    /// <param name="fontes">As fontes que a mesa dele usa, como em qualquer personagem daqui.</param>
    public static ResultadoDaImportacaoDePersonagem Importar(
        CaminhosDoProjeto caminhos,
        SistemaRpg sistema,
        string nome,
        IReadOnlyList<string> fontes,
        FichaPreenchida ficha)
    {
        var doModelo = CamposDoModelo(caminhos, sistema).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Sem modelo não há como separar: tudo vira campo aproveitado, e quem vai esbarrar na
        // diferença é a geração do PDF — que já não é possível nesse sistema de qualquer forma.
        var aproveitados = doModelo.Count == 0
            ? new Dictionary<string, string>(ficha.Valores)
            : ficha.Valores.Where(par => doModelo.Contains(par.Key)).ToDictionary();

        var foraDoModelo = ficha.Valores
            .Where(par => !aproveitados.ContainsKey(par.Key))
            .ToDictionary();

        var personagem = RepositorioDePersonagens.Criar(caminhos, sistema.Id, nome, fontes);

        var nomeDoArquivo = Path.GetFileName(ficha.Arquivo);
        var copia = CopiarParaSaida(caminhos, ficha.Arquivo, personagem);

        personagem.Campos = new Dictionary<string, string>(aproveitados);
        personagem.FichaGerada = Path.GetRelativePath(caminhos.Raiz, copia).Replace('\\', '/');
        personagem.FichaGeradaEm = DateTimeOffset.Now;
        personagem.Status = StatusDoPersonagem.Concluido;
        personagem.Resumo = $"Importado de {nomeDoArquivo} — ainda não conferido contra as regras.";

        // A ficha trazida é o estado do personagem no nível em que ele está hoje: ela abre o
        // histórico. O nível sai dos próprios campos quando a ficha o diz sem ambiguidade — e a
        // conferência com o Dungeon Master corrige o registro se ele estiver errado.
        var registro = FichasDoPersonagem.Arquivar(
            caminhos, personagem, copia, FichasDoPersonagem.DeduzirNivel(ficha.Valores));

        personagem.Anotar(
            $"Importado da ficha preenchida '{nomeDoArquivo}' ({ficha.Valores.Count} campo(s) com valor)" +
            (registro is null ? "." : $", guardada no histórico como {registro.Rotulo}."));

        File.WriteAllText(
            RepositorioDePersonagens.CaminhoDaFichaEmTexto(caminhos, sistema.Id, personagem.Id),
            DossieProvisorio(sistema, personagem, nomeDoArquivo, aproveitados, foraDoModelo));

        RepositorioDePersonagens.Salvar(caminhos, personagem);

        return new ResultadoDaImportacaoDePersonagem(personagem, personagem.FichaGerada, aproveitados, foraDoModelo);
    }

    /// <summary>
    /// O nome do personagem, quando a própria ficha o diz. Devolve vazio quando não dá para
    /// saber — aí quem responde é o usuário.
    ///
    /// <para>Vale a tentativa porque o nome é o primeiro campo de toda ficha de RPG e o usuário
    /// acabou de apontar o arquivo: perguntá-lo em seguida é pedir que ele digite o que o
    /// aplicativo tem na mão. O campo do <em>jogador</em> é descartado de propósito — numa ficha
    /// de mesa ele está preenchido tanto quanto o do personagem, e importar o personagem com o
    /// nome de quem o joga é o engano que essa vizinhança produz.</para>
    /// </summary>
    public static string SugerirNome(FichaPreenchida ficha)
    {
        string[] doPersonagem = ["nomedopersonagem", "nomepersonagem", "charactername", "personagem", "nome", "name"];
        string[] doJogador = ["jogador", "player", "usuario", "usuário"];

        var candidatos = ficha.Valores
            .Where(par => !par.Value.Contains('\n') && par.Value.Length <= 60)
            .Where(par => !doJogador.Any(termo => Achatar(par.Key).Contains(termo)))
            .ToList();

        foreach (var termo in doPersonagem)
        {
            var achado = candidatos.FirstOrDefault(par => Achatar(par.Key) == termo);

            if (achado.Value is { Length: > 0 })
            {
                return achado.Value;
            }
        }

        foreach (var termo in doPersonagem)
        {
            var achado = candidatos.FirstOrDefault(par => Achatar(par.Key).Contains(termo));

            if (achado.Value is { Length: > 0 })
            {
                return achado.Value;
            }
        }

        return "";
    }

    private static string Achatar(string nomeDoCampo) =>
        new(TextoNormalizado.SemAcento(nomeDoCampo).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// Guarda uma cópia do PDF trazido em <c>Output/Personagens/</c>.
    ///
    /// <para><b>Por que copiar.</b> O original está numa pasta qualquer do usuário e pode ser
    /// movido, renomeado ou aberto e salvo por cima a qualquer momento; o dossiê aponta para um
    /// caminho relativo à raiz do projeto porque tudo o mais aqui aponta.</para>
    ///
    /// <para>Ela entra com o mesmo nome que qualquer ficha daquele personagem teria
    /// (<see cref="FichasDoPersonagem.NomeNaSaida"/>): <c>Output/</c> guarda uma ficha por
    /// personagem, e a primeira evolução substitui esta. O estado "antes" não se perde — quem o
    /// guarda é o histórico por nível, dentro do dossiê.</para>
    /// </summary>
    private static string CopiarParaSaida(CaminhosDoProjeto caminhos, string origem, Personagem personagem)
    {
        Directory.CreateDirectory(caminhos.SaidaPersonagens);

        var destino = CaminhosDoProjeto.ResolverDentroDe(
            caminhos.SaidaPersonagens,
            FichasDoPersonagem.NomeNaSaida(caminhos, personagem));

        File.Copy(origem, destino, overwrite: true);

        return destino;
    }

    /// <summary>
    /// O <c>ficha.md</c> que o personagem importado ganha antes de qualquer conversa.
    ///
    /// <para><b>Por que já nasce com um.</b> É o arquivo que o Dungeon Master lê para saber quem
    /// é o personagem, e o único que sobrevive à conversa que o produziu. Um personagem importado
    /// sem ele seria uma pasta com um PDF dentro: a interface o listaria, e a primeira conversa
    /// começaria perguntando tudo de novo.</para>
    ///
    /// <para>O texto diz, em toda linha que puder, que estes valores <b>não foram conferidos</b>.
    /// Eles vieram de um PDF preenchido à mão: podem estar errados, desatualizados ou seguir uma
    /// regra de casa. Um dossiê que se apresenta como verdade validada faria o agente construir em
    /// cima de erro sem nunca desconfiar.</para>
    /// </summary>
    private static string DossieProvisorio(
        SistemaRpg sistema,
        Personagem personagem,
        string nomeDoArquivo,
        IReadOnlyDictionary<string, string> aproveitados,
        IReadOnlyDictionary<string, string> foraDoModelo)
    {
        var texto = new StringBuilder();

        texto
            .AppendLine($"# {personagem.Rotulo}")
            .AppendLine()
            .AppendLine($"- Sistema: {sistema.Id}")
            .AppendLine($"- Origem: ficha preenchida `{nomeDoArquivo}`, importada em {DateTime.Now:dd/MM/yyyy}")
            .AppendLine($"- Fontes da mesa: {(personagem.Fontes.Count > 0 ? string.Join(", ", personagem.Fontes) : "jogo base")}")
            .AppendLine()
            .AppendLine("> **Nada aqui foi conferido contra as regras.** Os valores abaixo são o que estava")
            .AppendLine("> escrito nos campos do PDF, transcritos sem interpretação. Antes de evoluir este")
            .AppendLine("> personagem, confira o que estiver em jogo na mudança e reescreva este arquivo com")
            .AppendLine("> `registrar_personagem`.")
            .AppendLine();

        EscreverCampos(texto, "Campos da ficha", aproveitados);

        if (foraDoModelo.Count > 0)
        {
            texto
                .AppendLine("Os campos abaixo estavam preenchidos no PDF trazido, mas não existem na ficha em")
                .AppendLine($"branco de {sistema.Id} — a ficha pode ser de outra edição, ou de uma versão adaptada.")
                .AppendLine("Eles não entram na hora de gerar o PDF de novo.")
                .AppendLine();

            EscreverCampos(texto, "Campos sem correspondência na ficha do sistema", foraDoModelo);
        }

        return texto.ToString();
    }

    /// <summary>
    /// Valor de uma linha vira item de lista; valor de várias vira seção própria — uma quebra de
    /// linha no meio de um item de lista Markdown some, e "história do personagem" é justamente o
    /// campo em que ela existe.
    /// </summary>
    private static void EscreverCampos(
        StringBuilder texto,
        string titulo,
        IReadOnlyDictionary<string, string> campos)
    {
        texto.AppendLine($"## {titulo}").AppendLine();

        if (campos.Count == 0)
        {
            texto.AppendLine("_Nenhum campo com valor._").AppendLine();
            return;
        }

        var ordenados = campos.OrderBy(par => par.Key, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var (campo, valor) in ordenados.Where(par => !par.Value.Contains('\n')))
        {
            texto.AppendLine($"- **{campo}**: {valor}");
        }

        foreach (var (campo, valor) in ordenados.Where(par => par.Value.Contains('\n')))
        {
            texto
                .AppendLine()
                .AppendLine($"### {campo}")
                .AppendLine()
                .AppendLine(valor);
        }

        texto.AppendLine();
    }
}
