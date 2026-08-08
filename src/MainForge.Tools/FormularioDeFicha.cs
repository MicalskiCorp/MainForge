using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;

namespace MainForge.Tools;

/// <summary>
/// Pega o AcroForm de um PDF, ou <c>null</c> quando ele não tem nenhum.
///
/// <para><b>Por que não basta ler <see cref="PdfDocument.AcroForm"/>.</b> No PDFsharp 6.2 essa
/// propriedade não devolve <c>null</c> num documento sem formulário: ela lança
/// <see cref="InvalidOperationException"/> com "'PdfAcroForm' must not be null here". O
/// <c>?? throw</c> que existia nos dois lugares que a usam nunca rodava, e quem tentasse importar
/// uma ficha digitalizada recebia essa mensagem em inglês, de dentro da biblioteca, no lugar da
/// explicação de que a ficha precisa ser o PDF editável.</para>
///
/// <para>A pergunta é respondida no catálogo do PDF, que é onde a chave <c>/AcroForm</c> mora —
/// sem depender de o PDFsharp mudar de ideia sobre o que fazer quando ela não está lá.</para>
/// </summary>
internal static class FormularioDeFicha
{
    public static PdfAcroForm? Obter(PdfDocument documento) =>
        documento.Internals.Catalog.Elements.ContainsKey("/AcroForm") ? documento.AcroForm : null;
}
