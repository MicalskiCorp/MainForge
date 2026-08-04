using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Cli;

/// <summary>
/// Panorama do que existe em disco: quais sistemas foram importados, quais já têm base de
/// conhecimento e quais têm ficha editável. É a tela que responde "por que o sistema X não
/// aparece na criação de personagem?".
/// </summary>
internal static class FluxoDeSistemas
{
    public static void Executar(ContextoDoAplicativo contexto)
    {
        var caminhos = contexto.Caminhos;
        var sistemas = SistemaRpg.DescobrirImportados(caminhos);

        ConsoleUi.Titulo("Sistemas");
        ConsoleUi.Detalhe($"Raiz do projeto: {caminhos.Raiz}");

        if (sistemas.Count == 0)
        {
            ConsoleUi.Aviso($"\nNenhum sistema em {caminhos.Sistemas}.");
            ConsoleUi.Info("Para adicionar um: crie Systems/<NomeDoSistema>/ com o PDF do livro,");
            ConsoleUi.Info("e Templates/<NomeDoSistema>/ com a ficha em PDF editável (AcroForm).");
            return;
        }

        ConsoleUi.Info("");
        ConsoleUi.Info($"  {"Sistema",-24} {"Livros",-8} {"Conhecimento",-32} {"Ficha",-8}");
        ConsoleUi.Detalhe($"  {new string('-', 24)} {new string('-', 8)} {new string('-', 32)} {new string('-', 8)}");

        var pendencias = new List<string>();

        foreach (var sistema in sistemas)
        {
            var livros = Contar(sistema.DiretorioSistemas(caminhos), "*.pdf");
            var fichas = Contar(sistema.DiretorioModelo(caminhos), "*.pdf");

            var descricaoConhecimento = "não gerado";

            if (sistema.TemConhecimento(caminhos))
            {
                var estado = EstadoDoProcessamento.Carregar(caminhos, sistema.Id);
                estado.SincronizarComDisco();
                descricaoConhecimento = estado.Resumo();

                if (estado.Pendentes.Count > 0 || estado.LivrosPendentes.Count > 0)
                {
                    pendencias.Add(sistema.Id);
                }
            }

            var descricaoFicha = fichas == 0 ? "faltando" : $"{fichas}";

            ConsoleUi.Info($"  {sistema.Id,-24} {livros,-8} {descricaoConhecimento,-32} {descricaoFicha,-8}");

            MostrarFontes(caminhos, sistema);
        }

        if (pendencias.Count > 0)
        {
            ConsoleUi.Info("");
            ConsoleUi.Aviso($"Com processamento incompleto: {string.Join(", ", pendencias)}.");
            ConsoleUi.Info("Processar de novo continua de onde parou — não recomeça do zero.");
        }

        OferecerMigracaoParaFontes(caminhos, sistemas);
        OferecerReindexacao(caminhos, sistemas);

        var personagens = Contar(caminhos.SaidaPersonagens, "*.pdf");
        ConsoleUi.Info("");
        ConsoleUi.Detalhe($"Fichas já geradas em Output/Personagens/: {personagens}");
    }

    /// <summary>
    /// Mostra de que fontes o sistema é feito — o jogo base e cada expansão, com quantos livros
    /// cada uma tem e se o conhecimento dela já foi gerado. É esta lista que vira a pergunta
    /// "quais expansões esta mesa usa?" na criação de personagem, então vale conferir aqui se
    /// um compêndio caiu na pasta certa.
    /// </summary>
    private static void MostrarFontes(CaminhosDoProjeto caminhos, SistemaRpg sistema)
    {
        var comLivro = sistema.DescobrirFontes(caminhos);

        if (comLivro.Count == 0)
        {
            return;
        }

        var comConhecimento = sistema
            .DescobrirFontesComConhecimento(caminhos)
            .Select(fonte => fonte.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var fonte in comLivro)
        {
            var livros = Contar(sistema.DiretorioDaFonte(caminhos, fonte), "*.pdf");
            var situacao = comConhecimento.Contains(fonte.Id) ? "conhecimento gerado" : "ainda não processada";

            ConsoleUi.Detalhe($"    · {fonte.Id,-20} {livros} livro(s) — {situacao}");
        }
    }

