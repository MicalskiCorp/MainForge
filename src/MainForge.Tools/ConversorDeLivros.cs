using System.Diagnostics;
using System.Text;
using MainForge.Core;

namespace MainForge.Tools;

/// <summary>Como chamar o markitdown nesta máquina.</summary>
/// <param name="Executavel">Caminho completo do programa a lançar.</param>
/// <param name="ArgumentosIniciais">O que vem antes dos argumentos da conversão (ex.: <c>-m markitdown</c>).</param>
/// <param name="Descricao">Como o usuário reconhece essa instalação, para aparecer no console.</param>
public sealed record ProgramaDeConversao(
    string Executavel,
    IReadOnlyList<string> ArgumentosIniciais,
    string Descricao);

/// <summary>O que aconteceu com um livro.</summary>
public enum SituacaoDaConversao
{
    /// <summary>O texto foi gerado agora.</summary>
    Convertido,

    /// <summary>Já havia texto mais novo que o PDF — nada a fazer.</summary>
    JaEstavaPronto,

    /// <summary>O markitdown recusou este arquivo (PDF protegido, corrompido, só imagem).</summary>
    Falhou,
}

/// <param name="Livro">Nome do PDF.</param>
/// <param name="CaminhoDoTexto">O .md gerado, relativo à raiz do projeto, quando houver.</param>
/// <param name="Detalhe">Motivo da falha, quando houver.</param>
/// <param name="Conversor">Quem extraiu o texto — o markitdown ou o extrator interno.</param>
public sealed record ConversaoDeLivro(
    string Livro,
    SituacaoDaConversao Situacao,
    string? CaminhoDoTexto = null,
    string? Detalhe = null,
    string Conversor = "");

/// <summary>
/// Converte os livros de <c>Systems/</c> em Markdown com o
/// <see href="https://github.com/microsoft/markitdown">markitdown</see>, da Microsoft (licença
/// MIT), antes de o Configurador começar a trabalhar.
///
/// <para><b>Por que existe.</b> Ler PDF é a operação mais cara do aplicativo, e cara duas vezes:
/// o Claude Code rasteriza as páginas pedidas e manda imagens ao modelo, então um livro de 300
/// páginas consome a janela da assinatura antes de o agente ter visto metade dele. Um livro de
/// regras é texto corrido com tabelas — quase nada se perde ao virar Markdown, e o mesmo
/// conteúdo passa a caber em uma fração dos tokens. A conversão roda na máquina do usuário, de
/// graça, e o resultado fica em cache: só é refeita quando o PDF muda.</para>
///
/// <para><b>Por que um programa externo, e por que ele não pode ser obrigatório.</b> Extrair
/// texto de PDF com qualidade (colunas, tabelas, listas) é um problema grande, e o markitdown já
/// o resolve com o ecossistema Python de extração. Mas ele é uma instalação a mais, e a leitura
/// dos livros é a operação central do produto: quando não há markitdown, quem converte é o
/// <see cref="ExtratorDeTextoDePdf"/>, que vem junto com o aplicativo. Sem esse piso, uma máquina
/// sem Python <em>e</em> sem poppler ficava sem nenhum caminho até o livro — o texto não existia
/// e o <c>Read</c> do PDF falhava com "pdftoppm is not installed".</para>
///
/// <para>O texto convertido fica em <c>Systems/&lt;Sistema&gt;/&lt;fonte&gt;/_texto/</c>: ao lado
/// do PDF de origem, dentro da mesma fonte, para que a regra "o conteúdo de um livro vai para a
/// pasta da fonte dele" continue valendo sem exceção. O underscore marca que é derivado — como
/// o <c>_estado-do-processamento.json</c> — e mantém a pasta fora de qualquer
/// <c>Glob(*.pdf)</c>.</para>
/// </summary>
public static class ConversorDeLivros
{
    /// <summary>Subpasta com o texto extraído dos PDFs daquela fonte.</summary>
    public const string NomeDaPastaDeTexto = "_texto";

    /// <summary>
    /// Aponta o executável do markitdown quando ele não está no PATH. Existe pelo mesmo motivo
    /// de <c>MAINFORGE_CLAUDE_CODE</c>: instalação em ambiente virtual é comum e o PATH do
    /// aplicativo não é o do terminal onde ela foi feita.
    /// </summary>
    public const string VariavelDeAmbiente = "MAINFORGE_MARKITDOWN";

    /// <summary>Como instalar, na mensagem que o usuário vê quando não há markitdown.</summary>
    public const string ComoInstalar =
        "O texto saiu pelo extrator interno, que basta para a maioria dos livros. O markitdown\n" +
        "(Microsoft, MIT) costuma sair melhor em tabelas e listas — se quiser usá-lo:\n" +
        "  pip install \"markitdown[pdf]\"        (precisa de Python 3.10+)\n" +
        "  ou: uv tool install \"markitdown[pdf]\"\n" +
        "Se ele já estiver instalado num ambiente virtual, aponte " + VariavelDeAmbiente +
        " para o executável.\n" +
        "Depois, apague a pasta _texto/ do sistema para os livros serem convertidos de novo.";

