using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace MainForge.Claude;

/// <summary>
/// Guarda a API key da Anthropic no perfil do usuário, cifrada com a DPAPI do Windows no
/// escopo <see cref="DataProtectionScope.CurrentUser"/> — só a mesma conta de usuário do
/// mesmo Windows consegue decifrar o arquivo. A chave nunca aparece no código-fonte, nunca é
/// commitada e nunca fica em texto puro no disco: o usuário a informa dentro do aplicativo,
/// no momento de usar.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ArmazenamentoDeChaveApi
{
    /// <summary>
    /// Rótulo misturado à cifragem (entropia adicional da DPAPI). Não é um segredo — só
    /// garante que um blob cifrado por outro aplicativo do mesmo usuário não seja aceito aqui.
    /// </summary>
    private static readonly byte[] EntropiaDoAplicativo = Encoding.UTF8.GetBytes("MainForge.ChaveApi.v1");

    public string CaminhoDoArquivo { get; }

    /// <param name="caminhoDoArquivo">
    /// Onde gravar o arquivo cifrado. Se omitido, usa
    /// <c>%APPDATA%\MainForge\chave-api.dat</c>. O parâmetro existe para os testes poderem
    /// usar um diretório temporário em vez do perfil real do usuário.
    /// </param>
    public ArmazenamentoDeChaveApi(string? caminhoDoArquivo = null)
    {
        CaminhoDoArquivo = caminhoDoArquivo ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MainForge",
            "chave-api.dat");
    }

    public bool Existe => File.Exists(CaminhoDoArquivo);

    /// <summary>
    /// Devolve a chave guardada, ou <c>null</c> se não houver nenhuma. Devolve <c>null</c>
    /// também quando o arquivo existe mas não pode ser decifrado (perfil de usuário diferente,
    /// máquina diferente, arquivo corrompido) — nesse caso o app deve simplesmente pedir a
    /// chave de novo, em vez de estourar uma exceção de criptografia na cara do usuário.
    /// </summary>
    public string? Ler()
    {
        if (!Existe)
        {
            return null;
        }

        try
        {
            var cifrado = File.ReadAllBytes(CaminhoDoArquivo);
            var decifrado = ProtectedData.Unprotect(cifrado, EntropiaDoAplicativo, DataProtectionScope.CurrentUser);
            var chave = Encoding.UTF8.GetString(decifrado);

            return string.IsNullOrWhiteSpace(chave) ? null : chave;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void Salvar(string chaveApi)
    {
        if (string.IsNullOrWhiteSpace(chaveApi))
        {
            throw new ArgumentException("A chave da API não pode ser vazia.", nameof(chaveApi));
        }

        var diretorio = Path.GetDirectoryName(CaminhoDoArquivo);

        if (!string.IsNullOrEmpty(diretorio))
        {
            Directory.CreateDirectory(diretorio);
        }

        var cifrado = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(chaveApi.Trim()),
            EntropiaDoAplicativo,
            DataProtectionScope.CurrentUser);

        File.WriteAllBytes(CaminhoDoArquivo, cifrado);
    }

    public void Apagar()
    {
        if (Existe)
        {
            File.Delete(CaminhoDoArquivo);
        }
    }
}
