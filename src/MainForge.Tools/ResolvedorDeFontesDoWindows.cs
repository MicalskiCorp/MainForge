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
        var arquivo = familyName.ToLowerInvariant() switch
        {
            var f when f.Contains("courier") => "cour.ttf",
            var f when f.Contains("times") => "times.ttf",
            _ => "arial.ttf",
        };

        return new FontResolverInfo(arquivo);
    }
}
