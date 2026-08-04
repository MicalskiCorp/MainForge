# Systems

Os livros oficiais (PDF) de cada sistema de RPG, um subdiretório por sistema e, **dentro dele,
um subdiretório por fonte**:

```
Systems/
    Aventura&Cia/
        base/               <- o jogo base; toda mesa usa
            Livro-Base.pdf
            Regras-Gerais.pdf
        Compendio-Arcano/           <- uma expansão; a mesa escolhe se usa
            Compendio-Arcano.pdf
    Cronicas-do-Norte/
        base/
            CoreRulebook.pdf
```

`base/` é o nome fixo do jogo base. Cada outra pasta é uma expansão (compêndio, suplemento) e
leva o nome que o usuário deu a ela na hora de importar.

## Por que separado

Na criação de personagem, o aplicativo pergunta quais expansões aquela mesa usa e **nega ao
Dungeon Master a leitura das que ficaram de fora**. Essa negação é por pasta, então a divisão
aqui é o que faz a escolha valer: um compêndio guardado dentro de `base/` viraria regra
obrigatória em toda mesa, sem como recusá-lo.

A divisão se repete em `Knowledge/<Sistema>/`: o conteúdo destilado dos livros de uma fonte vai
para a pasta de mesmo nome.

## Como os arquivos chegam aqui

Pelo menu do aplicativo, não na mão:

- **Importar um sistema** cria o sistema e põe os livros em `base/`.
- **Adicionar livro a um sistema** pergunta se o livro é do jogo base ou de uma expansão (e, se
  for de uma expansão nova, qual o nome dela).

Um sistema importado por uma versão anterior do aplicativo tem os PDFs soltos na raiz; a tela de
sistemas oferece movê-los para `base/`. É só mover arquivo — nenhum livro é relido.

O Agente Configurador lê esta pasta e gera a estrutura correspondente em `Knowledge/<Sistema>/`.
Ele nunca modifica os PDFs originais.
