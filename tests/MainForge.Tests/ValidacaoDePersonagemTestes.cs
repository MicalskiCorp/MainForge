using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// A validação existe para pegar por máquina o que ninguém confere de olho numa ficha de trinta
/// campos — e para <b>não</b> pegar o que depende de julgamento, que é o erro que a tornaria
/// inútil. Estes testes prendem as duas metades: o que ela acusa e o que ela deixa passar.
/// </summary>
public class ValidacaoDePersonagemTestes
{
    private static RegrasDaFicha Regras(params RegraDeCampo[] campos) =>
        new() { Sistema = "Aventura&Cia", Campos = [.. campos] };

    private static ResultadoDaValidacao Validar(
        RegrasDaFicha regras,
        params (string Campo, string Valor)[] campos) =>
        ValidacaoDePersonagem.Validar(
            regras,
            campos.ToDictionary(par => par.Campo, par => par.Valor, StringComparer.Ordinal));

    /// <summary>
    /// Sem regras não há validação — e "não conferi" precisa ser distinguível de "está tudo certo",
    /// ou a interface anunciaria aprovação para um sistema que nunca foi conferido.
    /// </summary>
    [Fact]
    public void Validar_SistemaSemRegras_NaoAprovaNemReprova()
    {
        var resultado = ValidacaoDePersonagem.Validar(null, new Dictionary<string, string> { ["Forca"] = "99" });

        Assert.True(resultado.SemRegras);
        Assert.Empty(resultado.Violacoes);
        Assert.Contains("nada foi conferido", resultado.Resumir("Aventura&Cia"));
    }

    [Fact]
    public void Validar_NumeroDentroDaFaixa_NaoAcusaNada()
    {
        var regras = Regras(new RegraDeCampo
        {
            Campo = "Forca",
            Rotulo = "FORÇA",
            Tipo = TipoDoCampo.Inteiro,
            Minimo = 3,
            Maximo = 20,
        });

        Assert.True(Validar(regras, ("Forca", "16")).Aprovado);
    }

    [Fact]
    public void Validar_NumeroAcimaDoMaximo_AcusaEDizQualEOLimite()
    {
        var regras = Regras(new RegraDeCampo
        {
            Campo = "Forca",
            Rotulo = "FORÇA",
            Tipo = TipoDoCampo.Inteiro,
            Maximo = 20,
            Observacao = "atributo não passa de 20 sem item mágico",
        });

        var violacao = Assert.Single(Validar(regras, ("Forca", "22")).Violacoes);

        Assert.Equal(TipoDaViolacao.ForaDaFaixa, violacao.Tipo);
        Assert.Contains("20", violacao.Detalhe);

        // A observação vai junto: sem ela, o usuário vê uma reprovação sem fonte.
        Assert.Contains("item mágico", violacao.Detalhe);
        Assert.Contains("FORÇA", violacao.Descrever());
    }

    /// <summary>
    /// Metade dos números de uma ficha é modificador, e o <c>Ficha-Mapeamento.md</c> manda
    /// escrevê-los com sinal. Recusar "+2" reprovaria a ficha certa.
    /// </summary>
    [Fact]
    public void Validar_ModificadorComSinal_EAceitoComoNumero()
    {
        var regras = Regras(new RegraDeCampo
        {
            Campo = "ModForca",
            Tipo = TipoDoCampo.Inteiro,
            Minimo = -5,
            Maximo = 10,
        });

        Assert.True(Validar(regras, ("ModForca", "+3")).Aprovado);
        Assert.True(Validar(regras, ("ModForca", "-1")).Aprovado);
    }

    [Fact]
    public void Validar_TextoOndeSeEsperaNumero_EAcusado()
    {
        var regras = Regras(new RegraDeCampo { Campo = "Nivel", Tipo = TipoDoCampo.Inteiro });

        var violacao = Assert.Single(Validar(regras, ("Nivel", "terceiro")).Violacoes);

        Assert.Equal(TipoDaViolacao.NaoEhNumero, violacao.Tipo);
    }

    [Fact]
    public void Validar_CampoObrigatorioVazio_EAcusadoUmaVezSo()
    {
        var regras = Regras(
            new RegraDeCampo { Campo = "Classe", Rotulo = "Classe", Obrigatorio = true, Tipo = TipoDoCampo.Inteiro },
            new RegraDeCampo { Campo = "Nome", Rotulo = "Nome", Obrigatorio = true });

        var violacoes = Validar(regras, ("Nome", "Thoradin")).Violacoes;

        var violacao = Assert.Single(violacoes);

        // Vazio não tem tipo a conferir: acusar "não é número" além de "está vazio" seria dizer
        // duas vezes a mesma coisa.
        Assert.Equal(TipoDaViolacao.CampoVazio, violacao.Tipo);
        Assert.Equal("Classe", violacao.Campo);
    }

    [Fact]
    public void Validar_CampoOpcionalVazio_NaoEAcusado()
    {
        var regras = Regras(new RegraDeCampo { Campo = "Tesouro", Tipo = TipoDoCampo.Inteiro });

        Assert.True(Validar(regras, ("Tesouro", "")).Aprovado);
    }

