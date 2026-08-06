using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Cli;

/// <summary>
/// Leva um sistema já mapeado de uma instalação para outra: exporta a base de conhecimento e a
/// ficha em PDF num arquivo só, e traz de volta do outro lado.
///
/// <para><b>Por que isto vale a pena.</b> Mapear um sistema é a única operação do aplicativo que
/// custa a cota da assinatura, e o resultado é o mesmo para todo mundo que tem aqueles livros.
/// Sem pacote, cada máquina paga de novo pela mesma leitura — e quem tem uma cota pequena não
/// paga.</para>
///
/// <para>Os PDFs dos livros ficam de fora: são obra comercial e pesam dezenas de MB. O que
/// viaja é a base destilada mais a ficha em branco, que é o suficiente para criar personagem.</para>
/// </summary>
internal static class FluxoDePacotes
{
    public static void Exportar(ContextoDoAplicativo contexto)
    {
        var caminhos = contexto.Caminhos;

        var exportaveis = SistemaRpg
            .DescobrirProntos(caminhos)
            .Where(sistema => sistema.TemConhecimento(caminhos))
            .ToList();

        if (exportaveis.Count == 0)
        {
            ConsoleUi.Aviso("Nenhum sistema com base de conhecimento para exportar.");
            return;
        }

        ConsoleUi.Titulo("Exportar um sistema");
        ConsoleUi.Info("O pacote leva a base de conhecimento em Markdown e a ficha em PDF do sistema.");
        ConsoleUi.Detalhe("Os livros originais não vão junto: são obra de quem os publicou, e a base já basta");
        ConsoleUi.Detalhe("para o Dungeon Master trabalhar. Quem receber pode criar personagem de imediato.");

        var sistema = ConsoleUi.Escolher(
            "Exportar qual sistema?",
            exportaveis,
            escolhido => DescreverParaExportar(caminhos, escolhido));

        if (sistema is null)
        {
            return;
        }

        var destino = Path.Combine(caminhos.SaidaPacotes, PacoteDeSistema.NomeSugerido(sistema.Id));

        try
        {
            var resultado = PacoteDeSistema.Exportar(caminhos, sistema.Id, destino);

            ConsoleUi.Sucesso($"\nPacote gerado: {Path.GetRelativePath(caminhos.Raiz, resultado.Arquivo)}");
            ConsoleUi.Info($"  {resultado.Manifesto.ArquivosDeConhecimento} arquivo(s) de conhecimento, " +
                           $"{resultado.Manifesto.Fichas.Count} ficha(s), {resultado.Bytes / 1024} KB.");
            ConsoleUi.Detalhe($"  Fontes: {string.Join(", ", resultado.Manifesto.Fontes)}.");
            ConsoleUi.Info("");
            ConsoleUi.Info("Basta copiar esse arquivo para a outra máquina e usar a importação de pacote lá.");
        }
        catch (Exception excecao) when (excecao is InvalidOperationException or ArgumentException or IOException)
        {
            ConsoleUi.Erro($"Exportação cancelada: {excecao.Message}");
        }
    }

    public static void Importar(ContextoDoAplicativo contexto)
    {
        var caminhos = contexto.Caminhos;

        ConsoleUi.Titulo("Importar um pacote de sistema");
        ConsoleUi.Detalhe($"Arquivo '{PacoteDeSistema.Extensao}' gerado pela exportação em outra instalação.");
        ConsoleUi.Detalhe("Arraste o arquivo para a janela do console para colar o caminho. Enter vazio cancela.");

        var caminho = EntradaDeArquivos.Limpar(ConsoleUi.LerLinha("\n  Pacote: "));

        if (caminho.Length == 0)
        {
            return;
        }

        ManifestoDoPacote manifesto;

        try
        {
            manifesto = PacoteDeSistema.LerManifesto(caminho);
        }
        catch (Exception excecao) when (excecao is InvalidOperationException or FileNotFoundException or IOException)
        {
            ConsoleUi.Erro(excecao.Message);
            return;
        }

        ConsoleUi.Info("");
        ConsoleUi.Info($"  Sistema:      {manifesto.Sistema}");
        ConsoleUi.Info($"  Gerado em:    {manifesto.GeradoEm:dd/MM/yyyy HH:mm}");
        ConsoleUi.Info($"  Fontes:       {string.Join(", ", manifesto.Fontes)}");
        ConsoleUi.Info($"  Conhecimento: {manifesto.ArquivosDeConhecimento} arquivo(s)");
        ConsoleUi.Info($"  Ficha:        {string.Join(", ", manifesto.Fichas)}");

        var nome = manifesto.Sistema;
        var substituir = false;

        if (new SistemaRpg(nome).TemConhecimento(caminhos))
        {
            ConsoleUi.Info("");
            ConsoleUi.Aviso($"Já existe uma base em Sistemas/{nome}/ nesta máquina.");
            ConsoleUi.Detalhe("Substituí-la apaga o que está lá — inclusive o que custou cota para gerar.");

            substituir = ConsoleUi.Confirmar("Substituir a base existente?");

            if (!substituir)
            {
                var alternativo = ConsoleUi.LerLinha("Importar com outro nome (Enter cancela): ");

                if (alternativo.Length == 0)
                {
                    return;
                }

                nome = alternativo;
            }
        }

        if (!ConsoleUi.Confirmar($"\nImportar '{manifesto.Sistema}' como '{nome}'?"))
        {
            return;
        }

        try
        {
            var resultado = PacoteDeSistema.Importar(caminhos, caminho, nome, substituir);

            ConsoleUi.Sucesso($"\nSistema '{resultado.Sistema.Id}' importado.");
            ConsoleUi.Info($"  {resultado.ArquivosDeConhecimento} arquivo(s) em Sistemas/{resultado.Sistema.Id}/");
            ConsoleUi.Info($"  {resultado.Fichas.Count} ficha(s) em Templates/{resultado.Sistema.Id}/");
            ConsoleUi.Info("");
            ConsoleUi.Info("Já dá para criar personagem neste sistema — a base veio pronta.");
            ConsoleUi.Detalhe("Os livros em PDF não vêm no pacote. Para acrescentar conteúdo depois, importe os");
            ConsoleUi.Detalhe("PDFs com 'Adicionar livros' e processe: só os livros novos são lidos.");
        }
        catch (Exception excecao) when (excecao is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            ConsoleUi.Erro($"Importação cancelada: {excecao.Message}");
        }
    }

    /// <summary>
    /// Diz de saída o que impede a exportação. Sem os dois arquivos da ficha o pacote não serve
    /// para nada do outro lado, e descobrir isso só ao tentar exportar é tarde.
    /// </summary>
    private static string DescreverParaExportar(CaminhosDoProjeto caminhos, SistemaRpg sistema)
    {
        var diretorio = sistema.DiretorioConhecimento(caminhos);

        var faltando = SistemaRpg.ArquivosDaFicha
            .Where(nome => !File.Exists(Path.Combine(diretorio, nome)))
            .ToList();

        var temFicha = Directory.Exists(sistema.DiretorioModelo(caminhos)) &&
                       Directory.EnumerateFiles(sistema.DiretorioModelo(caminhos), "*.pdf").Any();

        var problemas = new List<string>();

        if (faltando.Count > 0)
        {
            problemas.Add($"falta {string.Join(" e ", faltando)}");
        }

        if (!temFicha)
        {
            problemas.Add("sem ficha em Templates/");
        }

        return problemas.Count == 0
            ? $"{sistema.Id}  (pronto para exportar)"
            : $"{sistema.Id}  ({string.Join("; ", problemas)})";
    }
}
