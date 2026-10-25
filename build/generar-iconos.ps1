# Genera assets\marca\lanzador.ico desde los SVG de assets\marca con el ffmpeg (librsvg) de .entorno o de RODAJE_ENTORNO:
# 16, 20, 24, 32, 40, 48, 64, 128 y 256 px en PNG dentro del .ico; de 16 a 32 px se usa lanzador-pequeno.svg.
# El .ico se versiona y va dentro del exe (ApplicationIcon): compilar no necesita este script.
#   powershell -NoProfile -ExecutionPolicy Bypass -File build\generar-iconos.ps1
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$marca = Join-Path $raiz 'assets\marca'
$entorno = if ($env:RODAJE_ENTORNO) { $env:RODAJE_ENTORNO } else { Join-Path $raiz '.entorno' }
$ffmpeg = Join-Path $entorno 'ffmpeg\bin\ffmpeg.exe'
if (-not (Test-Path -LiteralPath $ffmpeg)) { throw "Falta $ffmpeg (corre build\preparar-entorno.ps1)." }
$tamanos = 16, 20, 24, 32, 40, 48, 64, 128, 256
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('lanzador-iconos-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    $pngs = foreach ($t in $tamanos) {
        $svg = Join-Path $marca $(if ($t -le 32) { 'lanzador-pequeno.svg' } else { 'lanzador.svg' })
        $png = Join-Path $tmp "lanzador-$t.png"
        & $ffmpeg -hide_banner -v error -y -width $t -height $t -i $svg -frames:v 1 -pix_fmt rgba $png
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $png)) { throw "ffmpeg no pudo rasterizar $svg a $t px." }
        $png
    }
    # Formato .ico: cabecera, una entrada de 16 bytes por imagen y los PNG tal cual (Windows los acepta desde Vista).
    $datos = foreach ($p in $pngs) { , [IO.File]::ReadAllBytes($p) }
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter($ms)
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$pngs.Count)
    $offset = 6 + 16 * $pngs.Count
    for ($i = 0; $i -lt $pngs.Count; $i++) {
        $l = if ($tamanos[$i] -ge 256) { 0 } else { $tamanos[$i] }
        $w.Write([byte]$l); $w.Write([byte]$l); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$datos[$i].Length); $w.Write([uint32]$offset)
        $offset += $datos[$i].Length
    }
    foreach ($d in $datos) { $w.Write($d) }
    $w.Flush()
    [IO.File]::WriteAllBytes((Join-Path $marca 'lanzador.ico'), $ms.ToArray())
    Copy-Item -LiteralPath (Join-Path $tmp 'lanzador-256.png') -Destination (Join-Path $marca 'lanzador-256.png') -Force
    Write-Host "  lanzador.ico ($((Get-Item (Join-Path $marca 'lanzador.ico')).Length) bytes), lanzador-256.png"
}
finally {
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
