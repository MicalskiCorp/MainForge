using Anthropic;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;
using RpgForge.Agents;
using RpgForge.Claude;
using RpgForge.Core;
using RpgForge.Tools;

// Harness de validação ponta a ponta do fluxo completo:
//   Configurador -> Knowledge/*.md -> Dungeon Master -> Output/Personagens/*.pdf
// Usa o sistema fictício "SistemaTeste" (PDF pequeno) para manter o custo de API baixo.
//
// Uso:
//   dotnet run --project tools/ValidacaoPontaAPonta                 (fluxo completo, gasta API)
//   dotnet run --project tools/ValidacaoPontaAPonta -- --somente-pdf (só valida os PDFs, sem custo)
//   dotnet run --project tools/ValidacaoPontaAPonta -- --sistema Outro

var somentePdf = args.Contains("--somente-pdf");
var sistema = ValorDoArgumento(args, "--sistema") ?? "SistemaTeste";

var caminhos = CaminhosDoProjeto.Descobrir();
Console.OutputEncoding = System.Text.Encoding.UTF8;

Titulo($"Raiz do projeto: {caminhos.Raiz}");
Titulo($"Sistema alvo: {sistema}");

// O construtor estático desta ferramenta registra o IFontResolver do PdfSharp. Sem isso,
// até só LER um campo de AcroForm lança "No appropriate font found" (ver seção 2 do
// CONTEXTO_SESSAO.txt).
_ = new FerramentaPreencherFichaPersonagem(caminhos);

// ---------------------------------------------------------------------------
// Etapa 1 — validar os PDFs de entrada (sem custo de API)
// ---------------------------------------------------------------------------
Titulo("Etapa 1 — validando os PDFs de entrada");

var caminhoRegras = Path.Combine(caminhos.Sistemas, sistema, "Regras.pdf");
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
using (var ficha = PdfReader.Open(caminhoFicha, PdfDocumentOpenMode.Import))
{
    if (ficha.AcroForm is null)
    {
        Erro("Ficha.pdf não tem AcroForm.");
        return 1;
    }

    camposDaFicha = [.. ficha.AcroForm.Fields.Names];
    Ok($"Ficha.pdf abre com PdfSharp — {camposDaFicha.Count} campo(s): {string.Join(", ", camposDaFicha)}");
}

if (somentePdf)
{
    Titulo("--somente-pdf: parando aqui, sem chamar a API.");
    return 0;
}

// ---------------------------------------------------------------------------
// Etapa 2 — cliente da Claude API
// ---------------------------------------------------------------------------
Titulo("Etapa 2 — criando o cliente da Claude API");

// Mesma resolução que o aplicativo usa: variável de ambiente primeiro, senão a chave que o
// usuário configurou dentro do RpgForge.Cli (cifrada com DPAPI).
var (origemDaChave, opcoes) = OpcoesClienteClaude.Resolver(new ArmazenamentoDeChaveApi());

if (opcoes is null)
{
    Erro("Nenhuma chave da Claude API configurada.");
    Erro("Configure-a no aplicativo (dotnet run --project src/RpgForge.Cli, opção 4)");
    Erro("ou defina a variável de ambiente ANTHROPIC_API_KEY.");
    return 1;
}

Ok($"Chave obtida de: {origemDaChave}.");

AnthropicClient cliente = FabricaClienteClaude.Criar(opcoes);
var mensagens = cliente.Beta.Messages;
Ok($"Cliente pronto — modelo {opcoes.Modelo}.");

// ---------------------------------------------------------------------------
// Etapa 3 — Configurador gera a base de conhecimento
// ---------------------------------------------------------------------------
Titulo("Etapa 3 — Configurador processando o sistema");

var configurador = new SessaoDeAgente(mensagens, DefinicaoDeAgente.Configurador, caminhos, opcoes);

var respostaConfigurador = await configurador.EnviarAsync(
    $"Processe o sistema '{sistema}': leia o(s) PDF(s) dele e gere a base de conhecimento " +
    $"completa em Knowledge/{sistema}/. Quando terminar, resuma os arquivos que criou.");

Console.WriteLine(respostaConfigurador);

var diretorioConhecimento = Path.Combine(caminhos.Conhecimento, sistema);
if (!Directory.Exists(diretorioConhecimento))
{
    Erro($"O Configurador não criou '{diretorioConhecimento}'.");
    return 1;
}

var arquivosGerados = Directory
    .EnumerateFiles(diretorioConhecimento, "*.md", SearchOption.AllDirectories)
    .ToList();

Ok($"{arquivosGerados.Count} arquivo(s) de conhecimento gerado(s):");
foreach (var arquivo in arquivosGerados)
{
    Console.WriteLine($"    {Path.GetRelativePath(caminhos.Raiz, arquivo)} ({new FileInfo(arquivo).Length} bytes)");
}

// ---------------------------------------------------------------------------
// Etapa 4 — Dungeon Master conduz a criação do personagem
// ---------------------------------------------------------------------------
Titulo("Etapa 4 — Dungeon Master criando o personagem");

Directory.CreateDirectory(caminhos.SaidaPersonagens);
var fichasAntes = FichasEmSaida(caminhos);

var dungeonMaster = new SessaoDeAgente(mensagens, DefinicaoDeAgente.DungeonMaster, caminhos, opcoes);

// A primeira mensagem já embute todas as decisões, para o teste rodar sem interação humana.
var roteiro = new[]
{
    $"Quero criar um personagem no sistema '{sistema}'. Já decidi tudo: classe Guerreiro, " +
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

    var resposta = await dungeonMaster.EnviarAsync(roteiro[turno]);
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
