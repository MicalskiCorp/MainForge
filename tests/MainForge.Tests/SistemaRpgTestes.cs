using MainForge.Core;

namespace MainForge.Tests;

/// <summary>
/// A descoberta de sistemas é o que a interface mostra ao usuário — e o sistema de teste
/// interno não faz parte disso.
/// </summary>
public sealed class SistemaRpgTestes : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "mainforge-sistemas-" + Guid.NewGuid());
    private readonly CaminhosDoProjeto _caminhos;

    public SistemaRpgTestes() => _caminhos = new CaminhosDoProjeto(_raiz);

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    private void CriarSistema(string id)
    {
        Directory.CreateDirectory(Path.Combine(_caminhos.Sistemas, id));

        var conhecimento = Path.Combine(_caminhos.Conhecimento, id);
        Directory.CreateDirectory(conhecimento);
        File.WriteAllText(Path.Combine(conhecimento, "Regras.md"), "# Regras");
    }

    /// <summary>
    /// O sistema do harness existe em disco como qualquer outro. Se aparecesse na lista, o
    /// usuário teria de adivinhar que aquilo não é para ele.
    /// </summary>
    [Fact]
    public void Descobrir_NaoMostraOSistemaDeTesteInterno()
    {
        CriarSistema("Aventura&Cia");
        CriarSistema(SistemaRpg.IdDoSistemaDeTeste);

        Assert.Equal(["Aventura&Cia"], SistemaRpg.DescobrirImportados(_caminhos).Select(sistema => sistema.Id));
        Assert.Equal(["Aventura&Cia"], SistemaRpg.DescobrirProntos(_caminhos).Select(sistema => sistema.Id));
    }

    /// <summary>
    /// Esconder é da interface, não do disco: o harness alcança o sistema pelo nome, e continuar
    /// funcionando é o que justifica ele existir.
    /// </summary>
    [Fact]
    public void SistemaDeTesteInterno_ContinuaAcessivelPeloNome()
    {
        CriarSistema(SistemaRpg.IdDoSistemaDeTeste);

        var sistema = new SistemaRpg(SistemaRpg.IdDoSistemaDeTeste);

        Assert.True(sistema.EhDeTesteInterno);
        Assert.True(sistema.TemConhecimento(_caminhos));
    }

    [Fact]
    public void Descobrir_SistemaSemNenhumMarkdown_NaoContaComoPronto()
    {
        Directory.CreateDirectory(Path.Combine(_caminhos.Sistemas, "Tormenta20"));
        Directory.CreateDirectory(Path.Combine(_caminhos.Conhecimento, "Tormenta20"));

        Assert.Empty(SistemaRpg.DescobrirProntos(_caminhos));
        Assert.Single(SistemaRpg.DescobrirImportados(_caminhos));
    }
}
