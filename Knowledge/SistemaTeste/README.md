# Sistema Teste — Base de Conhecimento

Sistema de RPG minimalista. Um personagem é definido por **nome**, **classe**,
**nível**, **três atributos** (Forca, Destreza, Constituicao), **Pontos de Vida**
e **equipamento**. Não existem raças, antecedentes, perícias, idiomas, magias ou
talentos neste sistema — se o jogador pedir algo assim, a resposta é que o Sistema
Teste não cobre esse conceito.

## Fluxo de criação de personagem

Ordem oficial do livro (seção 5 — Resumo do Processo de Criação):

1. **Escolher a classe** — Guerreiro ou Batedor. Ver `Classes/`.
2. **Distribuir os 3 pontos extras** entre Forca, Destreza e Constituicao.
   Todos os atributos começam em 10. Ver `Atributos/Atributos.md`.
3. **Aplicar o bônus de +2 da classe** ao atributo correspondente.
4. **Calcular os Pontos de Vida** pela fórmula da classe escolhida.
5. **Anotar o equipamento inicial** (item da classe + Mochila + Tocha).
   Ver `Equipamento/Equipamento-Inicial.md`.

O nome do personagem é livre e não tem regra associada; peça-o ao jogador.
O nível inicial é sempre 1 (ver `Progressao/Niveis.md`).

## Ordem de cálculo (importante)

Os 3 pontos livres e o +2 de classe são somados **sobre a base 10**, e a soma dos
atributos finais deve sempre fechar em **35** (10+10+10 = 30, +3 livres, +2 da
classe). Use isso como verificação rápida ao validar uma ficha.

Os Pontos de Vida usam a Constituicao **final** (depois de pontos livres e de
qualquer bônus de classe aplicado a ela).

## Índice dos arquivos

| Arquivo | Conteúdo |
| --- | --- |
| `Atributos/Atributos.md` | Os três atributos, base 10, distribuição dos 3 pontos |
| `Classes/Guerreiro.md` | Bônus, equipamento e fórmula de PV do Guerreiro |
| `Classes/Batedor.md` | Bônus, equipamento e fórmula de PV do Batedor |
| `Equipamento/Equipamento-Inicial.md` | Itens iniciais de todo personagem |
| `Progressao/Niveis.md` | Nível inicial e o que o livro não define |
| `Ficha-Mapeamento.md` | Cada campo do PDF da ficha e o que vai nele |
| `Ficha-ModeloEmTexto.md` | Desenho em ASCII da ficha, para mostrar ao jogador |

## Lacunas conhecidas do livro

O livro básico **não** define: regras de teste/resolução de ações, combate,
iniciativa, dano das armas, evolução acima do nível 1, experiência, magia,
perícias, raças ou antecedentes. Nada disso deve ser inventado ao validar uma
ficha.
