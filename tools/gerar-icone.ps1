# Desenha o ícone do MainForge — uma bigorna sobre a brasa da forja, em pixel art — e grava o
# .ico do executável.
#
# Por que um script, e não um arquivo binário solto no repositório: ícone é a única coisa do
# projeto que ninguém consegue revisar num diff. Aqui o desenho É o código — o mapa de pixels
# abaixo se lê como um desenho em texto, e mudar o ícone volta a ser uma alteração revisável.
#
# Não usa nenhuma arte de terceiros: o sprite é desenhado aqui, pixel a pixel, e o resto é GDI+
# (System.Drawing, que já vem no Windows). Nada a creditar, nada a licenciar.
#
#   .\tools\gerar-icone.ps1
#   .\tools\gerar-icone.ps1 -Destino outro-lugar\icone.ico -Amostra

[CmdletBinding()]
param(
    [string]$Destino = "src\MainForge.Cli\mainforge.ico",

    # Grava também PNGs para conferir o desenho no olho, que é a única forma de revisar ícone.
    # Eles não entram no repositório.
    [switch]$Amostra
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

<#
    O sprite é 16x16 e todos os tamanhos saem dele por ampliação de vizinho mais próximo. Por
    isso a lista só tem múltiplos inteiros de 16: em 24 (1,5x) metade dos pixels sairia com
    largura diferente da outra metade, e a peça perde o alinhamento que faz o estilo funcionar.
#>
$ladoDoSprite = 16
$tamanhos = @(16, 32, 48, 64, 128, 256)

<#
    O desenho, uma linha por linha de pixels. É a bigorna clássica: chifre à esquerda, face de
    trabalho larga, cintura estreita e base em bloco — a única silhueta que ainda se reconhece
    com 16 pixels de lado, que é o tamanho em que este ícone vai aparecer na barra de título.

    Legenda:
      .  fundo (o gradiente em faixas)
      #  contorno escuro do metal
      H  aço iluminado (topo da face, topo da base)
      S  aço médio
      D  aço na sombra (o lado direito de cada bloco)
      e  brasa acesa
      o  brasa apagando
      *  fagulha

    Os símbolos são todos distintos ignorando maiúscula e minúscula: chave de hashtable no
    PowerShell não diferencia caixa, e um par "e"/"E" na paleta nem chega a ser lido — o script
    morre no parser.
#>
$sprite = @(
    "................",
    ".............*..",
    "..........*.....",
    "...###########..",
    "..#HHHHHHHHHHH#.",
    ".#SSSSSSSSSSSSD#",
    "#SSSSSSSSSSSSSD#",
    ".#DDDDDDDDDDDD#.",
    "...##SSSSSD##...",
    "......#SSD#.....",
    "......#SSD#.....",
    "......#SSD#.....",
    ".....##SSD##....",
    "..#HHHHHHHHHHH#.",
    "..#SSSSSSSSSSD#.",
    "..############.."
)

# A paleta é curta de propósito: pixel art com meio-tom em todo canto vira borrão quando o
# Windows reduz o ícone. Três tons de aço, dois de brasa, e o contorno.
$paleta = @{
    "#" = [System.Drawing.Color]::FromArgb(255, 16,  19,  24)
    "H" = [System.Drawing.Color]::FromArgb(255, 233, 239, 245)
    "S" = [System.Drawing.Color]::FromArgb(255, 165, 176, 189)
    "D" = [System.Drawing.Color]::FromArgb(255, 104, 115, 129)
    "e" = [System.Drawing.Color]::FromArgb(255, 255, 138, 26)
    "o" = [System.Drawing.Color]::FromArgb(255, 176, 74,  12)
    "*" = [System.Drawing.Color]::FromArgb(255, 255, 199, 92)
}

<#
    O fundo em faixas horizontais, do grafite ao calor da forja lá embaixo. São faixas chapadas,
    e não um degradê: degradê em pixel art de 16 pixels vira ruído, e é justamente o que a
    ampliação por vizinho mais próximo escancara.

    Uma faixa por linha do sprite, de cima para baixo.
#>
$fundo = @(
    @(38, 43, 51), @(38, 43, 51), @(36, 41, 49), @(36, 41, 49),
    @(34, 39, 47), @(34, 39, 47), @(33, 37, 45), @(33, 37, 45),
    @(36, 36, 43), @(46, 38, 41), @(60, 40, 38), @(78, 44, 34),
    @(104, 50, 28), @(140, 62, 22), @(182, 80, 18), @(226, 104, 20)
)

<#
    Os cantos que o quadrado perde para não ficar um bloco duro. É o arredondamento possível em
    pixel art: um pixel fora em cada canto, dois no de 16. Nada de anti-aliasing — a borda serrilhada
    é o estilo, não um defeito.
#>
function EhCantoVazado {
    param([int]$X, [int]$Y, [int]$Lado)

    $ultimo = $Lado - 1
    $dx = [Math]::Min($X, $ultimo - $X)
    $dy = [Math]::Min($Y, $ultimo - $Y)

    return ($dx + $dy) -lt 1
}

function Novo-Sprite {
    param([int]$Lado)

    $imagem = New-Object System.Drawing.Bitmap($Lado, $Lado, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

    for ($y = 0; $y -lt $Lado; $y++) {
        $linha = $sprite[$y]

        for ($x = 0; $x -lt $Lado; $x++) {
            if (EhCantoVazado -X $x -Y $y -Lado $Lado) {
                $imagem.SetPixel($x, $y, [System.Drawing.Color]::Transparent)
                continue
            }

            $simbolo = $linha.Substring($x, 1)

            if ($paleta.ContainsKey($simbolo)) {
                $imagem.SetPixel($x, $y, $paleta[$simbolo])
                continue
            }

            $faixa = $fundo[$y]
            $imagem.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $faixa[0], $faixa[1], $faixa[2]))
        }
    }

    return $imagem
}

