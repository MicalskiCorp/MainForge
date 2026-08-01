namespace MainForge.Tools;

/// <summary>
/// Falha que deve ser <em>devolvida ao modelo</em> em vez de derrubar a operação: campo
/// obrigatório ausente, arquivo inexistente, campo de formulário que não existe na ficha. O
/// agente lê a mensagem e corrige a chamada sozinho, então a mensagem é escrita para ele —
/// direta, concreta e dizendo qual é a alternativa válida.
/// </summary>
public sealed class ErroDeFerramenta : Exception
{
    public ErroDeFerramenta(string mensagem) : base(mensagem)
    {
    }

    public ErroDeFerramenta(string mensagem, Exception interna) : base(mensagem, interna)
    {
    }
}
