using PdfSharp.Fonts;

namespace MainForge.Tools;

/// <summary>
/// Resolvedor de fontes mínimo para o PdfSharp 6, que não registra nenhum por padrão — sem
/// isto, até só ler o valor de um campo de texto de AcroForm lança "No appropriate font
/// found", porque o PdfSharp recalcula a aparência do campo usando a fonte declarada no PDF.
/// Mapeia as três famílias base do PDF (Helvetica, Times, Courier) para as fontes TrueType
/// equivalentes já instaladas em qualquer Windows, em vez de embutir binários de fonte no
/// projeto (o que traria questão de licenciamento).
/// </summary>
internal sealed class ResolvedorDeFontesDoWindows : IFontResolver
{
    private static readonly string PastaDeFontes = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    public byte[] GetFont(string faceName) => File.ReadAllBytes(Path.Combine(PastaDeFontes, faceName));

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var familia = familyName.ToLowerInvariant() switch
        {
            var f when f.Contains("courier") => "cour",
            var f when f.Contains("times") => "times",
            _ => "arial",
        };

        return new FontResolverInfo(Variante(familia, isBold, isItalic));
    }

    /// <summary>
    /// O arquivo da variante pedida, com o regular como reserva.
    ///
    /// <para><b>Por que a variante importa.</b> Enquanto o resolvedor só devolvia o regular, negrito
    /// e itálico saíam idênticos ao texto comum — invisível numa ficha em AcroForm, que quase não
    /// usa nenhum dos dois, e evidente na folha de magias, em que o negrito é o que separa o nome
    /// de uma magia da descrição da anterior.</para>
    ///
    /// <para>A reserva existe porque o nome do arquivo é convenção do Windows, não garantia: uma
    /// instalação sem <c>arialbd.ttf</c> deve render texto sem negrito, e não uma exceção de
    /// arquivo não encontrado no meio da geração do PDF.</para>
    /// </summary>
    private static string Variante(string familia, bool isBold, bool isItalic)
    {
        var sufixo = (isBold, isItalic) switch
        {
            (true, true) => "bi",
            (true, false) => "bd",
            (false, true) => "i",
            _ => "",
        };

        var candidato = $"{familia}{sufixo}.ttf";

        return sufixo.Length > 0 && !File.Exists(Path.Combine(PastaDeFontes, candidato))
            ? $"{familia}.ttf"
            : candidato;
    }
}
