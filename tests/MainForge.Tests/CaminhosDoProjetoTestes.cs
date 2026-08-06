using MainForge.Core;

namespace MainForge.Tests;

public class CaminhosDoProjetoTestes
{
    [Fact]
    public void ResolverDentroDe_PermiteCaminhoDentroDaBase()
    {
        var caminhoBase = Path.GetTempPath();

        var resolvido = CaminhosDoProjeto.ResolverDentroDe(caminhoBase, "algum-arquivo.md");

        Assert.StartsWith(Path.GetFullPath(caminhoBase), resolvido);
    }

    [Theory]
    [InlineData("../fora.md")]
    [InlineData("../../fora.md")]
    [InlineData("sub/../../fora.md")]
    public void ResolverDentroDe_RejeitaPathTraversal(string travessia)
    {
        var caminhoBase = Path.Combine(Path.GetTempPath(), "rpgforge-tests-raiz");

        Assert.Throws<UnauthorizedAccessException>(
            () => CaminhosDoProjeto.ResolverDentroDe(caminhoBase, travessia));
    }

    /// <summary>
    /// Quem baixa o aplicativo pronto tem o executável e os prompts dos agentes, e nada mais. Se
    /// a descoberta continuasse exigindo <c>Input/</c>, essa instalação cairia no diretório
    /// atual — que é de onde o programa foi chamado e não tem relação com onde ele está.
    /// </summary>
    [Fact]
    public void Descobrir_InstalacaoRecemBaixada_UsaAPastaDoExecutavel()
    {
        using var pasta = new PastaTemporaria();
        Directory.CreateDirectory(Path.Combine(pasta.Caminho, "Agents"));
        File.WriteAllText(Path.Combine(pasta.Caminho, "Agents", "Configurador.md"), "# prompt");

        var caminhos = CaminhosDoProjeto.Descobrir(pasta.Caminho);

        Assert.Equal(Path.GetFullPath(pasta.Caminho), caminhos.Raiz);
    }

    /// <summary>
    /// Uma pasta "Agents" vazia não é uma instalação — é uma coincidência de nome. Sem os prompts
    /// não há o que mandar ao Claude Code, então a busca segue subindo até achar quem os tenha.
    /// </summary>
    [Fact]
    public void Descobrir_PastaDeAgentesSemPrompt_ContinuaProcurandoAcima()
    {
        using var pasta = new PastaTemporaria();
        Directory.CreateDirectory(Path.Combine(pasta.Caminho, "Agents"));
        File.WriteAllText(Path.Combine(pasta.Caminho, "Agents", "Configurador.md"), "# prompt");

        var isca = Directory.CreateDirectory(Path.Combine(pasta.Caminho, "sub", "Agents")).Parent!.FullName;

        var caminhos = CaminhosDoProjeto.Descobrir(isca);

        Assert.Equal(Path.GetFullPath(pasta.Caminho), caminhos.Raiz);
    }

    [Fact]
    public void GarantirEstrutura_CriaAsPastasDeDadosEDizQuaisCriou()
    {
        using var pasta = new PastaTemporaria();
        var caminhos = new CaminhosDoProjeto(pasta.Caminho);

        var criadas = caminhos.GarantirEstrutura();

        Assert.True(Directory.Exists(caminhos.Entrada));
        Assert.True(Directory.Exists(caminhos.Modelos));
        Assert.True(Directory.Exists(caminhos.Conhecimento));
        Assert.True(Directory.Exists(caminhos.Personagens));
        Assert.True(Directory.Exists(caminhos.SaidaPersonagens));
        Assert.Equal(5, criadas.Count);

        // Rodar de novo não recria nada: o aviso ao usuário é sobre o que mudou agora.
        Assert.Empty(caminhos.GarantirEstrutura());
    }

    private sealed class PastaTemporaria : IDisposable
    {
        public string Caminho { get; } = Path.Combine(Path.GetTempPath(), "mainforge-caminhos-" + Guid.NewGuid());

        public PastaTemporaria() => Directory.CreateDirectory(Caminho);

        public void Dispose()
        {
            if (Directory.Exists(Caminho))
            {
                Directory.Delete(Caminho, recursive: true);
            }
        }
    }
}
