using System.Text.Json.Nodes;
using MainForge.Core;
using MainForge.Tools;

namespace MainForge.Mcp;

/// <summary>O que uma ferramenta devolveu: um texto para o modelo e se foi erro.</summary>
public sealed record ResultadoDaFerramenta(string Texto, bool Erro = false);

/// <summary>
/// As ferramentas que o Claude Code <em>não</em> consegue fazer sozinho, e por isso continuam
/// em C#:
///
/// <list type="bullet">
///   <item>manipulação de AcroForm (ler e preencher os campos da ficha) — o Claude Code não
///   faz PDF editável;</item>
///   <item>escrita em Knowledge/ — poderia ser a ferramenta <c>Write</c> embutida, mas passar
///   por aqui permite impor o confinamento de diretório em código, que é o guardrail que a
///   arquitetura do produto promete.</item>
/// </list>
///
/// O que ficou de fora é tão importante quanto: <c>Read</c> e <c>Glob</c> embutidos do Claude
/// Code substituem as antigas ferramentas de leitura e listagem. Ler PDF <em>tem</em> que ser
/// pelo <c>Read</c> nativo: uma ferramenta MCP que devolvesse o PDF só faria o Claude Code
/// gravar o binário em disco e passar o caminho ao modelo, que não conseguiria lê-lo.
/// </summary>
public sealed class CatalogoDeFerramentas(CaminhosDoProjeto caminhos)
{
    public JsonArray Descrever()
    {
        return
        [
            Ferramenta(
                "escrever_arquivo_conhecimento",
                "Cria ou sobrescreve um arquivo Markdown dentro de Knowledge/<sistema>/ com o conteúdo informado. Use sempre esta ferramenta para gravar a base de conhecimento.",
                """
                {
                  "type": "object",
                  "properties": {
                    "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Knowledge/)." },
                    "caminho": { "type": "string", "description": "Caminho do arquivo .md relativo a Knowledge/<sistema>/, ex.: \"Classes/Guerreiro.md\"." },
                    "conteudo": { "type": "string", "description": "Conteúdo Markdown completo a gravar no arquivo." }
                  },
                  "required": ["sistema", "caminho", "conteudo"]
                }
                """),

            Ferramenta(
                "listar_campos_da_ficha",
                "Lista os nomes exatos dos campos preenchíveis (AcroForm) da ficha em Templates/<sistema>/, exatamente como devem ser informados a preencher_ficha_personagem.",
                """
                {
                  "type": "object",
                  "properties": {
                    "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Templates/)." },
                    "arquivoModelo": { "type": "string", "description": "Nome do PDF dentro de Templates/<sistema>/. Só é obrigatório se houver mais de um PDF nessa pasta." }
                  },
                  "required": ["sistema"]
                }
                """),

            Ferramenta(
                "preencher_ficha_personagem",
                "Preenche os campos do template PDF em Templates/<sistema>/ com os dados do personagem e salva em Output/Personagens/.",
                """
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
        ];
    }

    public async Task<ResultadoDaFerramenta> ExecutarAsync(
        string nome,
        JsonObject argumentos,
        CancellationToken cancelamento)
    {
        try
        {
            return nome switch
            {
                "escrever_arquivo_conhecimento" => await EscreverConhecimentoAsync(argumentos, cancelamento),
                "listar_campos_da_ficha" => ListarCamposDaFicha(argumentos),
                "preencher_ficha_personagem" => PreencherFicha(argumentos),
                _ => new ResultadoDaFerramenta($"Ferramenta desconhecida: '{nome}'.", Erro: true),
            };
        }
        catch (ErroDeFerramenta excecao)
        {
            return new ResultadoDaFerramenta(excecao.Message, Erro: true);
        }
        catch (UnauthorizedAccessException excecao)
        {
            // Tentativa de escapar do diretório permitido — o guardrail em ação.
            return new ResultadoDaFerramenta(excecao.Message, Erro: true);
        }
        catch (IOException excecao)
        {
            return new ResultadoDaFerramenta($"Falha de arquivo: {excecao.Message}", Erro: true);
        }
    }

    private async Task<ResultadoDaFerramenta> EscreverConhecimentoAsync(JsonObject argumentos, CancellationToken cancelamento)
    {
        var gravado = await EscritorDeConhecimento.EscreverAsync(
            caminhos,
            Obrigatorio(argumentos, "sistema"),
            Obrigatorio(argumentos, "caminho"),
            Obrigatorio(argumentos, "conteudo"),
            cancelamento);

        return new ResultadoDaFerramenta($"Arquivo gravado: {gravado}");
    }

    private ResultadoDaFerramenta ListarCamposDaFicha(JsonObject argumentos)
    {
        var campos = PreenchedorDeFicha.ListarCampos(
            caminhos,
            Obrigatorio(argumentos, "sistema"),
            Opcional(argumentos, "arquivoModelo"));

        var lista = string.Join("\n", campos.Select(campo => $"- {campo}"));

        return new ResultadoDaFerramenta(
            $"{campos.Count} campo(s) preenchível(is), exatamente como devem ser informados " +
            $"a preencher_ficha_personagem:\n{lista}");
    }

    private ResultadoDaFerramenta PreencherFicha(JsonObject argumentos)
    {
        var campos = MapaDeTexto(argumentos, "campos");

        var gerado = PreenchedorDeFicha.Preencher(
            caminhos,
            Obrigatorio(argumentos, "sistema"),
            Opcional(argumentos, "arquivoModelo"),
            campos,
            Obrigatorio(argumentos, "nomeArquivoSaida"));

        return new ResultadoDaFerramenta($"Ficha salva em {gerado} ({campos.Count} campo(s) preenchido(s)).");
    }

    private static string Obrigatorio(JsonObject argumentos, string campo)
    {
        var valor = argumentos[campo]?.GetValue<string>();

        return string.IsNullOrWhiteSpace(valor)
            ? throw new ErroDeFerramenta($"Campo obrigatório '{campo}' ausente ou vazio.")
            : valor;
    }

    private static string? Opcional(JsonObject argumentos, string campo)
    {
        var valor = argumentos[campo];

        return valor is JsonValue texto && texto.TryGetValue<string>(out var conteudo) && conteudo.Length > 0
            ? conteudo
            : null;
    }

    private static IReadOnlyDictionary<string, string> MapaDeTexto(JsonObject argumentos, string campo)
    {
        if (argumentos[campo] is not JsonObject objeto)
        {
            throw new ErroDeFerramenta($"Campo obrigatório '{campo}' ausente ou inválido (esperado um objeto).");
        }

        return objeto.ToDictionary(
            par => par.Key,
            par => par.Value switch
            {
                JsonValue valor when valor.TryGetValue<string>(out var texto) => texto,
                null => "",
                var outro => outro.ToJsonString(),
            });
    }

    private static JsonObject Ferramenta(string nome, string descricao, string esquemaJson) => new()
    {
        ["name"] = nome,
        ["description"] = descricao,
        ["inputSchema"] = JsonNode.Parse(esquemaJson),
    };
}
