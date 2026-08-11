using MainForge.Core;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;

namespace MainForge.Tools;

/// <summary>
/// Lê e preenche os campos de formulário (AcroForm) das fichas em Templates/. É a única
/// parte do produto que o Claude Code não consegue fazer com as ferramentas embutidas dele —
/// por isso continua em C# e é exposta ao agente por um servidor MCP.
///
/// Todo caminho recebido do modelo passa por <see cref="CaminhosDoProjeto.ResolverDentroDe"/>:
/// o confinamento a Templates/ e Output/ é aplicado aqui, em código, não confiando em
/// configuração de permissão externa.
/// </summary>
public static class PreenchedorDeFicha
{
    static PreenchedorDeFicha()
    {
        GlobalFontSettings.FontResolver ??= new ResolvedorDeFontesDoWindows();
    }

    /// <summary>
    /// Devolve os nomes exatos dos campos preenchíveis da ficha de um sistema — que é o que
    /// <see cref="Preencher"/> vai exigir depois. O Configurador usa isto para montar o
    /// Ficha-Mapeamento.md.
    /// </summary>
    public static IReadOnlyList<string> ListarCampos(
        CaminhosDoProjeto caminhos,
        string sistema,
        string? arquivoModelo)
    {
        var caminhoModelo = ResolverModelo(caminhos, sistema, arquivoModelo);

        try
        {
            return ImportadorDeSistema.LerCamposDaFicha(caminhoModelo);
        }
        catch (InvalidOperationException excecao)
        {
            throw new ErroDeFerramenta(excecao.Message, excecao);
        }
    }

    /// <summary>
    /// Os mesmos campos de <see cref="ListarCampos"/>, mas <b>na ordem em que estão impressos</b>
    /// e cada um com o rótulo que aparece ao lado dele na ficha.
    ///
    /// <para>É o que o Configurador precisa para escrever o Ficha-Mapeamento.md sem adivinhar:
    /// só o nome do campo não diz em que linha ele está, e numa ficha traduzida os dois divergem.
    /// Ver <see cref="LayoutDaFicha"/>.</para>
    /// </summary>
    public static IReadOnlyList<CampoDaFicha> ListarLayoutDaFicha(
        CaminhosDoProjeto caminhos,
        string sistema,
        string? arquivoModelo)
    {
        var caminhoModelo = ResolverModelo(caminhos, sistema, arquivoModelo);

        try
        {
            return LayoutDaFicha.Ler(caminhoModelo);
        }
        catch (InvalidOperationException excecao)
        {
            throw new ErroDeFerramenta(excecao.Message, excecao);
        }
    }

    /// <summary>
    /// Preenche o template do sistema com <paramref name="campos"/> e grava o resultado em
    /// Output/Personagens/. Devolve o caminho do arquivo gerado, relativo à raiz do projeto.
    /// </summary>
    public static string Preencher(
        CaminhosDoProjeto caminhos,
        string sistema,
        string? arquivoModelo,
        IReadOnlyDictionary<string, string> campos,
        string nomeArquivoSaida)
    {
        if (!nomeArquivoSaida.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new ErroDeFerramenta($"'{nomeArquivoSaida}' precisa terminar em .pdf.");
        }

        var caminhoModelo = ResolverModelo(caminhos, sistema, arquivoModelo);
        var caminhoSaida = CaminhosDoProjeto.ResolverDentroDe(caminhos.SaidaPersonagens, nomeArquivoSaida);

        using var documento = PdfReader.Open(caminhoModelo, PdfDocumentOpenMode.Modify);
        var formulario = FormularioDeFicha.Obter(documento)
            ?? throw new ErroDeFerramenta(
                $"O template '{Path.GetFileName(caminhoModelo)}' não tem campos de formulário (AcroForm).");

        var naoEncontrados = new List<string>();

        foreach (var (nomeCampo, valor) in campos)
        {
            var campo = formulario.Fields[nomeCampo];

            if (campo is null)
            {
                naoEncontrados.Add(nomeCampo);
                continue;
            }

            PreencherCampo(campo, valor);
        }

        if (naoEncontrados.Count > 0)
        {
            throw new ErroDeFerramenta(
                $"Campo(s) não encontrados no template: {string.Join(", ", naoEncontrados)}. " +
                $"Campos disponíveis: {string.Join(", ", formulario.Fields.Names)}.");
        }

        ExigirRedesenhoDosCampos(formulario);

        Directory.CreateDirectory(caminhos.SaidaPersonagens);
        documento.Save(caminhoSaida);

        return Path.GetRelativePath(caminhos.Raiz, caminhoSaida);
    }

