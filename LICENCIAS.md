# Licencias

Qué lleva Lanzador y con qué licencia. Ningún binario ni modelo de terceros va en este repositorio: los modelos se descargan con huella fijada.

## Código

| Pieza | Licencia | Cómo entra |
|---|---|---|
| Código propio (`src/`, `build/`) | GPL-3.0-only | Este repositorio |
| Rodaje (`externo/rodaje`, submódulo fijado a un commit) | GPL-3.0-only con los términos adicionales de la sección 7 de su `LICENSE` | Se compilan sus proyectos; el código no se copia |
| SDK de .NET 10 y runtime de Native AOT | MIT | `.entorno\`, con huella SHA-512 |
| FFmpeg, compilación LGPL de Rodaje | LGPL v3 | `.entorno\ffmpeg`, con huella SHA-256; se distribuye como DLL y ejecutables sin renombrar |

El Lanzador no trae modelos: descarga las herramientas y sus modelos, cada uno con su propio `LICENCIAS.md`.
