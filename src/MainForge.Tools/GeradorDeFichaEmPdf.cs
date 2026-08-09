using MainForge.Core;

namespace MainForge.Tools;

/// <summary>Em que pé ficou a ficha em PDF de um personagem depois de conferida.</summary>
public enum SituacaoDaFichaEmPdf
{
    /// <summary>O PDF que o dossiê aponta está lá — nada foi refeito.</summary>
    JaExistia,

    /// <summary>Não havia PDF (ou ele sumiu de <c>Output/</c>) e um novo foi gerado.</summary>
    Gerada,

    /// <summary>O dossiê não tem valores de campo: não há o que escrever na ficha em branco.</summary>
    SemCampos,

    /// <summary>Faltou o que gerar a partir de — ficha em branco ausente, ilegível ou incompatível.</summary>
    NaoDeuParaGerar,
}

/// <summary>O que a conferência da ficha produziu, para a interface poder contar ao usuário.</summary>
/// <param name="Situacao">O que aconteceu.</param>
/// <param name="Caminho">O PDF do personagem, relativo à raiz do projeto, quando há um.</param>
/// <param name="CamposIgnorados">
/// Campos guardados no dossiê que não existem na ficha em branco do sistema. Ficam de fora do PDF
/// em vez de reprovarem a geração inteira — é a mesma regra que a importação já aplica.
/// </param>
/// <param name="Motivo">Por que não deu, quando não deu.</param>
public sealed record ResultadoDaFichaEmPdf(
    SituacaoDaFichaEmPdf Situacao,
    string? Caminho,
    IReadOnlyList<string> CamposIgnorados,
    string? Motivo);

/// <summary>
/// Garante que o personagem tem, em <c>Output/Personagens/</c>, a ficha em PDF correspondente ao
/// que está guardado no dossiê dele — gerando-a a partir da ficha em branco do sistema quando
/// ela não estiver lá.
///
/// <para><b>Por que existe.</b> A ficha em PDF só nascia dentro de uma conversa, por
/// <c>preencher_ficha_personagem</c>. Bastava o usuário apagar o arquivo, mover a pasta ou
/// receber o personagem por um caminho que não passa pelo agente para o dossiê apontar para um
/// PDF que não existe — e a única forma de tê-lo de volta era abrir uma conversa e pagar cota
/// para o agente reescrever campo por campo valores que já estavam gravados aqui.</para>
///
/// <para><b>Não decide nada sobre o personagem.</b> Preenche a ficha com o que o dossiê já diz,
/// exatamente como o agente a deixou. Nenhuma regra de sistema é aplicada aqui: se o valor
/// guardado está errado, o PDF sai errado igual — corrigi-lo é conversa com o Dungeon Master.</para>
/// </summary>
public static class GeradorDeFichaEmPdf
{
    /// <summary>
    /// Confere se a ficha do personagem está em <c>Output/</c> e a gera se não estiver,
    /// registrando-a no dossiê como qualquer ficha gerada — inclusive no histórico por nível.
    ///
    /// <para>O personagem em memória <b>não</b> é atualizado: quem grava é
    /// <see cref="RepositorioDePersonagens.RegistrarFichaGerada"/>, que releva o dossiê do disco.
    /// Quem chamou precisa recarregá-lo se for continuar usando-o.</para>
    /// </summary>
    public static ResultadoDaFichaEmPdf Garantir(CaminhosDoProjeto caminhos, Personagem personagem)
    {
        if (FichaNoDisco(caminhos, personagem) is { } existente)
        {
            return new ResultadoDaFichaEmPdf(SituacaoDaFichaEmPdf.JaExistia, existente, [], null);
        }

        var produzida = Produzir(caminhos, personagem);

        if (produzida is not { Situacao: SituacaoDaFichaEmPdf.Gerada, Caminho: { } gerada })
        {
            return produzida;
        }

        // Os campos vão inteiros, e não só os que couberam na ficha em branco: o dossiê é o
        // estado do personagem, e podar dele o que este template não conhece apagaria o valor
        // que o próximo (o do sistema corrigido, o da edição certa) saberia escrever.
        RepositorioDePersonagens.RegistrarFichaGerada(
            caminhos,
            personagem.Sistema,
            personagem.Id,
            gerada,
            personagem.Campos,
            NivelDaFicha(personagem));

        return produzida;
    }