    /// <summary>
    /// Quais campos da ficha do sistema são caixas de marcação.
    ///
    /// <para><b>Por que a pergunta existe.</b> Metade de uma ficha de RPG é caixa de marcação, e
    /// o valor guardado nelas (<c>true</c>, <c>Yes</c>, o nome do estado no PDF) não é o que se
    /// escreve num desenho da ficha — ali cabe um <c>X</c> ou um espaço, e nada mais. Sem saber o
    /// tipo do campo, quem desenha a ficha em texto teria de adivinhar pelo valor, e um campo de
    /// texto com "Sim" escrito viraria uma caixa marcada.</para>
    ///
    /// <para>Devolve vazio quando o sistema não tem ficha em branco: quem chama continua
    /// desenhando, só sem distinguir marcação de texto.</para>
    /// </summary>
    public static IReadOnlySet<string> ListarCamposDeMarcacao(
        CaminhosDoProjeto caminhos,
        string sistema,
        string? arquivoModelo = null)
    {
        var marcacoes = new HashSet<string>(StringComparer.Ordinal);

        string caminhoModelo;

        try
        {
            caminhoModelo = ResolverModelo(caminhos, sistema, arquivoModelo);
        }
        catch (Exception excecao) when (excecao is ErroDeFerramenta or UnauthorizedAccessException)
        {
            return marcacoes;
        }

        try
        {
            using var documento = PdfReader.Open(caminhoModelo, PdfDocumentOpenMode.Import);
            var formulario = FormularioDeFicha.Obter(documento);

            if (formulario is null)
            {
                return marcacoes;
            }

            foreach (var nome in formulario.Fields.Names)
            {
                if (formulario.Fields[nome] is PdfCheckBoxField)
                {
                    marcacoes.Add(nome);
                }
            }
        }
        catch (Exception excecao) when (excecao is not OutOfMemoryException)
        {
            // Template ilegível não impede desenhar a ficha em texto — só faz as caixas de
            // marcação saírem com o valor cru. Quem cuida de template quebrado é a importação
            // do sistema, e derrubar a visualização por causa dela seria trocar um problema
            // pequeno por um maior.
        }

        return marcacoes;
    }

    private static string ResolverModelo(CaminhosDoProjeto caminhos, string sistema, string? arquivoModelo)
    {
        var diretorioModelo = CaminhosDoProjeto.ResolverDentroDe(caminhos.Modelos, sistema);
        return LocalizadorDeFichaModelo.Resolver(diretorioModelo, arquivoModelo);
    }

    /// <summary>
    /// Manda o leitor de PDF redesenhar os campos a partir do valor e da aparência declarada no
    /// próprio formulário (<c>/DA</c>), em vez de exibir o desenho que o PdfSharp gerou.
    ///
    /// <para><b>Por que é preciso.</b> Ao gravar um campo, o PdfSharp monta um fluxo de aparência
    /// (<c>/AP</c>) com o texto inteiro num único operador <c>Tj</c> — inclusive as quebras de
    /// linha, que dentro de uma string de PDF não quebram nada. Num campo de várias linhas
    /// (traços de personalidade, ideais, história) o resultado é tudo espremido numa linha só,
    /// cortada na borda do quadro; e a cor e o corpo de letra pedidos pelo <c>/DA</c> do template
    /// se perdem, porque o desenho gerado usa preto num tamanho fixo. Era isso que fazia o campo
    /// aparecer errado até alguém clicar nele e editá-lo: o clique faz o leitor refazer o desenho,
    /// que é justamente o que esta marca pede que ele faça na abertura.</para>
    ///
    /// <para>O <c>/AP</c> do PdfSharp continua no arquivo de propósito — é o que um visualizador
    /// que ignore a marca vai mostrar, e um texto espremido ainda é melhor que campo vazio.</para>
    /// </summary>
    private static void ExigirRedesenhoDosCampos(PdfAcroForm formulario) =>
        formulario.Elements.SetBoolean("/NeedAppearances", true);

