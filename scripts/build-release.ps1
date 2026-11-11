# Gera todos os artefatos de distribuição do MiniPrint Desktop.
#
# Uso:
#   powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1 -SkipTests
#
# Requisitos: .NET SDK 8.0+; Inno Setup em tools\InnoSetup (para o instalador).

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

# Versão única do projeto: vem de Directory.Build.props e nomeia o ZIP e o
# instalador, para que cada correção gere artefatos distinguíveis.
$versionMatch = Select-String -Path (Join-Path $root "Directory.Build.props") -Pattern '<Version>([^<]+)</Version>'
if (-not $versionMatch) {
    throw "Versão não encontrada em Directory.Build.props."
}

$version = $versionMatch.Matches[0].Groups[1].Value.Trim()
$versionQuad = if ($version.Split('.').Count -ge 4) { $version } else { "$version.0" }

function Write-Step([string]$text) {
    Write-Host ""
    Write-Host "==> $text" -ForegroundColor Cyan
}

function Resolve-Dotnet {
    $candidates = @(
        (Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
        (Join-Path $env:LOCALAPPDATA "dotnet-sdk\dotnet.exe"),
        (Join-Path $env:ProgramFiles "dotnet\dotnet.exe")
    ) | Where-Object { $_ -and (Test-Path $_) }

    foreach ($candidate in $candidates) {
        $sdks = & $candidate --list-sdks 2>$null
        if ($sdks) { return $candidate }
    }

    throw "SDK do .NET não encontrado. Instale o .NET SDK 8.0 ou superior."
}

$dotnet = Resolve-Dotnet
Write-Host "Usando dotnet: $dotnet"
& $dotnet --version
Write-Host "Versão do produto: $version"

$publishApp = Join-Path $root "publish\singlefile"
$publishTools = Join-Path $root "publish\tools"
$portable = Join-Path $root "publish\portable"
$dist = Join-Path $root "dist"

Write-Step "Compilando a solução"
& $dotnet build "MiniPrint.sln" -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar." }

if (-not $SkipTests) {
    Write-Step "Executando testes"
    & $dotnet test "tests\MiniPrint.Tests\MiniPrint.Tests.csproj" -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Testes falharam." }
}

Write-Step "Publicando aplicativo (arquivo único, self-contained)"
Remove-Item $publishApp -Recurse -Force -ErrorAction SilentlyContinue
& $dotnet publish "src\MiniPrint.Desktop\MiniPrint.Desktop.csproj" -c $Configuration -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -o $publishApp
if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar o aplicativo." }

Write-Step "Publicando ferramenta de diagnóstico"
Remove-Item $publishTools -Recurse -Force -ErrorAction SilentlyContinue
& $dotnet publish "src\MiniPrint.Diagnostics\MiniPrint.Diagnostics.csproj" -c $Configuration -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -o $publishTools
if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar a ferramenta de diagnóstico." }

Write-Step "Montando a versão portátil"
Remove-Item $portable -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $portable | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $portable "tools") | Out-Null
Copy-Item (Join-Path $publishApp "MiniPrint.exe") $portable
Copy-Item (Join-Path $publishTools "miniprint-diag.exe") (Join-Path $portable "tools")
Copy-Item "README.md", "LICENSE.txt" $portable
Copy-Item "docs" (Join-Path $portable "docs") -Recurse
if (Test-Path "docs\exemplos") {
    New-Item -ItemType Directory -Force -Path (Join-Path $portable "modelos") | Out-Null
    Copy-Item "docs\exemplos\*.json" (Join-Path $portable "modelos")
}
Copy-Item "assets\logo-256.png" (Join-Path $portable "assets") -Recurse -Force -ErrorAction SilentlyContinue

Write-Step "Verificando o executável publicado"
$exe = Join-Path $portable "MiniPrint.exe"
$process = Start-Process -FilePath $exe -ArgumentList "--selftest" -PassThru -Wait
$report = Join-Path $env:LOCALAPPDATA "MiniPrintDesktop\selftest.txt"
if (Test-Path $report) {
    Get-Content $report | Select-Object -First 6 | ForEach-Object { Write-Host "    $_" }
}

if ($process.ExitCode -ne 0) {
    throw "A autoverificação da interface falhou (código $($process.ExitCode))."
}

Write-Step "Gerando o ZIP portátil"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$zip = Join-Path $dist "MiniPrintDesktop-$version-portable.zip"
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $portable "*") -DestinationPath $zip -CompressionLevel Optimal
Write-Host ("    {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))

$iscc = Join-Path $root "tools\InnoSetup\ISCC.exe"
if (Test-Path $iscc) {
    Write-Step "Compilando o instalador $version"
    Push-Location (Join-Path $root "installer")
    try {
        & $iscc "/DAppVersion=$version" "/DAppVersionQuad=$versionQuad" "MiniPrint.iss"
        if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar o instalador." }
    }
    finally {
        Pop-Location
    }

    $setupName = "MiniPrint-Setup-$version.exe"
    $setupSource = Join-Path $root ("installer\output\" + $setupName)
    if (-not (Test-Path $setupSource)) {
        throw "O instalador não foi gerado em $setupSource."
    }

    Copy-Item $setupSource $dist -Force
}
else {
    Write-Warning "Inno Setup não encontrado em tools\InnoSetup; instalador não gerado."
}

Write-Step "Artefatos"
Get-ChildItem $dist | Select-Object Name, @{n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize

Write-Host "Concluído." -ForegroundColor Green
