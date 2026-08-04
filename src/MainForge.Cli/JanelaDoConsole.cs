using System.Runtime.InteropServices;
using System.Text;

namespace MainForge.Cli;

/// <summary>
/// A janela em que o aplicativo aparece — só o estado dela —, separada do
/// <see cref="ConsoleUi"/>, que cuida do que é escrito dentro. É o único lugar do aplicativo
/// com interoperabilidade nativa do Windows.
///
/// <para>Abrir maximizado não é capricho: a tela inicial tem cerca de 145 colunas, o desenho da
/// ficha em texto tem 78, e o progresso do agente imprime chamadas de ferramenta longas. Numa
/// janela de 80x25 tudo isso quebra linha e vira ruído.</para>
///
/// <para><b>Achar a janela certa é o problema inteiro.</b> São dois mundos:</para>
///
/// <list type="number">
///   <item><b>Console clássico (conhost).</b> <c>GetConsoleWindow</c> devolve a janela que o
///   usuário vê. Simples.</item>
///   <item><b>Windows Terminal, e qualquer host via ConPTY.</b> <c>GetConsoleWindow</c> devolve
///   uma <c>PseudoConsoleWindow</c>, que é um objeto interno do <em>próprio</em> processo, sem
///   pixel nenhum na tela. Maximizá-la não muda nada e chega a travar o console — foi o que
///   aconteceu na primeira versão daqui. Pior: o terminal <b>não</b> é processo ancestral
///   nosso (o handoff de console faz dele um processo à parte), então subir a árvore de
///   processos também não chega nele.</item>
/// </list>
///
/// <para>O que liga o aplicativo à janela do terminal é o <b>título</b>: o terminal espelha no
/// título da janela o título do console da aba ativa. Então o caminho é escrever um título
/// único, procurar a janela que o exibe e maximizar aquela. Medido nesta máquina: o Windows
/// Terminal reflete o título em cerca de 15ms.</para>
///
/// <para>Consequência que vale saber: rodando o aplicativo dentro de um terminal que já estava
/// aberto com outras abas, é aquela janela inteira que é maximizada — a janela não é nossa,
/// nós só pedimos. <see cref="VariavelParaDesligar"/> desativa tudo isto.</para>
/// </summary>
internal static class JanelaDoConsole
{
    /// <summary>Defina para <c>1</c> para o aplicativo não mexer na janela.</summary>
    public const string VariavelParaDesligar = "MAINFORGE_SEM_MAXIMIZAR";

    /// <summary>Título final da janela, depois que ela foi localizada.</summary>
    public const string Titulo = "MainForge — Criação de Fichas de RPG";

    private const uint ComandoDeSistema = 0x0112;   // WM_SYSCOMMAND
    private const nint PedidoDeMaximizar = 0xF030;  // SC_MAXIMIZE
    private const int JaEstaMaximizada = 3;         // SW_SHOWMAXIMIZED

    private const string ConsoleClassico = "ConsoleWindowClass";

    /// <summary>
    /// Por quanto tempo insistir na janela maximizada depois do primeiro pedido.
    ///
    /// <para>Um disparo só não basta: o terminal ainda está se montando quando o aplicativo
    /// começa, e o tamanho de inicialização que ele aplica em seguida desfaz o nosso pedido. Era
    /// isso que fazia a janela abrir maximizada e encolher logo depois, de forma intermitente —
    /// quem chegasse por último ganhava a disputa.</para>
    ///
    /// <para>Cinco segundos porque o pior caso é o terminal subindo do zero junto com o
    /// aplicativo, quando ele leva alguns segundos para assentar o tamanho. Passado o prazo, o
    /// aplicativo não mexe mais na janela: quem quiser restaurá-la na mão manda.</para>
    /// </summary>
    private static readonly TimeSpan TempoInsistindo = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IntervaloDaInsistencia = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Quanto esperar o terminal repetir o título que acabamos de escrever. Medido em ~15ms;
    /// meio segundo é folga larga, e só é gasto por inteiro quando não há janela para achar —
    /// caso em que o aplicativo segue normalmente, só que do tamanho de sempre.
    /// </summary>
    private static readonly TimeSpan EsperaPeloTitulo = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan IntervaloEntreTentativas = TimeSpan.FromMilliseconds(25);

    public static void Maximizar()
    {
        // Uma saída para quem preferir a janela do jeito que estava — ou para quem esbarrar num
        // terminal onde isto se comporte mal, sem precisar recompilar nada.
        if (Environment.GetEnvironmentVariable(VariavelParaDesligar) is "1" or "true")
        {
            return;
        }

        // Sem janela para maximizar quando a saída está redirecionada (pipe, arquivo, teste).
        if (Console.IsOutputRedirected)
        {
            return;
        }

        var janela = Localizar();

        DefinirTitulo(Titulo);

        if (janela == IntPtr.Zero)
        {
            return;
        }

        Pedir(janela);
        InsistirEnquantoOTerminalSeMonta(janela);
    }