    /// <summary>
    /// Onde fica (ou ficaria) o texto extraído de um PDF: <c>&lt;pasta do PDF&gt;/_texto/&lt;nome&gt;.md</c>.
    /// </summary>
    public static string CaminhoDoTexto(string caminhoDoPdf)
    {
        var pasta = Path.GetDirectoryName(caminhoDoPdf)
            ?? throw new ArgumentException($"'{caminhoDoPdf}' não tem diretório.", nameof(caminhoDoPdf));

        return Path.Combine(pasta, NomeDaPastaDeTexto, Path.GetFileNameWithoutExtension(caminhoDoPdf) + ".md");
    }

    /// <summary>
    /// O texto já convertido de um PDF, ou <c>null</c> se ele não existe ou está mais velho que o
    /// PDF. Quem pergunta é o prompt do Configurador, para mandar o agente ler o Markdown em vez
    /// do PDF — e só quando o Markdown de fato corresponde ao livro que está lá.
    /// </summary>
    public static string? TextoAtualizadoDe(string caminhoDoPdf)
    {
        var texto = CaminhoDoTexto(caminhoDoPdf);

        return EstaEmDia(caminhoDoPdf, texto) ? texto : null;
    }

    /// <summary>
    /// Acha o markitdown nesta máquina, na ordem em que ele costuma estar. Devolve <c>null</c>
    /// quando não há nenhum — que não é erro: o aplicativo continua funcionando lendo os PDFs.
    /// </summary>
    public static ProgramaDeConversao? Localizar()
    {
        var apontado = Environment.GetEnvironmentVariable(VariavelDeAmbiente);

        if (!string.IsNullOrWhiteSpace(apontado) && File.Exists(apontado))
        {
            return new ProgramaDeConversao(apontado, [], $"{VariavelDeAmbiente}={apontado}");
        }

        if (NoCaminhoDoSistema("markitdown") is { } executavel)
        {
            return new ProgramaDeConversao(executavel, [], executavel);
        }

        // O módulo instalado sem o script de console (pip install em ambiente virtual, sobretudo)
        // só aparece por aqui.
        foreach (var python in new[] { "python", "python3", "py" })
        {
            if (NoCaminhoDoSistema(python) is { } interpretador && TemOModulo(interpretador))
            {
                return new ProgramaDeConversao(interpretador, ["-m", "markitdown"], $"{interpretador} -m markitdown");
            }
        }

        // Último recurso: o uv baixa e roda o markitdown sob demanda, sem instalar nada
        // permanentemente. Só serve com rede, então fica depois das instalações locais.
        if (NoCaminhoDoSistema("uvx") is { } uvx)
        {
            return new ProgramaDeConversao(uvx, ["--from", "markitdown[pdf]", "markitdown"], "uvx markitdown");
        }

        return null;
    }

    /// <summary>Nome do extrator que vem junto com o aplicativo, para a interface mostrar.</summary>
    public const string ExtratorInterno = "extrator interno";

    /// <summary>
    /// Converte os PDFs de um sistema que ainda não têm texto em dia, um a um, avisando o
    /// progresso — a conversão de um livro grande leva dezenas de segundos e o console não pode
    /// ficar mudo.
    /// </summary>
    /// <param name="programa">
    /// O markitdown, quando há um. Sem ele — e quando ele falha num livro —, quem converte é o
    /// <see cref="ExtratorDeTextoDePdf"/>: nenhum livro pode ficar sem texto só porque uma
    /// dependência opcional não está instalada.
    /// </param>
    /// <param name="aoComecar">Chamado com o nome do livro antes de convertê-lo.</param>
    public static async Task<IReadOnlyList<ConversaoDeLivro>> ConverterSistemaAsync(
        CaminhosDoProjeto caminhos,
        string sistema,
        ProgramaDeConversao? programa,
        Action<string>? aoComecar = null,
        CancellationToken cancelamento = default)
    {
        var diretorio = CaminhosDoProjeto.ResolverDentroDe(caminhos.Sistemas, sistema);

        if (!Directory.Exists(diretorio))
        {
            return [];
        }

        var resultados = new List<ConversaoDeLivro>();

        foreach (var pdf in Directory.EnumerateFiles(diretorio, "*.pdf", SearchOption.AllDirectories).Order())
        {
            cancelamento.ThrowIfCancellationRequested();

            var nome = Path.GetFileName(pdf);
            var destino = CaminhoDoTexto(pdf);

            if (EstaEmDia(pdf, destino))
            {
                resultados.Add(new ConversaoDeLivro(
                    nome, SituacaoDaConversao.JaEstavaPronto, Relativo(caminhos, destino)));
                continue;
            }

            aoComecar?.Invoke(nome);
            resultados.Add(await ConverterAsync(caminhos, programa, pdf, destino, cancelamento));
        }

        return resultados;
    }

