using System.IO.Compression;
using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// O pacote existe para não pagar duas vezes pela mesma leitura de livro. Os testes cobrem a
/// ida e a volta, as recusas que evitam entregar um pacote inútil do outro lado (mapeamento da
/// ficha incompleto, sistema sem base) e a única falha que seria grave de verdade: um
/// <c>.zip</c> vem de fora e pode carregar caminhos que escapam da pasta de destino.
/// </summary>
public sealed class PacoteDeSistemaTestes : IDisposable
{
    private const string Sistema = "Aventura&Cia";

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "mainforge-pacote-" + Guid.NewGuid());
    private readonly CaminhosDoProjeto _caminhos;

    public PacoteDeSistemaTestes()
    {
        _caminhos = new CaminhosDoProjeto(_raiz);

        GravarConhecimento("base/Classes/Guerreiro.md", "# Guerreiro\n\nDado de vida d10.");
        GravarConhecimento("base/index.md", "# Indice — base");
        GravarConhecimento("Compendio-Arcano/Classes/Mago-Sombrio.md", "# Mago Sombrio");
        GravarConhecimento(SistemaRpg.ArquivosDaFicha[0], "# Mapeamento\n\nNome -> Nome.");
        GravarConhecimento(SistemaRpg.ArquivosDaFicha[1], "# Modelo\n\n{{Nome}}");

        GravarFicha("Ficha.pdf");
    }

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    private void GravarConhecimento(string relativo, string conteudo, string sistema = Sistema)
    {
        var caminho = Path.Combine(_caminhos.Conhecimento, sistema, relativo.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, conteudo);
    }

    private void GravarFicha(string nome, string sistema = Sistema)
    {
        var caminho = Path.Combine(_caminhos.Modelos, sistema, nome);

        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, "%PDF-1.4 fingido");
    }

    private string Exportar() =>
        PacoteDeSistema.Exportar(_caminhos, Sistema, Path.Combine(_raiz, "saida", PacoteDeSistema.NomeSugerido(Sistema))).Arquivo;

    [Fact]
    public void Exportar_LevaConhecimentoFichaEManifesto()
    {
        var resultado = PacoteDeSistema.Exportar(
            _caminhos, Sistema, Path.Combine(_raiz, "saida", PacoteDeSistema.NomeSugerido(Sistema)));

        Assert.True(File.Exists(resultado.Arquivo));
        Assert.Equal(Sistema, resultado.Manifesto.Sistema);
        Assert.Equal(5, resultado.Manifesto.ArquivosDeConhecimento);
        Assert.Contains("Ficha.pdf", resultado.Manifesto.Fichas);
        Assert.Contains("base", resultado.Manifesto.Fontes);
        Assert.Contains("Compendio-Arcano", resultado.Manifesto.Fontes);
    }

    /// <summary>
    /// Os PDFs dos livros são obra de quem os publicou e pesam dezenas de MB. O pacote leva o
    /// que foi destilado deles, não eles.
    /// </summary>
    [Fact]
    public void Exportar_NaoLevaOsLivrosOriginais()
    {
        var livro = Path.Combine(_caminhos.Entrada, Sistema, "base", "Livro.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(livro)!);
        File.WriteAllText(livro, "%PDF fingido");

        using var pacote = ZipFile.OpenRead(Exportar());

        Assert.DoesNotContain(pacote.Entries, entrada => entrada.FullName.Contains("Livro.pdf"));
    }

    /// <summary>
    /// Sem os dois arquivos da ficha o destinatário recebe uma base que não consegue preencher
    /// PDF nenhum — e descobrir isso do outro lado é tarde demais.
    /// </summary>
    [Fact]
    public void Exportar_SemMapeamentoDaFicha_ERecusado()
    {
        File.Delete(Path.Combine(_caminhos.Conhecimento, Sistema, SistemaRpg.ArquivosDaFicha[0]));

        var erro = Assert.Throws<InvalidOperationException>(Exportar);

        Assert.Contains(SistemaRpg.ArquivosDaFicha[0], erro.Message);
    }

    [Fact]
    public void Exportar_SistemaSemBase_ERecusado()
    {
        var erro = Assert.Throws<InvalidOperationException>(() =>
            PacoteDeSistema.Exportar(_caminhos, "Inexistente", Path.Combine(_raiz, "saida", "x.zip")));

        Assert.Contains("base de conhecimento", erro.Message);
    }

    [Fact]
    public void Importar_ReconstroiOSistemaComIndiceEProgresso()
    {
        var pacote = Exportar();

        var destino = new CaminhosDoProjeto(Path.Combine(_raiz, "outra-maquina"));
        destino.GarantirEstrutura();

        var resultado = PacoteDeSistema.Importar(destino, pacote);

        Assert.Equal(Sistema, resultado.Sistema.Id);
        Assert.True(File.Exists(Path.Combine(destino.Conhecimento, Sistema, "base", "Classes", "Guerreiro.md")));
        Assert.True(File.Exists(Path.Combine(destino.Modelos, Sistema, "Ficha.pdf")));
        Assert.True(File.Exists(Path.Combine(destino.Conhecimento, Sistema, IndiceDeConhecimento.NomeDoArquivo)));

        // Sem os livros não há o que ler: o sistema chega pronto, e não "pela metade".
        var estado = EstadoDoProcessamento.Carregar(destino, Sistema);
        estado.SincronizarComDisco();

        Assert.Empty(estado.Pendentes);
        Assert.Empty(estado.LivrosPendentes);
    }

    /// <summary>
    /// O sistema que chegou por pacote não tem livro nenhum em <c>Input/</c>. Se a interface
    /// olhasse só ali, ele sumiria da tela logo depois de ter sido importado com sucesso —
    /// embora dê para criar personagem nele na mesma hora.
    /// </summary>
    [Fact]
    public void Importar_SistemaAparecaNaListaMesmoSemLivros()
    {
        var pacote = Exportar();

        var destino = new CaminhosDoProjeto(Path.Combine(_raiz, "outra-maquina"));
        destino.GarantirEstrutura();

        PacoteDeSistema.Importar(destino, pacote);

        Assert.Empty(SistemaRpg.DescobrirImportados(destino));
        Assert.Contains(SistemaRpg.DescobrirTodos(destino), sistema => sistema.Id == Sistema);
        Assert.Contains(SistemaRpg.DescobrirProntos(destino), sistema => sistema.Id == Sistema);
    }

    /// <summary>
    /// Acrescentar os PDFs depois é o caminho que a própria tela de importação sugere. A
    /// exigência de "importe o livro básico antes" existe contra um compêndio solto, sem jogo
    /// base em lugar nenhum — e aqui a base existe, só veio pronta.
    /// </summary>
    [Fact]
    public void Importar_DepoisAceitaLivrosNovosNoSistemaRecebido()
    {
        var pacote = Exportar();

        var destino = new CaminhosDoProjeto(Path.Combine(_raiz, "outra-maquina"));
        destino.GarantirEstrutura();
        PacoteDeSistema.Importar(destino, pacote);

        var livro = Path.Combine(_raiz, "Compendio.pdf");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "RegrasTeste.pdf"), livro);

        var copiados = ImportadorDeSistema.AdicionarLivros(
            destino, Sistema, new FonteDoSistema("Compendio"), [livro]);

        Assert.Equal(["Compendio.pdf"], copiados);
    }

    /// <summary>
    /// Uma base pode ter custado horas de cota. Apagá-la porque um pacote de mesmo nome chegou
    /// seria o pior erro possível deste fluxo.
    /// </summary>
    [Fact]
    public void Importar_SobreSistemaExistente_ERecusadoSemAutorizacao()
    {
        var pacote = Exportar();

        var erro = Assert.Throws<InvalidOperationException>(() => PacoteDeSistema.Importar(_caminhos, pacote));

        Assert.Contains("Já existe", erro.Message);
    }

    [Fact]
    public void Importar_ComOutroNome_NaoMexeNoSistemaOriginal()
    {
        var pacote = Exportar();

        var resultado = PacoteDeSistema.Importar(_caminhos, pacote, "Aventura&Cia-Copia");

        Assert.Equal("Aventura&Cia-Copia", resultado.Sistema.Id);
        Assert.True(File.Exists(Path.Combine(_caminhos.Conhecimento, Sistema, "base", "Classes", "Guerreiro.md")));
    }

    /// <summary>
    /// "Zip slip": o caminho de dentro do arquivo é caminho vindo de fora, e vale a mesma regra
    /// de todo caminho não confiável — quem resolve é o C#, e o que escapa é recusado.
    /// </summary>
    [Fact]
    public void Importar_ComCaminhoQueEscapaDaPastaDeDestino_ERecusado()
    {
        var malicioso = Path.Combine(_raiz, "malicioso.zip");

        using (var arquivo = new FileStream(malicioso, FileMode.Create))
        using (var pacote = new ZipArchive(arquivo, ZipArchiveMode.Create))
        {
            Escrever(pacote, PacoteDeSistema.NomeDoManifesto, """
                { "formato": 1, "sistema": "Invasor", "fontes": ["base"], "arquivosDeConhecimento": 1, "fichas": [] }
                """);

            Escrever(pacote, "conhecimento/../../../../invadido.md", "# invadido");
        }

        Assert.Throws<UnauthorizedAccessException>(() => PacoteDeSistema.Importar(_caminhos, malicioso));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_raiz)!, "invadido.md")));
    }

    [Fact]
    public void LerManifesto_ArquivoQueNaoEPacote_ExplicaOQueFalta()
    {
        var qualquerZip = Path.Combine(_raiz, "qualquer.zip");

        using (var arquivo = new FileStream(qualquerZip, FileMode.Create))
        using (var pacote = new ZipArchive(arquivo, ZipArchiveMode.Create))
        {
            Escrever(pacote, "leiame.txt", "nada a ver");
        }

        var erro = Assert.Throws<InvalidOperationException>(() => PacoteDeSistema.LerManifesto(qualquerZip));

        Assert.Contains(PacoteDeSistema.NomeDoManifesto, erro.Message);
    }

    [Fact]
    public void LerManifesto_DeFormatoMaisNovo_PedeParaAtualizarEmVezDeAdivinhar()
    {
        var doFuturo = Path.Combine(_raiz, "futuro.zip");

        using (var arquivo = new FileStream(doFuturo, FileMode.Create))
        using (var pacote = new ZipArchive(arquivo, ZipArchiveMode.Create))
        {
            Escrever(pacote, PacoteDeSistema.NomeDoManifesto, """
                { "formato": 999, "sistema": "Futuro" }
                """);
        }

        var erro = Assert.Throws<InvalidOperationException>(() => PacoteDeSistema.LerManifesto(doFuturo));

        Assert.Contains("versão mais nova", erro.Message);
    }

    private static void Escrever(ZipArchive pacote, string nome, string conteudo)
    {
        using var fluxo = pacote.CreateEntry(nome).Open();
        using var escritor = new StreamWriter(fluxo);

        escritor.Write(conteudo);
    }
}
