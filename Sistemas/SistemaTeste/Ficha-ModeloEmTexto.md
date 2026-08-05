# Modelo da Ficha em Texto — Sistema Teste

Reproduz o leiaute de `Templates/SistemaTeste/Ficha.pdf` na mesma ordem de blocos:
título, Nome, Classe/Nivel, os três atributos, Pontos de Vida e Equipamento.

Substitua cada marcador `{{Campo}}` pelo valor correspondente antes de mostrar ao
jogador. Os nomes dos marcadores são idênticos aos campos do PDF.

```
+--------------------------------------------------------------------------+
|  FICHA DE PERSONAGEM - SISTEMA TESTE                                     |
+--------------------------------------------------------------------------+
|  Nome: {{Nome}}                                                          |
|                                                                          |
|  Classe: {{Classe}}                         Nivel: {{Nivel}}             |
+--------------------------------------------------------------------------+
|  ATRIBUTOS                                                               |
|                                                                          |
|    Forca .......... {{Forca}}                                            |
|    Destreza ....... {{Destreza}}                                         |
|    Constituicao ... {{Constituicao}}                                     |
+--------------------------------------------------------------------------+
|  Pontos de Vida: {{PontosDeVida}}                                        |
+--------------------------------------------------------------------------+
|  EQUIPAMENTO                                                             |
|                                                                          |
|    {{Equipamento}}                                                       |
|                                                                          |
|                                                                          |
+--------------------------------------------------------------------------+
```

## Exemplo preenchido

```
+--------------------------------------------------------------------------+
|  FICHA DE PERSONAGEM - SISTEMA TESTE                                     |
+--------------------------------------------------------------------------+
|  Nome: Bran de Pedravale                                                 |
|                                                                          |
|  Classe: Guerreiro                          Nivel: 1                     |
+--------------------------------------------------------------------------+
|  ATRIBUTOS                                                               |
|                                                                          |
|    Forca .......... 12                                                   |
|    Destreza ....... 10                                                   |
|    Constituicao ... 13                                                   |
+--------------------------------------------------------------------------+
|  Pontos de Vida: 23                                                      |
+--------------------------------------------------------------------------+
|  EQUIPAMENTO                                                             |
|                                                                          |
|    Espada Longa, Mochila, Tocha                                          |
|                                                                          |
|                                                                          |
+--------------------------------------------------------------------------+
```

Observações de uso:

- Somente ASCII, largura de 76 colunas — cabe em terminal padrão.
- Se o valor substituído for mais longo que o espaço, a borda direita daquela
  linha desalinha; isso é aceitável e não deve ser corrigido cortando o valor.
