# Lanzador

El punto de entrada de las herramientas de IA de Dathinel para Windows: lista Subtitular y Rastrear, las descarga, comprueba su firma y las instala o actualiza, junto con los modelos de IA que necesitan.

> **Estado**: Ventana WinUI 3 AOT (`Lanzador.exe`, 7,7 MB, abre en 0,7 s en frío), CLI AOT (`Lanzador.Cli.exe`, 6,2 MB), paquete autocontenido zip, entrada de catálogo firmado e instalador Inno Setup por usuario. Van a `%LOCALAPPDATA%\Dathinel\Herramientas\<app>\<versión>` y lo compartido a `%LOCALAPPDATA%\Dathinel\Comun`.

## Cómo funciona

- Lee el índice firmado de `Dathinel/rodaje-catalogo` y rechaza cualquier entrada si la firma no cuadra.
- Baja cada herramienta (zip autocontenido, sin instalador) y sus modelos, con reanudación y SHA-256 comprobado, y los deja en una carpeta del usuario.
- Comparte entre herramientas lo que es común (FFmpeg, módulos y modelos), para no bajarlo dos veces.
- La ventana muestra una tarjeta por herramienta (versión, peso de descarga, espacio que ocupa y lo compartido), con Instalar, Abrir, Actualizar, Quitar y Cancelar, y nunca ofrece instalar si el índice no se verifica.

## Cómo se ejecuta (desarrollo)

```powershell
git clone --recursive https://github.com/Dathinel/lanzador
cd lanzador
powershell -NoProfile -ExecutionPolicy Bypass -File build\preparar-entorno.ps1
build\dotnet.cmd build src\Lanzador.slnx -c Release
build\dotnet.cmd test src\Lanzador.slnx -c Release
```

El SDK de .NET y FFmpeg se instalan en `.entorno\` con huella fijada, sin tocar nada global. Para no bajar el SDK otra vez se puede apuntar la variable de entorno `RODAJE_ENTORNO` al `.entorno` de Rodaje.

## Distribución y publicación

Para armar el paquete de distribución completo de forma local:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build\distribuir.ps1
```

Esto genera en `dist\`:
1. `Lanzador-<versión>-win-x64.zip`: paquete zip autocontenido con la App (`Lanzador.exe`), la CLI (`Lanzador.Cli.exe`), `LICENSE`, `LICENCIAS.md` y guía de uso.
2. `Lanzador-<versión>-instalador.exe`: instalador portable por usuario compilado con Inno Setup 7 (`build\instalador\Lanzador.iss`).
3. `catalogo-lanzador.json`: entrada tipo `app` para el catálogo de Rodaje con id `lanzador`, URL de release y SHA-256 del zip.
4. `notas-version.md` y `SHA256SUMS`: notas de versión desde el `CHANGELOG.md` y sumas de verificación SHA-256 de todos los artefactos.

### Firma del catálogo

El script `build\catalogo-entrada.ps1` **no firma nada** por seguridad: deja la entrada en `dist\catalogo-lanzador.json`. Daniel incorpora esta entrada en el catálogo general y la firma con la clave privada ECDSA P-256 mediante `firmar-indice.ps1` del repositorio `Dathinel/rodaje-catalogo`.

### Releases

El flujo `.github/workflows/release.yml` se dispara manualmente por `workflow_dispatch` y crea exclusivamente una **release en borrador** (`gh release create --draft`) con los artefactos empaquetados. Nunca publica directamente.

## Licencia

GPLv3, solo la versión 3 (`GPL-3.0-only`). Va con GPLv3 porque reutiliza código de Rodaje (la descarga firmada de complementos), que es GPLv3: Lanzador está basado en el editor de video de código abierto de Dathinel / Rodaje (<https://github.com/Dathinel/rodaje>). Ver [LICENSE](LICENSE) y [LICENCIAS.md](LICENCIAS.md).
