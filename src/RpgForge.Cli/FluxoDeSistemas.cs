using RpgForge.Core;

namespace RpgForge.Cli;

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
        ConsoleUi.Info($"  {"Sistema",-24} {"Livros",-8} {"Conhecimento",-16} {"Ficha",-8}");
        ConsoleUi.Detalhe($"  {new string('-', 24)} {new string('-', 8)} {new string('-', 16)} {new string('-', 8)}");

        foreach (var sistema in sistemas)
        {
            var livros = Contar(sistema.DiretorioSistemas(caminhos), "*.pdf");
            var conhecimento = Contar(sistema.DiretorioConhecimento(caminhos), "*.md");
            var fichas = Contar(sistema.DiretorioModelo(caminhos), "*.pdf");

            var descricaoConhecimento = conhecimento == 0 ? "não gerado" : $"{conhecimento} arquivo(s)";
            var descricaoFicha = fichas == 0 ? "faltando" : $"{fichas}";

            ConsoleUi.Info($"  {sistema.Id,-24} {livros,-8} {descricaoConhecimento,-16} {descricaoFicha,-8}");
        }

        var personagens = Contar(caminhos.SaidaPersonagens, "*.pdf");
        ConsoleUi.Info("");
        ConsoleUi.Detalhe($"Fichas já geradas em Output/Personagens/: {personagens}");
    }

    private static int Contar(string diretorio, string padrao) =>
        Directory.Exists(diretorio)
            ? Directory.EnumerateFiles(diretorio, padrao, SearchOption.AllDirectories).Count()
            : 0;
}
