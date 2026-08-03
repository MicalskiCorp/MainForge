# Mapeamento da Ficha — Sistema Teste

Modelo: `Templates/SistemaTeste/Ficha.pdf` — **8 campos preenchíveis**.

Os nomes abaixo são exatamente as strings retornadas pela ferramenta de listagem
de campos e são as que devem ser passadas para `preencher_ficha_personagem`.
Nenhum campo do PDF ficou de fora desta tabela.

| Campo no PDF | O que vai nele | Formato | Observações |
| --- | --- | --- | --- |
| `Nome` | Nome do personagem, escolhido livremente pelo jogador | Texto livre, uma linha | Sem regra no livro; não há tabela de nomes nem restrição. Nunca deixar em branco — perguntar ao jogador. |
| `Classe` | Classe escolhida | Texto, uma linha | Só dois valores válidos: `Guerreiro` ou `Batedor`. Escrever exatamente assim, com inicial maiúscula. |
| `Nivel` | Nível do personagem | Número puro, sem sinal | Sempre `1` na criação. O livro não define progressão. |
| `Forca` | Valor final de Forca | Número puro (ex.: `13`), sem sinal e sem "+" | `Forca = 10 + pontos livres em Forca + (2 se Classe = Guerreiro)`. Anotar o valor total, nunca só o bônus. |
| `Destreza` | Valor final de Destreza | Número puro (ex.: `15`), sem sinal | `Destreza = 10 + pontos livres em Destreza + (2 se Classe = Batedor)`. |
| `Constituicao` | Valor final de Constituicao | Número puro (ex.: `12`), sem sinal | `Constituicao = 10 + pontos livres em Constituicao`. Nenhuma classe dá bônus a este atributo. |
| `PontosDeVida` | Pontos de Vida iniciais | Número puro (ex.: `23`) | Guerreiro: `PontosDeVida = 10 + Constituicao`. Batedor: `PontosDeVida = 8 + Constituicao`. Usar a Constituicao **final** já anotada no campo acima. |
| `Equipamento` | Equipamento inicial completo | Lista separada por vírgula, em uma linha | Guerreiro: `Espada Longa, Mochila, Tocha`. Batedor: `Arco Curto, Mochila, Tocha`. Item da classe primeiro. |

## Campos do PDF sem correspondência nas regras

Nenhum. Os 8 campos preenchíveis têm origem definida nas regras, com uma
ressalva: `Nome` não é derivado de regra alguma — é entrada livre do jogador.

## Conceitos das regras sem campo na ficha

A ficha **não tem** campo para registrar como os 3 pontos livres foram
distribuídos (só o resultado final aparece), nem para o bônus de +2 da classe.
Esses valores já entram embutidos nos campos `Forca`, `Destreza` e
`Constituicao`. Não crie campos novos para eles.

## Checagem antes de preencher o PDF

1. `Classe` é `Guerreiro` ou `Batedor`.
2. `Nivel` é `1`.
3. `Forca + Destreza + Constituicao` = **35**.
4. O atributo bonificado bate com a classe (Guerreiro → Forca; Batedor → Destreza).
5. `PontosDeVida` bate com a fórmula da classe.
6. `Equipamento` contém o item correto da classe + Mochila + Tocha.
7. `Nome` está preenchido.
