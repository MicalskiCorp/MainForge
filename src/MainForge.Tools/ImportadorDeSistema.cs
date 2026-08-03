using MainForge.Core;
using PdfSharp.Fonts;
using PdfSharp.Pdf.IO;

namespace MainForge.Tools;

/// <summary>
/// Resultado de uma importação bem-sucedida, para a interface poder mostrar ao usuário o que
/// entrou no projeto.
/// </summary>
/// <param name="Sistema">O sistema criado ou atualizado.</param>
/// <param name="Livros">Nomes dos arquivos de livro copiados para Systems/&lt;sistema&gt;/.</param>
/// <param name="Ficha">Nome do arquivo de ficha copiado para Templates/&lt;sistema&gt;/.</param>
/// <param name="CamposDaFicha">Campos de formulário encontrados na ficha.</param>
public sealed record ResultadoDaImportacao(
    SistemaRpg Sistema,
    IReadOnlyList<string> Livros,
    string Ficha,
    IReadOnlyList<string> CamposDaFicha);

/// <summary>
/// Traz para dentro do projeto os arquivos que o usuário informa: os livros do sistema (PDF)
/// e a ficha de personagem editável (PDF com AcroForm). Valida antes de copiar — um livro
/// ilegível ou uma "ficha" sem campos de formulário só apareceria muito depois, no meio de
/// uma conversa com o Dungeon Master, e aí já teria custado tokens.
///
/// Mora em MainForge.Tools (e não na interface) porque é aqui que estão o PdfSharp e o
/// resolvedor de fontes do Windows de que a leitura de AcroForm depende.
/// </summary>
public static class ImportadorDeSistema
{
    static ImportadorDeSistema()
    {
        GlobalFontSettings.FontResolver ??= new ResolvedorDeFontesDoWindows();
    }

    public static ResultadoDaImportacao Importar(
        CaminhosDoProjeto caminhos,
        string nomeDoSistema,
        IReadOnlyList<string> caminhosDosLivros,
        string caminhoDaFicha)
    {
        var sistema = ValidarNome(nomeDoSistema);

        if (caminhosDosLivros.Count == 0)
        {
            throw new ArgumentException("Informe ao menos um livro em PDF.", nameof(caminhosDosLivros));
        }

        foreach (var livro in caminhosDosLivros)
        {
            ValidarPdfLegivel(livro, "livro");
        }

        ValidarPdfLegivel(caminhoDaFicha, "ficha");
        var camposDaFicha = LerCamposDaFicha(caminhoDaFicha);

        // Só copia depois de tudo validado, para não deixar o projeto num estado pela metade.
        var diretorioDoSistema = CaminhosDoProjeto.ResolverDentroDe(caminhos.Sistemas, sistema.Id);
        var diretorioDoModelo = CaminhosDoProjeto.ResolverDentroDe(caminhos.Modelos, sistema.Id);

        Directory.CreateDirectory(diretorioDoSistema);
        Directory.CreateDirectory(diretorioDoModelo);

        var livrosCopiados = new List<string>();

        foreach (var livro in caminhosDosLivros)
        {
            var nomeDoArquivo = Path.GetFileName(livro);
            File.Copy(livro, Path.Combine(diretorioDoSistema, nomeDoArquivo), overwrite: true);
            livrosCopiados.Add(nomeDoArquivo);
        }

        var nomeDaFicha = Path.GetFileName(caminhoDaFicha);
        File.Copy(caminhoDaFicha, Path.Combine(diretorioDoModelo, nomeDaFicha), overwrite: true);

        return new ResultadoDaImportacao(sistema, livrosCopiados, nomeDaFicha, camposDaFicha);
    }