    /// <summary>
    /// Preenche a ficha em branco do sistema com os campos do dossiê e grava o PDF em
    /// <c>Output/Personagens/</c>, com o nome que aquele personagem tem lá. <b>Não mexe no
    /// dossiê</b> — é o que permite à importação montar o personagem inteiro e salvá-lo uma vez só.
    /// </summary>
    public static ResultadoDaFichaEmPdf Produzir(CaminhosDoProjeto caminhos, Personagem personagem)
    {
        if (personagem.Campos.Count == 0)
        {
            return new ResultadoDaFichaEmPdf(
                SituacaoDaFichaEmPdf.SemCampos,
                null,
                [],
                "O dossiê deste personagem ainda não tem valores de campo para escrever na ficha.");
        }

        try
        {
            var doModelo = PreenchedorDeFicha
                .ListarCampos(caminhos, personagem.Sistema, null)
                .ToHashSet(StringComparer.Ordinal);

            var aproveitados = personagem.Campos
                .Where(par => doModelo.Contains(par.Key))
                .ToDictionary(StringComparer.Ordinal);

            var ignorados = personagem.Campos.Keys
                .Where(campo => !doModelo.Contains(campo))
                .OrderBy(campo => campo, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (aproveitados.Count == 0)
            {
                return new ResultadoDaFichaEmPdf(
                    SituacaoDaFichaEmPdf.NaoDeuParaGerar,
                    null,
                    ignorados,
                    $"Nenhum dos {personagem.Campos.Count} campo(s) guardados existe na ficha em branco " +
                    $"de {personagem.Sistema} — ela deve ser de outra edição ou de uma versão adaptada.");
            }

            var gerada = PreenchedorDeFicha.Preencher(
                caminhos,
                personagem.Sistema,
                null,
                aproveitados,
                FichasDoPersonagem.NomeNaSaida(caminhos, personagem));

            return new ResultadoDaFichaEmPdf(
                SituacaoDaFichaEmPdf.Gerada,
                gerada.Replace('\\', '/'),
                ignorados,
                null);
        }
        catch (Exception excecao) when (excecao is ErroDeFerramenta or IOException or UnauthorizedAccessException)
        {
            return new ResultadoDaFichaEmPdf(SituacaoDaFichaEmPdf.NaoDeuParaGerar, null, [], excecao.Message);
        }
    }

    /// <summary>
    /// O PDF que o dossiê aponta, se ele ainda estiver lá. Caminho gravado apontando para arquivo
    /// que não existe é o caso que motiva esta classe — o dossiê sobrevive a
    /// <c>Output/</c> ser esvaziada, e não o contrário.
    /// </summary>
    private static string? FichaNoDisco(CaminhosDoProjeto caminhos, Personagem personagem)
    {
        if (personagem.FichaGerada is not { Length: > 0 } registrada)
        {
            return null;
        }

        try
        {
            return File.Exists(CaminhosDoProjeto.ResolverDentroDe(caminhos.Raiz, registrada))
                ? registrada
                : null;
        }
        catch (Exception excecao) when (excecao is UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// A que nível esta cópia pertence no histórico. Quem responde primeiro são os campos — eles
    /// <em>são</em> a ficha que está sendo gerada. O histórico entra quando eles não dizem: a
    /// ficha refeita é a do estado atual do personagem, que é o último nível já registrado, e
    /// deixá-la cair no lugar reservado a "nível não informado" apagaria aquele registro.
    /// </summary>
    private static int? NivelDaFicha(Personagem personagem) =>
        FichasDoPersonagem.DeduzirNivel(personagem.Campos)
        ?? (personagem.Fichas.Count > 0 ? personagem.Fichas[^1].Nivel : null);
}
