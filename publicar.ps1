# Gera o aplicativo pronto para distribuir: um executável único, com o runtime .NET dentro.
#
# Quem baixa o resultado precisa instalar só o Claude Code — o .NET, o leitor de PDF (PdfPig) e
# o preenchedor de ficha (PDFsharp) vão embutidos. Os prompts dos agentes viajam em Agents/,
# ao lado do executável, e as demais pastas de dados nascem no primeiro uso.
#
#   .\publicar.ps1                 # gera em .\publicado\
#   .\publicar.ps1 -Versao 1.2.0   # carimba a versão no executável e no nome do .zip

[CmdletBinding()]
param(
    [string]$Versao = "1.1.0",
    [string]$Destino = "publicado"
)

$ErrorActionPreference = "Stop"

$raiz = $PSScriptRoot

# -Destino aceita caminho relativo (à pasta do projeto) ou absoluto; Join-Path com um caminho
# já absoluto produziria "D:\projeto\C:\outra\pasta".
$saida = if ([System.IO.Path]::IsPathRooted($Destino)) { $Destino } else { Join-Path $raiz $Destino }
$pacote = Join-Path $saida "MainForge"

if (Test-Path $pacote) {
    Remove-Item -Recurse -Force $pacote
}

Write-Host "Publicando MainForge $Versao (win-x64, self-contained)..." -ForegroundColor Cyan

$projeto = Join-Path $raiz "src\MainForge.Cli\MainForge.Cli.csproj"
$argumentos = @(
    "publish", $projeto,
    "-c", "Release",
    "-p:PublishSingleFile=true",
    "-p:Version=$Versao",
    "-o", $pacote
)

& dotnet $argumentos

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish falhou."
}

# O servidor MCP é o mesmo executável (modo --mcp): um segundo binário aqui só duplicaria o
# runtime embutido e poderia divergir de versão.
Get-ChildItem -Path $pacote -Filter "MainForge.Mcp*" -ErrorAction SilentlyContinue | Remove-Item -Force

# Os .pdb não servem a quem usa o aplicativo e carregam os caminhos da máquina onde ele foi
# compilado — não é o tipo de coisa que se distribui num release.
Get-ChildItem -Path $pacote -Filter "*.pdb" -ErrorAction SilentlyContinue | Remove-Item -Force

# Pastas de dados vazias, para quem abrir o zip entender onde as coisas vão. O aplicativo as
# cria sozinho se não existirem, mas um zip que já mostra a estrutura explica o produto.
foreach ($pasta in @("Input", "Templates", "Sistemas", "Personagens", "Output\Personagens")) {
    $caminho = Join-Path $pacote $pasta
    New-Item -ItemType Directory -Force $caminho | Out-Null
    Set-Content -Path (Join-Path $caminho ".gitkeep") -Value "" -Encoding utf8
}

Copy-Item (Join-Path $raiz "README.md") $pacote

# A licença viaja com o binário: quem recebe o programa precisa receber junto os termos sob os
# quais pode usá-lo, redistribuí-lo e modificá-lo.
Copy-Item (Join-Path $raiz "LICENSE") $pacote

$zip = Join-Path $saida "MainForge-$Versao-win-x64.zip"

if (Test-Path $zip) {
    Remove-Item -Force $zip
}

Compress-Archive -Path (Join-Path $pacote "*") -DestinationPath $zip

# A soma de verificação vai junto do release. Enquanto o executável não for assinado, é ela que
# permite a quem baixa provar que o arquivo é exatamente o que este script gerou — e ela cobre o
# que a assinatura cobriria: adulteração no caminho entre o build e a máquina de quem usa.
$hash = (Get-FileHash -Path $zip -Algorithm SHA256).Hash.ToLower()
$arquivoDoHash = "$zip.sha256"
Set-Content -Path $arquivoDoHash -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ascii -NoNewline

$tamanho = [math]::Round((Get-Item $zip).Length / 1MB, 1)

Write-Host ""
Write-Host "Pronto: $zip ($tamanho MB)" -ForegroundColor Green
Write-Host "SHA-256: $hash" -ForegroundColor Green
Write-Host "Publique os dois arquivos (.zip e .sha256) como assets de um release no GitHub."