    /// <summary>
    /// Converte um livro pelo markitdown, caindo para o extrator interno quando ele não existe ou
    /// não dá conta daquele arquivo. O relato de falha guarda o motivo do markitdown junto do
    /// motivo do extrator: quando os dois desistem, saber por quê é o que diz ao usuário se o
    /// problema é o livro (digitalizado, protegido) ou a instalação.
    /// </summary>
    private static async Task<ConversaoDeLivro> ConverterAsync(
        CaminhosDoProjeto caminhos,
        ProgramaDeConversao? programa,
        string pdf,
        string destino,
        CancellationToken cancelamento)
    {
        if (programa is null)
        {
            return ExtrairInternamente(caminhos, pdf, destino, cancelamento);
        }

        var pelaFerramenta = await ConverterComMarkitdownAsync(caminhos, programa, pdf, destino, cancelamento);

        if (pelaFerramenta.Situacao != SituacaoDaConversao.Falhou)
        {
            return pelaFerramenta;
        }

        var peloExtrator = ExtrairInternamente(caminhos, pdf, destino, cancelamento);

        return peloExtrator.Situacao == SituacaoDaConversao.Falhou
            ? peloExtrator with { Detalhe = $"markitdown: {pelaFerramenta.Detalhe}; extrator interno: {peloExtrator.Detalhe}" }
            : peloExtrator with { Detalhe = $"o markitdown falhou ({pelaFerramenta.Detalhe})" };
    }

    private static ConversaoDeLivro ExtrairInternamente(
        CaminhosDoProjeto caminhos,
        string pdf,
        string destino,
        CancellationToken cancelamento)
    {
        var nome = Path.GetFileName(pdf);

        try
        {
            ExtratorDeTextoDePdf.ExtrairPara(pdf, destino, cancelamento);

            return new ConversaoDeLivro(
                nome, SituacaoDaConversao.Convertido, Relativo(caminhos, destino), Conversor: ExtratorInterno);
        }
        catch (OperationCanceledException)
        {
            Descartar(destino);
            throw;
        }
        catch (Exception excecao) when (excecao is ErroDeFerramenta or IOException or SystemException)
        {
            Descartar(destino);

            return new ConversaoDeLivro(nome, SituacaoDaConversao.Falhou, Detalhe: excecao.Message);
        }
    }

    private static async Task<ConversaoDeLivro> ConverterComMarkitdownAsync(
        CaminhosDoProjeto caminhos,
        ProgramaDeConversao programa,
        string pdf,
        string destino,
        CancellationToken cancelamento)
    {
        var nome = Path.GetFileName(pdf);

        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);

        var inicio = MontarInicio(programa, pdf, destino);

