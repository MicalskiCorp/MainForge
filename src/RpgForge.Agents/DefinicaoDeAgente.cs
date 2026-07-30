using RpgForge.Core;

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
