namespace MainForge.Core;

/// <summary>
/// Quanto o usuário quer gastar da cota da assinatura para ter quanta qualidade.
///
/// <para><b>Por que isto é uma escolha, e não uma constante.</b> O aplicativo rodava tudo no
/// melhor modelo disponível, com o esforço de raciocínio que viesse por padrão. Isso é a resposta
/// certa para uma parte do trabalho e cara demais para a outra — e quem sabe onde está o limite é
/// quem paga a assinatura, não este código.</para>
///
/// <para><b>Por que mora no Core.</b> É preferência do usuário, gravada em disco pelo
/// <c>MainForge.Tools</c> e traduzida em modelo e esforço pelo <c>MainForge.ClaudeCode</c>. Os
/// dois lados só se encontram aqui.</para>
/// </summary>
public enum PerfilDeExecucao
{
    /// <summary>O mínimo que dá conta. Serve para provar um sistema novo antes de investir nele.</summary>
    Economico,

    /// <summary>O padrão: modelo à altura de cada trabalho, sem pagar Opus para copiar tabela.</summary>
    Equilibrado,

    /// <summary>O melhor modelo em tudo, com raciocínio longo. É o comportamento antigo.</summary>
    Qualidade,
}
