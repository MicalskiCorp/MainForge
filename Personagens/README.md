# Personagens

O **dossiê** de cada personagem: o que ele é, em que pé está a criação e qual conversa a
produziu. Uma pasta por sistema e, dentro dela, uma por personagem:

```
Personagens/
    Aventura&Cia/
        Thoradin/
            personagem.json     escrito pelo aplicativo
            ficha.md            escrito pelo Agente Dungeon Master
```

- `personagem.json` — situação (**desenvolvendo**, **concluído** ou **descontinuado**), fontes
  que aquela mesa usa, id da conversa no Claude Code, valores da última ficha gerada e o
  histórico. É o que a tela de personagens lê para montar a lista.
- `ficha.md` — o estado completo do personagem em texto, gravado pelo agente pela ferramenta
  `registrar_personagem`. É o que reconstrói o personagem numa conversa nova, meses depois,
  quando a original não existe mais.

**Não é a mesma coisa que `Output/Personagens/`.** Lá ficam as fichas em PDF, que são a entrega
para o usuário e de onde o código não tira decisão nenhuma. Aqui fica o dado de trabalho: o
aplicativo lê daqui para saber o que continuar, e é a única pasta de resultado que o Dungeon
Master alcança.

Nada aqui é escrito à mão: quem grava é o aplicativo e o agente, pelas ferramentas. Apagar a
pasta de um personagem o remove do aplicativo — o PDF já gerado, se houver, continua em
`Output/Personagens/`.