    private static void PreencherCampo(PdfAcroField campo, string valor)
    {
        switch (campo)
        {
            case PdfTextField texto:
                texto.Text = NormalizarQuebrasDeLinha(texto, valor);
                break;
            case PdfCheckBoxField caixa:
                caixa.Checked = InterpretarMarcacao(caixa, valor);
                break;
            default:
                campo.Value = new PdfString(valor);
                break;
        }
    }

    /// <summary>
    /// Ajusta as quebras de linha ao tipo do campo.
    ///
    /// <para>Num campo de várias linhas elas viram <c>\r</c> sozinho, que é a forma canônica de
    /// separador de linha num valor de AcroForm — a que o próprio Acrobat grava e a que todo
    /// leitor entende ao redesenhar o campo. Num campo de uma linha só, quebra de linha não tem
    /// como ser exibida: virar espaço deixa o texto legível, enquanto deixá-la passar renderiza um
    /// caractere de controle ou corta o resto do valor.</para>
    /// </summary>
    private static string NormalizarQuebrasDeLinha(PdfTextField campo, string valor)
    {
        var linhas = valor.ReplaceLineEndings("\n");

        return campo.MultiLine ? linhas.Replace("\n", "\r") : linhas.Replace('\n', ' ');
    }

    /// <summary>
    /// Decide se uma caixa de marcação fica marcada.
    ///
    /// <para>Além de true/false, aceita os nomes dos estados do próprio campo no PDF —
    /// tipicamente <c>Yes</c> e <c>Off</c>. É o vocabulário que aparece para quem inspeciona o
    /// AcroForm de fora, então é o que o agente escreve no Ficha-Mapeamento.md do sistema e o
    /// que ele manda de volta na hora de preencher; recusar isso reprovava a ficha inteira por
    /// uma diferença de grafia.</para>
    /// </summary>
    private static bool InterpretarMarcacao(PdfCheckBoxField caixa, string valor)
    {
        var texto = valor.Trim().TrimStart('/');

        // Campo mandado vazio é campo desmarcado: é assim que a ficha em branco já está, e é o
        // que o agente quer dizer com "deixe em branco".
        if (texto.Length == 0)
        {
            return false;
        }

        if (EstadoDoCampo(caixa.CheckedName, texto))
        {
            return true;
        }

        if (EstadoDoCampo(caixa.UncheckedName, texto))
        {
            return false;
        }

        return texto.ToLowerInvariant() switch
        {
            "true" or "1" or "sim" or "yes" or "on" or "x" or "marcado" => true,
            "false" or "0" or "nao" or "não" or "no" or "off" or "desmarcado" => false,
            _ => throw new ErroDeFerramenta(
                $"Valor '{valor}' inválido para o campo de marcação '{caixa.Name}'. " +
                $"Use true/false, vazio para desmarcar, ou os estados deste campo no PDF: " +
                $"'{SemBarra(caixa.CheckedName)}' e '{SemBarra(caixa.UncheckedName)}'."),
        };
    }

    private static bool EstadoDoCampo(string? estado, string texto) =>
        SemBarra(estado) is { Length: > 0 } nome && nome.Equals(texto, StringComparison.OrdinalIgnoreCase);

    private static string SemBarra(string? nomeDeEstado) => (nomeDeEstado ?? "").TrimStart('/');
}
