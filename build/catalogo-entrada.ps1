# Genera el JSON de la entrada «app» para el catálogo firmado de Rodaje.
# Deja el JSON en dist\catalogo-lanzador.json (sin firmar).
# Uso:
#   powershell -NoProfile -ExecutionPolicy Bypass -File build\catalogo-entrada.ps1 [-Version 1.0.0] [-Zip dist\Lanzador-1.0.0-win-x64.zip] [-Salida dist\catalogo-lanzador.json]
#
# Solo usa cmdlets de Microsoft.PowerShell.Management y .NET: cuando pwsh 7 lanza powershell.exe 5.1 (la CI, las pruebas),
# el hijo hereda un PSModulePath con los módulos de la 7 y no consigue cargar Microsoft.PowerShell.Utility, así que
# Get-FileHash, ConvertTo-Json, Write-Host o New-Object salen como «no reconocido». Con .NET funciona en las dos.
[CmdletBinding()]
param(
    [string]$Version = '',
    [string]$Zip = '',
    [string]$Salida = ''
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

# Un error sale como una sola línea «Error: …» en stderr y código 1, igual en pwsh 7 y en 5.1 (la vista «concisa» de pwsh parte el mensaje en varias líneas).
try {

$Raiz = Split-Path -Parent $PSScriptRoot

if (-not $Version) {
    $propsPath = Join-Path $Raiz 'Directory.Build.props'
    if (Test-Path -LiteralPath $propsPath) {
        $props = [xml][System.IO.File]::ReadAllText($propsPath)
        $Version = @($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
    }
    if (-not $Version) { throw 'No se pudo determinar la versión desde Directory.Build.props.' }
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "La versión «$Version» no tiene la forma mayor.menor.parche."
}

if (-not $Salida) {
    $Salida = Join-Path $Raiz 'dist\catalogo-lanzador.json'
}

$dirSalida = Split-Path -Parent $Salida
if ($dirSalida -and -not (Test-Path -LiteralPath $dirSalida)) {
    New-Item -ItemType Directory -Force -Path $dirSalida | Out-Null
}

if (-not $Zip) {
    $Zip = Join-Path $dirSalida "Lanzador-$Version-win-x64.zip"
}

if (-not (Test-Path -LiteralPath $Zip)) {
    throw "No se encontró el archivo zip de distribución: $Zip (corre antes build\distribuir.ps1)."
}

$zipCompleto = (Get-Item -LiteralPath $Zip).FullName
$size = (Get-Item -LiteralPath $Zip).Length
if ($size -le 0) {
    throw "El zip «$Zip» está vacío."
}

$sha = [System.Security.Cryptography.SHA256]::Create()
try {
    $flujo = [System.IO.File]::OpenRead($zipCompleto)
    try {
        $sha256 = ([System.BitConverter]::ToString($sha.ComputeHash($flujo)) -replace '-', '').ToLowerInvariant()
    } finally {
        $flujo.Dispose()
    }
} finally {
    $sha.Dispose()
}

$url = "https://github.com/Dathinel/lanzador/releases/download/v$Version/Lanzador-$Version-win-x64.zip"

# El mismo orden y formato que daba ConvertTo-Json (dos espacios, una propiedad por línea).
$jsonTexto = @"
{
  "id": "lanzador",
  "version": "$Version",
  "kind": "app",
  "url": "$url",
  "sha256": "$sha256",
  "size": $size
}
"@
[System.IO.File]::WriteAllText($Salida, $jsonTexto, [System.Text.UTF8Encoding]::new($false))

[Console]::WriteLine("Entrada de catálogo generada: $Salida")
[Console]::WriteLine("  Id: lanzador $Version")
[Console]::WriteLine("  URL: $url")
[Console]::WriteLine("  SHA256: $sha256")
[Console]::WriteLine("  Tamaño: $([math]::Round($size / 1MB, 2)) MB")

} catch {
    [Console]::Error.WriteLine("Error: $($_.Exception.Message)")
    exit 1
}
