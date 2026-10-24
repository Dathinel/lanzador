# Lanzador

El punto de entrada de las herramientas de IA de Dathinel para Windows: lista Subtitular y Rastrear, las descarga, comprueba su firma y las instala o actualiza, junto con los modelos de IA que necesitan.

> En construcción: fase 0 (esqueleto). Todavía no hace nada.

## Cómo funciona

- Lee el índice firmado de `Dathinel/rodaje-catalogo` y rechaza cualquier entrada si la firma no cuadra.
- Baja cada herramienta (zip autocontenido, sin instalador) y sus modelos, con reanudación y SHA-256 comprobado, y los deja en una carpeta del usuario.
- Comparte entre herramientas lo que es común (FFmpeg, módulos y modelos), para no bajarlo dos veces.

## Cómo se ejecuta (desarrollo)

```
git clone --recursive https://github.com/Dathinel/lanzador
cd lanzador
powershell -NoProfile -ExecutionPolicy Bypass -File build\preparar-entorno.ps1
build\dotnet.cmd build src\Lanzador.slnx
```

El SDK de .NET y FFmpeg se instalan en `.entorno\` con huella fijada, sin tocar nada global. Los scripts son los de Rodaje (submódulo `externo/rodaje`, fijado a un commit). Para no bajar el SDK otra vez se puede apuntar `RODAJE_ENTORNO` al `.entorno` de Rodaje.

## Licencia

GPLv3, solo la versión 3 (`GPL-3.0-only`). Va con GPLv3 porque reutiliza código de Rodaje (la descarga firmada de complementos), que es GPLv3. Ver [LICENSE](LICENSE) y [LICENCIAS.md](LICENCIAS.md).