        try
        {
            using var processo = Process.Start(inicio)
                ?? throw new IOException($"não foi possível lançar '{programa.Executavel}'");

            // A saída de erro é lida enquanto o processo roda: um livro grande enche o buffer do
            // canal e o markitdown travaria esperando alguém consumi-lo.
            var erros = processo.StandardError.ReadToEndAsync(cancelamento);
            var saida = processo.StandardOutput.ReadToEndAsync(cancelamento);

            await processo.WaitForExitAsync(cancelamento);

            if (processo.ExitCode == 0 && File.Exists(destino) && new FileInfo(destino).Length > 0)
            {
                return new ConversaoDeLivro(
                    nome, SituacaoDaConversao.Convertido, Relativo(caminhos, destino), Conversor: "markitdown");
            }

            // Meio arquivo é pior que nenhum: ficaria em dia pela data e o agente leria um livro
            // truncado sem ter como saber disso.
            Descartar(destino);

            var detalhe = (await erros).Trim();
            detalhe = detalhe.Length > 0 ? detalhe : (await saida).Trim();

            return new ConversaoDeLivro(
                nome,
                SituacaoDaConversao.Falhou,
                Detalhe: PrimeiraLinha(detalhe.Length > 0 ? detalhe : $"código de saída {processo.ExitCode}"));
        }
        catch (OperationCanceledException)
        {
            Descartar(destino);
            throw;
        }
        catch (Exception excecao) when (excecao is IOException or SystemException)
        {
            Descartar(destino);

            return new ConversaoDeLivro(nome, SituacaoDaConversao.Falhou, Detalhe: excecao.Message);
        }
    }

    /// <summary>
    /// Monta a chamada <c>&lt;programa&gt; &lt;pdf&gt; -o &lt;destino&gt;</c>.
    ///
    /// <para><b>O desvio pelo cmd.exe não é firula.</b> O pip instala o markitdown no Windows
    /// como <c>.cmd</c> em algumas configurações, e o .NET não escapa <c>&amp;</c> ao lançar um
    /// arquivo de lote: o caminho <c>Systems\D&amp;D5e\base\Livro.pdf</c> chega ao script cortado
    /// em <c>Systems\D</c>, e o resto — <c>D5e\base\Livro.pdf</c> — o cmd tenta executar como
    /// comando. Como o nome da pasta vem do sistema de RPG que o usuário importou, isso não é
    /// hipótese: <c>D&amp;D5e</c> é o caso comum. Montando a linha de comando aqui, com cada
    /// argumento entre aspas e o <c>/s</c> que faz o cmd tratar o resto literalmente, o caminho
    /// chega inteiro.</para>
    /// </summary>
    private static ProcessStartInfo MontarInicio(ProgramaDeConversao programa, string pdf, string destino)
    {
        var inicio = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        var argumentos = new List<string>([.. programa.ArgumentosIniciais, pdf, "-o", destino]);

        if (EhArquivoDeLote(programa.Executavel))
        {
            inicio.FileName = "cmd.exe";
            inicio.Arguments =
                "/s /c \"" + string.Join(" ", argumentos.Prepend(programa.Executavel).Select(EntreAspas)) + "\"";

            return inicio;
        }

        inicio.FileName = programa.Executavel;

        foreach (var argumento in argumentos)
        {
            inicio.ArgumentList.Add(argumento);
        }

        return inicio;
    }

    private static bool EhArquivoDeLote(string executavel) =>
        Path.GetExtension(executavel) is var extensao &&
        (extensao.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
         extensao.Equals(".bat", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Dentro de aspas o cmd não interpreta <c>&amp;</c>, <c>^</c> nem os outros metacaracteres.
    /// Aspas dentro do próprio caminho não têm como ser escapadas de forma confiável no cmd, e um
    /// nome de arquivo com aspas não existe no Windows — então a única saída honesta é recusar.
    /// </summary>
    private static string EntreAspas(string argumento) =>
        argumento.Contains('"')
            ? throw new ErroDeFerramenta($"O caminho '{argumento}' tem aspas e não pode ser passado ao conversor.")
            : $"\"{argumento}\"";

    /// <summary>
    /// O texto existe e é mais novo que o PDF. Comparar data em vez de guardar um registro à
    /// parte mantém a regra visível: apagar o .md manda converter de novo, e trocar o PDF também.
    /// </summary>
    private static bool EstaEmDia(string pdf, string texto) =>
        File.Exists(texto) &&
        new FileInfo(texto).Length > 0 &&
        File.GetLastWriteTimeUtc(texto) >= File.GetLastWriteTimeUtc(pdf);

    private static void Descartar(string caminho)
    {
        try
        {
            if (File.Exists(caminho))
            {
                File.Delete(caminho);
            }
        }
        catch (IOException)
        {
            // Arquivo preso por outro processo: a data continua velha, então a próxima execução
            // tenta converter de novo — que é o comportamento certo de qualquer forma.
        }
    }

    private static string Relativo(CaminhosDoProjeto caminhos, string caminho) =>
        Path.GetRelativePath(caminhos.Raiz, caminho).Replace('\\', '/');

    private static string PrimeiraLinha(string texto)
    {
        var linha = texto
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? texto;

        return linha.Length <= 200 ? linha : string.Concat(linha.AsSpan(0, 200), "...");
    }

    private static string? NoCaminhoDoSistema(string programa) => ProgramaDoSistema.Localizar(programa);

    /// <summary>
    /// O interpretador achado importa o markitdown? O <c>python.exe</c> do PATH no Windows pode
    /// ser o atalho da Microsoft Store, que não é um Python — a importação é o que distingue os
    /// dois sem depender de mensagem de erro.
    /// </summary>
    private static bool TemOModulo(string interpretador)
    {
        try
        {
            var inicio = new ProcessStartInfo
            {
                FileName = interpretador,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            inicio.ArgumentList.Add("-c");
            inicio.ArgumentList.Add("import markitdown");

            using var processo = Process.Start(inicio);

            if (processo is null)
            {
                return false;
            }

            // Um Python que trave aqui trava a abertura do processamento; 10s é folga suficiente
            // para o interpretador subir e falhar o import.
            if (!processo.WaitForExit(TimeSpan.FromSeconds(10)))
            {
                processo.Kill(entireProcessTree: true);
                return false;
            }

            return processo.ExitCode == 0;
        }
        catch (Exception excecao) when (excecao is IOException or SystemException)
        {
            return false;
        }
    }
}
