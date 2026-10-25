# Licencias

Qué lleva Lanzador y con qué licencia. Ningún binario ni modelo de terceros va en este repositorio: los modelos se descargan con huella fijada.

## Código

| Pieza | Licencia | Cómo entra |
|---|---|---|
| Código propio (`src/`, `build/`) | GPL-3.0-only | Este repositorio |
| Rodaje (`externo/rodaje`, submódulo fijado a un commit) | GPL-3.0-only con los términos adicionales de la sección 7 de su `LICENSE` | Se compilan sus proyectos; el código no se copia |
| SDK de .NET 10 y runtime de Native AOT | MIT | `.entorno\`, con huella SHA-512 |
| Windows App SDK 2.5.1 (WinUI 3): `Microsoft.WindowsAppSDK.WinUI` 2.3.9, `.Foundation` 2.3.12, `.InteractiveExperiences` 2.1.9 | Términos de licencia de software de Microsoft (`license.txt` de cada paquete; **no** MIT, aunque el código fuente de WinUI en GitHub sí lo sea). Su cláusula 3.a.i permite redistribuir lo que el paquete coloca junto al exe; el choque con la GPLv3 es el mismo pendiente que en Rodaje (`LICENCIAS.md` y `docs/legal.md` de Rodaje) | Sí: las DLL, `.pri`, `.winmd` y carpetas de idioma que acompañan a `Lanzador.exe` en el zip (unos 20 MB comprimidos de los 27) |
| Microsoft.Web.WebView2 (dependencia de WinUI) | Licencia propia de Microsoft de tipo BSD de 3 cláusulas | Sí: `Microsoft.Web.WebView2.Core.dll` y `WebView2Loader.dll` en el zip |
| FFmpeg, compilación LGPL de Rodaje | LGPL v3 | Solo en `.entorno\ffmpeg` para desarrollo y pruebas; el Lanzador **no** lo distribuye: lo comparten las herramientas en `%LOCALAPPDATA%\Dathinel\Comun` |

El Lanzador no trae modelos: descarga las herramientas y sus modelos, cada uno con su propio `LICENCIAS.md`.
