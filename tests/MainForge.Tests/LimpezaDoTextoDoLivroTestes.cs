using System.Text;
using MainForge.Tools;

namespace MainForge.Tests;

/// <summary>
/// A limpeza do texto convertido. Ela roda uma vez por livro, de graça, e o resultado é o que o
/// agente relê dezenas de vezes durante o processamento — mas errar para o lado de remover
/// demais apaga regra, que é o pior resultado possível aqui. Por isso metade destes testes
/// verifica o que ela <b>não</b> pode tirar.
/// </summary>
public sealed class LimpezaDoTextoDoLivroTestes
{
    /// <summary>
    /// Um livro como sai da conversão: cabeçalho no alto de cada página, nome do capítulo e
    /// número embaixo, e o conteúdo no meio.
    ///
    /// <para>As linhas de enchimento existem para a página ter miolo. Sem elas a página inteira
    /// seria borda, e a limpeza — corretamente — não mexeria em nada: não dá para distinguir
    /// moldura de conteúdo numa página que só tem moldura.</para>
    /// </summary>
    private static string LivroCom(int paginas, params string[] conteudoDaPagina)
    {
        var texto = new StringBuilder();

        texto.AppendLine("# Livro Base").AppendLine();

        for (var pagina = 1; pagina <= paginas; pagina++)
        {
            texto.AppendLine($"## Página {pagina}").AppendLine();
            texto.AppendLine("Aventura & Cia — Livro Basico");
            texto.AppendLine($"Abertura da pagina {pagina}.");
            texto.AppendLine($"Continuacao da pagina {pagina}.");

            // O conteúdo vai no miolo, que é onde conteúdo fica num livro de verdade. A moldura
            // — cabeçalho e rodapé — é o que ocupa a borda, e é isso que a limpeza distingue.
            foreach (var linha in conteudoDaPagina)
            {
                texto.AppendLine(linha);
            }

            texto.AppendLine($"Fechamento da pagina {pagina}.");
            texto.AppendLine("Capitulo 3: Combate");
            texto.AppendLine($"{pagina}");
            texto.AppendLine();
        }

        return texto.ToString();
    }

    [Fact]
    public void Remove_CabecalhoERodapeQueSeRepetemEmTodaPagina()
    {
        var (limpo, removidas) = LimpezaDoTextoDoLivro.Aplicar(LivroCom(20, "Conteudo da regra."));

        Assert.DoesNotContain("Aventura & Cia — Livro Basico", limpo);
        Assert.DoesNotContain("Capitulo 3: Combate", limpo);
        Assert.True(removidas >= 40, $"removeu só {removidas}");
    }

    [Fact]
    public void Remove_NumeroDePaginaSolto()
    {
        var limpo = LimpezaDoTextoDoLivro.Aplicar(LivroCom(20, "Conteudo da regra.")).Texto;

        Assert.DoesNotContain("\n7\n", limpo);
        Assert.DoesNotContain("\n13\n", limpo);
    }

    /// <summary>
    /// O que a limpeza existe para preservar: a regra no miolo da página. Mesmo repetida em todas
    /// as páginas — o caso mais difícil para uma detecção por frequência —, ela fica.
    /// </summary>
    [Fact]
    public void Preserva_OConteudoDeCadaPagina()
    {
        var limpo = LimpezaDoTextoDoLivro.Aplicar(LivroCom(20, "A carga maxima e Forca vezes 5.")).Texto;

        Assert.Equal(20, limpo.Split("A carga maxima e Forca vezes 5.").Length - 1);
        Assert.Contains("## Página 7", limpo);
    }

    /// <summary>
    /// Livro curto não tem repetição distinguível de coincidência. Detectar cabeçalho ali
    /// removeria conteúdo por acaso.
    /// </summary>
    [Fact]
    public void LivroCurto_NaoTemCabecalhoDetectado()
    {
        var (limpo, removidas) = LimpezaDoTextoDoLivro.Aplicar(LivroCom(3, "Regra."));

        Assert.Contains("Aventura & Cia — Livro Basico", limpo);
        Assert.Equal(0, removidas);
    }

    /// <summary>
    /// A hifenização quebra a busca: quem procura "resistência" nunca acha "resis-" seguido de
    /// "tência" na linha de baixo.
    /// </summary>
    [Fact]
    public void Rejunta_PalavraPartidaNoFimDaLinha()
    {
        var texto = "O personagem ganha resis-\ntencia a fogo neste nivel.\n";

        var limpo = LimpezaDoTextoDoLivro.Aplicar(texto).Texto;

        Assert.Contains("resistencia", limpo);
        Assert.DoesNotContain("resis-", limpo);
    }

    /// <summary>
    /// Hífen entre duas palavras inteiras é composição, não quebra: "meio-elfo" não vira
    /// "meioelfo", e a linha seguinte começando com maiúscula é outra frase.
    /// </summary>
    [Fact]
    public void NaoRejunta_HifenQueNaoEQuebraDePalavra()
    {
        var texto = "O meio-elfo tem visao no escuro.\nEle enxerga a 18 metros.\n";

        Assert.Contains("meio-elfo", LimpezaDoTextoDoLivro.Aplicar(texto).Texto);
    }

    /// <summary>
    /// Dentro de um quadro ou tabela, repetição e alinhamento são conteúdo. Uma tabela de armas
    /// com "1d6" em várias linhas não pode perder linhas por isso.
    /// </summary>
    [Fact]
    public void NaoMexe_DentroDeBlocoDeCodigo()
    {
        var texto = new StringBuilder();
        texto.AppendLine("# Livro").AppendLine();

        for (var pagina = 1; pagina <= 20; pagina++)
        {
            texto.AppendLine($"## Página {pagina}").AppendLine();
            texto.AppendLine("```");
            texto.AppendLine("Adaga      1d4");
            texto.AppendLine("```");
            texto.AppendLine();
        }

        var limpo = LimpezaDoTextoDoLivro.Aplicar(texto.ToString()).Texto;

        Assert.Equal(20, limpo.Split("Adaga      1d4").Length - 1);
    }

    [Fact]
    public void Colapsa_LinhasEmBrancoSeguidas()
    {
        var limpo = LimpezaDoTextoDoLivro.Aplicar("Uma regra.\n\n\n\n\nOutra regra.\n").Texto;

        Assert.Equal("Uma regra.\n\nOutra regra.\n", limpo);
    }

    /// <summary>Título nunca sai, mesmo repetido: é ele que dá a seção às buscas.</summary>
    [Fact]
    public void NuncaRemove_Titulo()
    {
        var texto = new StringBuilder();

        for (var pagina = 1; pagina <= 20; pagina++)
        {
            texto.AppendLine($"## Página {pagina}").AppendLine();
            texto.AppendLine("### Armas");
            texto.AppendLine("Conteudo.").AppendLine();
        }

        var limpo = LimpezaDoTextoDoLivro.Aplicar(texto.ToString()).Texto;

        Assert.Equal(20, limpo.Split("### Armas").Length - 1);
    }

    /// <summary>
    /// Rodar a limpeza de novo sobre o texto já limpo não pode mudar mais nada — senão cada
    /// reconversão iria comendo o livro aos poucos.
    /// </summary>
    [Fact]
    public void EIdempotente()
    {
        var umaVez = LimpezaDoTextoDoLivro.Aplicar(LivroCom(20, "A regra.")).Texto;
        var duasVezes = LimpezaDoTextoDoLivro.Aplicar(umaVez);

        Assert.Equal(umaVez, duasVezes.Texto);
        Assert.Equal(0, duasVezes.LinhasRemovidas);
    }
}
