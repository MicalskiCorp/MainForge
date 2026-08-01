using System.Text;
using RpgForge.Claude;

namespace RpgForge.Tests;

public sealed class ArmazenamentoDeChaveApiTestes : IDisposable
{
    private readonly string _diretorioTemporario =
        Path.Combine(Path.GetTempPath(), "RpgForgeTestes", Guid.NewGuid().ToString("N"));

    private ArmazenamentoDeChaveApi CriarArmazenamento() =>
        new(Path.Combine(_diretorioTemporario, "chave-api.dat"));

    public void Dispose()
    {
        if (Directory.Exists(_diretorioTemporario))
        {
            Directory.Delete(_diretorioTemporario, recursive: true);
        }
    }

    [Fact]
    public void Ler_SemChaveGuardada_DevolveNulo()
    {
        var armazenamento = CriarArmazenamento();

        Assert.False(armazenamento.Existe);
        Assert.Null(armazenamento.Ler());
    }

    [Fact]
    public void SalvarELer_FazRoundTrip()
    {
        var armazenamento = CriarArmazenamento();

        armazenamento.Salvar("sk-ant-chave-de-teste-123");

        Assert.True(armazenamento.Existe);
        Assert.Equal("sk-ant-chave-de-teste-123", armazenamento.Ler());
    }

    [Fact]
    public void Salvar_NaoDeixaAChaveEmTextoPuroNoDisco()
    {
        var armazenamento = CriarArmazenamento();
        const string chave = "sk-ant-chave-de-teste-123";

        armazenamento.Salvar(chave);

        var bytes = File.ReadAllBytes(armazenamento.CaminhoDoArquivo);
        Assert.DoesNotContain(chave, Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        Assert.DoesNotContain(chave, Encoding.Unicode.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void Ler_ArquivoCorrompido_DevolveNuloEmVezDeExplodir()
    {
        var armazenamento = CriarArmazenamento();
        armazenamento.Salvar("sk-ant-chave-de-teste-123");

        File.WriteAllBytes(armazenamento.CaminhoDoArquivo, [1, 2, 3, 4, 5]);

        Assert.Null(armazenamento.Ler());
    }

    [Fact]
    public void Apagar_RemoveOArquivo()
    {
        var armazenamento = CriarArmazenamento();
        armazenamento.Salvar("sk-ant-chave-de-teste-123");

        armazenamento.Apagar();

        Assert.False(armazenamento.Existe);
        Assert.Null(armazenamento.Ler());
    }

    [Fact]
    public void Salvar_ChaveVazia_Rejeita()
    {
        var armazenamento = CriarArmazenamento();

        Assert.Throws<ArgumentException>(() => armazenamento.Salvar("   "));
    }

    /// <summary>
    /// Os três cenários de <see cref="OpcoesClienteClaude.Resolver"/> num único teste de
    /// propósito: ele mexe na variável de ambiente do processo, que é estado global — separar
    /// em três testes deixaria o xUnit rodá-los em paralelo, um pisando no outro.
    /// </summary>
    [Fact]
    public void Resolver_PrefereVariavelDeAmbiente_DepoisArquivo_SenaoNenhuma()
    {
        var armazenamento = CriarArmazenamento();
        var original = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);

            var (origemSemNada, opcoesSemNada) = OpcoesClienteClaude.Resolver(armazenamento);
            Assert.Equal(OrigemDaChaveApi.Nenhuma, origemSemNada);
            Assert.Null(opcoesSemNada);

            armazenamento.Salvar("sk-ant-do-arquivo");

            var (origemDoArquivo, opcoesDoArquivo) = OpcoesClienteClaude.Resolver(armazenamento);
            Assert.Equal(OrigemDaChaveApi.ArquivoProtegido, origemDoArquivo);
            Assert.Equal("sk-ant-do-arquivo", opcoesDoArquivo!.ChaveApi);

            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-ant-do-ambiente");

            var (origemDoAmbiente, opcoesDoAmbiente) = OpcoesClienteClaude.Resolver(armazenamento);
            Assert.Equal(OrigemDaChaveApi.VariavelDeAmbiente, origemDoAmbiente);
            Assert.Equal("sk-ant-do-ambiente", opcoesDoAmbiente!.ChaveApi);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", original);
        }
    }
}
