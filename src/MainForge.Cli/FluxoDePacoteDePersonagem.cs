using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Cli;

/// <summary>
/// Leva um personagem inteiro de uma instalação para outra: o dossiê, o estado em texto e o
/// histórico de fichas por nível.
///
/// <para><b>Por que é um pacote separado do de sistema.</b> São coisas de donos diferentes. O
/// sistema é o resultado de ler os livros, igual para todo mundo que tem aqueles livros; o
/// personagem é de quem joga, e muda de máquina com a pessoa. Exportar um sistema não pode levar
/// junto os personagens de quem o exportou.</para>
///
/// <para>Quem tem só o PDF da ficha continua entrando por 'Importar personagem' com o arquivo em
/// mãos — o que muda é o que chega: uma ficha, sem passado. Com o pacote, chega o personagem
/// inteiro.</para>
/// </summary>
internal static class FluxoDePacoteDePersonagem
{
    public static void Exportar(ContextoDoAplicativo contexto, IReadOnlyList<Personagem> personagens)
    {
        var caminhos = contexto.Caminhos;

        if (personagens.Count == 0)
        {
            ConsoleUi.Info("Nenhum personagem para exportar.");
            return;
        }

        ConsoleUi.Titulo("Exportar um personagem");
        ConsoleUi.Info("O pacote leva o dossiê, o estado em texto e a ficha de cada nível concluído.");
        ConsoleUi.Detalhe("A base de conhecimento do sistema não vai junto: ela se exporta pelo menu Sistemas,");
        ConsoleUi.Detalhe("e quem receber precisa dela para continuar evoluindo o personagem.");

        var personagem = ConsoleUi.Escolher("Exportar qual personagem?", personagens, DescreverParaExportar);

        if (personagem is null)
        {
            return;
        }

        var destino = Path.Combine(caminhos.SaidaPacotes, PacoteDePersonagem.NomeSugerido(personagem));

        try
        {
            var resultado = PacoteDePersonagem.Exportar(caminhos, personagem, destino);

            ConsoleUi.Sucesso($"\nPacote gerado: {Path.GetRelativePath(caminhos.Raiz, resultado.Arquivo)}");
            ConsoleUi.Info($"  {resultado.Manifesto.Niveis.Count} ficha(s) de nível, {resultado.Bytes / 1024} KB.");
            ConsoleUi.Detalhe($"  Sistema: {resultado.Manifesto.Sistema}; fontes: {DescreverFontes(resultado.Manifesto.Fontes)}.");
            ConsoleUi.Info("");
            ConsoleUi.Info("Copie o arquivo para a outra máquina e use 'Importar personagem' lá.");
            ConsoleUi.Detalhe("Lá também precisa existir o sistema — por pacote de sistema ou processado dos livros.");
        }
        catch (Exception excecao) when (excecao is InvalidOperationException or ArgumentException or IOException)
        {
            ConsoleUi.Erro($"Exportação cancelada: {excecao.Message}");
        }
    }

