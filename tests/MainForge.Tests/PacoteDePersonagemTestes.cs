using System.IO.Compression;
using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// O personagem inteiro viajando de uma instalação para outra: dossiê, estado em texto e a ficha
/// de cada nível concluído.
///
/// <para>O teste que importa de verdade é o de ponta a ponta — exportar aqui e importar numa raiz
/// vazia. É lá que aparecem os erros que uma verificação por partes não pega: caminho do
/// histórico que continua apontando para a pasta da máquina de origem, ficha atual que não é
/// recriada em <c>Output/</c>, identificador trocado na importação que deixa o dossiê achando as
/// fichas de outro personagem.</para>
/// </summary>
public sealed class PacoteDePersonagemTestes : IDisposable
{
    private const string Sistema = "Aventura&Cia";

    private readonly string _origem;
    private readonly string _destino;
    private readonly CaminhosDoProjeto _daOrigem;
    private readonly CaminhosDoProjeto _doDestino;

    public PacoteDePersonagemTestes()
    {
        var identificador = Guid.NewGuid();
        _origem = Path.Combine(Path.GetTempPath(), "mainforge-origem-" + identificador);
        _destino = Path.Combine(Path.GetTempPath(), "mainforge-destino-" + identificador);

        _daOrigem = new CaminhosDoProjeto(_origem);
        _doDestino = new CaminhosDoProjeto(_destino);

        _daOrigem.GarantirEstrutura();
        _doDestino.GarantirEstrutura();

        PrepararTemplate(_daOrigem);
        PrepararTemplate(_doDestino);
    }

    public void Dispose()
    {
        foreach (var raiz in new[] { _origem, _destino })
        {
            if (Directory.Exists(raiz))
            {
                Directory.Delete(raiz, recursive: true);
            }
        }
    }

    [Fact]
    public void Exportar_LevaODossieEUmaFichaPorNivel()
    {
        var personagem = PersonagemComTresNiveis();

        var resultado = PacoteDePersonagem.Exportar(_daOrigem, personagem, PacoteEm(_origem));

        Assert.Equal([1, 2, 3], resultado.Manifesto.Niveis);
        Assert.True(resultado.Manifesto.TemFichaAtual);

        using var pacote = ZipFile.OpenRead(resultado.Arquivo);
        var nomes = pacote.Entries.Select(entrada => entrada.FullName).ToList();

        Assert.Contains("dossie/personagem.json", nomes);
        Assert.Contains("dossie/ficha.md", nomes);
        Assert.Contains("fichas/nivel-03.pdf", nomes);
        Assert.Contains("ficha-atual.pdf", nomes);
    }

    /// <summary>
    /// O caminho de ida e volta inteiro, que é o que o usuário faz: exportar numa máquina e
    /// importar noutra, vazia.
    /// </summary>
    [Fact]
    public void ExportarEImportar_ReconstroiOPersonagemNaOutraInstalacao()
    {
        var original = PersonagemComTresNiveis();
        var pacote = PacoteEm(_origem);
        PacoteDePersonagem.Exportar(_daOrigem, original, pacote);

        var resultado = PacoteDePersonagem.Importar(_doDestino, pacote);
        var importado = RepositorioDePersonagens.Carregar(_doDestino, Sistema, original.Id)!;

        Assert.Equal(original.Rotulo, importado.Rotulo);
        Assert.Equal(StatusDoPersonagem.Concluido, importado.Status);
        Assert.Equal(original.Fontes, importado.Fontes);
        Assert.Equal([1, 2, 3], importado.Fichas.Select(ficha => ficha.Nivel));
        Assert.Equal(3, resultado.FichasDoHistorico);

        Assert.All(importado.Fichas, ficha => Assert.True(File.Exists(Path.Combine(_destino, ficha.Arquivo))));
        Assert.Contains(
            "Thoradin",
            RepositorioDePersonagens.LerFichaEmTexto(_doDestino, Sistema, importado.Id));
    }

