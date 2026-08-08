using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;

namespace MainForge.Tools;

/// <summary>
/// O que veio dentro de uma ficha de personagem já preenchida.
/// </summary>
/// <param name="Arquivo">O PDF de origem, como o usuário o informou.</param>
/// <param name="Valores">Campo do formulário -> valor, só dos que têm conteúdo.</param>
/// <param name="CamposEmBranco">Os campos que existem na ficha e estão vazios.</param>
public sealed record FichaPreenchida(
    string Arquivo,
    IReadOnlyDictionary<string, string> Valores,
    IReadOnlyList<string> CamposEmBranco)
{
    public int TotalDeCampos => Valores.Count + CamposEmBranco.Count;
}

/// <summary>
/// Lê os valores de uma ficha de personagem preenchida em PDF (AcroForm).
///
/// <para><b>Por que existe.</b> Quem já joga tem personagem — numa ficha preenchida à mão no
/// PDF editável, muitas vezes há anos. Sem uma porta de entrada, usar o MainForge com esse
/// personagem significava recriá-lo do zero numa conversa, pagando cota para o agente
/// redescobrir escolhas que já estavam decididas e escritas.</para>
///
/// <para><b>É o inverso exato de <see cref="PreenchedorDeFicha"/>.</b> Lê pelo mesmo caminho que
/// ele usa para escrever (<c>AcroForm.Fields</c> pelo nome), e desfaz as mesmas normalizações —
/// separador de linha do PDF de volta para <c>\n</c>, caixa marcada de volta para
/// <c>true</c>. Isso é o que garante o valor lido poder ser reescrito: um personagem importado
/// precisa gerar ficha de novo depois de subir de nível, e um round-trip que perde o formato
/// entrega um PDF pior que o original.</para>
///
/// <para><b>O caminho vem de fora da raiz do projeto</b>, e é assim de propósito: o PDF está no
/// computador do usuário, como os livros que ele importa. Quem chama é a interface, com um
/// caminho digitado por uma pessoa — nunca o agente, que não tem esta ferramenta.</para>
/// </summary>
public static class LeitorDeFichaPreenchida
{
    static LeitorDeFichaPreenchida()
    {
        GlobalFontSettings.FontResolver ??= new ResolvedorDeFontesDoWindows();
    }

    /// <summary>
    /// Abre o PDF e devolve o que está escrito nos campos dele.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Quando o arquivo não abre como PDF, não tem AcroForm (ficha digitalizada ou achatada) ou
    /// tem o formulário vazio. São três situações diferentes com a mesma consequência para o
    /// usuário, e a mensagem diz qual delas aconteceu — "não deu certo" mandaria ele tentar o
    /// mesmo arquivo de novo.
    /// </exception>
    public static FichaPreenchida Ler(string caminhoDoPdf)
    {
        if (!File.Exists(caminhoDoPdf))
        {
            throw new FileNotFoundException($"Ficha não encontrada: '{caminhoDoPdf}'.", caminhoDoPdf);
        }

        PdfDocument documento;

        try
        {
            documento = PdfReader.Open(caminhoDoPdf, PdfDocumentOpenMode.Import);
        }
        catch (Exception excecao)
        {
            throw new InvalidOperationException(
                $"Não consegui abrir '{Path.GetFileName(caminhoDoPdf)}' como PDF: {excecao.Message}", excecao);
        }

        using (documento)
        {
            var formulario = FormularioDeFicha.Obter(documento)
                ?? throw new InvalidOperationException(
                    $"'{Path.GetFileName(caminhoDoPdf)}' não tem campos de formulário (AcroForm). " +
                    "A importação lê os campos preenchíveis da ficha — um PDF digitalizado, impresso " +
                    "para arquivo ou com o formulário achatado não tem de onde tirar os valores.");

            var valores = new Dictionary<string, string>(StringComparer.Ordinal);
            var emBranco = new List<string>();

            foreach (var nome in formulario.Fields.Names)
            {
                var campo = formulario.Fields[nome];

                if (campo is null)
                {
                    continue;
                }

                // Ficha de RPG real vem de editor de PDF de todo tipo, e um campo montado de um
                // jeito que o PdfSharp não espera derrubaria a importação inteira por causa de um
                // valor. Ele entra como vazio: o usuário vê a contagem e redigita aquele campo na
                // conversa, em vez de perder a ficha toda.
                string valor;

                try
                {
                    valor = LerValor(campo);
                }
                catch (Exception excecao) when (excecao is not OutOfMemoryException)
                {
                    valor = "";
                }

                if (valor.Length == 0)
                {
                    emBranco.Add(nome);
                }
                else
                {
                    valores[nome] = valor;
                }
            }

            if (valores.Count + emBranco.Count == 0)
            {
                throw new InvalidOperationException(
                    $"'{Path.GetFileName(caminhoDoPdf)}' tem um AcroForm, mas nenhum campo dentro dele.");
            }

            return new FichaPreenchida(caminhoDoPdf, valores, emBranco);
        }
    }

    private static string LerValor(PdfAcroField campo) => campo switch
    {
        PdfTextField texto => Normalizar(texto.Text),
        PdfCheckBoxField caixa => caixa.Checked ? "true" : "",
        _ => Normalizar(DoItem(campo.Value)),
    };

    /// <summary>
    /// O valor de um campo que não é texto nem caixa de marcação — botão de opção, lista,
    /// combo. Pode chegar como string ou como nome do PDF (<c>/Guerreiro</c>), e é o nome que
    /// <see cref="PreenchedorDeFicha"/> aceita de volta sem a barra.
    /// </summary>
    private static string DoItem(PdfItem? item) => item switch
    {
        null => "",
        PdfString texto => texto.Value,
        PdfName nome => nome.Value.TrimStart('/'),
        _ => item.ToString() ?? "",
    };

    /// <summary>
    /// Desfaz o que o formato do PDF impôs ao valor: o separador de linha de AcroForm (<c>\r</c>
    /// sozinho, que é o que o Acrobat grava) volta a ser <c>\n</c>, e o estado "desmarcado"
    /// (<c>Off</c>) vira campo vazio — que é o que ele significa para quem lê a ficha.
    /// </summary>
    private static string Normalizar(string? valor)
    {
        var texto = (valor ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();

        return texto.Equals("Off", StringComparison.OrdinalIgnoreCase) ? "" : texto;
    }
}
