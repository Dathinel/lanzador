# Pulido visual e identidad (24-oct-2026)

Hecho por Claude (Fable 5.1) en la rama `pulido-visual` a partir de `..\encargos\pulido-visual.md`; capturas en `docs/capturas/`.

- **Identidad:** azul eléctrico `#1F5EFF` sobre `#F4F6FA` en claro y `#6D9BFF` sobre gris azulado `#1B2029` en oscuro (sigue al tema de Windows; alto contraste con los colores del sistema). Título, lema («Las herramientas, listas»), nombres de tarjeta y estado vacío en **Bahnschrift**, la sans geométrica que viene con Windows 10 y 11: cero bytes de fuente en el exe. Icono propio (flecha de lanzamiento en un círculo, `assets/marca/lanzador.svg` y `-pequeno.svg`; `build\generar-iconos.ps1` genera el `.ico` con el ffmpeg de `.entorno`), incrustado en el exe (`ApplicationIcon`) y puesto en la ventana y la barra de tareas sin archivo aparte (`LoadIcon` + `Win32Interop.GetIconIdFromIcon`).
- **Ventana:** tamaño mínimo 640x480 (escalado por DPI), posición y tamaño recordados en `%LOCALAPPDATA%\Dathinel\Lanzador\ventana.json` (`EstadoVentana` en Core; se descarta si el rectángulo ya no cae en ninguna pantalla). En AOT `Presenter is OverlappedPresenter` nunca acierta: se usa `Presenter.Kind` + `As<OverlappedPresenter>()`.
- **Estado vacío** con ilustración vectorial (anillo y flecha en el acento) y qué hacer. **Progreso:** etapa en acento, barra propia con animación de 300 ms (`ScaleTransition`), bytes y velocidad, `transcurrido · quedan ~estimado` (`Tiempos` en Core, reloj de 1 s), y al terminar «Hecho», barra llena y un tic que aparece con escala y opacidad antes de recargar la tarjeta. Instalar y Actualizar comparten el mismo código.
- **Ritmo:** rejilla de 8 px (márgenes 32/24/16/8), tarjetas con radio 12, borde y `ThemeShadow`, botones primarios en el acento (los recursos `AccentButton*` e `HyperlinkButton*` van en `Acento.xaml`, mezclado en la raíz de la ventana y en el diálogo: puestos en `App.Resources` WinUI no los respeta). Estilos con `ThemeResource` en los setters para que las tarjetas creadas en código cambien de tema en vivo.
- **Legal:** «Acerca de» en el pie (término 1 de la licencia de Rodaje): la frase literal con enlace a `https://github.com/Dathinel/rodaje`, nombre, versión de `Directory.Build.props` y GPLv3; casilla marcada en `docs/legal.md`.

## Cifras

- `build -c Release -warnaserror`: 0 advertencias; `publish` AOT: 0 avisos de recorte ni de CsWinRT. Pruebas: 31 de 31 (7 nuevas: ventana que se guarda y recupera, archivo ausente, corrupto o diminuto; formato de reloj; estimación de lo que queda).
- `Lanzador.exe`: **8.054.272 bytes (7,68 MB)** frente a 7.758.848 (7,40 MB) antes: +295 KB, de los que 22 KB son el icono, unos 60 KB el diálogo «Acerca de» (ContentDialog + HyperlinkButton) y el resto las proyecciones de los tipos WinUI nuevos (`ThemeShadow` solo cuesta 11 KB, medido). Sigue por debajo de los exe de fase 2 de Subtitular (7.771.648) y Rastrear (7.835.648) solo si su pulido les añade más de 220–280 KB: a vigilar en su informe.
- Arranque en frío (`medir-arranque.ps1 -Frio`, 3 vueltas, mediana): ventana creada **637 ms**, visible 718 ms (fase 2: 791 ms hasta la ventana).
- Comprobado con el exe publicado contra un servidor local con una app de 7,6 MB a 1,5 MB/s: progreso, tiempos, tic de «Hecho», recarga, tamaño mínimo (pedir 300x300 deja 640x480), cierre que guarda `ventana.json` y reapertura en el mismo sitio, diálogo «Acerca de» con enlaces en el acento.

## Queda

- Alto contraste no se ha mirado a ojo (los pinceles propios apuntan a los colores del sistema; los `AccentButton*` quedan en los de WinUI).
- La captura del tema claro no se pudo hacer (el equipo va en oscuro y no se cambia el tema del sistema desde el agente): lo juzga Daniel (`docs/pruebas-a-mano.md`, bloque «Identidad»).
- «Actualizar catálogo» durante una instalación sigue perdiendo el botón Cancelar (pendiente de fase 2).
