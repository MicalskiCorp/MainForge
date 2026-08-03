using System.Text;

namespace MainForge.Cli;

/// <summary>
/// Abertura do aplicativo: o portão do castelo, com uma tocha acesa sobre cada torre, em
/// arte de texto. Só ASCII de propósito — caracteres de desenho de caixa e emoji saem
/// quebrados em consoles antigos do Windows com fonte raster, e a tela inicial é justamente
/// a primeira impressão do programa.
///
/// O desenho é montado por composição (torre esquerda + vão central + torre direita) em vez
/// de ser um bloco de texto solto: assim o alinhamento das colunas é garantido pela
/// construção, e mexer numa peça não desalinha o resto.
/// </summary>
internal static class TelaInicial
{
    public static void Desenhar()
    {
        Console.WriteLine();
        ConsoleUi.EscreverColorido(" -+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+- ", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|..                                    .;;;;;;:                                                    :;;;;;;.                                     ..|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|                                      ..;;;;;:                                                    .;;;;;:.                                       |", ConsoleColor.DarkGray);        
        ConsoleUi.EscreverColorido("|.......   ............   ......       .;;;;;;: .........  ..........    ..............   .......  :;;;;;;.     ........  ..............  ....... |", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|.......   ............   ...██████   ██████;;: ........███.........    ███████████....  ......... :;;;;;;.     ........   .............  ....... |", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|........  ............   ...░░██████ ██████+;: ........░░░........  :;;░░███░░░░░░█...  ......... :;+;;;;.     ........  ..............  ....... |", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|.....  .....   ..  .........░███░█████░███xx;██████...████..████████+++░███x;.█.░...██████..████████xx+███████ .██████.  .............  .......  |", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|.....  .....   ..  .........░███░░███ ░███xx░░░░░███ ░░███ ░░███░░███;;░███████.:::███░░███░░███░░███ ███░░███ ███░░███::...   ..........        |", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|............................░███ ░░░  ░███xx;███████;:░███;;░███;░███+X░███░░░█:::░███;░███.░███.░░░x░███:░███░███████......  ............  .... |", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|..............  .....:;;;;;.░███;:.   ░███XX███░░███: ░███;;░███.░███;x░███x:░+.:;░███;░███.░███: ;;X░███;░███░███░░░;;;;;;:. ............  .... |", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|................:;;;;;;;;;;;█████;..  █████░░████████ █████;████$█████$█████:;+XX$░░██████ .█████.;+$░░███████░░██████;;;;;;: :;;;:.......  .... |", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|................:;;;;;;;;;;;░░░░░;   .░░░░░$+░░░░░░░░ ░░░░░$░░░░;░░░░░$░░░░░;;;;$$$$░░░░░░  ░░░░░ ;+$$$░░░░░███:░░░░░░;;;;;;: :;;;:.......  .... |", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|........  :;;;;;;;;;;;; ;;;x$$+;;  .  ;;X$$$x; .;;;;;;;;;X$$$$$$Xx+;+X;;;+$x;;+X$$$$$x$;;;;;;;;;. ;x$$$███.░███:;;;;;;;;.;;;;;;;;;;;;;. :;;;;;:. |", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|.......  .;;;;;;;;;;;;; ;;;xXX+;;  . .;;X$$$x;.:;;;;;;;;$$$X$$$X+;: .:::;::  .;+X$$Xx$$$;;;;;;;;: ;x$$$░░██████;;;;;;;;; ;;;;;;;;;;;;;: ;;;;;;;. |", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|;;:;;;;: .;;;;;;;;;;;;; ;;;;;;;;;  ..:;;+$$$+;.:;;;;;;+$$$$$$;;                  ;x$$$$$$;;;;;;;:.;+$$$░░░░░░  ;;;+;;;;; ;;;;;;;;;;;;;; ;;;;;;;;:|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|;...:::. .;;;;;;;;;;;;; ;;;;;;;;:  . .;;x$$$X;. ;;;;;;$$$$$$;                      ;$$$$$$:;;;;;..;x$$$x+;.    :;;;;;;;: ;;;;;;;;;;;;;; ;;;;;;;; |", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|...   ...:;;;  ;;;+;+++x++;;;   .:...:;;$$$$X;:;;;;: x$$$$$;                        ;$$$$$X :;;;;:;X$$$$+;: ..::::  ;+++++;;; ;;;;;;;;;;;;...... |", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|;;;. :;;;;;;;;.;+$$$$$$$$XX;;.:;;;;..:;;x$$$X;;;x$$+:$$$$$;                          ;$$$$$.;$$x;;;X$$$x;;:..;;;;;;.+$$$$$X;;:;;;;;;;;;;;;::;;;;.|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|;;;. ;;;;;;;;;:;;x$$$$$$$+;;;.;;;;;..:;;+$$$$;;;;;;;:$Xxxx;                          ;$XxX$:;;+;;;;X$$$++;:..;;;;;;.;$$$$$x;;:;;;;;;;;;;;;.;;;;;:|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|;;;: ;;;;;;;;;:;;;;;;;;;;;;;;.;;;;;..:;;X$$$$;;;;;;;.;;;;;;                          ;x;;;; ;;;;;;;$$$$$+;:..;;;;;;.;;;++;;;;.;;;;;;;;;;;;.;;;;;:|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|;;;. .::::::: .;;::::::::::::  .    .:;;X$$$$;:.. ;&&$$$$$&$;                      ;x&&$$$$&&+   :;X$$$X;;:           .. .    :;;;;;;::;;:.:;;;;.|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|;;;;;;;;:;;;x$$$$;:;;+X$$$$$+;;; ;;..:;;X$$$X;;;;;:$$$$$$$$+;                      ;;$$$$$X$$;;;;;;X$$$X+;:.  .;;;X$$$X;.;;+$$$$$$$$$$+.;$$$;;;;:|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|;;;;;;;;:;;x$$$$$;.;;;;;;;;;;;;;.;;..:;;x$$$$;;;;;.  ......                          ......  .;;;;;$$$$x+;:.  .;;;;++++;.;;;+x$$$$$$$$;.;$$X;;;;:|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|;;;;;;;;:;;;+xx+;;:;;;;;;;;;;;;;.;;..:;;X$$$$;;;;;;: ;;;;;;:                        :;;;;;; :;;;;;;$$$$X+;:.. .;;;;;;;;;.;;;;;++x$$X++;.;X+;;;;;:|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|;;;;;;;;:;;;;;;;;;:;;;;;;;;;;;;. ....:;;$$$$$;;;;;;..x$x$$+:                        ;;$$x$$ .;;;;;;X$$$$+;:.   :;;;;;;;: ;;;;;;;;;;;;;;.;;;;;;;;:|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|;;;;;x$$$$$+:;X$$$$$$++x+;.;++++;;:..:;;$$$$X;;$$$$x:X$$$$+:                        ;;$$$$X.x$$$x;;x$$$$;;:..:;;;;.;+x$$$$$$X;;$$$$$XXxXx+;:;+++;|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|;;;;+$$$$$$X;;;;;++;;;;;+;.;;;;;;;:..:;+$$$$X;:+$$$;:X$$$$;;                        ;;$$$$x.;$$$+:;X$$$$+;: .:;;;;:;;;;+;;;;;;.;++xx+;;;;;;;;;;;:|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|;;;;;x$$$Xx;;;;;;;;;;;;;;;.;;;;;;;:..:;;X$$$X;:;;;;;.x$$$$+:                        ;;$$$$x ;;;;;:;X$$$$+;: .:;;;:.;;;;;;;;;;;.;;;;;;;;;;;;:;;;;:|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|;;;;;;;;;;;;::;;;;;;;;;;;; ;;;;;;:;..;;;;X$$X+X;;;;;.x$$$$+:                        ;;$$$$X ;;;;;x+x$$$+;;:.:;;;;:.;;;;;;;;;;: ;;;;;;;;;;;;.;;;;:|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|;;;;;;;;;;.:.....      :;;++;;;;...+&&&&&&&&&&$;     X$$$$+:                        :;$$$$$     ;$&&&&&&&&&&x:. .:::....:.. ...:::.....:;;;;;;;;:|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|;;;;;;;;;;.;;+++;;x+;;;;$$$X+;;;:..;x$$$$$$$$$;;;;;;:X$$$$+;                        :;$$$$$.;;;;;+$$$$$$$$$+;...;;;;;+x$$X;;;;++++;;;;;.++++++;;:|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|;;;;;;;;;: ;;;;;;;;;;;;:;++;;;;;:..;+xxxx$$$$X;;;;;;:+$$XX;:  ..::::::.. .;;:.:..   :;$X$$x.;;;;;;x$$$$xxxx+;..:;;;;;;;++;::;;;;;;;;;;;.;;;;;;;;:|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|::....:;;  ;;;;;;;;;;;..::::::.....x$$$$$$$$$$+;;;;  ;+;;;; ;;+++++++x;;;+++++++;;;; .+;++; .::;+x$$$$$$$$$$x:.....::.::::.:;;;;;;;;;;: :;;;;::: |", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|;;;;;;;;;;;;;;;;;;;;;;++++;;:;;:...;;;;;;;;;;;;;;x$$$$$x ;$$$$X+;x$$$$&$$$$&x$&$&&$$$X;:+$$$$$+:;;;;;;;;;;;;;:..:;;.;;;x++;;;;;;;;;;;;;;;:.;;;;;.|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|;;;;;;;;;;;;;;;;;;;;;;;;;;;+$$$+;:.;;;;;;;;;;;;+$&&&&&&;$&&&&&&&&&&&&&&&&&&&&$$&&$$x+xX+;$&&&&&$x;;;;;;;;;;;;..;;x$$x;;;;;;;;;;;;;;;;;;;;;;:;;;;.|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|;;;;;;;;+++xxxxxxX$$$$$+;x$$$$$$$&&&&$$&&&&&&&&&&&&&&$$&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&X$&&&&&&&&&&&&&&&&&&&&&&$$$$$X;x$$$$$$$$$xx+++++;;;;;;x;|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|;;++++++XXXxx$$$$$$$+x$$$$&$$&&&&&&&&$&&&&&&&&&&&&&;;;$$$&&&$&&&&&&&&&&&&$$$&$$$$$$&&$$$$$$$X$&&&&&&&&&&&&&$$$$$$$$$&&$$$$X+x$$$$$XX$$$$Xx++++;;:|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|+xx$$$$$$$$$$$$$$$$x$$$$$$$$&&$&&&&&&&&&&&&&&&&&&&$$&&&&&&&&&&&&$$&&&&&&&&&&&&$$&&&&&&&&&&&&$$&&&&&&&&&&&&&&&&&&&&&&&&$$$$$$x+$$$$$$$$$$$$$$Xxxx;|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|X$$$$$$$$$$$$$$$$;$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$;$$$$$$$$$$$$$$;$$$$$$$$$$$$$$;$$$$$$$$$$$$$$;$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$+;$$$$$$$$$$$$$$$$+|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;.;;;;;;;;;;;;;;:;;;;;;;;;;;;;; ;;;;;;;;;;;;;;.;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;.|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido("|:;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;.;CRIAÇÃO DE FICHAS DE RPG COM O CLAUDE CODE;.;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;.|", ConsoleColor.DarkGray);
        ConsoleUi.EscreverColorido(" -+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+- ", ConsoleColor.DarkGray);        
    }
}
