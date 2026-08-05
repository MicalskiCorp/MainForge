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
        var completo = Path.Combine(_caminhos.Entrada, "Aventura&Cia", nome);
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

    /// <summary>
    /// Copiar a pasta do projeto, restaurar um backup ou deixar uma sincronização de nuvem passar
    /// por cima muda a data de modificação sem trocar nada dentro do livro. Enquanto a data
    /// mandava, isso marcava os dois livros de D&amp;D5e como "ainda não lidos" numa base que
    /// estava inteira — e a execução seguinte oferecia relê-los, que é a operação mais cara do
    /// aplicativo.
    /// </summary>
    [Fact]
    public void Livros_SoADataMudou_ContinuaLido()
    {
        var caminho = GravarLivro("Livro Basico.pdf");
        var estado = Carregar();
        estado.MarcarLivrosConcluidos();
        estado.Salvar();

        File.SetLastWriteTimeUtc(caminho, DateTime.UtcNow.AddYears(1));

        Assert.Empty(Carregar().LivrosPendentes);
    }

    /// <summary>
    /// O contrapeso do teste acima: conteúdo diferente do mesmo tamanho — uma errata, uma edição
    /// revisada — tem que voltar a pendente, senão o conteúdo novo nunca entraria na base.
    /// </summary>
    [Fact]
    public void Livros_MesmoTamanhoEConteudoDiferente_VoltaAPendente()
    {
        var caminho = GravarLivro("Livro Basico.pdf", "conteudo original");
        var estado = Carregar();
        estado.MarcarLivrosConcluidos();
        estado.Salvar();

        File.WriteAllText(caminho, "conteudo revisado");
        File.SetLastWriteTimeUtc(caminho, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(["Livro Basico.pdf"], Carregar().LivrosPendentes.Select(livro => livro.Arquivo));
    }

    /// <summary>
    /// Um registro gravado antes de existir o hash não tem como ser conferido pelo conteúdo. Aí o
    /// tamanho idêntico basta: errar para o lado de "mudou" custaria a releitura do livro inteiro.
    /// </summary>
    [Fact]
    public void Livros_RegistroAntigoSemHash_NaoVoltaAPendenteSoPelaData()
    {
        var caminho = GravarLivro("Livro Basico.pdf");
        var informacao = new FileInfo(caminho);

        var estado = EstadoDoProcessamento.Carregar(_caminhos, "Aventura&Cia");
        estado.Livros.Add(new LivroDoSistema
        {
            Arquivo = "Livro Basico.pdf",
            Tamanho = informacao.Length,
            ModificadoEm = informacao.LastWriteTimeUtc.AddYears(-3).Ticks,
            Estado = EstadoDoItem.Concluido,
        });
        estado.Salvar();

        Assert.Empty(Carregar().LivrosPendentes);
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

    /// <summary>
    /// Aconteceu de verdade: o Configurador não conseguiu abrir o PDF de um compêndio
    /// recém-adicionado (o <c>Read</c> do Claude Code precisa do <c>pdftoppm</c>, que não estava
    /// instalado), terminou o turno sem gerar arquivo nenhum e, como o plano antigo já estava
    /// completo, o livro foi registrado como lido. O sistema ficava "pronto" com uma expansão que
    /// ninguém leu — e o aplicativo passava a recusar reprocessá-lo.
    /// </summary>
    [Fact]
    public void FontesSemConhecimento_ApontaAFonteQueConstaComoLidaSemTerGeradoNada()
    {
        GravarConhecimento("base/Classes/Guerreiro.md");
        GravarLivro(Path.Combine("base", "Livro Basico.pdf"));
        GravarLivro(Path.Combine("Compendio-Arcano", "Compendio-Arcano.pdf"));

        var estado = Carregar();
        estado.MarcarLivrosConcluidos();

        Assert.Equal(["Compendio-Arcano"], estado.FontesSemConhecimento(_caminhos));

        // E é possível devolvê-los à fila sem mexer no resto do registro.
        Assert.Equal(["Compendio-Arcano.pdf"], estado.MarcarFontesComoPendentes(["Compendio-Arcano"]));
        Assert.Equal(["Compendio-Arcano.pdf"], estado.LivrosPendentes.Select(livro => livro.Arquivo));
    }

    /// <summary>
    /// Os arquivos da ficha ficam na raiz do sistema e valem para todas as fontes; contá-los como
    /// conteúdo da base faria uma base vazia parecer gerada.
    /// </summary>
    [Fact]
    public void FontesSemConhecimento_ArquivosDaFichaNaoContamComoConteudoDeFonte()
    {
        GravarConhecimento("Ficha-Mapeamento.md");
        GravarLivro(Path.Combine("base", "Livro Basico.pdf"));

        var estado = Carregar();
        estado.MarcarLivrosConcluidos();

        Assert.Equal(["base"], estado.FontesSemConhecimento(_caminhos));
    }

    /// <summary>
    /// Numa base do layout antigo o conhecimento está solto na raiz do sistema. Ele conta como
    /// conteúdo da base — senão um sistema que só espera migração pareceria nunca ter sido lido.
    /// </summary>
    [Fact]
    public void FontesSemConhecimento_LayoutAntigo_NaoAcusaFalsoPositivo()
    {
        GravarConhecimento("Classes/Guerreiro.md");
        GravarLivro("Livro Basico.pdf");

        var estado = Carregar();
        estado.MarcarLivrosConcluidos();

        Assert.Empty(estado.FontesSemConhecimento(_caminhos));
    }

    /// <summary>
    /// É o que autoriza o fluxo a recusar um processamento. Sem plano pendente e sem livro por
    /// ler, chamar o agente só faria ele reler os livros e regravar arquivo pronto — cota gasta
    /// para chegar exatamente onde já se estava.
    /// </summary>
    [Fact]
    public void EstaCompleto_SoQuandoNaoSobraNemArquivoNemLivro()
    {
        GravarConhecimento("Classes/Guerreiro.md");
        GravarLivro("Livro Basico.pdf");

        var estado = Carregar();

        // Livro ainda não incorporado: há o que fazer.
        Assert.False(estado.EstaCompleto);

        estado.MarcarLivrosConcluidos();
        Assert.True(estado.EstaCompleto);

        // Um arquivo planejado que não existe em disco reabre o trabalho.
        estado.RegistrarPlano([new ItemDoPlano { Caminho = "Classes/Mago.md" }]);
        estado.SincronizarComDisco();
        Assert.False(estado.EstaCompleto);
    }

    [Fact]
    public void EstaCompleto_SistemaNuncaProcessado_NaoContaComoCompleto()
    {
        GravarLivro("Livro Basico.pdf");

        Assert.False(Carregar().EstaCompleto);
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
