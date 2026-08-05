using MainForge.Core;

namespace MainForge.ClaudeCode;

/// <summary>
/// O <c>Read</c> do Claude Code consegue abrir um PDF nesta máquina?
///
/// <para>Ele não lê PDF como texto: <b>rasteriza</b> as páginas pedidas e manda imagens ao
/// modelo. Quem rasteriza é o <c>pdftoppm</c>, do poppler, que não vem com o Claude Code nem com
/// o Windows. Sem ele, toda leitura de PDF falha com <c>pdftoppm is not installed</c> — e o
/// agente só descobre isso gastando um turno.</para>
///
/// <para>Saber disso <em>antes</em> é o que permite ao aplicativo não oferecer ao agente um
/// caminho que não existe, em vez de deixá-lo tropeçar nele a cada livro.</para>
/// </summary>
public static class LeituraDePdf
{
    /// <summary>O programa que o Claude Code chama para transformar página de PDF em imagem.</summary>
    public const string ProgramaNecessario = "pdftoppm";

    /// <summary>
    /// Como instalar, para a mensagem ao usuário. O poppler é opcional aqui: o aplicativo já
    /// converte os livros para texto sozinho, e a leitura do PDF só serve como recurso extra
    /// (leiaute de uma tabela, um quadro que só existe como imagem).
    /// </summary>
    public const string ComoInstalar =
        "Sem o poppler (pdftoppm), o agente não consegue abrir PDF nenhum — nem os livros, nem a\n" +
        "ficha em branco. Os livros ele lê pelo texto convertido, mas a ficha ele lê do PDF.\n" +
        "  winget install --id oschwartz10612.Poppler   (ou baixe o poppler e ponha o bin/ no PATH)";

    public static bool Disponivel() => ProgramaDoSistema.Localizar(ProgramaNecessario) is not null;
}