    /// <summary>
    /// Acrescenta livros a um sistema que já existe — o caso dos compêndios e expansões, que
    /// não trazem ficha nova nem substituem o livro básico, só somam conteúdo ao sistema.
    ///
    /// <para>Exige que o sistema já tenha sido importado: um compêndio sozinho não descreve a
    /// criação de personagem inteira, e processá-lo sem o livro básico produziria uma base de
    /// conhecimento cheia de buracos.</para>
    /// </summary>
    /// <returns>Os nomes dos arquivos copiados para <c>Systems/&lt;sistema&gt;/</c>.</returns>
    public static IReadOnlyList<string> AdicionarLivros(
        CaminhosDoProjeto caminhos,
        string nomeDoSistema,
        IReadOnlyList<string> caminhosDosLivros)
    {
        var sistema = ValidarNome(nomeDoSistema);
        var diretorioDoSistema = CaminhosDoProjeto.ResolverDentroDe(caminhos.Sistemas, sistema.Id);

        if (!Directory.Exists(diretorioDoSistema))
        {
            throw new InvalidOperationException(
                $"O sistema '{sistema.Id}' ainda não foi importado. Importe o livro básico e a ficha antes " +
                "de acrescentar um compêndio ou expansão.");
        }

        if (caminhosDosLivros.Count == 0)
        {
            throw new ArgumentException("Informe ao menos um livro em PDF.", nameof(caminhosDosLivros));
        }

        foreach (var livro in caminhosDosLivros)
        {
            ValidarPdfLegivel(livro, "livro");
        }

        var copiados = new List<string>();

        foreach (var livro in caminhosDosLivros)
        {
            var nomeDoArquivo = Path.GetFileName(livro);
            File.Copy(livro, Path.Combine(diretorioDoSistema, nomeDoArquivo), overwrite: true);
            copiados.Add(nomeDoArquivo);
        }

        return copiados;
    }

    /// <summary>
    /// Lê os nomes dos campos de formulário de uma ficha, sem copiar nada. A interface usa
    /// isto para mostrar ao usuário o que encontrou antes de ele confirmar a importação.
    /// </summary>
    public static IReadOnlyList<string> LerCamposDaFicha(string caminhoDaFicha)
    {
        using var documento = PdfReader.Open(caminhoDaFicha, PdfDocumentOpenMode.Import);

        var formulario = documento.AcroForm
            ?? throw new InvalidOperationException(
                $"'{Path.GetFileName(caminhoDaFicha)}' não tem campos de formulário (AcroForm). " +
                "A ficha precisa ser um PDF editável/preenchível, não um PDF só de leitura ou digitalizado.");

        var campos = formulario.Fields.Names.ToList();

        return campos.Count > 0
            ? campos
            : throw new InvalidOperationException(
                $"'{Path.GetFileName(caminhoDaFicha)}' tem um AcroForm, mas nenhum campo preenchível dentro dele.");
    }

    /// <summary>
    /// O nome do sistema vira nome de pasta em Systems/, Templates/ e Knowledge/, então
    /// precisa ser um nome de pasta simples — nada de barra, "..", nem caractere proibido.
    /// </summary>
    private static SistemaRpg ValidarNome(string nomeDoSistema)
    {
        var nome = nomeDoSistema.Trim();

        if (string.IsNullOrEmpty(nome))
        {
            throw new ArgumentException("O nome do sistema não pode ser vazio.", nameof(nomeDoSistema));
        }

        if (nome != Path.GetFileName(nome) || nome is "." or "..")
        {
            throw new ArgumentException(
                $"'{nomeDoSistema}' não serve como nome de pasta — use um nome simples, ex.: \"Aventura&Cia\".",
                nameof(nomeDoSistema));
        }

        var proibidos = Path.GetInvalidFileNameChars().Where(nome.Contains).ToList();

        return proibidos.Count == 0
            ? new SistemaRpg(nome)
            : throw new ArgumentException(
                $"O nome '{nomeDoSistema}' tem caractere(s) não permitido(s): {string.Join(" ", proibidos)}",
                nameof(nomeDoSistema));
    }

    private static void ValidarPdfLegivel(string caminho, string oQueE)
    {
        if (!File.Exists(caminho))
        {
            throw new FileNotFoundException($"Arquivo de {oQueE} não encontrado: '{caminho}'.", caminho);
        }

        if (!caminho.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"O {oQueE} '{Path.GetFileName(caminho)}' não é um PDF.", nameof(caminho));
        }

        try
        {
            using var documento = PdfReader.Open(caminho, PdfDocumentOpenMode.Import);

            if (documento.PageCount == 0)
            {
                throw new InvalidOperationException($"O {oQueE} '{Path.GetFileName(caminho)}' não tem páginas.");
            }
        }
        catch (Exception excecao) when (excecao is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Não consegui abrir o {oQueE} '{Path.GetFileName(caminho)}' como PDF: {excecao.Message}", excecao);
        }
    }
}
