using MainForge.Agents;
using MainForge.ClaudeCode;
using MainForge.Core;
using MainForge.Tools;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;

// Harness de validação ponta a ponta do fluxo completo:
//   Configurador -> Sistemas/*.md -> Dungeon Master -> Output/Personagens/*.pdf
// Usa o sistema fictício "SistemaTeste" (PDF pequeno) para manter o consumo baixo.
//
// Uso:
//   dotnet run --project tools/ValidacaoPontaAPonta                  (fluxo completo, consome cota)
//   dotnet run --project tools/ValidacaoPontaAPonta -- --somente-pdf (só valida os PDFs, sem consumo)
//   dotnet run --project tools/ValidacaoPontaAPonta -- --ate-configurador  (para depois da etapa 3)
//   dotnet run --project tools/ValidacaoPontaAPonta -- --sistema Outro

var somentePdf = args.Contains("--somente-pdf");
var ateConfigurador = args.Contains("--ate-configurador");
var sistema = ValorDoArgumento(args, "--sistema") ?? "SistemaTeste";

var caminhos = CaminhosDoProjeto.Descobrir();
Console.OutputEncoding = System.Text.Encoding.UTF8;

Titulo($"Raiz do projeto: {caminhos.Raiz}");
Titulo($"Sistema alvo: {sistema}");

// ---------------------------------------------------------------------------
// Etapa 1 — validar os PDFs de entrada (sem consumo)
// ---------------------------------------------------------------------------
Titulo("Etapa 1 — validando os PDFs de entrada");

var caminhoRegras = Path.Combine(caminhos.Entrada, sistema, FonteDoSistema.IdDaBase, "Regras.pdf");
var caminhoFicha = Path.Combine(caminhos.Modelos, sistema, "Ficha.pdf");

if (!File.Exists(caminhoRegras) || !File.Exists(caminhoFicha))
{
    Erro($"Faltando arquivo: '{caminhoRegras}' e/ou '{caminhoFicha}'.");
    return 1;
}

using (var regras = PdfReader.Open(caminhoRegras, PdfDocumentOpenMode.Import))
{
    Ok($"Regras.pdf abre com PdfSharp — {regras.PageCount} página(s).");
}

List<string> camposDaFicha;

try
{
    // Passar por ImportadorDeSistema (e não abrir o PDF na mão) importa: o construtor
    // estático dele registra o IFontResolver do PdfSharp, sem o qual até só LER um campo de
    // AcroForm lança "No appropriate font found".
    camposDaFicha = [.. ImportadorDeSistema.LerCamposDaFicha(caminhoFicha)];
}
catch (InvalidOperationException excecao)
{
    Erro(excecao.Message);
    return 1;
}

Ok($"Ficha.pdf abre com PdfSharp — {camposDaFicha.Count} campo(s): {string.Join(", ", camposDaFicha)}");

if (somentePdf)
{
    Titulo("--somente-pdf: parando aqui, sem executar agente nenhum.");
    return 0;
}

// ---------------------------------------------------------------------------
// Etapa 2 — Claude Code
// ---------------------------------------------------------------------------
Titulo("Etapa 2 — localizando o Claude Code");

var opcoes = OpcoesDoClaudeCode.Resolver();

if (opcoes is null)
{
    Erro("Claude Code não encontrado. Instale-o e rode 'claude' uma vez para autenticar,");
    Erro($"ou aponte a variável de ambiente {LocalizadorDoClaudeCode.VariavelDeAmbiente}.");
    return 1;
}

Ok($"Executável: {opcoes.CaminhoExecutavel}");

var diagnostico = await DiagnosticoDoClaudeCode.ExecutarAsync(opcoes);

if (!diagnostico.Autenticado)
{
    Erro($"O Claude Code não completou um turno de teste: {diagnostico.Detalhe ?? "motivo não informado"}");
    return 1;
}

