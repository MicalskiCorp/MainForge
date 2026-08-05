using MainForge.Core;

namespace MainForge.Tools;

/// <summary>O que a migração moveu, para a interface poder contar ao usuário.</summary>
/// <param name="Livros">PDFs movidos para <c>Input/&lt;sistema&gt;/base/</c>.</param>
/// <param name="Conhecimento">Arquivos e pastas movidos para <c>Sistemas/&lt;sistema&gt;/base/</c>.</param>
public sealed record ResultadoDaMigracao(IReadOnlyList<string> Livros, IReadOnlyList<string> Conhecimento)
{
    public bool MoveuAlgo => Livros.Count > 0 || Conhecimento.Count > 0;
}

/// <summary>
/// Move um sistema do layout antigo — tudo solto dentro da pasta do sistema — para o layout com
/// fonte, em que o jogo base fica em <c>base/</c> e cada expansão na pasta com o nome dela.
///
/// <para><b>Por que existe.</b> A separação por fonte é o que permite escolher, na criação do
/// personagem, quais expansões valem. Um sistema importado antes disso tem tudo misturado, e
/// reprocessá-lo custaria a leitura dos livros inteiros de novo — a operação mais cara do
/// aplicativo. Mover arquivo não custa token nenhum e chega no mesmo lugar.</para>
///
/// <para>O que <b>não</b> se move: o registro de progresso e os dois arquivos da ficha, que
/// valem para o sistema inteiro, e o <c>index.md</c>, que é regerado no fim a partir do que
/// ficou em disco.</para>
/// </summary>
public static class MigracaoDeFontes
{
    public static ResultadoDaMigracao Migrar(CaminhosDoProjeto caminhos, string nomeDoSistema)
    {
        var sistema = new SistemaRpg(NomeDePasta.Validar(nomeDoSistema, "sistema", nameof(nomeDoSistema)));

        // Em Input/ só os PDFs soltos descem para base/: uma subpasta que já exista ali já é
        // uma fonte, e engoli-la dentro de base/ apagaria justamente a separação que se quer.
        var livros = MoverParaBase(
            sistema.DiretorioEntrada(caminhos),
            sistema.DiretorioDaFonte(caminhos, FonteDoSistema.Base),
            nome => false,
            moverPastas: false);

        // Em Sistemas/ as pastas descem junto: "Classes/", "Racas/" e companhia são conteúdo do
        // jogo base, não fontes — antes desta mudança o Configurador nunca criava pasta de fonte.
        var conhecimento = MoverParaBase(
            sistema.DiretorioConhecimento(caminhos),
            sistema.DiretorioConhecimentoDaFonte(caminhos, FonteDoSistema.Base),
            FicaNaRaizDeKnowledge,
            moverPastas: true);

        if (conhecimento.Count > 0)
        {
            ReapontarPlano(caminhos, sistema);
        }

        if (livros.Count > 0 || conhecimento.Count > 0)
        {
            // Os index.md vieram junto no arrasto e agora descrevem um nível que mudou de lugar.
            IndiceDeConhecimento.Reconstruir(caminhos, sistema.Id);

            var estado = EstadoDoProcessamento.Carregar(caminhos, sistema.Id);
            estado.SincronizarComDisco();
            estado.Salvar();
        }

        return new ResultadoDaMigracao(livros, conhecimento);
    }

    /// <summary>
    /// O registro de progresso guarda caminhos relativos à raiz do sistema; depois da mudança
    /// todos eles ganham o prefixo <c>base/</c>. Sem isso o próximo processamento veria o plano
    /// inteiro como pendente e mandaria o agente gerar de novo o que já está pronto.
    /// </summary>
    private static void ReapontarPlano(CaminhosDoProjeto caminhos, SistemaRpg sistema)
    {
        var estado = EstadoDoProcessamento.Carregar(caminhos, sistema.Id);

        if (estado.Plano.Count == 0)
        {
            return;
        }

        var raizDoConhecimento = sistema.DiretorioConhecimento(caminhos);

        for (var indice = 0; indice < estado.Plano.Count; indice++)
        {
            var item = estado.Plano[indice];
            var nome = Path.GetFileName(item.Caminho);

            if (FicaNaRaizDeKnowledge(nome) || item.Caminho.StartsWith(FonteDoSistema.IdDaBase + "/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var noNovoLugar = Path.Combine(raizDoConhecimento, FonteDoSistema.IdDaBase, item.Caminho.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(noNovoLugar))
            {
                estado.Plano[indice] = item with { Caminho = $"{FonteDoSistema.IdDaBase}/{item.Caminho}" };
            }
        }

        estado.Salvar();
    }

    /// <summary>
    /// Move para <c>base/</c> o que estiver solto na raiz. Se <c>base/</c> já existe, o sistema
    /// já está migrado e nada acontece — é o que torna a operação segura de repetir.
    /// </summary>
    private static IReadOnlyList<string> MoverParaBase(
        string raiz,
        string destino,
        Func<string, bool> ficaNaRaiz,
        bool moverPastas)
    {
        if (!Directory.Exists(raiz) || Directory.Exists(destino))
        {
            return [];
        }

        var arquivos = Directory
            .EnumerateFiles(raiz, "*", SearchOption.TopDirectoryOnly)
            .Where(caminho => !ficaNaRaiz(Path.GetFileName(caminho)))
            .ToList();

        var pastas = moverPastas ? Directory.EnumerateDirectories(raiz).ToList() : [];

        if (arquivos.Count == 0 && pastas.Count == 0)
        {
            return [];
        }

        Directory.CreateDirectory(destino);

        var movidos = new List<string>();

        foreach (var arquivo in arquivos)
        {
            var nome = Path.GetFileName(arquivo);
            File.Move(arquivo, Path.Combine(destino, nome));
            movidos.Add(nome);
        }

        foreach (var pasta in pastas)
        {
            var nome = Path.GetFileName(pasta);
            Directory.Move(pasta, Path.Combine(destino, nome));
            movidos.Add(nome + "/");
        }

        return movidos;
    }

    private static bool FicaNaRaizDeKnowledge(string nome) =>
        nome.Equals(IndiceDeConhecimento.NomeDoArquivo, StringComparison.OrdinalIgnoreCase) ||
        nome.Equals(EstadoDoProcessamento.NomeDoArquivo, StringComparison.OrdinalIgnoreCase) ||
        SistemaRpg.ArquivosDaFicha.Contains(nome, StringComparer.OrdinalIgnoreCase);
}
