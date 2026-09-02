using System.Text;
using System.Text.RegularExpressions;
using MainForge.Core;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace MainForge.Tools;

/// <summary>Por que a folha extra de magias saiu, ou por que não saiu.</summary>
public enum SituacaoDaFolhaDeMagias
{
    /// <summary>O sistema não usa magias — não há folha a fazer.</summary>
    SistemaSemMagias,

    /// <summary>
    /// A ficha em PDF do próprio sistema já tem espaço para as magias por extenso. A folha extra
    /// seria uma segunda cópia da mesma coisa.
    /// </summary>
    FichaJaTemEspaco,

    /// <summary>O personagem não tem magia nenhuma escrita na ficha.</summary>
    PersonagemSemMagias,

    /// <summary>A folha foi gerada.</summary>
    Gerada,

    /// <summary>Havia magias e não deu para escrever o PDF.</summary>
    NaoDeuParaGerar,
}

/// <summary>O que a folha extra produziu, para a interface poder contar ao usuário.</summary>
/// <param name="Situacao">O que aconteceu.</param>
/// <param name="Caminho">O PDF gerado, relativo à raiz do projeto, quando há um.</param>
/// <param name="Magias">Os nomes das magias que entraram na folha.</param>
/// <param name="SemDescricao">
/// As magias que entraram só com o nome, porque a base de conhecimento não tem a descrição delas
/// nas fontes desta mesa. Aparecem na folha assim mesmo: o jogador precisa saber que a magia está
/// na ficha dele, e uma linha dizendo "descrição não encontrada" é informação, não falha.
/// </param>
/// <param name="Motivo">Por que não deu, quando não deu.</param>
public sealed record ResultadoDaFolhaDeMagias(
    SituacaoDaFolhaDeMagias Situacao,
    string? Caminho,
    IReadOnlyList<string> Magias,
    IReadOnlyList<string> SemDescricao,
    string? Motivo);

/// <summary>
/// A folha extra com as magias do personagem por extenso, gerada em
/// <c>Output/Personagens/&lt;Id&gt;-Magias.pdf</c>, ao lado da ficha.
///
/// <para><b>Por que ela existe.</b> A ficha de personagem de boa parte dos sistemas tem espaço
/// para a <em>lista</em> de magias e não para o que cada uma faz: trinta linhas de uma linha cada,
/// onde cabe "Fireball" e mais nada. Na mesa isso significa jogar com o livro aberto ao lado — e
/// aqui significa que a ficha gerada, que era para bastar, não basta. Quando o sistema usa magias
/// e a ficha dele não as comporta por extenso, o aplicativo produz a folha que falta, com a
/// descrição completa de cada magia que o personagem tem.</para>
///
/// <para><b>Ela não decide nada sobre o personagem.</b> As magias são as que estão escritas nos
/// campos da ficha, e as descrições são as que a base de conhecimento já tem — copiadas, não
/// resumidas. Nenhum agente é chamado e nenhum token é gasto: é a regra 11 da estrutura do projeto
/// outra vez, o que o C# monta de graça não vira conversa.</para>
///
/// <para><b>Ela respeita a mesa.</b> A descrição é procurada só nas fontes que o personagem usa
/// (<see cref="Personagem.Fontes"/>). Uma magia de um compêndio que a mesa dispensou não vem
/// parar na folha por uma porta lateral — é a mesma regra que vale para a busca no conhecimento.</para>
/// </summary>
public static class FolhaDeMagias
{
    static FolhaDeMagias()
    {
        GlobalFontSettings.FontResolver ??= new ResolvedorDeFontesDoWindows();
    }

    /// <summary>O sufixo que distingue a folha de magias da ficha, no mesmo diretório.</summary>
    public const string Sufixo = "-Magias";