<#
    Amplia o sprite sem inventar pixel nenhum: vizinho mais próximo, que é o que mantém a borda
    dura do pixel art. Qualquer interpolação aqui devolveria o ícone borrado de sempre — e o
    estilo inteiro depende de não haver meio-tom entre um pixel e o vizinho.
#>
function Ampliar {
    param([System.Drawing.Bitmap]$Sprite, [int]$Lado)

    $imagem = New-Object System.Drawing.Bitmap($Lado, $Lado, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($imagem)

    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy

    $g.DrawImage($Sprite, 0, 0, $Lado, $Lado)
    $g.Dispose()

    return $imagem
}

<#
    O mesmo desenho no formato antigo de ícone (DIB): cabeçalho BITMAPINFOHEADER, os pixels em
    BGRA de baixo para cima, e a máscara AND no fim.

    Por que os dois formatos convivem no mesmo arquivo: PNG dentro de .ico só é entendido a
    partir do Windows Vista, e nem todo consumidor o lê — o próprio System.Drawing.Icon do .NET
    Framework falha ao abrir uma entrada PNG, que é como este script foi pego. O Explorer usa os
    tamanhos pequenos o tempo todo, então eles vão em DIB; o de 256 fica em PNG, que é o
    combinado desde o Vista e evita um arquivo de megabytes.
#>
function Bytes-Dib {
    param([System.Drawing.Bitmap]$Imagem)

    $largura = $Imagem.Width
    $altura = $Imagem.Height

    $fluxo = New-Object System.IO.MemoryStream
    $escritor = New-Object System.IO.BinaryWriter($fluxo)

    $escritor.Write([UInt32]40)                    # biSize
    $escritor.Write([Int32]$largura)
    $escritor.Write([Int32]($altura * 2))          # XOR + máscara AND, como o formato exige
    $escritor.Write([UInt16]1)                     # biPlanes
    $escritor.Write([UInt16]32)                    # biBitCount
    $escritor.Write([UInt32]0)                     # biCompression: BI_RGB
    $escritor.Write([UInt32]0)                     # biSizeImage
    $escritor.Write([Int32]0)                      # biXPelsPerMeter
    $escritor.Write([Int32]0)                      # biYPelsPerMeter
    $escritor.Write([UInt32]0)                     # biClrUsed
    $escritor.Write([UInt32]0)                     # biClrImportant

    # De baixo para cima: é a ordem das linhas num DIB.
    for ($y = $altura - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $largura; $x++) {
            $cor = $Imagem.GetPixel($x, $y)
            $escritor.Write([Byte]$cor.B)
            $escritor.Write([Byte]$cor.G)
            $escritor.Write([Byte]$cor.R)
            $escritor.Write([Byte]$cor.A)
        }
    }

    # A máscara AND é ignorada em 32 bits (quem manda é o alfa), mas precisa existir e ter o
    # tamanho certo: cada linha ocupa um múltiplo de 4 bytes.
    $bytesPorLinha = [Math]::Floor(($largura + 31) / 32) * 4

    for ($y = 0; $y -lt $altura; $y++) {
        for ($b = 0; $b -lt $bytesPorLinha; $b++) {
            $escritor.Write([Byte]0)
        }
    }

    $escritor.Flush()
    $bytes = $fluxo.ToArray()
    $escritor.Dispose()
    $fluxo.Dispose()

    return ,$bytes
}

