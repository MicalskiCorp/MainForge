using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// O registro de progresso é o que transforma "recomeçar do zero" em "continuar de onde
/// parou". O que precisa valer sempre: o disco manda (arquivo que existe está pronto, arquivo
/// que sumiu voltou a faltar), replanejar não apaga progresso, e livro trocado precisa ser
/// lido de novo.
/// </summary>
public sealed class EstadoDoProcessamentoTestes : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "mainforge-estado-" + Guid.NewGuid());
    private readonly CaminhosDoProjeto _caminhos;

    public EstadoDoProcessamentoTestes() => _caminhos = new CaminhosDoProjeto(_raiz);

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    private void GravarConhecimento(string caminhoRelativo, string conteudo = "# x")
    {
        var completo = Path.Combine(_caminhos.Conhecimento, "Aventura&Cia", caminhoRelativo);
        Directory.CreateDirectory(Path.GetDirectoryName(completo)!);
        File.WriteAllText(completo, conteudo);
    }

    private string GravarLivro(string nome, string conteudo = "pdf")
    {
        var completo = Path.Combine(_caminhos.Sistemas, "Aventura&Cia", nome);
        Directory.CreateDirectory(Path.GetDirectoryName(completo)!);
        File.WriteAllText(completo, conteudo);
        return completo;
    }

    private EstadoDoProcessamento Carregar()
    {
        var estado = EstadoDoProcessamento.Carregar(_caminhos, "Aventura&Cia");
        estado.SincronizarComDisco();
        return estado;
    }

    [Fact]
    public void Sincronizar_ItemComArquivoEmDiscoContaComoConcluido()
    {
        GravarConhecimento("Classes/Guerreiro.md");

        var estado = EstadoDoProcessamento.Carregar(_caminhos, "Aventura&Cia");
        estado.RegistrarPlano(
        [
            new ItemDoPlano { Caminho = "Classes/Guerreiro.md" },
            new ItemDoPlano { Caminho = "Classes/Mago.md" },
        ]);
        estado.SincronizarComDisco();

        Assert.Equal(["Classes/Mago.md"], estado.Pendentes.Select(item => item.Caminho));
        Assert.Equal(["Classes/Guerreiro.md"], estado.Concluidos.Select(item => item.Caminho));
    }

    /// <summary>
    /// Apagar um .md na mão é a forma mais direta de dizer "regere este aqui". Se o registro
    /// insistisse que ele está pronto, o usuário não teria como pedir isso sem apagar a base
    /// inteira.
    /// </summary>
    [Fact]
    public void Sincronizar_ArquivoApagadoVoltaAPendente()
    {
        GravarConhecimento("Classes/Guerreiro.md");
        var estado = Carregar();
        estado.MarcarArquivo("Classes/Guerreiro.md");
        estado.Salvar();

        File.Delete(Path.Combine(_caminhos.Conhecimento, "Aventura&Cia", "Classes", "Guerreiro.md"));

        Assert.Equal(["Classes/Guerreiro.md"], Carregar().Pendentes.Select(item => item.Caminho));
    }

    /// <summary>
    /// Bases geradas antes de o registro existir precisam contar como prontas — senão a
    /// primeira execução depois da atualização recomeçaria tudo.
    /// </summary>
    [Fact]
    public void Sincronizar_ArquivoForaDoPlanoEntraComoConcluido()
    {
        GravarConhecimento("Regras-Fundamentais.md");

        var estado = Carregar();

        Assert.Equal(["Regras-Fundamentais.md"], estado.Concluidos.Select(item => item.Caminho));
        Assert.Empty(estado.Pendentes);
    }

    /// <summary>
    /// Adotar a base sem trazer as descrições produziria um registro de caminhos mudos — quem
    /// fosse continuar o processamento veria "o que já existe" sem saber o que há dentro.
    /// </summary>
    [Fact]
    public void Sincronizar_ArquivoAdotadoHerdaADescricaoDoIndice()
    {
        GravarConhecimento("Racas.md", "# Racas\n\nAs linhagens jogaveis e seus bonus.\n");
        IndiceDeConhecimento.Reconstruir(_caminhos, "Aventura&Cia");

        var item = Assert.Single(Carregar().Concluidos);

        Assert.Equal("Racas.md", item.Caminho);
        Assert.Contains("linhagens jogaveis", item.Descricao);
    }

    [Fact]
    public void Sincronizar_IgnoraOsIndicesGerados()
    {
        GravarConhecimento("Classes/Guerreiro.md");
        GravarConhecimento("Classes/" + IndiceDeConhecimento.NomeDoArquivo);

        Assert.Equal(["Classes/Guerreiro.md"], Carregar().Concluidos.Select(item => item.Caminho));
    }

    [Fact]
    public void RegistrarPlano_DeNovo_NaoApagaOProgressoNemADescricao()
    {
        GravarConhecimento("Classes/Guerreiro.md");
        var estado = Carregar();
        estado.RegistrarPlano([new ItemDoPlano { Caminho = "Classes/Guerreiro.md", Descricao = "Classe Guerreiro" }]);

        estado.RegistrarPlano([new ItemDoPlano { Caminho = "Classes/Guerreiro.md" }]);

        var item = Assert.Single(estado.Plano);
        Assert.Equal(EstadoDoItem.Concluido, item.Estado);
        Assert.Equal("Classe Guerreiro", item.Descricao);
    }

    [Fact]
    public void Livros_NovoEntraComoPendenteEDepoisDeMarcadoFicaLido()
    {
        GravarLivro("Livro Basico.pdf");

        var estado = Carregar();
        Assert.Equal(["Livro Basico.pdf"], estado.LivrosPendentes.Select(livro => livro.Arquivo));

        estado.MarcarLivrosConcluidos();
        estado.Salvar();

        Assert.Empty(Carregar().LivrosPendentes);
    }

    /// <summary>
    /// Trocar o PDF mantendo o nome é como o usuário atualiza um livro. Sem detectar isso, o
    /// conteúdo novo nunca entraria na base.
    /// </summary>
    [Fact]
    public void Livros_ArquivoTrocadoVoltaAPendente()
    {
        var caminho = GravarLivro("Livro Basico.pdf");
        var estado = Carregar();
        estado.MarcarLivrosConcluidos();
        estado.Salvar();

        File.WriteAllText(caminho, "conteudo diferente, tamanho diferente");
        File.SetLastWriteTimeUtc(caminho, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(["Livro Basico.pdf"], Carregar().LivrosPendentes.Select(livro => livro.Arquivo));
    }

    [Fact]
    public void Persistencia_SobreviveAUmNovoCarregamento()
    {
        GravarConhecimento("Classes/Guerreiro.md");
        var estado = Carregar();
        estado.RegistrarPlano([new ItemDoPlano { Caminho = "Classes/Mago.md", Descricao = "Classe Mago" }]);
        estado.Salvar();

        var recarregado = Carregar();

        Assert.Equal("Classe Mago", Assert.Single(recarregado.Pendentes).Descricao);
        Assert.Contains("1 de 2", recarregado.Resumo());
    }

    [Fact]
    public void Carregar_ArquivoCorrompido_ComecaDoZeroEmVezDeQuebrar()
    {
        var caminho = EstadoDoProcessamento.CaminhoDoArquivo(_caminhos, "Aventura&Cia");
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, "{ isto nao e json valido");

        var estado = EstadoDoProcessamento.Carregar(_caminhos, "Aventura&Cia");

        Assert.Empty(estado.Plano);
        Assert.False(estado.TemHistorico);
    }

    [Fact]
    public void Limpar_ApagaORegistroParaOUsuarioPoderRecomecarDoZero()
    {
        GravarConhecimento("Classes/Guerreiro.md");
        var estado = Carregar();
        estado.Salvar();

        estado.Limpar();

        Assert.False(File.Exists(EstadoDoProcessamento.CaminhoDoArquivo(_caminhos, "Aventura&Cia")));
        Assert.Empty(estado.Plano);
    }
}
