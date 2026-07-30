using Anthropic.Helpers.Beta;
using RpgForge.Core;
using RpgForge.Tools;

namespace RpgForge.Agents;

/// <summary>
/// Um agente nomeado: seu prompt de sistema (carregado de Agents/*.md) mais o conjunto
/// exato de ferramentas que ele pode chamar. É aqui que os guardrails de segurança da
/// especificação do produto são aplicados em código — um agente nunca recebe uma
/// ferramenta fora da sua lista, não importa o que o modelo peça.
/// </summary>
public sealed record DefinicaoDeAgente(string Nome, string NomeArquivoPrompt, IReadOnlyList<string> FerramentasPermitidas)
{
    public string CarregarPromptDeSistema(CaminhosDoProjeto caminhos)
    {
        var caminho = Path.Combine(caminhos.Agentes, NomeArquivoPrompt);

        if (!File.Exists(caminho))
        {
            throw new FileNotFoundException(
                $"Prompt do agente '{Nome}' não encontrado em '{caminho}'.", caminho);
        }

        return File.ReadAllText(caminho);
    }

    /// <summary>
    /// Resolve as instâncias de ferramenta correspondentes a <see cref="FerramentasPermitidas"/>
    /// a partir do catálogo completo em <see cref="RegistroDeFerramentas"/>. É aqui que o
    /// allowlist de cada agente vira, de fato, a lista de ferramentas que o
    /// <c>BetaToolRunner</c> recebe — um agente nunca vê a definição de uma ferramenta fora
    /// da sua lista, então o modelo não pode nem tentar chamá-la.
    /// </summary>
    public IReadOnlyList<IBetaRunnableTool> ResolverFerramentas(CaminhosDoProjeto caminhos)
    {
        var catalogo = RegistroDeFerramentas.CriarTodas(caminhos);

        return FerramentasPermitidas
            .Select(nome => catalogo.TryGetValue(nome, out var ferramenta)
                ? ferramenta
                : throw new InvalidOperationException(
                    $"Ferramenta '{nome}' listada em FerramentasPermitidas de '{Nome}' não está registrada em RegistroDeFerramentas."))
            .ToList();
    }

    /// <summary>
    /// Só pode tocar em Systems/, Templates/ e Knowledge/ (escrita limitada a Knowledge/).
    /// Nunca conversa com o usuário final.
    /// </summary>
    public static readonly DefinicaoDeAgente Configurador = new(
        Nome: "Configurador",
        NomeArquivoPrompt: "Configurador.md",
        FerramentasPermitidas:
        [
            "listar_sistemas",
            "ler_pdf_do_sistema",
            "escrever_arquivo_conhecimento",
            "listar_conhecimento",
        ]);

    /// <summary>
    /// Só pode ler Knowledge/ e Templates/, e escrever em Output/Personagens/. Nunca lê os
    /// PDFs originais e nunca modifica Knowledge/.
    /// </summary>
    public static readonly DefinicaoDeAgente DungeonMaster = new(
        Nome: "DungeonMaster",
        NomeArquivoPrompt: "DungeonMaster.md",
        FerramentasPermitidas:
        [
            "listar_sistemas_prontos",
            "ler_arquivo_conhecimento",
            "listar_conhecimento",
            "preencher_ficha_personagem",
        ]);
}
