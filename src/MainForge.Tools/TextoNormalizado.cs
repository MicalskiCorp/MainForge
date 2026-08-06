using System.Globalization;
using System.Text;

namespace MainForge.Tools;

/// <summary>
/// Comparação de texto que ignora acento, usada pelas buscas do aplicativo.
///
/// <para>Existe separada porque as duas buscas — a dos livros e a da base de conhecimento —
/// dependem exatamente da mesma regra: o agente escreve "Maos" e o livro traz "Mãos". Uma busca
/// que falha por causa disso manda ele ler o arquivo inteiro, que é o custo que a busca existe
/// para evitar.</para>
/// </summary>
internal static class TextoNormalizado
{
    public static string SemAcento(string texto)
    {
        var decomposto = texto.Normalize(NormalizationForm.FormD);
        var limpo = new StringBuilder(decomposto.Length);

        foreach (var caractere in decomposto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caractere) != UnicodeCategory.NonSpacingMark)
            {
                limpo.Append(caractere);
            }
        }

        return limpo.ToString().Normalize(NormalizationForm.FormC);
    }
}
