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
        var formulario = documento.AcroForm
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

        Directory.CreateDirectory(caminhos.SaidaPersonagens);
        documento.Save(caminhoSaida);

        return Path.GetRelativePath(caminhos.Raiz, caminhoSaida);
    }

    private static string ResolverModelo(CaminhosDoProjeto caminhos, string sistema, string? arquivoModelo)
    {
        var diretorioModelo = CaminhosDoProjeto.ResolverDentroDe(caminhos.Modelos, sistema);
        return LocalizadorDeFichaModelo.Resolver(diretorioModelo, arquivoModelo);
    }

    private static void PreencherCampo(PdfAcroField campo, string valor)
    {
        switch (campo)
        {
            case PdfTextField texto:
                texto.Text = valor;
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
