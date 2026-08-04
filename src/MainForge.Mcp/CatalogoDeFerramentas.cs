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
                "Cria ou sobrescreve um arquivo Markdown dentro de Knowledge/<sistema>/ com o conteúdo informado. Use sempre esta ferramenta para gravar a base de conhecimento. O index.md de cada pasta e o registro de progresso são atualizados automaticamente a cada gravação.",
                """
                {
                  "type": "object",
                  "properties": {
                    "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Knowledge/)." },
                    "caminho": { "type": "string", "description": "Caminho do arquivo .md relativo a Knowledge/<sistema>/, ex.: \"Classes/Guerreiro.md\". Nao use \"index.md\": ele e gerado automaticamente." },
                    "conteudo": { "type": "string", "description": "Conteúdo Markdown completo a gravar no arquivo." },
                    "resumo": { "type": "string", "description": "Uma linha dizendo o que ha neste arquivo. Vai para o index.md da pasta e e o que outro agente le para decidir se precisa abrir o arquivo." },
                    "livro": { "type": "string", "description": "Nome do PDF de onde veio o conteúdo, quando ele vem de um compêndio ou expansão." }
                  },
                  "required": ["sistema", "caminho", "conteudo", "resumo"]
                }
                """),

            Ferramenta(
                "descrever_pasta_de_conhecimento",
                "Registra no index.md de uma pasta de Knowledge/<sistema>/ a descrição do que existe naquele nível. Use depois de criar os arquivos da pasta.",
                """
                {
                  "type": "object",
                  "properties": {
                    "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Knowledge/)." },
                    "pasta": { "type": "string", "description": "Caminho da pasta relativo a Knowledge/<sistema>/, ex.: \"Classes\". Use \"\" para a raiz do sistema." },
                    "descricao": { "type": "string", "description": "Um parágrafo curto dizendo o que ha nesse nível e quando vale a pena abrir os arquivos dele." }
                  },
                  "required": ["sistema", "pasta", "descricao"]
                }
                """),

            Ferramenta(
                "registrar_plano_de_conhecimento",
                "Registra a lista de arquivos que você pretende gerar para o sistema, antes de começar a gravá-los. É o que permite retomar o processamento de onde parou se a sessão for interrompida.",
                """
                {
                  "type": "object",
                  "properties": {
                    "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Knowledge/)." },
                    "itens": {
                      "type": "array",
                      "description": "Arquivos planejados. Registrar de novo um caminho ja existente nao apaga o progresso dele.",
                      "items": {
                        "type": "object",
                        "properties": {
                          "caminho": { "type": "string", "description": "Caminho do .md relativo a Knowledge/<sistema>/, ex.: \"Classes/Guerreiro.md\"." },
                          "descricao": { "type": "string", "description": "Uma linha dizendo o que vai no arquivo." }
                        },
                        "required": ["caminho"]
                      }
                    },
                    "livro": { "type": "string", "description": "Nome do PDF que originou estes itens, quando vierem de um compêndio ou expansão." }
                  },
                  "required": ["sistema", "itens"]
                }
                """),

            Ferramenta(
                "consultar_progresso",
                "Diz o que ja foi gerado para o sistema, o que ainda falta do plano registrado e quais livros ainda não foram lidos. Consulte antes de começar a trabalhar para não refazer o que ja existe.",
                """
                {
                  "type": "object",
                  "properties": {
                    "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Knowledge/)." }
                  },
                  "required": ["sistema"]
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
                      "description": "Mapa de nome do campo do formulário PDF para o valor (texto) a preencher. Campo de marcação (checkbox) aceita \"true\"/\"false\", os nomes de estado do próprio PDF (\"Yes\"/\"Off\") ou texto vazio para deixar desmarcado.",
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
                "descrever_pasta_de_conhecimento" => DescreverPasta(argumentos),
                "registrar_plano_de_conhecimento" => RegistrarPlano(argumentos),
                "consultar_progresso" => ConsultarProgresso(argumentos),
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
        var sistema = Obrigatorio(argumentos, "sistema");

        var gravado = await EscritorDeConhecimento.EscreverAsync(
            caminhos,
            sistema,
            Obrigatorio(argumentos, "caminho"),
            Obrigatorio(argumentos, "conteudo"),
            Opcional(argumentos, "resumo"),
            Opcional(argumentos, "livro"),
            cancelamento);

        var estado = EstadoDoProcessamento.Carregar(caminhos, sistema);

        var restante = estado.Pendentes.Count == 0
            ? ""
            : $" Ainda faltam {estado.Pendentes.Count} arquivo(s) do plano.";

        return new ResultadoDaFerramenta($"Arquivo gravado: {gravado}. Indices atualizados.{restante}");
    }

    private ResultadoDaFerramenta DescreverPasta(JsonObject argumentos)
    {
        var pasta = argumentos["pasta"]?.GetValue<string>() ?? "";

        EscritorDeConhecimento.DescreverPasta(
            caminhos,
            Obrigatorio(argumentos, "sistema"),
            pasta,
            Obrigatorio(argumentos, "descricao"));

        var onde = pasta.Length == 0 ? "raiz do sistema" : pasta;

        return new ResultadoDaFerramenta($"Descrição registrada no index.md de {onde}.");
    }

    private ResultadoDaFerramenta RegistrarPlano(JsonObject argumentos)
    {
        var sistema = Obrigatorio(argumentos, "sistema");
        var livro = Opcional(argumentos, "livro");

        if (argumentos["itens"] is not JsonArray itens || itens.Count == 0)
        {
            throw new ErroDeFerramenta("Campo obrigatório 'itens' ausente ou vazio (esperado um array de objetos).");
        }

        var planejados = new List<ItemDoPlano>();

        foreach (var item in itens)
        {
            if (item is not JsonObject objeto)
            {
                throw new ErroDeFerramenta("Cada item de 'itens' precisa ser um objeto com 'caminho'.");
            }

            var caminho = Obrigatorio(objeto, "caminho");

            if (!caminho.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                throw new ErroDeFerramenta($"'{caminho}' precisa terminar em .md.");
            }

            planejados.Add(new ItemDoPlano
            {
                Caminho = caminho,
                Descricao = Opcional(objeto, "descricao") ?? "",
                Livro = livro,
            });
        }

        var estado = EstadoDoProcessamento.Carregar(caminhos, sistema);
        estado.SincronizarComDisco();
        estado.RegistrarPlano(planejados);
        estado.Salvar();

        return new ResultadoDaFerramenta(
            $"Plano registrado: {planejados.Count} item(ns) informado(s). {estado.Resumo()}. " +
            "Grave os arquivos um a um; o progresso é salvo a cada gravação.");
    }

    private ResultadoDaFerramenta ConsultarProgresso(JsonObject argumentos)
    {
        var estado = EstadoDoProcessamento.Carregar(caminhos, Obrigatorio(argumentos, "sistema"));
        estado.SincronizarComDisco();
        estado.Salvar();

        return new ResultadoDaFerramenta(estado.DescreverParaOAgente());
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