    /// <summary>
    /// Leva um sistema do layout antigo — livros e conhecimento soltos na pasta do sistema —
    /// para o layout com fonte, movendo tudo para <c>base/</c>.
    ///
    /// <para>Enquanto isso não acontece, a pergunta "quais expansões esta mesa usa?" não tem o
    /// que oferecer: não há como distinguir o que veio do livro básico do que veio de um
    /// compêndio. Reprocessar resolveria também, mas custaria a releitura dos livros inteiros;
    /// mover arquivo não custa nada.</para>
    /// </summary>
    private static void OferecerMigracaoParaFontes(CaminhosDoProjeto caminhos, IReadOnlyList<SistemaRpg> sistemas)
    {
        var antigos = sistemas.Where(sistema => sistema.PrecisaMigrarParaFontes(caminhos)).ToList();

        if (antigos.Count == 0)
        {
            return;
        }

        ConsoleUi.Info("");
        ConsoleUi.Aviso($"No layout antigo, sem separação entre jogo base e expansões: {string.Join(", ", antigos.Select(sistema => sistema.Id))}.");
        ConsoleUi.Info($"Os livros e o conhecimento desses sistemas vão para a pasta '{FonteDoSistema.IdDaBase}/', e");
        ConsoleUi.Info("a partir daí cada compêndio novo pode entrar numa pasta própria — que é o que");
        ConsoleUi.Info("permite escolher, ao criar um personagem, quais expansões aquela mesa usa.");
        ConsoleUi.Detalhe("É só mover arquivo: não relê livro nenhum e não custa tokens.");

        if (!ConsoleUi.Confirmar("Migrar agora?"))
        {
            return;
        }

        foreach (var sistema in antigos)
        {
            try
            {
                var resultado = MigracaoDeFontes.Migrar(caminhos, sistema.Id);

                ConsoleUi.Sucesso(
                    $"  {sistema.Id}: {resultado.Livros.Count} livro(s) e {resultado.Conhecimento.Count} item(ns) " +
                    $"de conhecimento em {FonteDoSistema.IdDaBase}/.");
            }
            catch (Exception excecao) when (excecao is IOException or UnauthorizedAccessException)
            {
                ConsoleUi.Erro($"  {sistema.Id}: {excecao.Message}");
            }
        }
    }

    /// <summary>
    /// Adota uma base que existe em disco mas não tem índice nem registro de progresso — o caso
    /// de uma base gerada por uma versão anterior do aplicativo, ou de um processamento que
    /// morreu antes de registrar qualquer coisa.
    ///
    /// <para>Nada aqui chama agente: os índices saem do conteúdo das pastas e o registro sai da
    /// comparação entre o que está em <c>Knowledge/</c> e os PDFs em <c>Systems/</c>. Como é de
    /// graça, vale oferecer sempre que faltar — é o que faz a próxima execução continuar em vez
    /// de recomeçar.</para>
    /// </summary>
    private static void OferecerReindexacao(CaminhosDoProjeto caminhos, IReadOnlyList<SistemaRpg> sistemas)
    {
        var semRegistro = sistemas
            .Where(sistema => sistema.TemConhecimento(caminhos))
            .Where(sistema => !File.Exists(Path.Combine(
                                  sistema.DiretorioConhecimento(caminhos),
                                  IndiceDeConhecimento.NomeDoArquivo)) ||
                              !File.Exists(EstadoDoProcessamento.CaminhoDoArquivo(caminhos, sistema.Id)))
            .ToList();

        if (semRegistro.Count == 0)
        {
            return;
        }

        ConsoleUi.Info("");
        ConsoleUi.Aviso($"Sem índice ou sem registro de progresso: {string.Join(", ", semRegistro.Select(sistema => sistema.Id))}.");
        ConsoleUi.Info("O índice é o que permite ao Dungeon Master achar uma regra sem abrir a base inteira;");
        ConsoleUi.Info("o registro é o que faz o Configurador continuar de onde parou em vez de recomeçar.");
        ConsoleUi.Detalhe("Gerar os dois não custa tokens — sai do que já está em disco.");

        if (!ConsoleUi.Confirmar("Gerar agora?"))
        {
            return;
        }

        foreach (var sistema in semRegistro)
        {
            var indices = IndiceDeConhecimento.Reconstruir(caminhos, sistema.Id);

            var estado = EstadoDoProcessamento.Carregar(caminhos, sistema.Id);
            estado.SincronizarComDisco();
            estado.Salvar();

            ConsoleUi.Sucesso($"  {sistema.Id}: {indices.Count} índice(s); {estado.Resumo()}.");
        }
    }

    private static int Contar(string diretorio, string padrao) =>
        Directory.Exists(diretorio)
            ? Directory.EnumerateFiles(diretorio, padrao, SearchOption.AllDirectories).Count()
            : 0;
}
