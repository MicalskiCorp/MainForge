using MainBuild.Core;

namespace MainBuild.Tests;

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
}
