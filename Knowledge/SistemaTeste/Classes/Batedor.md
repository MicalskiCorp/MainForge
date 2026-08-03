# Classe: Batedor

Uma das **duas** classes disponíveis no Sistema Teste (a outra é o Guerreiro).
Todo personagem escolhe exatamente uma classe.

## Bônus de atributo

**+2 em Destreza.**

Aplicado depois da distribuição dos 3 pontos livres, sobre a base 10.

## Pontos de Vida

```
PontosDeVida = 8 + Constituicao
```

Usa-se a **Constituicao final** do personagem (base 10 + pontos livres alocados
em Constituicao; o Batedor não dá bônus de classe a Constituicao).

Faixa possível no nível 1: de **18** (Constituicao 10) a **21** (Constituicao 13,
com os 3 pontos livres todos em Constituicao).

Atenção: a fórmula do Batedor usa **8**, não 10 — é a única diferença numérica
de PV em relação ao Guerreiro.

## Equipamento inicial

- **Arco Curto** (item da classe)
- Mochila (item comum a todos)
- Tocha (item comum a todos)

Ver `Equipamento/Equipamento-Inicial.md`.

## Exemplo completo

Batedor com 2 pontos livres em Destreza e 1 em Constituicao:

- Forca = 10 + 0 = **10**
- Destreza = 10 + 2 + 2 = **14**
- Constituicao = 10 + 1 = **11**
- PontosDeVida = 8 + 11 = **19**
- Equipamento = Arco Curto, Mochila, Tocha

## Não definido pelo livro

O livro não define dano do Arco Curto, munição/flechas, habilidades especiais do
Batedor, nem progressão além do nível 1.