    /// <summary>O nome do arquivo da folha de magias de um personagem em <c>Output/</c>.</summary>
    public static string NomeNaSaida(CaminhosDoProjeto caminhos, Personagem personagem)
    {
        var daFicha = FichasDoPersonagem.NomeNaSaida(caminhos, personagem);

        return Path.GetFileNameWithoutExtension(daFicha) + Sufixo + ".pdf";
    }

    /// <summary>
    /// Gera a folha de magias do personagem, se ele precisar de uma.
    ///
    /// <para>Devolve sem fazer nada — e sem erro — nos três casos em que a folha não faz sentido:
    /// sistema sem magias, ficha que já traz as magias por extenso, e personagem sem magia
    /// nenhuma. São situações normais, não falhas: a maior parte dos personagens de um sistema com
    /// magia não é conjurador.</para>
    ///
    /// <para><b>Não mexe no dossiê</b>, pela mesma razão que <see cref="GeradorDeFichaEmPdf"/> não
    /// mexe: quem chama costuma estar montando o personagem inteiro para salvá-lo uma vez só.</para>
    /// </summary>
    public static ResultadoDaFolhaDeMagias Produzir(CaminhosDoProjeto caminhos, Personagem personagem)
    {
        var regras = RegrasDaFicha.CarregarOuGerar(caminhos, personagem.Sistema);

        if (regras is not { UsaMagias: true })
        {
            return Nada(SituacaoDaFolhaDeMagias.SistemaSemMagias);
        }

        if (regras.TrazMagiasPorExtenso)
        {
            return Nada(SituacaoDaFolhaDeMagias.FichaJaTemEspaco);
        }

        var magias = MagiasDoPersonagem(regras, personagem.Campos);

        if (magias.Count == 0)
        {
            return Nada(SituacaoDaFolhaDeMagias.PersonagemSemMagias);
        }

        var descritas = magias
            .Select(magia => new MagiaNaFolha(magia, Descricao(caminhos, personagem, magia)))
            .ToList();

        try
        {
            Directory.CreateDirectory(caminhos.SaidaPersonagens);

            var destino = CaminhosDoProjeto.ResolverDentroDe(
                caminhos.SaidaPersonagens,
                NomeNaSaida(caminhos, personagem));

            Desenhar(destino, personagem, descritas);

            return new ResultadoDaFolhaDeMagias(
                SituacaoDaFolhaDeMagias.Gerada,
                Path.GetRelativePath(caminhos.Raiz, destino).Replace('\\', '/'),
                magias,
                [.. descritas.Where(magia => magia.Descricao is null).Select(magia => magia.Nome)],
                null);
        }
        catch (Exception excecao) when (excecao is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new ResultadoDaFolhaDeMagias(
                SituacaoDaFolhaDeMagias.NaoDeuParaGerar, null, magias, [], excecao.Message);
        }
    }

    /// <summary>
    /// Apaga a folha de magias de um personagem que deixou de precisar dela — perdeu as magias,
    /// ou passou a usar uma ficha que as comporta.
    ///
    /// <para>Sem isto, "uma ficha por personagem" valeria para a ficha e não para a folha: o
    /// arquivo de ontem ficaria em <c>Output/</c> descrevendo magias que o personagem não tem mais,
    /// e nada na pasta diria que ele está velho.</para>
    /// </summary>
    public static void ApagarSeSobrou(CaminhosDoProjeto caminhos, Personagem personagem)
    {
        try
        {
            var caminho = CaminhosDoProjeto.ResolverDentroDe(
                caminhos.SaidaPersonagens,
                NomeNaSaida(caminhos, personagem));

            if (File.Exists(caminho))
            {
                File.Delete(caminho);
            }
        }
        catch (Exception excecao) when (excecao is IOException or UnauthorizedAccessException)
        {
            // Arquivo aberto no leitor de PDF é o caso comum. Deixá-lo para trás é melhor que
            // derrubar a geração da ficha por causa da limpeza — a mesma escolha de FichasDoPersonagem.
        }
    }

