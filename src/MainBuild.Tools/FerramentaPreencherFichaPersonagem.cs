using Anthropic.Helpers.Beta;
using Anthropic.Models.Beta.Messages;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;
using MainBuild.Core;

namespace MainBuild.Tools;

/// <summary>
/// Preenche os campos de formulário (AcroForm) do template em Templates/&lt;sistema&gt;/ com os
/// dados do personagem e salva o resultado em Output/Personagens/. Ferramenta exclusiva do
/// Agente Dungeon Master, chamada só ao final da criação, após confirmação do usuário.
/// </summary>
public sealed class FerramentaPreencherFichaPersonagem(CaminhosDoProjeto caminhos) : IBetaRunnableTool
{
    static FerramentaPreencherFichaPersonagem()
    {
        GlobalFontSettings.FontResolver ??= new ResolvedorDeFontesDoWindows();
    }

    public string Name => "preencher_ficha_personagem";

    public BetaToolUnion Definition => new BetaTool
    {
        Name = Name,
        Description = "Preenche os campos do template PDF em Templates/<sistema>/ com os dados do personagem e salva em Output/Personagens/.",
        InputSchema = EsquemaFerramenta.CriarAPartirDe("""
            {
              "type": "object",
              "properties": {
                "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Templates/)." },
                "arquivoModelo": { "type": "string", "description": "Nome do PDF de template dentro de Templates/<sistema>/. Só é obrigatório se houver mais de um PDF nessa pasta." },
                "campos": {
                  "type": "object",
                  "description": "Mapa de nome do campo do formulário PDF para o valor (texto) a preencher.",
                  "additionalProperties": { "type": "string" }
                },
                "nomeArquivoSaida": { "type": "string", "description": "Nome do arquivo PDF de saída (sem caminho), ex.: \"Thoradin.pdf\". Salvo em Output/Personagens/." }
              },
              "required": ["sistema", "campos", "nomeArquivoSaida"]
            }
            """),
    };

    public Task<BetaToolResultBlockParamContent> ExecuteAsync(BetaToolUseBlock chamada, CancellationToken cancelamento)
    {
        var sistema = EntradaFerramenta.Obrigatorio(chamada.Input, "sistema");
        var nomeArquivoSaida = EntradaFerramenta.Obrigatorio(chamada.Input, "nomeArquivoSaida");
        var arquivoModelo = EntradaFerramenta.Opcional(chamada.Input, "arquivoModelo");
        var campos = EntradaFerramenta.ObjetoDeTexto(chamada.Input, "campos");

        if (!nomeArquivoSaida.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new BetaToolError($"'{nomeArquivoSaida}' precisa terminar em .pdf.");
        }

        var diretorioModelo = CaminhosDoProjeto.ResolverDentroDe(caminhos.Modelos, sistema);
        var caminhoModelo = ResolverCaminhoDoModelo(diretorioModelo, arquivoModelo);
        var caminhoSaida = CaminhosDoProjeto.ResolverDentroDe(caminhos.SaidaPersonagens, nomeArquivoSaida);

        using var documento = PdfReader.Open(caminhoModelo, PdfDocumentOpenMode.Modify);
        var formulario = documento.AcroForm
            ?? throw new BetaToolError($"O template '{Path.GetFileName(caminhoModelo)}' não tem campos de formulário (AcroForm).");

        var camposNaoEncontrados = new List<string>();

        foreach (var (nomeCampo, valor) in campos)
        {
            var campo = formulario.Fields[nomeCampo];

            if (campo is null)
            {
                camposNaoEncontrados.Add(nomeCampo);
                continue;
            }

            PreencherCampo(campo, valor);
        }

        if (camposNaoEncontrados.Count > 0)
        {
            throw new BetaToolError(
                $"Campo(s) não encontrados no template: {string.Join(", ", camposNaoEncontrados)}. " +
                $"Campos disponíveis: {string.Join(", ", formulario.Fields.Names)}.");
        }

        Directory.CreateDirectory(caminhos.SaidaPersonagens);
        documento.Save(caminhoSaida);

        BetaToolResultBlockParamContent resultado =
            $"Ficha salva em Output/Personagens/{nomeArquivoSaida} ({campos.Count} campo(s) preenchido(s)).";
        return Task.FromResult(resultado);
    }

    private static string ResolverCaminhoDoModelo(string diretorioModelo, string? arquivoModelo)
    {
        if (arquivoModelo is not null)
        {
            var caminho = CaminhosDoProjeto.ResolverDentroDe(diretorioModelo, arquivoModelo);

            if (!File.Exists(caminho))
            {
                throw new BetaToolError($"Template '{arquivoModelo}' não encontrado em '{diretorioModelo}'.");
            }

            return caminho;
        }

        if (!Directory.Exists(diretorioModelo))
        {
            throw new BetaToolError($"Não há templates em '{diretorioModelo}'.");
        }

        var pdfs = Directory.EnumerateFiles(diretorioModelo, "*.pdf", SearchOption.TopDirectoryOnly).ToList();

        return pdfs.Count switch
        {
            0 => throw new BetaToolError($"Nenhum template PDF encontrado em '{diretorioModelo}'."),
            1 => pdfs[0],
            _ => throw new BetaToolError(
                $"Há mais de um template PDF em '{diretorioModelo}' — informe 'arquivoModelo'. " +
                $"Opções: {string.Join(", ", pdfs.Select(Path.GetFileName))}."),
        };
    }

    private static void PreencherCampo(PdfAcroField campo, string valor)
    {
        switch (campo)
        {
            case PdfTextField texto:
                texto.Text = valor;
                break;
            case PdfCheckBoxField caixa:
                caixa.Checked = InterpretarBooleano(caixa.Name, valor);
                break;
            default:
                campo.Value = new PdfString(valor);
                break;
        }
    }

    private static bool InterpretarBooleano(string nomeCampo, string valor) => valor.Trim().ToLowerInvariant() switch
    {
        "true" or "1" or "sim" => true,
        "false" or "0" or "nao" or "não" => false,
        _ => throw new BetaToolError($"Valor '{valor}' inválido para o campo de marcação '{nomeCampo}' (use true/false)."),
    };
}