Ok($"Versão {diagnostico.Versao} — autenticado, modelo {opcoes.Modelo}.");

// ---------------------------------------------------------------------------
// Etapa 3 — Configurador gera a base de conhecimento
// ---------------------------------------------------------------------------
Titulo("Etapa 3 — Configurador processando o sistema");

using var configurador = new SessaoDeAgente(opcoes, DefinicaoDeAgente.Configurador, caminhos);

var respostaConfigurador = await configurador.EnviarAsync(
    $"Processe o sistema '{sistema}' do zero: leia o(s) PDF(s) em " +
    $"Input/{sistema}/{FonteDoSistema.IdDaBase}/ e a ficha em branco, registre o plano de " +
    $"arquivos e gere a base de conhecimento completa em Sistemas/{sistema}/{FonteDoSistema.IdDaBase}/. " +
    $"Os dois arquivos da ficha ({string.Join(" e ", SistemaRpg.ArquivosDaFicha)}) ficam na raiz " +
    $"de Sistemas/{sistema}/, fora da pasta de fonte. Quando terminar, resuma os arquivos que criou.",
    Progresso);

Console.WriteLine(respostaConfigurador);

var diretorioConhecimento = Path.Combine(caminhos.Conhecimento, sistema);

if (!Directory.Exists(diretorioConhecimento))
{
    Erro($"O Configurador não criou '{diretorioConhecimento}'.");
    return 1;
}

var arquivosGerados = Directory
    .EnumerateFiles(diretorioConhecimento, "*.md", SearchOption.AllDirectories)
    .Where(arquivo => !Path.GetFileName(arquivo).Equals(IndiceDeConhecimento.NomeDoArquivo, StringComparison.OrdinalIgnoreCase))
    .ToList();

Ok($"{arquivosGerados.Count} arquivo(s) de conhecimento gerado(s):");

foreach (var arquivo in arquivosGerados)
{
    Console.WriteLine($"    {Path.GetRelativePath(caminhos.Raiz, arquivo)} ({new FileInfo(arquivo).Length} bytes)");
}

// O índice é o que o Dungeon Master usa para navegar: sem ele, a etapa seguinte só
// funcionaria por sorte (ou lendo a base inteira, que é o que se quer evitar).
var indiceDoSistema = Path.Combine(diretorioConhecimento, IndiceDeConhecimento.NomeDoArquivo);

if (!File.Exists(indiceDoSistema))
{
    Erro($"Faltou o índice em '{indiceDoSistema}'.");
    return 1;
}

Ok($"Índices gerados, a partir de {Path.GetFileName(indiceDoSistema)} na raiz do sistema.");

var estadoDoSistema = EstadoDoProcessamento.Carregar(caminhos, sistema);
estadoDoSistema.SincronizarComDisco();

if (estadoDoSistema.Pendentes.Count > 0)
{
    Erro($"O Configurador deixou {estadoDoSistema.Pendentes.Count} arquivo(s) do plano por fazer:");

    foreach (var item in estadoDoSistema.Pendentes)
    {
        Console.WriteLine($"    {item.Caminho}");
    }

    return 1;
}

Ok($"Progresso registrado sem pendências: {estadoDoSistema.Resumo()}.");

if (ateConfigurador)
{
    Titulo("--ate-configurador: parando aqui, sem rodar o Dungeon Master.");
    return 0;
}

// ---------------------------------------------------------------------------
// Etapa 4 — Dungeon Master conduz a criação do personagem
// ---------------------------------------------------------------------------
Titulo("Etapa 4 — Dungeon Master criando o personagem");

Directory.CreateDirectory(caminhos.SaidaPersonagens);
var fichasAntes = FichasEmSaida(caminhos);

using var dungeonMaster = new SessaoDeAgente(opcoes, DefinicaoDeAgente.DungeonMaster, caminhos);

