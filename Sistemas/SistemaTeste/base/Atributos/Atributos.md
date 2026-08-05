# Atributos

Todo personagem do Sistema Teste tem **exatamente três atributos**:

- **Forca**
- **Destreza**
- **Constituicao**

Não existem outros atributos neste sistema.

## Valor base

Cada um dos três atributos **começa em 10**.

## Pontos de distribuição

O jogador recebe **3 pontos extras** para distribuir entre os três atributos,
como preferir. O livro é explícito: **pode colocar os 3 pontos em um só atributo**.

Regras da distribuição:

- São exatamente 3 pontos — não mais, não menos. Todos devem ser gastos.
- Cada ponto vale +1 no atributo escolhido.
- Não há limite máximo por atributo além dos próprios 3 pontos disponíveis.
- Distribuições válidas incluem, por exemplo: 3/0/0, 2/1/0, 1/1/1, 0/0/3.

## Bônus de classe

Depois dos pontos livres, aplica-se o **+2 do atributo da classe escolhida**:

| Classe | Atributo que recebe +2 |
| --- | --- |
| Guerreiro | Forca |
| Batedor | Destreza |

## Fórmula do valor final

```
AtributoFinal = 10 + pontos livres alocados nele + (2 se for o atributo da classe)
```

## Verificação de soma

A soma dos três atributos finais é **sempre 35**:

```
30 (base 10 x 3) + 3 (pontos livres) + 2 (bônus de classe) = 35
```

Se a soma der diferente de 35, a ficha está errada.

## Exemplos

**Guerreiro que pôs 2 pontos em Constituicao e 1 em Forca:**
Forca = 10 + 1 + 2 = 13 · Destreza = 10 · Constituicao = 12 → soma 35. Válido.

**Batedor que pôs os 3 pontos em Destreza:**
Forca = 10 · Destreza = 10 + 3 + 2 = 15 · Constituicao = 10 → soma 35. Válido.

## Não definido pelo livro

O livro não define modificadores derivados de atributo (do tipo "(valor-10)/2"),
nem testes de atributo. Os atributos são usados apenas como valores anotados na
ficha e como entrada da fórmula de Pontos de Vida.
