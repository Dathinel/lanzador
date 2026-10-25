# Distribución de Lanzador (publicación AOT, zip, catálogo, notas e instalador).
# Uso:
#   powershell -NoProfile -ExecutionPolicy Bypass -File build\distribuir.ps1 [-Version 1.0.0] [-Salida dist] [-SinPublicar] [-SinInstalador]
[CmdletBinding()]
param(
    [string]$Version = '',
    [string]$Salida = '',
    [switch]$SinPublicar,
    [switch]$SinInstalador
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

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
    $Salida = Join-Path $Raiz 'dist'
}

$CarpetaApp = Join-Path $Salida 'Lanzador'
New-Item -ItemType Directory -Force -Path $Salida, $CarpetaApp | Out-Null

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Distribución de Lanzador $Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Entorno propio (.entorno\ del repo) salvo que RODAJE_ENTORNO apunte a otro, como el de Rodaje.
if (-not $env:RODAJE_ENTORNO) { $env:RODAJE_ENTORNO = Join-Path $Raiz '.entorno' }

# 1. Compilación y publicación Native AOT (App y Cli) sobre una carpeta limpia, para no arrastrar restos de otra publicación.
if (-not $SinPublicar) {
    Write-Host "`n[1/5] Publicando Lanzador.App y Lanzador.Cli con Native AOT..." -ForegroundColor Yellow
    Remove-Item -LiteralPath $CarpetaApp -Recurse -Force
    $dotnet = Join-Path $PSScriptRoot 'dotnet.cmd'
    foreach ($proyecto in 'Lanzador.App', 'Lanzador.Cli') {
        & $dotnet publish (Join-Path $Raiz "src\$proyecto\$proyecto.csproj") -c Release -r win-x64 -o $CarpetaApp -warnaserror
        if ($LASTEXITCODE -ne 0) {
            throw "Falló la publicación de $proyecto con Native AOT (código $LASTEXITCODE)."
        }
    }
}

# Fuera lo que solo sirve para compilar: símbolos y la documentación XML de los ensamblados (ya van dentro del exe AOT).
Get-ChildItem -LiteralPath $CarpetaApp -Recurse -File | Where-Object { $_.Extension -in '.pdb', '.xml' } | Remove-Item -Force

# Verificar que existen los ejecutables publicados
$exeApp = Join-Path $CarpetaApp 'Lanzador.exe'
$exeCli = Join-Path $CarpetaApp 'Lanzador.Cli.exe'
foreach ($exe in $exeApp, $exeCli) {
    if (-not (Test-Path -LiteralPath $exe)) {
        throw "No se encontró el ejecutable requerido: $exe"
    }
}

# 2. Documentos y README de uso dentro de la carpeta
Write-Host "`n[2/5] Copiando licencias y README de uso..." -ForegroundColor Yellow
Copy-Item -LiteralPath (Join-Path $Raiz 'LICENSE') -Destination (Join-Path $CarpetaApp 'LICENSE') -Force
Copy-Item -LiteralPath (Join-Path $Raiz 'LICENCIAS.md') -Destination (Join-Path $CarpetaApp 'LICENCIAS.md') -Force

$readmeUso = @"
Lanzador $Version
=================

Punto de entrada para las herramientas de IA de Dathinel (Subtitular, Rastrear).

Contenido de esta distribución:
- Lanzador.exe: Aplicación gráfica moderna en WinUI 3 (haz doble clic para iniciar).
- Lanzador.Cli.exe: Herramienta de línea de comandos para scripts y automatización.
- LICENSE / LICENCIAS.md: Licencia GPL-3.0-only y avisos de componentes de terceros.

Uso rápido (App):
1. Ejecuta Lanzador.exe.
2. La interfaz listará las herramientas disponibles en el catálogo firmado.
3. Pulsa «Instalar» en la herramienta deseada o «Abrir» si ya se encuentra instalada.
4. El Lanzador descargará la herramienta, sus módulos y modelos compartidos en:
   %LOCALAPPDATA%\Dathinel\Herramientas\ y %LOCALAPPDATA%\Dathinel\Comun\

Uso rápido (CLI):
  Lanzador.Cli.exe lista
  Lanzador.Cli.exe instalar subtitular
  Lanzador.Cli.exe actualizar
  Lanzador.Cli.exe quitar rastrear
  Lanzador.Cli.exe --help

Opciones de la CLI:
  --indice <url>     Dirección del catálogo firmado (por defecto el oficial)
  --clave <archivo>  Clave pública para verificar el catálogo
  --destino <dir>    Carpeta base de herramientas
  --comun <dir>      Carpeta base de recursos comunes

