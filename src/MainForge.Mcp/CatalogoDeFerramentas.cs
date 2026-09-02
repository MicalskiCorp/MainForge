using System.Text;
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
///   <item>escrita em Sistemas/ — poderia ser a ferramenta <c>Write</c> embutida, mas passar
///   por aqui permite impor o confinamento de diretório em código, que é o guardrail que a
///   arquitetura do produto promete.</item>
/// </list>
///
/// O que ficou de fora é tão importante quanto: <c>Read</c> e <c>Glob</c> embutidos do Claude
/// Code substituem as antigas ferramentas de leitura e listagem. Ler PDF <em>tem</em> que ser
/// pelo <c>Read</c> nativo: uma ferramenta MCP que devolvesse o PDF só faria o Claude Code
/// gravar o binário em disco e passar o caminho ao modelo, que não conseguiria lê-lo.
/// </summary>
/// <param name="escopo">
/// O que esta sessão pode tocar — as fontes da mesa e o personagem dela —, quando há uma mesa
/// definida. Ferramenta que lê <c>Sistemas/</c> ou grava um personagem precisa conferi-lo: a
/// escolha das expansões e a identidade do personagem são aplicadas do lado do agente, e
/// nenhuma dessas restrições alcança este processo.
/// </param>
public sealed class CatalogoDeFerramentas(CaminhosDoProjeto caminhos, EscopoDaSessao? escopo = null)
{
    public JsonArray Descrever()
    {
        return
        [
            Ferramenta(
                "escrever_arquivo_conhecimento",
                "Cria ou sobrescreve um arquivo Markdown dentro de Sistemas/<sistema>/ com o conteúdo informado. Use sempre esta ferramenta para gravar a base de conhecimento. O index.md de cada pasta e o registro de progresso são atualizados automaticamente a cada gravação.",
                """
                {
                  "type": "object",
                  "properties": {
                    "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Sistemas/)." },
                    "caminho": { "type": "string", "description": "Caminho do arquivo .md relativo a Sistemas/<sistema>/, começando pela pasta da fonte de onde veio o conteúdo, ex.: \"base/Classes/Guerreiro.md\" ou \"Compendio-Arcano/Classes/Guerreiro-Subclasses.md\". Só os dois arquivos da ficha ficam na raiz. Nao use \"index.md\": ele e gerado automaticamente." },
                    "conteudo": { "type": "string", "description": "Conteúdo Markdown completo a gravar no arquivo." },
                    "resumo": { "type": "string", "description": "Uma linha dizendo o que ha neste arquivo. Vai para o index.md da pasta e e o que outro agente le para decidir se precisa abrir o arquivo." },
                    "livro": { "type": "string", "description": "Nome do PDF de onde veio o conteúdo, quando ele vem de um compêndio ou expansão." }
                  },
                  "required": ["sistema", "caminho", "conteudo", "resumo"]
                }
                """),

            Ferramenta(
                "descrever_pasta_de_conhecimento",
                "Registra no index.md de uma pasta de Sistemas/<sistema>/ a descrição do que existe naquele nível. Use depois de criar os arquivos da pasta.",
                """
                {
                  "type": "object",
                  "properties": {
                    "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Sistemas/)." },
                    "pasta": { "type": "string", "description": "Caminho da pasta relativo a Sistemas/<sistema>/, começando pela fonte, ex.: \"base\" ou \"base/Classes\". Use \"\" para a raiz do sistema." },
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
                    "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Sistemas/)." },
                    "itens": {
                      "type": "array",
                      "description": "Arquivos planejados. Registrar de novo um caminho ja existente nao apaga o progresso dele.",
                      "items": {
                        "type": "object",
                        "properties": {
                          "caminho": { "type": "string", "description": "Caminho do .md relativo a Sistemas/<sistema>/, começando pela pasta da fonte, ex.: \"base/Classes/Guerreiro.md\"." },
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
                "Diz o que ja foi gerado para o sistema, o que ainda falta do plano registrado e quais livros ainda não foram lidos, cada um com a fonte (jogo base ou expansão) a que pertence. Consulte antes de começar a trabalhar para não refazer o que ja existe.",
                """
                {
                  "type": "object",
                  "properties": {
                    "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Sistemas/)." }
                  },
                  "required": ["sistema"]
                }
                """),

            Ferramenta(
                "procurar_no_texto_dos_livros",
                "Procura um termo no texto extraído dos livros do sistema (a versão Markdown dos PDFs, em Input/<sistema>/<fonte>/_texto/) e devolve arquivo, linha e seção de cada ocorrência. Use para achar onde uma regra está antes de abrir o arquivo com Read: ler o livro inteiro para achar uma tabela é o maior desperdício de cota que existe aqui. Informe 'contexto' para receber as linhas em volta e dispensar o Read seguinte. A busca ignora acentos e maiúsculas.",
                """
                {
                  "type": "object",
                  "properties": {
                    "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Input/)." },
                    "termo": { "type": "string", "description": "Texto a procurar, ex.: \"Pontos de Vida\". Ignora acentos e maiúsculas." },
                    "livro": { "type": "string", "description": "Nome do PDF a que restringir a busca. Sem ele, procura em todos os livros do sistema." },
                    "maximo": { "type": "integer", "description": "Máximo de ocorrências a devolver (padrão 30)." },
                    "contexto": { "type": "integer", "description": "Linhas ao redor de cada ocorrência a devolver junto (0 a 40, padrão 0). Com um valor aqui, o trecho vem no resultado e você nao precisa de um Read depois — o que sai mais barato que a chamada extra. Combine com um 'maximo' menor." }
                  },
                  "required": ["sistema", "termo"]
                }
                """),

            Ferramenta(
                "estrutura_do_livro",
                "Devolve o sumário de um livro convertido: os títulos com o número da linha de cada um. Use ANTES de procurar ou ler, num livro que você ainda não conhece — é o mapa que diz onde cada assunto começa, por algumas centenas de tokens. Sem ele, achar a seção de magias num livro de 30 mil linhas é tentativa e erro.",
                """
                {
                  "type": "object",
                  "properties": {
                    "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Input/)." },
                    "livro": { "type": "string", "description": "Nome do PDF a que restringir. Sem ele, devolve o sumário de todos os livros do sistema." },
                    "nivelMaximo": { "type": "integer", "description": "Profundidade máxima de título (1 = só os capítulos, 6 = tudo; padrão 6). Comece raso num livro grande." },
                    "maximo": { "type": "integer", "description": "Máximo de títulos por livro (padrão 300)." }
                  },
                  "required": ["sistema"]
                }
                """),

            Ferramenta(
                "procurar_no_conhecimento",
                "Procura um termo na base de conhecimento do sistema (Sistemas/<sistema>/), dentro das fontes que esta mesa usa, e devolve arquivo, linha e seção de cada ocorrência. Use quando a pergunta não cai direto na estrutura do índice (\"onde está a regra de carga?\", \"que magias curam?\"): descer índice por índice custa uma leitura por nível. Informe 'contexto' para receber as linhas em volta e responder sem abrir o arquivo. A busca ignora acentos e maiúsculas.",
                """
                {
                  "type": "object",
                  "properties": {
                    "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Sistemas/)." },
                    "termo": { "type": "string", "description": "Texto a procurar, ex.: \"Pontos de Vida\". Ignora acentos e maiúsculas." },
                    "maximo": { "type": "integer", "description": "Máximo de ocorrências a devolver (padrão 30)." },
                    "contexto": { "type": "integer", "description": "Linhas ao redor de cada ocorrência a devolver junto (0 a 40, padrão 0). Numa conversa longa, receber o trecho aqui sai mais barato que um Read depois: cada chamada a mais reenvia a conversa inteira. Combine com um 'maximo' menor." }
                  },
                  "required": ["sistema", "termo"]
                }
                """),

            Ferramenta(
                "registrar_personagem",
                "Grava o estado atual do personagem em Personagens/<sistema>/<personagem>/, para que a criação possa ser retomada depois e para que ele possa evoluir (subir de nível, trocar equipamento) numa conversa futura. Chame a cada bloco de decisões fechado — atributos definidos, classe escolhida, equipamento comprado —, não só no fim.",
                """
                {
                  "type": "object",
                  "properties": {
                    "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Sistemas/)." },
                    "personagem": { "type": "string", "description": "Identificador do personagem, exatamente como a mensagem inicial da conversa informou." },
                    "nome": { "type": "string", "description": "Nome do personagem, quando ele já tiver um." },
                    "resumo": { "type": "string", "description": "Uma linha dizendo quem ele é, ex.: \"Anao guerreiro de nivel 3\". E o que aparece na lista de personagens do aplicativo." },
                    "ficha": { "type": "string", "description": "O estado COMPLETO do personagem em Markdown: atributos, escolhas feitas, equipamento, magias, nivel, e o que ainda falta decidir. E o que reconstroi o personagem numa conversa futura, entao escreva pensando em quem nao acompanhou esta." },
                    "campos": {
                      "type": "object",
                      "description": "Opcional: valores por nome de campo do PDF, como iriam para preencher_ficha_personagem.",
                      "additionalProperties": { "type": "string" }
                    }
                  },
                  "required": ["sistema", "personagem", "ficha"]
                }
                """),

            Ferramenta(
                "listar_campos_da_ficha",
                "Lista os campos preenchíveis (AcroForm) da ficha em Templates/<sistema>/ NA ORDEM EM QUE ESTAO IMPRESSOS, cada um com o rotulo que aparece ao lado dele na pagina e se e caixa de marcacao. O nome vai exatamente como listado para preencher_ficha_personagem. Use o rotulo, e nunca o nome do campo, para decidir o que vai em cada linha: numa ficha traduzida os dois divergem.",
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
                "conferir_ficha_do_sistema",
                "Confere o Ficha-ModeloEmTexto.md do sistema contra a ficha em PDF: para cada campo, o rotulo que o desenho em texto lhe da tem de ser o que esta impresso ao lado dele na pagina. Chame depois de gravar os dois arquivos da ficha; se acusar divergencia, corrija o modelo e confira de novo.",
                """
                {
                  "type": "object",
                  "properties": {
                    "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Templates/)." }
                  },
                  "required": ["sistema"]
                }
                """),

            Ferramenta(
                "registrar_validacao_da_ficha",
                "Grava as regras que dizem se um personagem e valido neste sistema, em Sistemas/<sistema>/Ficha-Validacao.json. O aplicativo as executa em C#, de graca, na criacao e na importacao de personagem. So entra aqui o que uma maquina decide sozinha: campo obrigatorio, faixa de numero, lista fechada de valores. Regra que dependa de julgamento fica no Ficha-Mapeamento.md. Chame depois de conferir_ficha_do_sistema, com UMA linha por campo preenchivel da ficha.",
                """
                {
                  "type": "object",
                  "properties": {
                    "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Sistemas/)." },
                    "usaMagias": { "type": "boolean", "description": "Este sistema tem magias, ou o equivalente dele (poderes, invocacoes, artes)." },
                    "camposDeMagia": {
                      "type": "array",
                      "description": "Nomes dos campos da ficha em que as magias do personagem sao escritas, exatamente como listar_campos_da_ficha os devolveu.",
                      "items": { "type": "string" }
                    },
                    "magiasPorExtenso": { "type": "boolean", "description": "A ficha em PDF tem espaco para a DESCRICAO COMPLETA de cada magia, e nao so para a lista de nomes. Se for false e o sistema usar magias, o aplicativo gera uma folha extra com as magias do personagem por extenso. Sem este campo, quem responde e o proprio PDF." },
                    "campos": {
                      "type": "array",
                      "description": "Uma entrada por campo preenchivel da ficha. Campo sem regra nenhuma tambem entra, so com nome e rotulo: e assim que a validacao sabe que ele existe.",
                      "items": {
                        "type": "object",
                        "properties": {
                          "campo": { "type": "string", "description": "Nome exato do campo no PDF, como listar_campos_da_ficha o devolveu." },
                          "rotulo": { "type": "string", "description": "O texto impresso ao lado do campo na ficha. E o que aparece nas mensagens ao usuario." },
                          "obrigatorio": { "type": "boolean", "description": "O personagem nao esta pronto enquanto este campo estiver vazio." },
                          "tipo": { "type": "string", "enum": ["Texto", "Inteiro", "Marcacao"], "description": "Texto (padrao), Inteiro ou Marcacao (caixa de marcacao)." },
                          "minimo": { "type": "integer", "description": "Menor valor aceito, para tipo Inteiro." },
                          "maximo": { "type": "integer", "description": "Maior valor aceito, para tipo Inteiro." },
                          "valores": {
                            "type": "array",
                            "description": "Os unicos valores aceitos, quando o campo e escolha fechada (classe, raca, tendencia). Vazio significa texto livre. A comparacao ignora acento e maiuscula.",
                            "items": { "type": "string" }
                          },
                          "observacao": { "type": "string", "description": "Por que a regra e essa, em uma linha. Vai junto da mensagem de erro, entao cite a regra do sistema." }
                        },
                        "required": ["campo"]
                      }
                    }
                  },
                  "required": ["sistema", "campos"]
                }
                """),

            Ferramenta(
                "validar_personagem",
                "Confere os valores de um personagem contra as regras do sistema (Ficha-Validacao.json) e devolve o que esta fora delas. Nao gasta cota e nao altera nada. Use antes da conferencia visual: ela pega de graca o que voce teria de conferir campo a campo — numero fora da faixa, campo obrigatorio vazio, valor fora da lista, magia sem o nome em ingles.",
                """
                {
                  "type": "object",
                  "properties": {
                    "sistema": { "type": "string", "description": "Identificador do sistema (nome da subpasta em Sistemas/)." },
                    "personagem": { "type": "string", "description": "Identificador do personagem, como a mensagem inicial informou. Sem ele, valem os campos passados em 'campos'." },
                    "campos": {
                      "type": "object",
                      "description": "Valores por nome de campo do PDF, como iriam para preencher_ficha_personagem. Sem isto, valem os campos ja gravados no dossie do personagem.",
                      "additionalProperties": { "type": "string" }
                    }
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
                    "personagem": { "type": "string", "description": "Identificador do personagem, como a mensagem inicial informou. Liga o PDF ao dossie em Personagens/ e marca a criacao como concluida, encerrando a conversa. Informe sempre que a conversa tiver um." },
                    "arquivoModelo": { "type": "string", "description": "Nome do PDF de template dentro de Templates/<sistema>/. Só é obrigatório se houver mais de um PDF nessa pasta." },
                    "campos": {
                      "type": "object",
                      "description": "Mapa de nome do campo do formulário PDF para o valor (texto) a preencher. Campo de marcação (checkbox) aceita \"true\"/\"false\", os nomes de estado do próprio PDF (\"Yes\"/\"Off\") ou texto vazio para deixar desmarcado.",
                      "additionalProperties": { "type": "string" }
                    },
                    "nivel": { "type": "integer", "description": "Nivel do personagem nesta ficha. Informe sempre: o aplicativo guarda uma copia por nivel concluido, e sem o numero o registro nao sabe a que nivel pertence." },
                    "nomeArquivoSaida": { "type": "string", "description": "Nome do arquivo PDF de saída (sem caminho), ex.: \"Thoradin.pdf\". Salvo em Output/Personagens/. Quando 'personagem' e informado, quem decide o nome e o aplicativo: e uma ficha por personagem ali, sempre a atual." }
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
                "procurar_no_texto_dos_livros" => ProcurarNosLivros(argumentos),
                "estrutura_do_livro" => EstruturaDoLivro(argumentos),
                "procurar_no_conhecimento" => ProcurarNoConhecimento(argumentos),
                "registrar_personagem" => RegistrarPersonagem(argumentos),
                "listar_campos_da_ficha" => ListarCamposDaFicha(argumentos),
                "conferir_ficha_do_sistema" => ConferirFichaDoSistema(argumentos),
                "registrar_validacao_da_ficha" => RegistrarValidacaoDaFicha(argumentos),
                "validar_personagem" => ValidarPersonagem(argumentos),
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

    private ResultadoDaFerramenta ProcurarNosLivros(JsonObject argumentos)
    {
        var termo = Obrigatorio(argumentos, "termo");
        var maximo = Inteiro(argumentos, "maximo") ?? BuscaNosLivros.MaximoPadraoDeOcorrencias;

        var ocorrencias = BuscaNosLivros.Procurar(
            caminhos,
            Obrigatorio(argumentos, "sistema"),
            termo,
            Opcional(argumentos, "livro"),
            Math.Clamp(maximo, 1, 200),
            Inteiro(argumentos, "contexto") ?? BuscaEmTexto.ContextoPadrao);

        return new ResultadoDaFerramenta(BuscaNosLivros.Descrever(ocorrencias, termo, maximo));
    }

    /// <summary>
    /// O sumário do livro: o mapa que <c>Sistemas/</c> tem por <c>index.md</c> e <c>Input/</c>
    /// não tinha por nada.
    /// </summary>
    private ResultadoDaFerramenta EstruturaDoLivro(JsonObject argumentos)
    {
        var maximo = Math.Clamp(
            Inteiro(argumentos, "maximo") ?? Tools.EstruturaDoLivro.MaximoPadraoDeTitulos,
            1,
            2000);

        var livros = Tools.EstruturaDoLivro.Levantar(
            caminhos,
            Obrigatorio(argumentos, "sistema"),
            Opcional(argumentos, "livro"),
            Math.Clamp(Inteiro(argumentos, "nivelMaximo") ?? 6, 1, 6),
            maximo);

        return new ResultadoDaFerramenta(Tools.EstruturaDoLivro.Descrever(livros, maximo));
    }

    /// <summary>
    /// A busca dentro da base já mapeada. Passa a restrição de fontes adiante: é aqui, e não na
    /// lista de negações do Claude Code, que a escolha de expansões da mesa continua valendo
    /// para uma ferramenta que roda em outro processo.
    /// </summary>
    private ResultadoDaFerramenta ProcurarNoConhecimento(JsonObject argumentos)
    {
        var termo = Obrigatorio(argumentos, "termo");
        var maximo = Inteiro(argumentos, "maximo") ?? BuscaNoConhecimento.MaximoPadraoDeOcorrencias;

        var ocorrencias = BuscaNoConhecimento.Procurar(
            caminhos,
            Obrigatorio(argumentos, "sistema"),
            termo,
            escopo,
            Math.Clamp(maximo, 1, 200),
            Inteiro(argumentos, "contexto") ?? BuscaEmTexto.ContextoPadrao);

        return new ResultadoDaFerramenta(BuscaNoConhecimento.Descrever(ocorrencias, termo, maximo));
    }

    /// <summary>
    /// Grava o dossiê — e confere antes que ele é o desta conversa.
    ///
    /// <para><b>Por que a conferência.</b> O campo <c>ficha</c> é o estado <em>completo</em> do
    /// personagem: gravar no dossiê errado não corrompe um pedaço, substitui o personagem
    /// inteiro por outro. O prompt proíbe mexer em personagem alheio, mas proibição no prompt é
    /// pedido, e o identificador chega aqui como texto que o modelo escreveu — numa evolução, com
    /// dois personagens do mesmo sistema em jogo, trocá-lo é um erro plausível e irreversível.</para>
    /// </summary>
    private ResultadoDaFerramenta RegistrarPersonagem(JsonObject argumentos)
    {
        var sistema = Obrigatorio(argumentos, "sistema");
        var id = Obrigatorio(argumentos, "personagem");

        ConferirEscopoDoPersonagem(sistema, id);

        var personagem = RepositorioDePersonagens.Registrar(
            caminhos,
            sistema,
            id,
            Opcional(argumentos, "nome"),
            Opcional(argumentos, "resumo"),
            Obrigatorio(argumentos, "ficha"),
            argumentos["campos"] is JsonObject ? MapaDeTexto(argumentos, "campos") : null);

        return new ResultadoDaFerramenta(
            $"Personagem '{personagem.Rotulo}' salvo em Personagens/{personagem.Sistema}/{personagem.Id}/. " +
            "Se a conversa for interrompida agora, o usuário consegue retomá-la daqui.");
    }

    private ResultadoDaFerramenta ListarCamposDaFicha(JsonObject argumentos)
    {
        var campos = PreenchedorDeFicha.ListarLayoutDaFicha(
            caminhos,
            Obrigatorio(argumentos, "sistema"),
            Opcional(argumentos, "arquivoModelo"));

        var texto = new StringBuilder();

        texto.AppendLine($"{campos.Count} campo(s) preenchível(is), na ordem em que estão impressos.");
        texto.AppendLine();
        texto.AppendLine("O nome vai exatamente como está aqui para preencher_ficha_personagem, inclusive");
        texto.AppendLine("espaços no fim. Quem diz para que o campo serve é o ROTULO — o texto impresso ao");
        texto.AppendLine("lado dele —, nunca o nome, que é interno do PDF e pode estar em outro idioma ou");
        texto.AppendLine("fora de ordem. Campos de uma mesma 'linha' são a mesma entrada da ficha (a caixa");
        texto.AppendLine("de marcação e o valor, por exemplo). A distância do rótulo vem junto: perto de");
        texto.AppendLine("zero o pareamento é certo; distante, confira no PDF antes de confiar.");

        foreach (var pagina in campos.GroupBy(campo => campo.Pagina))
        {
            texto.AppendLine();
            texto.AppendLine($"## Página {pagina.Key}");

            foreach (var linha in pagina.GroupBy(campo => campo.Linha))
            {
                texto.AppendLine($"linha {linha.Key}:");

                foreach (var campo in linha)
                {
                    var tipo = campo.DeMarcacao ? "marcação" : "texto";

                    var rotulo = campo.Direcao is DirecaoDoRotulo.Nenhum
                        ? "(nenhum texto impresso por perto)"
                        : $"\"{campo.Rotulo}\" ({Lado(campo.Direcao)}, {campo.DistanciaDoRotulo:0.#} pt)";

                    texto.AppendLine($"  {campo.Ordem,4}. [{tipo}] {campo.Nome}  ->  {rotulo}");
                }
            }
        }

        return new ResultadoDaFerramenta(texto.ToString().TrimEnd());
    }

    private ResultadoDaFerramenta ConferirFichaDoSistema(JsonObject argumentos)
    {
        var sistema = Obrigatorio(argumentos, "sistema");
        var resultado = ConferenciaDaFicha.Conferir(caminhos, sistema);

        var avisos = resultado.Avisos.Count == 0
            ? ""
            : "\n\nAVISOS:\n" + string.Join("\n", resultado.Avisos.Select(aviso => $"- {aviso}"));

        if (resultado.Aprovada)
        {
            return new ResultadoDaFerramenta(
                $"Ficha de '{sistema}' conferida: cada campo do {SistemaRpg.NomeDoModeloEmTexto} está " +
                "na linha em que está impresso no PDF — nome e posição concordam." + avisos);
        }

        var lista = string.Join(
            "\n",
            resultado.Divergencias.Select(divergencia => $"- {divergencia.Descrever()}"));

        // Erro, e não aviso: seguir com o modelo torto produz uma ficha em PDF errada em silêncio,
        // que é o defeito mais caro de descobrir depois.
        return new ResultadoDaFerramenta(
            $"{resultado.Divergencias.Count} divergência(s) entre o {SistemaRpg.NomeDoModeloEmTexto} de " +
            $"'{sistema}' e a ficha em PDF. O que manda é a linha impressa no PDF — corrija o modelo, " +
            $"nunca o PDF:\n{lista}{avisos}",
            Erro: true);
    }

    /// <summary>
    /// Grava as regras de validação do sistema.
    ///
    /// <para><b>Por que é ferramenta e não arquivo escrito à mão.</b> O agente já sabe escrever em
    /// <c>Sistemas/</c> por <c>escrever_arquivo_conhecimento</c> — mas aquela ferramenta só aceita
    /// <c>.md</c>, e de propósito: o que ela grava é para outro agente ler. Este arquivo é para o
    /// C# executar, então o formato dele é conferido na entrada em vez de descoberto na hora de
    /// usar, quando o erro já seria uma validação que não roda em silêncio.</para>
    /// </summary>
    private ResultadoDaFerramenta RegistrarValidacaoDaFicha(JsonObject argumentos)
    {
        var sistema = Obrigatorio(argumentos, "sistema");

        if (argumentos["campos"] is not JsonArray campos || campos.Count == 0)
        {
            throw new ErroDeFerramenta(
                "Campo obrigatório 'campos' ausente ou vazio (esperado um array com uma entrada por " +
                "campo preenchível da ficha).");
        }

        var regras = new RegrasDaFicha
        {
            Origem = OrigemDasRegras.Configurador,
            UsaMagias = Booleano(argumentos, "usaMagias") ?? false,
            CamposDeMagia = [.. Textos(argumentos, "camposDeMagia")],
            MagiasPorExtenso = Booleano(argumentos, "magiasPorExtenso"),
            // O idioma não vem do modelo: ele é medido da ficha em branco, e é o que decide se a
            // regra do nome das magias em inglês vale neste sistema. Deixar o agente declará-lo
            // faria a regra depender de quem ela existe para conferir.
            Idioma = IdiomaDoSistema(sistema),
        };

        foreach (var item in campos)
        {
            if (item is not JsonObject objeto)
            {
                throw new ErroDeFerramenta("Cada item de 'campos' precisa ser um objeto com 'campo'.");
            }

            regras.Campos.Add(new RegraDeCampo
            {
                Campo = Obrigatorio(objeto, "campo"),
                Rotulo = Opcional(objeto, "rotulo") ?? "",
                Obrigatorio = Booleano(objeto, "obrigatorio") ?? false,
                Tipo = Tipo(Opcional(objeto, "tipo")),
                Minimo = Inteiro(objeto, "minimo"),
                Maximo = Inteiro(objeto, "maximo"),
                Valores = [.. Textos(objeto, "valores")],
                Observacao = Opcional(objeto, "observacao"),
            });
        }

        // Sem esta conferência a validação nasceria cega justamente onde ela importa: um campo com
        // nome errado nunca casa com valor nenhum, então ele nunca reprova nada — e "não reprovou"
        // é indistinguível de "está certo" para quem lê o resultado.
        var daFicha = CamposDaFicha(sistema);

        var inventados = daFicha.Count == 0
            ? []
            : regras.Campos
                .Select(regra => regra.Campo)
                .Where(campo => !daFicha.Contains(campo))
                .ToList();

        if (inventados.Count > 0)
        {
            throw new ErroDeFerramenta(
                $"{inventados.Count} campo(s) não existem na ficha em branco de '{sistema}': " +
                $"{string.Join(", ", inventados.Take(10))}. Use listar_campos_da_ficha e copie os nomes " +
                "exatamente como ela os devolve — nome de campo com um caractere de diferença nunca " +
                "casa, e a regra dele passa a não valer para ninguém.");
        }

        var gravado = RegrasDaFicha.Gravar(caminhos, sistema, regras);

        var faltando = daFicha.Count == 0
            ? 0
            : daFicha.Count(campo => regras.Campos.All(regra => regra.Campo != campo));

        var pendencia = faltando == 0
            ? ""
            : $" Faltam {faltando} campo(s) da ficha sem entrada aqui — eles não são conferidos.";

        return new ResultadoDaFerramenta(
            $"Regras gravadas em {gravado}: {regras.Campos.Count} campo(s), " +
            $"{regras.Campos.Count(regra => regra.Obrigatorio)} obrigatório(s). " +
            (regras.UsaMagias
                ? regras.TrazMagiasPorExtenso
                    ? "O sistema usa magias e a ficha as comporta por extenso."
                    : "O sistema usa magias e a ficha NÃO as comporta por extenso — o aplicativo vai " +
                      "gerar uma folha extra com as descrições para cada personagem conjurador."
                : "O sistema não usa magias.") +
            pendencia);
    }

    /// <summary>
    /// Confere um personagem contra as regras do sistema. Os valores podem vir na chamada (o que
    /// o agente está prestes a escrever) ou do dossiê (o que já está gravado).
    /// </summary>
    private ResultadoDaFerramenta ValidarPersonagem(JsonObject argumentos)
    {
        var sistema = Obrigatorio(argumentos, "sistema");
        var identificador = Opcional(argumentos, "personagem") ?? escopo?.Personagem;

        ConferirEscopoDoPersonagem(sistema, Opcional(argumentos, "personagem"));

        var campos = argumentos["campos"] is JsonObject
            ? MapaDeTexto(argumentos, "campos")
            : identificador is null
                ? throw new ErroDeFerramenta(
                    "Informe 'campos' com os valores a conferir, ou 'personagem' para conferir o que " +
                    "já está gravado no dossiê dele.")
                : RepositorioDePersonagens.Carregar(caminhos, sistema, identificador)?.Campos
                  ?? throw new ErroDeFerramenta($"Não há personagem '{identificador}' em Personagens/{sistema}/.");

        var resultado = ValidacaoDePersonagem.Validar(caminhos, sistema, campos);

        // Não é erro de ferramenta: a validação aponta e não impede. Devolver isError faria o
        // agente tratar um personagem fora da regra como uma chamada malfeita e tentar de novo,
        // quando o que se espera dele é levar os apontamentos ao usuário.
        return new ResultadoDaFerramenta(
            resultado.Aprovado || resultado.SemRegras
                ? resultado.Resumir(sistema)
                : resultado.Relatorio(sistema) +
                  "\n\nLeve isto ao usuário antes de gerar a ficha, citando a regra de cada ponto. " +
                  "NÃO corrija por conta própria: pergunte, porque a mesa pode ter combinado diferente.");
    }

    /// <summary>
    /// Os nomes dos campos da ficha em branco do sistema, ou vazio quando não há ficha para
    /// conferir — um sistema que chegou por pacote sem <c>Templates/</c>. Vazio significa "não dá
    /// para conferir", e não "nenhum campo existe".
    /// </summary>
    private IReadOnlySet<string> CamposDaFicha(string sistema)
    {
        try
        {
            return PreenchedorDeFicha.ListarCampos(caminhos, sistema, null).ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception excecao) when (excecao is ErroDeFerramenta or UnauthorizedAccessException or IOException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// O idioma da ficha em branco do sistema, medido agora. Sem ficha legível fica
    /// indeterminado — que é o valor que faz a regra do nome das magias se calar em vez de
    /// chutar.
    /// </summary>
    private IdiomaDetectado IdiomaDoSistema(string sistema)
    {
        try
        {
            return IdiomaDaFicha.Detectar(PreenchedorDeFicha
                .ListarLayoutDaFicha(caminhos, sistema, null)
                .Where(campo => campo.Rotulo is { Length: > 0 })
                .Select(campo => campo.Rotulo!));
        }
        catch (Exception excecao) when (excecao is not OutOfMemoryException)
        {
            return IdiomaDetectado.Indeterminado;
        }
    }

    private static TipoDoCampo Tipo(string? declarado) => declarado?.ToLowerInvariant() switch
    {
        "inteiro" or "numero" or "int" => TipoDoCampo.Inteiro,
        "marcacao" or "marcação" or "checkbox" or "booleano" => TipoDoCampo.Marcacao,
        _ => TipoDoCampo.Texto,
    };

    private static string Lado(DirecaoDoRotulo direcao) => direcao switch
    {
        DirecaoDoRotulo.Direita => "à direita",
        DirecaoDoRotulo.Esquerda => "à esquerda",
        DirecaoDoRotulo.Acima => "acima",
        DirecaoDoRotulo.Abaixo => "abaixo",
        _ => "",
    };

    private ResultadoDaFerramenta PreencherFicha(JsonObject argumentos)
    {
        var sistema = Obrigatorio(argumentos, "sistema");
        var campos = MapaDeTexto(argumentos, "campos");

        // Antes de gerar o PDF: gerar a ficha de outro personagem escreveria em Output/ e ainda
        // marcaria o dossiê alheio como concluído.
        ConferirEscopoDoPersonagem(sistema, Opcional(argumentos, "personagem"));

        // Quando o argumento não vem, quem responde é o escopo da sessão. O parâmetro era só
        // pedido no texto da ferramenta, e omiti-lo gerava o PDF sem fechar o dossiê: o
        // personagem ficava "em desenvolvimento" com a ficha pronta, a interface não via a
        // conclusão para encerrar a conversa, e o agente emendava a próxima etapa (subir de
        // nível) numa criação que, para o usuário, tinha acabado.
        var identificador = Opcional(argumentos, "personagem") ?? escopo?.Personagem;

        var dossie = identificador is null
            ? null
            : RepositorioDePersonagens.Carregar(caminhos, sistema, identificador);

        // O nome do arquivo de um personagem é do aplicativo, não do modelo: em Output/ fica uma
        // ficha por personagem, sempre a atual. Deixar o agente nomear fazia cada evolução somar
        // um PDF à pasta, e nada dizia qual dos quatro era o que valia hoje.
        var gerado = PreenchedorDeFicha.Preencher(
            caminhos,
            sistema,
            Opcional(argumentos, "arquivoModelo"),
            campos,
            dossie is null
                ? Obrigatorio(argumentos, "nomeArquivoSaida")
                : FichasDoPersonagem.NomeNaSaida(caminhos, dossie));

        // A validação roda mesmo quando ninguém a pediu: é o último momento em que os valores
        // ainda são vistos por alguém, e ela não custa cota nenhuma. Não impede a geração — o
        // apontamento vai na resposta, para o agente levá-lo ao usuário.
        var validacao = ValidacaoDePersonagem.Validar(caminhos, sistema, campos);

        var apontamentos = validacao.Aprovado || validacao.SemRegras
            ? ""
            : "\n\nATENÇÃO — a ficha saiu, mas " + validacao.Relatorio(sistema) +
              "\nDiga isso ao usuário citando a regra de cada ponto, e pergunte antes de corrigir.";

        // O PDF existir é o sinal de conclusão que não depende de o modelo declarar que
        // terminou — por isso é aqui, e não numa ferramenta à parte, que o dossiê fecha.
        if (identificador is not { } personagem)
        {
            return new ResultadoDaFerramenta(
                $"Ficha salva em {gerado} ({campos.Count} campo(s) preenchido(s)).{apontamentos}");
        }

        RepositorioDePersonagens.RegistrarFichaGerada(
            caminhos, sistema, personagem, gerado, campos, Inteiro(argumentos, "nivel"));

        var folha = RepositorioDePersonagens.Carregar(caminhos, sistema, personagem)?.FolhaDeMagias;

        return new ResultadoDaFerramenta(
            $"Ficha salva em {gerado} ({campos.Count} campo(s) preenchido(s)). " +
            $"O personagem '{personagem}' está registrado como concluído e pode ser evoluído depois. " +
            "Uma cópia desta ficha ficou guardada no histórico de níveis dele." +
            (folha is { Length: > 0 }
                ? $" A ficha deste sistema não comporta as magias por extenso, então saiu também uma " +
                  $"folha extra com as descrições em {folha} — avise o usuário dos dois arquivos."
                : "") +
            apontamentos);
    }

    /// <summary>
    /// Esta sessão pode mexer neste personagem deste sistema?
    ///
    /// <para>É a regra 9 da estrutura do projeto aplicada às ferramentas de personagem: a negação
    /// de <c>Read</c> por caminho para no agente e não alcança este processo, então o que
    /// <em>escreve</em> precisa conferir o escopo aqui, em C#. A busca no conhecimento já fazia
    /// isso; as duas ferramentas que gravam o personagem, não — e são elas que perdem dado.</para>
    ///
    /// <para>Sessão sem escopo (o Configurador, o servidor rodado à mão) não tem o que conferir e
    /// passa direto. Quem quer confinar precisa mandar o escopo — recusar por ausência dele
    /// quebraria todo uso legítimo sem mesa definida.</para>
    /// </summary>
    private void ConferirEscopoDoPersonagem(string sistema, string? personagem)
    {
        if (escopo is null)
        {
            return;
        }

        if (!escopo.EhOSistema(sistema))
        {
            throw new ErroDeFerramenta(
                $"Esta conversa é do sistema '{escopo.Sistema}' — não dá para mexer em personagem " +
                $"de '{sistema}'. Use o sistema desta conversa.");
        }

        if (personagem is not null && !escopo.PermitePersonagem(personagem))
        {
            throw new ErroDeFerramenta(
                $"Esta conversa é do personagem '{escopo.Personagem}', e '{personagem}' é outro. " +
                "Use o identificador que a mensagem inicial informou — gravar por cima do dossiê " +
                "de outro personagem apagaria o estado inteiro dele.");
        }
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

    /// <summary>
    /// Número opcional. O modelo tanto manda <c>30</c> quanto <c>"30"</c>, e recusar a segunda
    /// forma só custaria uma chamada perdida para o usuário.
    /// </summary>
    private static int? Inteiro(JsonObject argumentos, string campo) => argumentos[campo] switch
    {
        JsonValue valor when valor.TryGetValue<int>(out var numero) => numero,
        JsonValue valor when valor.TryGetValue<string>(out var texto) && int.TryParse(texto, out var numero) => numero,
        _ => null,
    };

    /// <summary>
    /// Booleano opcional. Como em <see cref="Inteiro"/>, o modelo tanto manda <c>true</c> quanto
    /// <c>"true"</c>, e recusar a segunda forma só custaria uma chamada perdida.
    /// </summary>
    private static bool? Booleano(JsonObject argumentos, string campo) => argumentos[campo] switch
    {
        JsonValue valor when valor.TryGetValue<bool>(out var logico) => logico,
        JsonValue valor when valor.TryGetValue<string>(out var texto) && bool.TryParse(texto, out var logico) => logico,
        _ => null,
    };

    /// <summary>Lista de textos opcional. Ausente ou de outro tipo vira lista vazia.</summary>
    private static IReadOnlyList<string> Textos(JsonObject argumentos, string campo)
    {
        if (argumentos[campo] is not JsonArray itens)
        {
            return [];
        }

        return
        [
            .. itens
                .OfType<JsonValue>()
                .Select(item => item.TryGetValue<string>(out var texto) ? texto : null)
                .OfType<string>()
                .Where(texto => texto.Length > 0),
        ];
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