// A primeira mensagem já embute todas as decisões, para o teste rodar sem interação humana.
var roteiro = new[]
{
    $"Quero criar um personagem no sistema '{sistema}'. Esta mesa usa só o jogo base: " +
    $"Sistemas/{sistema}/{FonteDoSistema.IdDaBase}/index.md. Já decidi tudo: classe Guerreiro, " +
    "nome 'Thoradin', e pode distribuir os 3 pontos livres de atributo como achar melhor " +
    "pelas regras. Calcule o resto conforme as regras do sistema, me mostre a ficha final " +
    "e peça minha confirmação antes de gerar o PDF.",
    "Confirmado, pode gerar o PDF da ficha.",
    "Confirmado, pode prosseguir.",
    "Confirmado, pode prosseguir.",
    "Confirmado, pode prosseguir.",
    "Confirmado, pode prosseguir.",
};

string? fichaNova = null;

for (var turno = 0; turno < roteiro.Length; turno++)
{
    Console.WriteLine($"\n--- turno {turno + 1} ---");
    Console.WriteLine($"[usuário] {roteiro[turno]}");

    var resposta = await dungeonMaster.EnviarAsync(roteiro[turno], Progresso);
    Console.WriteLine($"[DM] {resposta}");

    fichaNova = FichasEmSaida(caminhos).Except(fichasAntes).FirstOrDefault();

    if (fichaNova is not null)
    {
        Ok($"Ficha gerada no turno {turno + 1}: {Path.GetRelativePath(caminhos.Raiz, fichaNova)}");
        break;
    }
}

if (fichaNova is null)
{
    Erro($"Nenhuma ficha nova apareceu em Output/Personagens/ depois de {roteiro.Length} turnos.");
    return 1;
}

// ---------------------------------------------------------------------------
// Etapa 5 — conferir os campos preenchidos
// ---------------------------------------------------------------------------
Titulo("Etapa 5 — campos da ficha preenchida");

using (var fichaGerada = PdfReader.Open(fichaNova, PdfDocumentOpenMode.Import))
{
    var formulario = fichaGerada.AcroForm!;

    foreach (var nomeCampo in formulario.Fields.Names)
    {
        var valor = formulario.Fields[nomeCampo] is PdfTextField campoTexto
            ? campoTexto.Text
            : formulario.Fields[nomeCampo]?.Value?.ToString();

        Console.WriteLine($"    {nomeCampo,-16} = {(string.IsNullOrWhiteSpace(valor) ? "(vazio)" : valor)}");
    }
}

Titulo("Validação ponta a ponta concluída. Confira acima se os valores batem com as regras.");
return 0;

// Mostrar o uso de ferramenta é o que torna o harness útil para depurar permissões: uma
// recusa aparece aqui, nomeada, em vez de virar um "o agente não fez o que eu pedi".
static void Progresso(EventoDeAgente evento)
{
    switch (evento)
    {
        case UsoDeFerramenta uso:
            Console.WriteLine($"    · {uso.Nome}({uso.Entrada})");
            break;
        case FalhaDeFerramenta erro:
            Console.WriteLine($"    · [recusado] {erro.Detalhe}");
            break;
    }
}

static List<string> FichasEmSaida(CaminhosDoProjeto caminhos) =>
    Directory.Exists(caminhos.SaidaPersonagens)
        ? [.. Directory.EnumerateFiles(caminhos.SaidaPersonagens, "*.pdf", SearchOption.TopDirectoryOnly)]
        : [];

static string? ValorDoArgumento(string[] args, string nome)
{
    var indice = Array.IndexOf(args, nome);
    return indice >= 0 && indice + 1 < args.Length ? args[indice + 1] : null;
}

static void Titulo(string texto)
{
    Console.WriteLine();
    Console.WriteLine($"=== {texto}");
}

static void Ok(string texto) => Console.WriteLine($"  [ok] {texto}");

static void Erro(string texto) => Console.Error.WriteLine($"  [ERRO] {texto}");