    /// <summary>
    /// Do outro lado também vale a regra: uma ficha por personagem em <c>Output/</c>, com o nome
    /// que ele tem <b>naquela</b> instalação.
    /// </summary>
    [Fact]
    public void Importar_RecriaAFichaAtualEmOutput()
    {
        var original = PersonagemComTresNiveis();
        var pacote = PacoteEm(_origem);
        PacoteDePersonagem.Exportar(_daOrigem, original, pacote);

        var importado = PacoteDePersonagem.Importar(_doDestino, pacote).Personagem;

        var naSaida = Directory.EnumerateFiles(_doDestino.SaidaPersonagens, "*.pdf").ToList();

        Assert.Equal($"{importado.Id}.pdf", Path.GetFileName(Assert.Single(naSaida)));
        Assert.Equal($"Output/Personagens/{importado.Id}.pdf", importado.FichaGerada);
    }

    /// <summary>
    /// Importar com outro identificador não pode deixar o dossiê apontando para as fichas da
    /// pasta de origem — que, na instalação de destino, é a pasta de outro personagem ou não
    /// existe.
    /// </summary>
    [Fact]
    public void Importar_ComOutroIdentificador_ReapontaOHistorico()
    {
        var original = PersonagemComTresNiveis();
        var pacote = PacoteEm(_origem);
        PacoteDePersonagem.Exportar(_daOrigem, original, pacote);

        var importado = PacoteDePersonagem.Importar(_doDestino, pacote, "Thoradin-da-outra-mesa").Personagem;

        Assert.Equal("Thoradin-da-outra-mesa", importado.Id);
        Assert.All(importado.Fichas, ficha =>
        {
            Assert.Contains("Thoradin-da-outra-mesa", ficha.Arquivo);
            Assert.True(File.Exists(Path.Combine(_destino, ficha.Arquivo)));
        });
    }

    /// <summary>
    /// O dossiê que já está lá pode ser outro personagem de mesmo nome, e o histórico dele não
    /// volta. Substituir precisa ser uma decisão, não o padrão.
    /// </summary>
    [Fact]
    public void Importar_SobrePersonagemExistente_ERecusadoSemConfirmacao()
    {
        var original = PersonagemComTresNiveis();
        var pacote = PacoteEm(_origem);
        PacoteDePersonagem.Exportar(_daOrigem, original, pacote);

        PacoteDePersonagem.Importar(_doDestino, pacote);

        var erro = Assert.Throws<InvalidOperationException>(() => PacoteDePersonagem.Importar(_doDestino, pacote));

        Assert.Contains(original.Id, erro.Message);
    }

    [Fact]
    public void Importar_ComSubstituicaoAutorizada_Sobrescreve()
    {
        var original = PersonagemComTresNiveis();
        var pacote = PacoteEm(_origem);
        PacoteDePersonagem.Exportar(_daOrigem, original, pacote);

        PacoteDePersonagem.Importar(_doDestino, pacote);
        var segundo = PacoteDePersonagem.Importar(_doDestino, pacote, idAlternativo: null, substituir: true);

        Assert.Equal(3, segundo.FichasDoHistorico);
    }

    /// <summary>
    /// Sem a base do sistema o personagem entra do mesmo jeito — perder o dossiê por causa disso
    /// seria pior —, mas quem importou precisa saber que ainda não dá para evoluí-lo.
    /// </summary>
    [Fact]
    public void Importar_SemOSistemaNaMaquina_EntraEAvisa()
    {
        var original = PersonagemComTresNiveis();
        var pacote = PacoteEm(_origem);
        PacoteDePersonagem.Exportar(_daOrigem, original, pacote);

        var resultado = PacoteDePersonagem.Importar(_doDestino, pacote);

        Assert.False(resultado.SistemaPresente);
        Assert.NotNull(RepositorioDePersonagens.Carregar(_doDestino, Sistema, original.Id));
    }

