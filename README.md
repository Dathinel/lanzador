# Lanzador

El punto de entrada de las herramientas de IA de Dathinel para Windows: lista Subtitular y Rastrear, las descarga, comprueba su firma y las instala o actualiza, junto con los modelos de IA que necesitan.

> En construcción: hay ventana (`Lanzador.exe`, WinUI 3 con Native AOT, 7,4 MB, abre en 0,8 s en frío) y línea de órdenes (`Lanzador.Cli lista | instalar <app> | actualizar [<app>] | quitar <app>`); el catálogo público todavía no lista herramientas. Van a `%LOCALAPPDATA%\Dathinel\Herramientas\<app>\<versión>` y lo compartido a `%LOCALAPPDATA%\Dathinel\Comun`.

## Cómo funciona

- Lee el índice firmado de `Dathinel/rodaje-catalogo` y rechaza cualquier entrada si la firma no cuadra.
- Baja cada herramienta (zip autocontenido, sin instalador) y sus modelos, con reanudación y SHA-256 comprobado, y los deja en una carpeta del usuario.
- Comparte entre herramientas lo que es común (FFmpeg, módulos y modelos), para no bajarlo dos veces.
- La ventana muestra una tarjeta por herramienta (versión, peso de descarga, espacio que ocupa y lo compartido), con Instalar, Abrir, Actualizar, Quitar y Cancelar, y nunca ofrece instalar si el índice no se verifica.

## Cómo se ejecuta (desarrollo)

```
git clone --recursive https://github.com/Dathinel/lanzador
cd lanzador
powershell -NoProfile -ExecutionPolicy Bypass -File build\preparar-entorno.ps1
build\dotnet.cmd build src\Lanzador.slnx
build\dotnet.cmd publish src\Lanzador.App\Lanzador.App.csproj -c Release -o dist\Lanzador
```

El SDK de .NET y FFmpeg se instalan en `.entorno\` con huella fijada, sin tocar nada global. Los scripts son los de Rodaje (submódulo `externo/rodaje`, fijado a un commit). Para no bajar el SDK otra vez se puede apuntar `RODAJE_ENTORNO` al `.entorno` de Rodaje.

## Licencia

GPLv3, solo la versión 3 (`GPL-3.0-only`). Va con GPLv3 porque reutiliza código de Rodaje (la descarga firmada de complementos), que es GPLv3. Ver [LICENSE](LICENSE) y [LICENCIAS.md](LICENCIAS.md).