    /// <summary>
    /// PostMessage, e não ShowWindow: a janela pertence a outro processo (o conhost ou o
    /// terminal). Postar enfileira o pedido e volta na hora; uma chamada síncrona ficaria à
    /// mercê de um terminal ocupado, e o aplicativo travaria antes de desenhar o menu. É a mesma
    /// mensagem que o Windows manda quando se clica no botão de maximizar.
    /// </summary>
    private static void Pedir(IntPtr janela) =>
        PostMessage(janela, ComandoDeSistema, PedidoDeMaximizar, IntPtr.Zero);

    /// <summary>
    /// Repete o pedido enquanto o terminal termina de se montar, porque o tamanho de
    /// inicialização que ele aplica depois desfaz o primeiro. Numa linha de execução em segundo
    /// plano: o menu do aplicativo aparece na hora, e esta insistência acontece por trás.
    /// </summary>
    private static void InsistirEnquantoOTerminalSeMonta(IntPtr janela)
    {
        var insistencia = new Thread(() =>
        {
            var limite = DateTime.UtcNow + TempoInsistindo;

            while (DateTime.UtcNow < limite)
            {
                Thread.Sleep(IntervaloDaInsistencia);

                // Confere o título antes de insistir: se aquele identificador de janela já é de
                // outra coisa (o terminal fechou e o Windows reaproveitou o número), o pedido
                // iria para a janela de outro programa.
                if (TituloDaJanela(janela) == Titulo && !EstaMaximizada(janela))
                {
                    Pedir(janela);
                }
            }
        })
        {
            IsBackground = true, // nunca segura o encerramento do aplicativo
            Name = "maximizar-janela",
        };

        insistencia.Start();
    }

    private static bool EstaMaximizada(IntPtr janela)
    {
        var posicao = new PosicaoDaJanela { Tamanho = Marshal.SizeOf<PosicaoDaJanela>() };

        return GetWindowPlacement(janela, ref posicao) && posicao.Estado == JaEstaMaximizada;
    }

    private static IntPtr Localizar()
    {
        var doConsole = GetConsoleWindow();

        // Console clássico: o handle já é a janela de verdade, não há o que procurar.
        if (doConsole != IntPtr.Zero && ClasseDaJanela(doConsole) == ConsoleClassico)
        {
            return doConsole;
        }

        return ProcurarPeloTitulo();
    }

    /// <summary>
    /// Escreve um título único no console e procura, entre as janelas visíveis, a que passou a
    /// exibi-lo — essa é a janela do terminal que hospeda o aplicativo. O título é único para
    /// que duas instâncias abertas ao mesmo tempo não maximizem uma a janela da outra.
    /// </summary>
    private static IntPtr ProcurarPeloTitulo()
    {
        var marca = $"MainForge {Guid.NewGuid():N}";

        if (!DefinirTitulo(marca))
        {
            return IntPtr.Zero;
        }

        var limite = DateTime.UtcNow + EsperaPeloTitulo;

        while (true)
        {
            var achada = JanelaComTitulo(marca);

            if (achada != IntPtr.Zero || DateTime.UtcNow >= limite)
            {
                return achada;
            }

            Thread.Sleep(IntervaloEntreTentativas);
        }
    }

    private static IntPtr JanelaComTitulo(string titulo)
    {
        var achada = IntPtr.Zero;

        EnumWindows(
            (janela, _) =>
            {
                if (!IsWindowVisible(janela) || TituloDaJanela(janela) != titulo)
                {
                    return true;
                }

                achada = janela;

                return false; // encontrada: para de enumerar
            },
            IntPtr.Zero);

        return achada;
    }

    private static bool DefinirTitulo(string titulo)
    {
        try
        {
            Console.Title = titulo;

            return true;
        }
        catch (Exception excecao) when (excecao is IOException or PlatformNotSupportedException)
        {
            // Console sem título (serviço, sessão sem janela): não há o que localizar.
            return false;
        }
    }

    private static string ClasseDaJanela(IntPtr janela)
    {
        var texto = new StringBuilder(64);

        return GetClassName(janela, texto, texto.Capacity) > 0 ? texto.ToString() : "";
    }

    private static string TituloDaJanela(IntPtr janela)
    {
        var texto = new StringBuilder(512);

        return GetWindowText(janela, texto, texto.Capacity) > 0 ? texto.ToString() : "";
    }

    private delegate bool AoEncontrarJanela(IntPtr janela, IntPtr parametro);

    [StructLayout(LayoutKind.Sequential)]
    private struct Ponto
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Retangulo
    {
        public int Esquerda;
        public int Topo;
        public int Direita;
        public int Base_;
    }

    /// <summary>WINDOWPLACEMENT: onde a janela está e em que estado (normal, mínima, máxima).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PosicaoDaJanela
    {
        public int Tamanho;
        public int Sinalizadores;
        public int Estado;
        public Ponto Minimizada;
        public Ponto Maximizada;
        public Retangulo Normal;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(AoEncontrarJanela retorno, IntPtr parametro);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr janela);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr janela, StringBuilder classe, int tamanho);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr janela, StringBuilder titulo, int tamanho);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr janela, uint mensagem, nint parametro, IntPtr complemento);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr janela, ref PosicaoDaJanela posicao);
}