    /// <summary>
    /// "Zip slip": o caminho de dentro do arquivo é caminho vindo de fora, e vale a mesma regra
    /// de todo caminho não confiável — quem resolve é o C#, e o que escapa é recusado.
    /// </summary>
    [Fact]
    public void Importar_ComCaminhoQueEscapaDaPastaDeDestino_ERecusado()
    {
        var malicioso = Path.Combine(_origem, "malicioso.zip");

        using (var arquivo = new FileStream(malicioso, FileMode.Create))
        using (var pacote = new ZipArchive(arquivo, ZipArchiveMode.Create))
        {
            Escrever(pacote, PacoteDePersonagem.NomeDoManifesto, """
                { "formato": 1, "sistema": "Aventura&Cia", "personagem": "Invasor", "fontes": ["base"], "niveis": [] }
                """);

            Escrever(pacote, "fichas/../../../../invadido.pdf", "%PDF-1.4");
        }

        Assert.Throws<UnauthorizedAccessException>(() => PacoteDePersonagem.Importar(_doDestino, malicioso));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_destino)!, "invadido.pdf")));
    }

    /// <summary>
    /// Um pacote de sistema tem o mesmo nome de manifesto e outra estrutura dentro. A mensagem
    /// precisa dizer qual é a porta certa, em vez de falhar por falta de um campo.
    /// </summary>
    [Fact]
    public void LerManifesto_DePacoteDeSistema_MandaUsarAImportacaoDeSistema()
    {
        var doSistema = Path.Combine(_origem, "sistema.zip");

        using (var arquivo = new FileStream(doSistema, FileMode.Create))
        using (var pacote = new ZipArchive(arquivo, ZipArchiveMode.Create))
        {
            Escrever(pacote, PacoteDePersonagem.NomeDoManifesto, """
                { "formato": 1, "sistema": "Aventura&Cia", "fontes": ["base"], "arquivosDeConhecimento": 3 }
                """);
        }

        var erro = Assert.Throws<InvalidOperationException>(() => PacoteDePersonagem.LerManifesto(doSistema));

        Assert.Contains("pacote de sistema", erro.Message);
    }

    [Fact]
    public void LerManifesto_DeFormatoMaisNovo_PedeParaAtualizarEmVezDeAdivinhar()
    {
        var doFuturo = Path.Combine(_origem, "futuro.zip");

        using (var arquivo = new FileStream(doFuturo, FileMode.Create))
        using (var pacote = new ZipArchive(arquivo, ZipArchiveMode.Create))
        {
            Escrever(pacote, PacoteDePersonagem.NomeDoManifesto, """
                { "formato": 99, "sistema": "Aventura&Cia", "personagem": "Thoradin" }
                """);
        }

        var erro = Assert.Throws<InvalidOperationException>(() => PacoteDePersonagem.LerManifesto(doFuturo));

        Assert.Contains("Atualize", erro.Message);
    }

    private Personagem PersonagemComTresNiveis()
    {
        var personagem = RepositorioDePersonagens.Criar(_daOrigem, Sistema, "Thoradin", [FonteDoSistema.IdDaBase]);

        RepositorioDePersonagens.Registrar(
            _daOrigem, Sistema, personagem.Id, "Thoradin", "Anão guerreiro", "# Thoradin\n\nAnão guerreiro.", null);

        foreach (var nivel in new[] { 1, 2, 3 })
        {
            var gerado = PreenchedorDeFicha.Preencher(
                _daOrigem,
                Sistema,
                null,
                new Dictionary<string, string> { ["Nome"] = "Thoradin" },
                FichasDoPersonagem.NomeNaSaida(_daOrigem, personagem));

            RepositorioDePersonagens.RegistrarFichaGerada(
                _daOrigem, Sistema, personagem.Id, gerado,
                new Dictionary<string, string> { ["Nome"] = "Thoradin" }, nivel);
        }

        return RepositorioDePersonagens.Carregar(_daOrigem, Sistema, personagem.Id)!;
    }

    private string PacoteEm(string raiz) =>
        Path.Combine(raiz, "Thoradin" + PacoteDePersonagem.Extensao);

    private static void Escrever(ZipArchive pacote, string nome, string conteudo)
    {
        using var fluxo = pacote.CreateEntry(nome).Open();
        using var escritor = new StreamWriter(fluxo);

        escritor.Write(conteudo);
    }

    private static void PrepararTemplate(CaminhosDoProjeto caminhos)
    {
        var diretorio = Path.Combine(caminhos.Modelos, Sistema);
        Directory.CreateDirectory(diretorio);

        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "FichaTeste.pdf"),
            Path.Combine(diretorio, "Ficha.pdf"),
            overwrite: true);
    }
}
