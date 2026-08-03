# Knowledge

Base de conhecimento gerada automaticamente pelo Agente Configurador a partir dos livros em
`Systems/`. Cada sistema tem sua própria subpasta, com uma estrutura de diretórios em
Markdown que reflete o fluxo de criação de personagens daquele sistema especificamente —
não existe um esqueleto fixo imposto pelo código.

Este diretório é a **única fonte de verdade** consultada pelo Agente Dungeon Master durante
a conversa com o usuário. Ele nunca é modificado por esse agente; apenas pelo Configurador.

## Índices

Cada nível tem um `index.md` gerado automaticamente: `Knowledge/index.md` lista os sistemas, e
o `index.md` de cada pasta lista os arquivos e subpastas dela com uma linha sobre cada um. É
por eles que o Dungeon Master navega — ler um índice de 2 KB para decidir abrir três arquivos
custa uma fração do que custaria varrer a base inteira, e a conta sai da cota da assinatura do
usuário.

Os índices são derivados do conteúdo das pastas, não escritos à mão: são regravados a cada
gravação do Configurador, e a ferramenta de escrita recusa um `index.md` vindo do agente. Para
reindexar uma base antiga basta processar o sistema de novo — a reconstrução acontece antes de
o agente ser chamado e não custa nada.

## Progresso

`_estado-do-processamento.json`, dentro da pasta de cada sistema, registra quais livros já
foram lidos e quais arquivos já foram gerados. É o que permite retomar um processamento
interrompido em vez de recomeçar do zero. Quem escreve esse arquivo é o aplicativo, não o
agente; apagá-lo só faz o progresso ser reconstruído a partir dos arquivos que existem.

Não edite manualmente os arquivos aqui a menos que esteja corrigindo uma extração ruim —
prefira regenerar via o Agente Configurador.
