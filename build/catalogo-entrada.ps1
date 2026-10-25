# Genera el JSON de la entrada «app» para el catálogo firmado de Rodaje.
# Deja el JSON en dist\catalogo-lanzador.json (sin firmar).
# Uso:
#   powershell -NoProfile -ExecutionPolicy Bypass -File build\catalogo-entrada.ps1 [-Version 1.0.0] [-Zip dist\Lanzador-1.0.0-win-x64.zip] [-Salida dist\catalogo-lanzador.json]
[CmdletBinding()]
param(
    [string]$Version = '',
    [string]$Zip = '',
    [string]$Salida = ''
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$Raiz = Split-Path -Parent $PSScriptRoot

if (-not $Version) {
    $propsPath = Join-Path $Raiz 'Directory.Build.props'
    if (Test-Path -LiteralPath $propsPath) {
        $props = [xml](Get-Content -LiteralPath $propsPath -Raw -Encoding UTF8)
        $Version = @($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
    }
    if (-not $Version) { throw 'No se pudo determinar la versión desde Directory.Build.props.' }
}

if (-not $Salida) {
    $Salida = Join-Path $Raiz 'dist\catalogo-lanzador.json'
}

$dirSalida = Split-Path -Parent $Salida
if (-not (Test-Path -LiteralPath $dirSalida)) {
    New-Item -ItemType Directory -Force -Path $dirSalida | Out-Null
}

if (-not $Zip) {
    $Zip = Join-Path $dirSalida "Lanzador-$Version-win-x64.zip"
}

if (-not (Test-Path -LiteralPath $Zip)) {
    throw "No se encontró el archivo zip de distribución: $Zip (corre antes build\distribuir.ps1)."
}

$sha256 = (Get-FileHash -LiteralPath $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item -LiteralPath $Zip).Length
$url = "https://github.com/Dathinel/lanzador/releases/download/v$Version/Lanzador-$Version-win-x64.zip"

$entrada = [ordered]@{
    id = 'lanzador'
    version = $Version
    kind = 'app'
    url = $url
    sha256 = $sha256
    size = $size
}

$jsonTexto = $entrada | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText($Salida, $jsonTexto, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "Entrada de catálogo generada: $Salida" -ForegroundColor Green
Write-Host "  Id: $($entrada.id) $($entrada.version)"
Write-Host "  URL: $url"
Write-Host "  SHA256: $sha256"
Write-Host "  Tamaño: $([math]::Round($size / 1MB, 2)) MB"