    [Fact]
    public void Validar_ValorForaDaListaFechada_EAcusadoComAsOpcoes()
    {
        var regras = Regras(new RegraDeCampo
        {
            Campo = "Classe",
            Rotulo = "Classe",
            Valores = ["Guerreiro", "Mago", "Ladino"],
        });

        var violacao = Assert.Single(Validar(regras, ("Classe", "Artífice")).Violacoes);

        Assert.Equal(TipoDaViolacao.ValorNaoPrevisto, violacao.Tipo);
        Assert.Contains("Guerreiro", violacao.Detalhe);
    }

    /// <summary>
    /// A lista fechada existe para pegar a classe que não existe, não para brigar com a grafia:
    /// acento e caixa são diferença de digitação, e reprovar por elas ensinaria o usuário a
    /// ignorar a validação.
    /// </summary>
    [Fact]
    public void Validar_ValorDaListaComOutroAcentoOuCaixa_EAceito()
    {
        var regras = Regras(new RegraDeCampo { Campo = "Classe", Valores = ["Clérigo"] });

        Assert.True(Validar(regras, ("Classe", "clerigo")).Aprovado);
    }

    [Fact]
    public void Validar_MarcacaoComOsEstadosDoProprioPdf_EAceita()
    {
        var regras = Regras(new RegraDeCampo { Campo = "Inspiracao", Tipo = TipoDoCampo.Marcacao });

        // É o vocabulário que PreenchedorDeFicha aceita na ida; recusá-lo aqui produziria uma
        // violação para um valor que gera a ficha sem problema nenhum.
        Assert.True(Validar(regras, ("Inspiracao", "Yes")).Aprovado);
        Assert.True(Validar(regras, ("Inspiracao", "/Off")).Aprovado);
        Assert.True(Validar(regras, ("Inspiracao", "true")).Aprovado);
    }

    [Fact]
    public void Validar_MarcacaoComTextoLivre_EAcusada()
    {
        var regras = Regras(new RegraDeCampo { Campo = "Inspiracao", Tipo = TipoDoCampo.Marcacao });

        var violacao = Assert.Single(Validar(regras, ("Inspiracao", "às vezes")).Violacoes);

        Assert.Equal(TipoDaViolacao.MarcacaoInvalida, violacao.Tipo);
    }

    /// <summary>
    /// Campo que a ficha do sistema não tem é a explicação de um valor que "some" na hora de
    /// gerar o PDF — a ficha trazida é de outra edição. Precisa aparecer, e não reprovar.
    /// </summary>
    [Fact]
    public void Validar_CampoQueASistemaNaoConhece_EAcusadoComoDesconhecido()
    {
        var regras = Regras(new RegraDeCampo { Campo = "Nome" });

        var violacao = Assert.Single(Validar(regras, ("Nome", "Thoradin"), ("Sanidade", "40")).Violacoes);

        Assert.Equal(TipoDaViolacao.CampoDesconhecido, violacao.Tipo);
        Assert.Equal("Sanidade", violacao.Campo);
    }

    /// <summary>
    /// Num sistema que não é em português, o nome da magia fica em inglês com a tradução ao lado.
    /// A regra vale também para o que o agente escreve na ficha, não só para a base.
    /// </summary>
    [Fact]
    public void Validar_MagiaSemNomeEmIngles_EAcusadaNumSistemaEmIngles()
    {
        var regras = Regras(new RegraDeCampo { Campo = "Magias", Rotulo = "Spells" });
        regras.Idioma = IdiomaDetectado.Ingles;
        regras.UsaMagias = true;
        regras.CamposDeMagia = ["Magias"];

        var violacao = Assert.Single(
            Validar(regras, ("Magias", "Fireball (Bola de Fogo)\nBola de Fogo\nShield (Escudo)")).Violacoes);

        Assert.Equal(TipoDaViolacao.MagiaSemNomeEmIngles, violacao.Tipo);
        Assert.Contains("Bola de Fogo", violacao.Detalhe);
    }

    /// <summary>
    /// A exceção da regra: num sistema em português o nome da magia já é o nome dela, e cobrar um
    /// original em inglês inventaria uma informação que os livros não têm.
    /// </summary>
    [Fact]
    public void Validar_MagiaEmPortugues_NaoEAcusadaNumSistemaEmPortugues()
    {
        var regras = Regras(new RegraDeCampo { Campo = "Magias" });
        regras.Idioma = IdiomaDetectado.Portugues;
        regras.UsaMagias = true;
        regras.CamposDeMagia = ["Magias"];

        Assert.True(Validar(regras, ("Magias", "Bola de Fogo\nEscudo Arcano")).Aprovado);
    }

    /// <summary>
    /// Há campo de AcroForm com espaço sobrando no fim do nome. Errar por causa dele acusaria o
    /// campo como vazio e como desconhecido ao mesmo tempo — duas violações inventadas.
    /// </summary>
    [Fact]
    public void Validar_NomeDeCampoComEspacoSobrando_CasaComOValor()
    {
        var regras = Regras(new RegraDeCampo { Campo = "Race ", Obrigatorio = true });

        Assert.True(Validar(regras, ("Race", "Anão")).Aprovado);
    }

    [Fact]
    public void Relatorio_ComVioloes_TrazOResumoEALista()
    {
        var regras = Regras(new RegraDeCampo { Campo = "Nivel", Rotulo = "Nível", Tipo = TipoDoCampo.Inteiro });

        var relatorio = Validar(regras, ("Nivel", "vinte")).Relatorio("Aventura&Cia");

        Assert.Contains("1 ponto(s) fora das regras", relatorio);
        Assert.Contains("Nível", relatorio);
    }
}
