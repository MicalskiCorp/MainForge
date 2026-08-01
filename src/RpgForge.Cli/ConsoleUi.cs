using System.Text;

namespace RpgForge.Cli;

/// <summary>
/// Entrada e saída no console num único lugar: cores, títulos, leitura de opção de menu e
/// leitura mascarada de segredo. O resto do aplicativo não chama <see cref="Console"/>
/// diretamente, para que a aparência (e um eventual troca por outra interface) fique contida
/// aqui.
/// </summary>
internal static class ConsoleUi
{
    public static void Preparar()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
    }

    public static void Titulo(string texto)
    {
        Console.WriteLine();
        EscreverColorido($"=== {texto}", ConsoleColor.Cyan);
    }

    public static void Info(string texto) => Console.WriteLine(texto);

    public static void Detalhe(string texto) => EscreverColorido(texto, ConsoleColor.DarkGray);

    public static void Sucesso(string texto) => EscreverColorido(texto, ConsoleColor.Green);

    public static void Aviso(string texto) => EscreverColorido(texto, ConsoleColor.Yellow);

    public static void Erro(string texto) => EscreverColorido(texto, ConsoleColor.Red);

    public static void EscreverColorido(string texto, ConsoleColor cor)
    {
        var anterior = Console.ForegroundColor;
        Console.ForegroundColor = cor;
        Console.WriteLine(texto);
        Console.ForegroundColor = anterior;
    }

    public static string LerLinha(string rotulo)
    {
        Console.Write(rotulo);
        return (Console.ReadLine() ?? string.Empty).Trim();
    }

    /// <summary>
    /// Lê um valor sensível (a API key) sem ecoar os caracteres digitados. Cai para
    /// <see cref="Console.ReadLine"/> quando a entrada está redirecionada (pipe, arquivo),
    /// caso em que <see cref="Console.ReadKey"/> não funciona.
    /// </summary>
    public static string LerSegredo(string rotulo)
    {
        Console.Write(rotulo);

        if (Console.IsInputRedirected)
        {
            return (Console.ReadLine() ?? string.Empty).Trim();
        }

        var digitado = new StringBuilder();

        while (true)
        {
            var tecla = Console.ReadKey(intercept: true);

            switch (tecla.Key)
            {
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    return digitado.ToString().Trim();

                case ConsoleKey.Escape:
                    Console.WriteLine();
                    return string.Empty;

                case ConsoleKey.Backspace when digitado.Length > 0:
                    digitado.Length--;
                    Console.Write("\b \b");
                    break;

                default:
                    if (!char.IsControl(tecla.KeyChar))
                    {
                        digitado.Append(tecla.KeyChar);
                        Console.Write('*');
                    }

                    break;
            }
        }
    }

    public static bool Confirmar(string pergunta)
    {
        while (true)
        {
            var resposta = LerLinha($"{pergunta} (s/n): ").ToLowerInvariant();

            switch (resposta)
            {
                case "s" or "sim":
                    return true;
                case "n" or "nao" or "não":
                    return false;
                default:
                    Aviso("Responda 's' ou 'n'.");
                    break;
            }
        }
    }

    /// <summary>
    /// Mostra uma lista numerada e devolve o item escolhido, ou <c>null</c> se o usuário
    /// cancelar com 0 (ou Enter vazio).
    /// </summary>
    public static T? Escolher<T>(string titulo, IReadOnlyList<T> itens, Func<T, string> rotulo) where T : class
    {
        Titulo(titulo);

        for (var indice = 0; indice < itens.Count; indice++)
        {
            Console.WriteLine($"  {indice + 1}) {rotulo(itens[indice])}");
        }

        Console.WriteLine("  0) Voltar");

        while (true)
        {
            var digitado = LerLinha("\nEscolha: ");

            if (digitado.Length == 0 || digitado == "0")
            {
                return null;
            }

            if (int.TryParse(digitado, out var escolha) && escolha >= 1 && escolha <= itens.Count)
            {
                return itens[escolha - 1];
            }

            Aviso($"Digite um número entre 0 e {itens.Count}.");
        }
    }

    public static void Pausar()
    {
        Console.WriteLine();
        Detalhe("Pressione Enter para voltar ao menu...");
        Console.ReadLine();
    }
}