    /// <summary>
    /// Traz um pacote de personagem. Quem chama já sabe que o caminho é um <c>.zip</c>: a escolha
    /// entre ficha solta e pacote é feita pela extensão do arquivo que o usuário informou, e não
    /// por uma pergunta a mais numa tela que já pergunta bastante.
    /// </summary>
    public static void Importar(ContextoDoAplicativo contexto, string caminhoDoPacote)
    {
        var caminhos = contexto.Caminhos;

        ManifestoDoPersonagem manifesto;

        try
        {
            manifesto = PacoteDePersonagem.LerManifesto(caminhoDoPacote);
        }
        catch (Exception excecao) when (excecao is InvalidOperationException or FileNotFoundException or IOException)
        {
            ConsoleUi.Erro(excecao.Message);
            return;
        }

        ConsoleUi.Info("");
        ConsoleUi.Info($"  Personagem: {(manifesto.Nome.Length > 0 ? manifesto.Nome : manifesto.Personagem)}");
        ConsoleUi.Info($"  Sistema:    {manifesto.Sistema}");
        ConsoleUi.Info($"  Gerado em:  {manifesto.GeradoEm:dd/MM/yyyy HH:mm}");
        ConsoleUi.Info($"  Fontes:     {DescreverFontes(manifesto.Fontes)}");
        ConsoleUi.Info($"  Histórico:  {DescreverNiveis(manifesto.Niveis)}");

        if (manifesto.Resumo.Length > 0)
        {
            ConsoleUi.Detalhe($"  {manifesto.Resumo}");
        }

        if (!new SistemaRpg(manifesto.Sistema).TemConhecimento(caminhos))
        {
            ConsoleUi.Info("");
            ConsoleUi.Aviso($"Esta máquina não tem base de conhecimento de '{manifesto.Sistema}'.");
            ConsoleUi.Detalhe("O personagem entra do mesmo jeito, mas evoluir só funciona depois que o sistema");
            ConsoleUi.Detalhe("existir aqui — por pacote de sistema, ou processando os livros.");
        }

        var id = manifesto.Personagem;
        var substituir = false;

        if (Directory.Exists(RepositorioDePersonagens.DiretorioDoPersonagem(caminhos, manifesto.Sistema, id)))
        {
            ConsoleUi.Info("");
            ConsoleUi.Aviso($"Já existe Personagens/{manifesto.Sistema}/{id}/ nesta máquina.");
            ConsoleUi.Detalhe("Substituir apaga o dossiê e o histórico que estão lá, e eles não voltam.");

            substituir = ConsoleUi.Confirmar("Substituir o personagem existente?");

            if (!substituir)
            {
                var alternativo = ConsoleUi.LerLinha("Importar com outro identificador (Enter cancela): ");

                if (alternativo.Length == 0)
                {
                    return;
                }

                id = alternativo;
            }
        }

        if (!ConsoleUi.Confirmar($"\nImportar '{manifesto.Personagem}' como '{id}'?"))
        {
            return;
        }

        try
        {
            var resultado = PacoteDePersonagem.Importar(caminhos, caminhoDoPacote, id, substituir);
            var personagem = resultado.Personagem;

            ConsoleUi.Sucesso($"\n'{personagem.Rotulo}' importado em {personagem.Sistema}.");
            ConsoleUi.Detalhe($"Dossiê: Personagens/{personagem.Sistema}/{personagem.Id}/");
            ConsoleUi.Detalhe($"Histórico: {resultado.FichasDoHistorico} ficha(s) em Personagens/{personagem.Sistema}/{personagem.Id}/{FichasDoPersonagem.NomeDaPasta}/");

            if (personagem.FichaGerada is { } ficha)
            {
                ConsoleUi.Detalhe($"Ficha atual: {ficha}");
            }

            ConsoleUi.Info("");
            ConsoleUi.Info(resultado.SistemaPresente
                ? "Dá para evoluir este personagem agora, pelo menu de personagens."
                : $"Traga a base de '{personagem.Sistema}' para esta máquina antes de evoluí-lo.");
        }
        catch (Exception excecao) when (excecao is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            ConsoleUi.Erro($"Importação cancelada: {excecao.Message}");
        }
    }

    private static string DescreverParaExportar(Personagem personagem)
    {
        var detalhes = new List<string> { personagem.Sistema, Personagem.DescreverStatus(personagem.Status) };

        detalhes.Add(personagem.Fichas.Count switch
        {
            0 => "sem ficha no histórico",
            1 => "1 ficha no histórico",
            var quantas => $"{quantas} fichas no histórico",
        });

        return $"{personagem.Rotulo}  ({string.Join("; ", detalhes)})";
    }

    private static string DescreverFontes(IReadOnlyList<string> fontes) =>
        fontes.Count > 0 ? string.Join(", ", fontes) : "só o jogo base";

    private static string DescreverNiveis(IReadOnlyList<int?> niveis) => niveis.Count == 0
        ? "nenhuma ficha guardada"
        : string.Join(", ", niveis.Select(nivel => nivel is { } numero ? $"nível {numero}" : "sem nível"));
}