Licencia: GNU GPL versión 3 (GPL-3.0-only).
Repositorio: https://github.com/Dathinel/lanzador
Basado en el editor de video de código abierto de Dathinel / Rodaje (https://github.com/Dathinel/rodaje):
la descarga firmada de herramientas y modelos es código de Rodaje.
"@
[System.IO.File]::WriteAllText((Join-Path $CarpetaApp 'README.txt'), $readmeUso, (New-Object System.Text.UTF8Encoding($false)))

# 3. Empaquetar zip autocontenido
Write-Host "`n[3/5] Creando zip autocontenido..." -ForegroundColor Yellow
$zipNombre = "Lanzador-$Version-win-x64.zip"
$zipSalida = Join-Path $Salida $zipNombre
if (Test-Path -LiteralPath $zipSalida) {
    Remove-Item -LiteralPath $zipSalida -Force
}

$archivoZip = [System.IO.Compression.ZipFile]::Open($zipSalida, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $padre = (Resolve-Path -LiteralPath $CarpetaApp).Path.TrimEnd('\') + '\'
    foreach ($f in Get-ChildItem -LiteralPath $CarpetaApp -Recurse -File) {
        $nombreRelativo = $f.FullName.Substring($padre.Length).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archivoZip, $f.FullName, $nombreRelativo, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally {
    $archivoZip.Dispose()
}

$tamZip = (Get-Item -LiteralPath $zipSalida).Length
Write-Host "  Zip creado: $zipSalida ($([math]::Round($tamZip / 1MB, 2)) MB)" -ForegroundColor Green

# 4. Generar notas de la versión desde CHANGELOG.md y entrada de catálogo
Write-Host "`n[4/5] Extrayendo notas y generando entrada de catálogo..." -ForegroundColor Yellow
$changelogPath = Join-Path $Raiz 'CHANGELOG.md'
$notasTexto = ''
if (Test-Path -LiteralPath $changelogPath) {
    $lineas = Get-Content -LiteralPath $changelogPath -Encoding UTF8
    $capturando = $false
    $lineasNotas = New-Object System.Collections.Generic.List[string]
    foreach ($l in $lineas) {
        if ($l -match "^##\s+\[$([regex]::Escape($Version))\]") {
            $capturando = $true
            $lineasNotas.Add($l)
            continue
        }
        if ($capturando -and $l -match '^##\s+\[') {
            break
        }
        if ($capturando) {
            $lineasNotas.Add($l)
        }
    }
    if ($lineasNotas.Count -gt 0) {
        $notasTexto = ($lineasNotas -join "`n").Trim()
    }
}

if (-not $notasTexto) {
    $notasTexto = "# Lanzador $Version`n`nLanzamiento inicial de Lanzador $Version."
}

$notasSalida = Join-Path $Salida 'notas-version.md'
[System.IO.File]::WriteAllText($notasSalida, $notasTexto, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "  Notas de versión: $notasSalida" -ForegroundColor Green

# Generar entrada de catálogo llamando a catalogo-entrada.ps1
$catalogoSalida = Join-Path $Salida 'catalogo-lanzador.json'
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'catalogo-entrada.ps1') -Version $Version -Zip $zipSalida -Salida $catalogoSalida
if ($LASTEXITCODE -ne 0) {
    throw "Falló la generación de catalogo-lanzador.json."
}

# 5. Instalador opcional con Inno Setup
$instaladorSalida = Join-Path $Salida "Lanzador-$Version-instalador.exe"
if (-not $SinInstalador) {
    Write-Host "`n[5/5] Compilando instalador Inno Setup..." -ForegroundColor Yellow
    $iscc = Join-Path $env:RODAJE_ENTORNO 'innosetup-7.1.0\ISCC.exe'
    if (-not (Test-Path -LiteralPath $iscc)) {
        $iscc = $null
        $cmdIscc = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
        if ($cmdIscc) { $iscc = $cmdIscc.Source }
    }

    if ($iscc) {
        $issScript = Join-Path $PSScriptRoot 'instalador\Lanzador.iss'
        $licenciaPath = Join-Path $Raiz 'LICENSE'
        $argumentosIscc = @(
            '/Q',
            "/DVersion=$Version",
            "/DOrigen=$CarpetaApp",
            "/DLicencia=$licenciaPath",
            "/O$Salida",
            $issScript
        )
        & $iscc @argumentosIscc
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $instaladorSalida)) {
            Write-Warning "ISCC terminó con código $LASTEXITCODE; no se pudo crear el instalador."
        } else {
            $tamInst = (Get-Item -LiteralPath $instaladorSalida).Length
            Write-Host "  Instalador creado: $instaladorSalida ($([math]::Round($tamInst / 1MB, 2)) MB)" -ForegroundColor Green
        }
    } else {
        Write-Warning "No se encontró ISCC.exe (Inno Setup). Se omite la creación del instalador."
    }
} else {
    Write-Host "`n[5/5] Instalador omitido (-SinInstalador)." -ForegroundColor Gray
}

# 6. Generar SHA256SUMS
$artefactos = New-Object System.Collections.Generic.List[string]
if (Test-Path -LiteralPath $zipSalida) { $artefactos.Add($zipSalida) }
if (Test-Path -LiteralPath $instaladorSalida) { $artefactos.Add($instaladorSalida) }
if (Test-Path -LiteralPath $catalogoSalida) { $artefactos.Add($catalogoSalida) }

$sumsLineas = @($artefactos | ForEach-Object {
    $sha = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
    $nombre = Split-Path -Leaf $_
    "$sha  $nombre"
})

$sumsSalida = Join-Path $Salida 'SHA256SUMS'
[System.IO.File]::WriteAllLines($sumsSalida, [string[]]$sumsLineas, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "`n========================================" -ForegroundColor Green
Write-Host " Distribución completada con éxito" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
foreach ($linea in $sumsLineas) {
    Write-Host "  $linea"
}
