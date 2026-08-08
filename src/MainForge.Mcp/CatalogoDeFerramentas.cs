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
                    "personagem": { "type": "string", "description": "Identificador do personagem, como a mensagem inicial informou. Liga o PDF ao dossie em Personagens/ e marca a criacao como concluida. Informe sempre que a conversa tiver um." },
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
                "procurar_no_texto_dos_livros" => ProcurarNosLivros(argumentos),
                "estrutura_do_livro" => EstruturaDoLivro(argumentos),
                "procurar_no_conhecimento" => ProcurarNoConhecimento(argumentos),
                "registrar_personagem" => RegistrarPersonagem(argumentos),
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
        var sistema = Obrigatorio(argumentos, "sistema");
        var campos = MapaDeTexto(argumentos, "campos");

        // Antes de gerar o PDF: gerar a ficha de outro personagem escreveria em Output/ e ainda
        // marcaria o dossiê alheio como concluído.
        ConferirEscopoDoPersonagem(sistema, Opcional(argumentos, "personagem"));

        var gerado = PreenchedorDeFicha.Preencher(
            caminhos,
            sistema,
            Opcional(argumentos, "arquivoModelo"),
            campos,
            Obrigatorio(argumentos, "nomeArquivoSaida"));

        // O PDF existir é o sinal de conclusão que não depende de o modelo declarar que
        // terminou — por isso é aqui, e não numa ferramenta à parte, que o dossiê fecha.
        if (Opcional(argumentos, "personagem") is not { } personagem)
        {
            return new ResultadoDaFerramenta($"Ficha salva em {gerado} ({campos.Count} campo(s) preenchido(s)).");
        }

        RepositorioDePersonagens.RegistrarFichaGerada(caminhos, sistema, personagem, gerado, campos);

        return new ResultadoDaFerramenta(
            $"Ficha salva em {gerado} ({campos.Count} campo(s) preenchido(s)). " +
            $"O personagem '{personagem}' está registrado como concluído e pode ser evoluído depois.");
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
