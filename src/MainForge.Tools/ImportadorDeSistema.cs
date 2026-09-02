using MainForge.Core;
using PdfSharp.Fonts;
using PdfSharp.Pdf.IO;

namespace MainForge.Tools;

/// <summary>
/// Resultado de uma importação bem-sucedida, para a interface poder mostrar ao usuário o que
/// entrou no projeto.
/// </summary>
/// <param name="Sistema">O sistema criado ou atualizado.</param>
/// <param name="Fonte">A fonte em que os livros entraram — sempre o jogo base, aqui.</param>
/// <param name="Livros">Nomes dos arquivos de livro copiados para Input/&lt;sistema&gt;/base/.</param>
/// <param name="Ficha">Nome do arquivo de ficha copiado para Templates/&lt;sistema&gt;/.</param>
/// <param name="CamposDaFicha">Campos de formulário encontrados na ficha.</param>
/// <param name="Regras">
/// O esqueleto de validação gerado a partir do AcroForm da ficha, ou <c>null</c> quando não deu
/// para lê-lo. É o que já permite conferir um personagem neste sistema antes de qualquer livro
/// ter sido processado — ver <see cref="RegrasDaFicha"/>.
/// </param>
public sealed record ResultadoDaImportacao(
    SistemaRpg Sistema,
    FonteDoSistema Fonte,
    IReadOnlyList<string> Livros,
    string Ficha,
    IReadOnlyList<string> CamposDaFicha,
    RegrasDaFicha? Regras = null);

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

    /// <summary>
    /// Cria o sistema a partir dos livros do jogo base e da ficha em branco. Os livros vão para
    /// <c>Input/&lt;sistema&gt;/base/</c>: quem importa um sistema está trazendo o jogo base
    /// por definição — expansão entra depois, por <see cref="AdicionarLivros"/>, e precisa de um
    /// sistema já existente para se somar.
    /// </summary>
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
        var diretorioDaFonte = sistema.DiretorioDaFonte(caminhos, FonteDoSistema.Base);
        var diretorioDoModelo = CaminhosDoProjeto.ResolverDentroDe(caminhos.Modelos, sistema.Id);

        Directory.CreateDirectory(diretorioDaFonte);
        Directory.CreateDirectory(diretorioDoModelo);

        var livrosCopiados = Copiar(caminhosDosLivros, diretorioDaFonte);

        var nomeDaFicha = Path.GetFileName(caminhoDaFicha);
        var fichaCopiada = Path.Combine(diretorioDoModelo, nomeDaFicha);
        File.Copy(caminhoDaFicha, fichaCopiada, overwrite: true);

        return new ResultadoDaImportacao(
            sistema,
            FonteDoSistema.Base,
            livrosCopiados,
            nomeDaFicha,
            camposDaFicha,
            GerarEsqueletoDeValidacao(caminhos, sistema, fichaCopiada));
    }

    /// <summary>
    /// Grava o <c>Ficha-Validacao.json</c> inicial do sistema, montado do AcroForm da ficha que
    /// acabou de entrar.
    ///
    /// <para><b>Por que na importação, e não só no mapeamento.</b> O Configurador é quem sabe as
    /// regras do jogo, mas ele roda depois — às vezes muito depois, às vezes nunca, num sistema
    /// que o usuário importou para conferir uma ficha pronta. O que dá para responder já é o que
    /// não depende de livro nenhum: quais campos a ficha tem, quais são caixa de marcação, em que
    /// idioma ela está e se há espaço para as magias por extenso. Com isso, um personagem
    /// importado neste sistema já é conferido no minuto seguinte, e a folha extra de magias já
    /// sabe se precisa existir.</para>
    ///
    /// <para>Falha em ler o leiaute não derruba a importação: o sistema entra do mesmo jeito, só
    /// sem validação — que é exatamente a situação de todo sistema anterior a esta versão.</para>
    /// </summary>
    private static RegrasDaFicha? GerarEsqueletoDeValidacao(
        CaminhosDoProjeto caminhos,
        SistemaRpg sistema,
        string caminhoDaFicha)
    {
        try
        {
            var regras = RegrasDaFicha.Esqueleto(LayoutDaFicha.Ler(caminhoDaFicha));
            RegrasDaFicha.Gravar(caminhos, sistema.Id, regras);

            return regras;
        }
        catch (Exception excecao) when (excecao is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// Acrescenta livros a uma fonte de um sistema que já existe: uma expansão nova, uma
    /// expansão que ganhou outro volume, ou um livro que faltava no próprio jogo base.
    ///
    /// <para>Exige que o sistema já tenha sido importado: um compêndio sozinho não descreve a
    /// criação de personagem inteira, e processá-lo sem o livro básico produziria uma base de
    /// conhecimento cheia de buracos.</para>
    ///
    /// <para>Cada expansão tem pasta própria — é ela que permite ao usuário dizer, na criação do
    /// personagem, que aquela mesa usa este compêndio e não aquele.</para>
    /// </summary>
    /// <returns>Os nomes dos arquivos copiados para <c>Input/&lt;sistema&gt;/&lt;fonte&gt;/</c>.</returns>
    public static IReadOnlyList<string> AdicionarLivros(
        CaminhosDoProjeto caminhos,
        string nomeDoSistema,
        FonteDoSistema fonte,
        IReadOnlyList<string> caminhosDosLivros)
    {
        var sistema = ValidarNome(nomeDoSistema);
        var diretorioDoSistema = CaminhosDoProjeto.ResolverDentroDe(caminhos.Entrada, sistema.Id);

        // Ter base de conhecimento vale tanto quanto ter livro: é o caso de um sistema que chegou
        // por pacote, em que a base veio pronta e os PDFs ficaram do outro lado. O que a exigência
        // impede é o oposto — um compêndio solto, sem jogo base em lugar nenhum, que produziria
        // uma base cheia de buracos.
        if (!Directory.Exists(diretorioDoSistema) && !sistema.TemConhecimento(caminhos))
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

        var destino = sistema.DiretorioDaFonte(caminhos, FonteDoSistema.Criar(fonte.Id));
        Directory.CreateDirectory(destino);

        return Copiar(caminhosDosLivros, destino);
    }

    private static IReadOnlyList<string> Copiar(IReadOnlyList<string> origens, string destino)
    {
        var copiados = new List<string>();

        foreach (var origem in origens)
        {
            var nomeDoArquivo = Path.GetFileName(origem);
            File.Copy(origem, Path.Combine(destino, nomeDoArquivo), overwrite: true);
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

        var formulario = FormularioDeFicha.Obter(documento)
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
    /// O nome do sistema vira nome de pasta em Input/, Templates/ e Sistemas/, então
    /// precisa ser um nome de pasta simples — nada de barra, "..", nem caractere proibido.
    /// A regra é a mesma do nome de expansão, e por isso mora em <see cref="NomeDePasta"/>.
    /// </summary>
    private static SistemaRpg ValidarNome(string nomeDoSistema) =>
        new(NomeDePasta.Validar(nomeDoSistema, "sistema", nameof(nomeDoSistema)));

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