    /// <summary>Uma magia da folha: o nome como está na ficha e a descrição, quando há uma.</summary>
    private sealed record MagiaNaFolha(string Nome, string? Descricao);

    /// <summary>
    /// As magias escritas na ficha do personagem, na ordem em que aparecem e sem repetição.
    ///
    /// <para>Os campos de magia de uma ficha são listas: uma magia por linha, ou separadas por
    /// vírgula. O que não tem letra nenhuma é espaço em branco do formulário.</para>
    /// </summary>
    public static IReadOnlyList<string> MagiasDoPersonagem(
        RegrasDaFicha regras,
        IReadOnlyDictionary<string, string> campos)
    {
        var magias = new List<string>();
        var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var campo in regras.CamposDeMagia)
        {
            if (!campos.TryGetValue(campo, out var valor) || valor.Trim().Length == 0)
            {
                continue;
            }

            var itens = valor
                .ReplaceLineEndings("\n")
                .Split(['\n', ',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(item => item.Any(char.IsLetter));

            foreach (var item in itens)
            {
                if (vistas.Add(TextoNormalizado.SemAcento(item).ToLowerInvariant()))
                {
                    magias.Add(item);
                }
            }
        }

        return magias;
    }

    /// <summary>
    /// A descrição de uma magia, tirada da base de conhecimento das fontes desta mesa, ou
    /// <c>null</c> quando ela não está lá.
    ///
    /// <para>Procura o título de Markdown cujo nome é o da magia e devolve a seção inteira, até o
    /// próximo título do mesmo nível ou mais alto. O casamento é pelo <b>nome em inglês</b> quando
    /// ele existe — na ficha e na base os nomes são escritos como <c>Fireball (Bola de Fogo)</c>,
    /// e comparar a string inteira quebraria por uma tradução escrita de forma diferente nos dois
    /// lugares. Ver <see cref="NomesDeMagia"/>.</para>
    /// </summary>
    private static string? Descricao(CaminhosDoProjeto caminhos, Personagem personagem, string magia)
    {
        var procurado = ChaveDaMagia(magia);

        foreach (var arquivo in ArquivosDeMagia(caminhos, personagem))
        {
            string[] linhas;

            try
            {
                linhas = File.ReadAllLines(arquivo);
            }
            catch (IOException)
            {
                continue;
            }

            if (Secao(linhas, procurado) is { Length: > 0 } achada)
            {
                return achada;
            }
        }

        return null;
    }

    /// <summary>
    /// Os arquivos de magia das fontes que este personagem usa. Restringir às fontes é a regra 9
    /// da estrutura do projeto: uma magia de um compêndio que a mesa dispensou não pode chegar ao
    /// jogador por aqui só porque este código roda em C# e não passa pelas negações do agente.
    /// </summary>
    private static IEnumerable<string> ArquivosDeMagia(CaminhosDoProjeto caminhos, Personagem personagem)
    {
        var sistema = new SistemaRpg(personagem.Sistema);
        var raiz = sistema.DiretorioConhecimento(caminhos);

        if (!Directory.Exists(raiz))
        {
            return [];
        }

        var daMesa = sistema
            .DescobrirFontesComConhecimento(caminhos)
            .Where(fonte => fonte.EhBase || personagem.Fontes.Contains(fonte.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return daMesa
            .Select(fonte => sistema.DiretorioConhecimentoDaFonte(caminhos, fonte))
            .Where(Directory.Exists)
            .SelectMany(diretorio => Directory.EnumerateFiles(diretorio, "*.md", SearchOption.AllDirectories))
            .Where(arquivo => !Path.GetFileName(arquivo).Equals(SistemaRpg.NomeDoIndice, StringComparison.OrdinalIgnoreCase))
            // Os arquivos de magia primeiro: a descrição está neles, e um arquivo de classe que só
            // cita a magia numa tabela produziria uma "descrição" de uma linha.
            .OrderByDescending(arquivo => NomesDeMagia.EhArquivoDeMagias(
                Path.GetRelativePath(raiz, arquivo)));
    }

    private static readonly Regex Titulo = new(@"^(#{1,6})\s+(.*\S)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// A seção de um arquivo cujo título é a magia procurada: do título até o próximo do mesmo
    /// nível ou mais alto.
    /// </summary>
    private static string? Secao(string[] linhas, string procurado)
    {
        for (var indice = 0; indice < linhas.Length; indice++)
        {
            var achado = Titulo.Match(linhas[indice]);

            if (!achado.Success || ChaveDaMagia(achado.Groups[2].Value) != procurado)
            {
                continue;
            }

            var nivel = achado.Groups[1].Value.Length;
            var texto = new StringBuilder();

            for (var atual = indice + 1; atual < linhas.Length; atual++)
            {
                var seguinte = Titulo.Match(linhas[atual]);

                if (seguinte.Success && seguinte.Groups[1].Value.Length <= nivel)
                {
                    break;
                }

                texto.AppendLine(linhas[atual]);
            }

            return texto.ToString().Trim();
        }

        return null;
    }

    /// <summary>
    /// Por que chave duas escritas do mesmo nome de magia casam: o nome antes dos parênteses, sem
    /// acento, sem pontuação e em minúscula. "Fireball (Bola de Fogo)" e "Fireball" são a mesma
    /// magia; a tradução varia entre quem escreveu a base e quem preencheu a ficha.
    /// </summary>
    private static string ChaveDaMagia(string nome)
    {
        var abre = nome.IndexOf('(');
        var semTraducao = abre > 0 ? nome[..abre] : nome;

        return new string(TextoNormalizado.SemAcento(semTraducao)
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private const double Margem = 48;
    private const double LarguraDaPagina = 595;   // A4 em pontos
    private const double AlturaDaPagina = 842;

    private static void Desenhar(string destino, Personagem personagem, IReadOnlyList<MagiaNaFolha> magias)
    {
        using var documento = new PdfDocument();

        documento.Info.Title = $"Magias de {personagem.Rotulo}";
        documento.Info.Creator = "MainForge";

        var titulo = new XFont("Arial", 16, XFontStyleEx.Bold);
        var nomeDaMagia = new XFont("Arial", 12, XFontStyleEx.Bold);
        var corpo = new XFont("Arial", 9.5);
        var rodape = new XFont("Arial", 8);

        var pagina = new PaginaEmConstrucao(documento);

        pagina.Escrever($"MAGIAS DE {personagem.Rotulo.ToUpperInvariant()}", titulo, 22);
        pagina.Escrever(
            $"Sistema: {personagem.Sistema}   ·   {magias.Count} magia(s)   ·   " +
            $"gerada em {DateTime.Now:dd/MM/yyyy}",
            rodape,
            20);

        foreach (var magia in magias)
        {
            // Um nome de magia sozinho no pé da página é órfão: se não cabe ele mais uma linha do
            // texto, a magia inteira começa na página seguinte.
            pagina.ReservarOuVirar(34);
            pagina.Escrever(magia.Nome, nomeDaMagia, 15);

            var texto = magia.Descricao ?? SemDescricao;

            foreach (var linha in texto.ReplaceLineEndings("\n").Split('\n'))
            {
                pagina.EscreverParagrafo(Limpar(linha), corpo, 12);
            }

            pagina.Espaco(8);
        }

        pagina.Fechar();
        documento.Save(destino);
    }

    private const string SemDescricao =
        "(A descrição desta magia não está na base de conhecimento das fontes desta mesa. " +
        "Reprocessar o sistema no menu Sistemas costuma resolver.)";

    /// <summary>
    /// Tira a marcação de Markdown que não faz sentido num PDF: os <c>#</c> de título, os
    /// asteriscos de ênfase, os acentos graves de código. O que sobra é o texto, que é o que o
    /// jogador vai ler na mesa.
    /// </summary>
    private static string Limpar(string linha)
    {
        var texto = linha.TrimEnd();

        if (texto.StartsWith('#'))
        {
            texto = texto.TrimStart('#', ' ');
        }

        return texto.Replace("**", "").Replace("`", "").Replace("*", "");
    }

    /// <summary>
    /// A página que está sendo escrita, com o cursor vertical e a régua de quebra de linha.
    ///
    /// <para>Existe porque o PdfSharp desenha texto numa coordenada e não sabe de parágrafo, de
    /// quebra de linha nem de página cheia: sem isto, cada chamada de <c>DrawString</c> precisaria
    /// recalcular onde está e decidir se ainda cabe.</para>
    /// </summary>
    private sealed class PaginaEmConstrucao(PdfDocument documento)
    {
        private XGraphics? _grafico;
        private double _y;

        private static double Largura => LarguraDaPagina - (2 * Margem);

        private XGraphics Grafico
        {
            get
            {
                if (_grafico is null)
                {
                    var pagina = documento.AddPage();
                    pagina.Width = XUnit.FromPoint(LarguraDaPagina);
                    pagina.Height = XUnit.FromPoint(AlturaDaPagina);

                    _grafico = XGraphics.FromPdfPage(pagina);
                    _y = Margem;
                }

                return _grafico;
            }
        }

        /// <summary>Uma linha de texto, sem quebra: títulos e cabeçalhos.</summary>
        public void Escrever(string texto, XFont fonte, double alturaDaLinha)
        {
            ReservarOuVirar(alturaDaLinha);

            Grafico.DrawString(texto, fonte, XBrushes.Black, new XPoint(Margem, _y + fonte.Size));
            _y += alturaDaLinha;
        }

        /// <summary>
        /// Um parágrafo, quebrado na largura da página. Linha vazia vira espaço vertical — é o que
        /// separa os parágrafos do texto da magia.
        /// </summary>
        public void EscreverParagrafo(string texto, XFont fonte, double alturaDaLinha)
        {
            if (texto.Trim().Length == 0)
            {
                Espaco(alturaDaLinha / 2);
                return;
            }

            // Quebra tudo antes de escrever: quebrar é medir, e medir usa o gráfico da página —
            // que muda no meio da escrita quando a página vira.
            foreach (var linha in Quebrar(texto, fonte).ToList())
            {
                Escrever(linha, fonte, alturaDaLinha);
            }
        }

        public void Espaco(double pontos) => _y += pontos;

        /// <summary>
        /// Garante que ainda cabem <paramref name="pontos"/> nesta página, virando para a próxima
        /// se não couberem.
        /// </summary>
        public void ReservarOuVirar(double pontos)
        {
            if (_grafico is not null && _y + pontos > AlturaDaPagina - Margem)
            {
                Fechar();
            }
        }

        public void Fechar()
        {
            _grafico?.Dispose();
            _grafico = null;
        }

        /// <summary>
        /// Quebra o texto na largura útil, palavra a palavra. Palavra que sozinha não cabe (uma URL,
        /// um nome longo demais) vai numa linha própria e transborda: cortá-la no meio deixaria o
        /// texto ilegível, e é um caso que não acontece numa descrição de magia.
        /// </summary>
        private IEnumerable<string> Quebrar(string texto, XFont fonte)
        {
            var linha = new StringBuilder();

            foreach (var palavra in texto.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidata = linha.Length == 0 ? palavra : $"{linha} {palavra}";

                if (linha.Length > 0 && Grafico.MeasureString(candidata, fonte).Width > Largura)
                {
                    yield return linha.ToString();
                    linha.Clear().Append(palavra);
                    continue;
                }

                linha.Clear().Append(candidata);
            }

            if (linha.Length > 0)
            {
                yield return linha.ToString();
            }
        }
    }

    private static ResultadoDaFolhaDeMagias Nada(SituacaoDaFolhaDeMagias situacao) =>
        new(situacao, null, [], [], null);
}
