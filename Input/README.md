# Input

Os livros oficiais (PDF) de cada sistema de RPG, um subdiretório por sistema e, **dentro dele,
um subdiretório por fonte**:

```
Input/
    Aventura&Cia/
        base/               <- o jogo base; toda mesa usa
            Livro-Base.pdf
            Regras-Gerais.pdf
            _texto/         <- gerado: os mesmos livros em Markdown
                Livro-Base.md
                Regras-Gerais.md
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

A divisão se repete em `Sistemas/<Sistema>/`: o conteúdo destilado dos livros de uma fonte vai
para a pasta de mesmo nome.

## Como os arquivos chegam aqui

Pelo menu do aplicativo, não na mão:

- **Importar um sistema** cria o sistema e põe os livros em `base/`.
- **Adicionar livro a um sistema** pergunta se o livro é do jogo base ou de uma expansão (e, se
  for de uma expansão nova, qual o nome dela).

Um sistema importado por uma versão anterior do aplicativo tem os PDFs soltos na raiz; a tela de
sistemas oferece movê-los para `base/`. É só mover arquivo — nenhum livro é relido.

O Agente Configurador lê esta pasta e gera a estrutura correspondente em `Sistemas/<Sistema>/`.
Ele nunca modifica os PDFs originais.

## A pasta `_texto/`

Antes de cada processamento, o aplicativo converte os PDFs daquela fonte para Markdown e grava o
resultado em `_texto/`. É o que o Configurador lê: o mesmo conteúdo por uma fração da cota da
assinatura, porque ler uma página de PDF custa uma imagem e ler texto custa texto.

Converte quem estiver disponível: o [markitdown](https://github.com/microsoft/markitdown), se
instalado, ou o extrator interno do aplicativo, que não exige instalação nenhuma.

É conteúdo **derivado** — daí o underscore, como no `_estado-do-processamento.json`. Não edite e
não se preocupe em versioná-lo: apagar um `.md` de lá só faz o próximo processamento convertê-lo
de novo, e trocar o PDF já obriga a isso sozinho. Se você instalar o markitdown depois, apague a
pasta para os livros serem reconvertidos por ele.