function Bytes-Png {
    param([System.Drawing.Bitmap]$Imagem)

    $fluxo = New-Object System.IO.MemoryStream
    $Imagem.Save($fluxo, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $fluxo.ToArray()
    $fluxo.Dispose()

    # A vírgula é obrigatória: sem ela o PowerShell desenrola o byte[] no retorno, quem chama
    # recebe um Object[] de bytes e o BinaryWriter grava zero byte de imagem — o .ico sai com
    # cabeçalho e nada dentro.
    return ,$bytes
}

# O .ico é montado na mão porque o Bitmap.Save do GDI+ com ImageFormat::Icon grava um ícone de
# 16x16 e 8 bits — perde os tamanhos grandes e o canal alfa. O formato em si é simples: um
# cabeçalho, uma entrada de diretório por tamanho e os PNGs em seguida (aceitos como conteúdo de
# ícone desde o Windows Vista).
function Gravar-Ico {
    param([string]$Caminho, [hashtable]$PorTamanho)

    $ordenados = $PorTamanho.Keys | Sort-Object
    $arquivo = [System.IO.File]::Create($Caminho)
    $escritor = New-Object System.IO.BinaryWriter($arquivo)

    $escritor.Write([UInt16]0)                     # reservado
    $escritor.Write([UInt16]1)                     # tipo: 1 = ícone
    $escritor.Write([UInt16]$ordenados.Count)

    $deslocamento = 6 + (16 * $ordenados.Count)

    foreach ($lado in $ordenados) {
        $bytes = $PorTamanho[$lado]

        # 256 é gravado como 0: o campo tem um byte só, e é assim que o formato representa o
        # tamanho máximo.
        $medida = 0
        if ($lado -lt 256) { $medida = $lado }

        $escritor.Write([Byte]$medida)             # largura
        $escritor.Write([Byte]$medida)             # altura
        $escritor.Write([Byte]0)                   # cores da paleta (0 = sem paleta)
        $escritor.Write([Byte]0)                   # reservado
        $escritor.Write([UInt16]1)                 # planos
        $escritor.Write([UInt16]32)                # bits por pixel
        $escritor.Write([UInt32]$bytes.Length)
        $escritor.Write([UInt32]$deslocamento)

        $deslocamento += $bytes.Length
    }

    foreach ($lado in $ordenados) {
        $escritor.Write([byte[]]$PorTamanho[$lado], 0, $PorTamanho[$lado].Length)
    }

    $escritor.Flush()
    $escritor.Dispose()
    $arquivo.Dispose()
}

$raiz = Split-Path -Parent $PSScriptRoot
$destinoAbsoluto = Join-Path $raiz $Destino
$pasta = Split-Path -Parent $destinoAbsoluto

if (-not (Test-Path $pasta)) {
    New-Item -ItemType Directory -Force $pasta | Out-Null
}

$base = Novo-Sprite -Lado $ladoDoSprite
$porTamanho = @{}

foreach ($lado in $tamanhos) {
    $imagem = Ampliar -Sprite $base -Lado $lado

    if ($lado -le 64) {
        $porTamanho[$lado] = Bytes-Dib -Imagem $imagem
    }
    else {
        $porTamanho[$lado] = Bytes-Png -Imagem $imagem
    }

    if ($Amostra -and ($lado -eq 256 -or $lado -eq 32)) {
        $imagem.Save((Join-Path $pasta "amostra-$lado.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    }

    $imagem.Dispose()
}

$base.Dispose()

Gravar-Ico -Caminho $destinoAbsoluto -PorTamanho $porTamanho

$tamanhoFinal = (Get-Item $destinoAbsoluto).Length
Write-Host "Icone gravado em $Destino ($([Math]::Round($tamanhoFinal / 1024, 1)) KB, tamanhos: $($tamanhos -join ', '))."
