# Pruebas a mano (fase 2: la ventana)

Lo que solo Daniel puede juzgar en la interfaz WinUI 3 Native AOT de `Lanzador.App`.

El ejecutable listo para usar se encuentra en:
`dist\Lanzador\Lanzador.exe`

---

## 1. Arranque en frío y apariencia general

1. Haz doble clic en `dist\Lanzador\Lanzador.exe`.
   - **Qué juzgar:** Comprueba la inmediatez de la apertura (medido: 792 ms en frío y 385 ms en caliente con `--medir`, ver `docs/informe-fase-2.md`).
   - La ventana debe aparecer de golpe, centrada, con tipografía nítida y espaciado consistente, sin parpadeos ni pantallas en blanco.
2. **Tema de Windows:** Cambia el tema de Windows entre Claro y Oscuro con la ventana abierta.
   - **Qué juzgar:** Los colores de fondo de las tarjetas, textos secundarios y botones de acento deben adaptarse con contraste adecuado y legibilidad cómoda.

---

## 2. Catálogo y tarjetas de herramientas

1. Observa las tarjetas mostradas en el panel principal:
   - **Subtitular:** «Subtítulos automáticos, traducción a otros idiomas y lectura en voz alta.»
   - **Rastrear:** «Seguimiento y visión por IA para video: puntos, fondo, rostros y encuadre.»
   - **Rodaje:** Si figura en el índice firmado.
2. **Qué juzgar en cada tarjeta:**
   - Línea de estado y versión: «Versión X.Y.Z» o «Instalada X.Y.Z · Disponible X.Y.Z».
   - Peso de descarga: «Descarga: X MB».
   - Espacio en disco: «No instalada» o «En disco: X MB (Y MB app, Z MB compartido)».
   - La distinción clara entre lo que ocupa la app y lo compartido en `Comun\`.

---

## 3. Comportamiento sin conexión o firma no válida

1. Inicia el Lanzador sin conexión a internet (o con un índice de prueba sin firmar):
   - **Qué juzgar:** Aparece la barra superior roja en español: «No se puede verificar el catálogo: Sin conexión con el catálogo… No se ofrecen aplicaciones para instalar.»
   - Nunca debe aparecer una traza de error en inglés ni un diálogo modal de bloqueo.
   - Las tarjetas no ofrecen botones de instalación para evitar estados corruptos.

---

## 4. Instalación, progreso agregado y velocidad

1. En una herramienta no instalada, pulsa **«Instalar»**.
   - **Qué juzgar:**
     - El botón pasa inmediatamente a estado ocupado y aparece el botón **«Cancelar»**.
     - La barra de progreso muestra el archivo actual, los bytes recibidos y totales, y la velocidad calculada en tiempo real (`X.X MB/s`).
     - La ventana se puede mover y redimensionar con fluidez durante la descarga.

---

## 5. Cancelación rápida (< 1 s) sin restos

1. Pulsa **«Instalar»** en una herramienta y, durante la descarga o extracción, pulsa **«Cancelar»**.
   - **Qué juzgar:**
     - La operación se interrumpe de inmediato (< 1 segundo).
     - La interfaz vuelve al estado anterior listo para volver a interactuar.
     - En `%LOCALAPPDATA%\Dathinel\Herramientas\<app>` no queda ninguna carpeta a medio extraer (`.instalando` se borra automáticamente).

---

## 6. Estados de los botones (Abrir / Actualizar / Quitar)

1. Una vez instalada la herramienta:
   - **«Abrir»:** Debe aparecer activo con estilo de acento. Púlsalo: debe abrir la aplicación instalada configurando la ruta a `Comun\modulos`.
   - **«Quitar»:** Púlsalo: debe eliminar la carpeta de la herramienta en segundos. Lo que estuviera en `Comun\modulos` y `Comun\modelos` permanece intacto para que otras herramientas no tengan que volver a descargarlo.
   - **«Actualizar»:** Solo debe estar disponible cuando la versión del catálogo sea superior a la versión instalada en disco.

---

## 7. Pie: Espacio total y «Abrir carpeta»

1. En la parte inferior de la ventana:
   - Revisa la etiqueta «Espacio total en %LOCALAPPDATA%\Dathinel: X MB».
   - Comprueba que el total coincide con el contenido de la carpeta.
2. Pulsa el botón **«Abrir carpeta»**.
   - Debe abrir el Explorador de Windows directamente en `%LOCALAPPDATA%\Dathinel\`.

---

## 8. Identidad (pulido visual, 24-oct)

Capturas de referencia en `docs/capturas/` (tema oscuro). Lo que solo tú puedes juzgar:

1. **Tres segundos:** al abrir, ¿se nota que es el Lanzador y no Rodaje ni sus hermanas? Flecha azul en círculo (icono de ventana y barra de tareas), «Lanzador» en Bahnschrift y el lema «Las herramientas, listas» en azul.
2. **Tema claro:** cambia Windows a claro con la ventana abierta. Fondo gris muy claro, tarjetas blancas con sombra suave, botones azul eléctrico con texto blanco. En oscuro: gris azulado, botones azul claro con texto oscuro. ¿Se lee cómodo en los dos? (en oscuro está comprobado; el claro no se pudo capturar).
3. **Estado vacío:** sin entradas `app` en el catálogo debe verse el anillo con la flecha y «Todavía no hay herramientas en el catálogo», no un texto gris suelto.
4. **Progreso con vida:** al instalar, la barra avanza suave (no a saltos), la etapa va en azul («Descargando», «Extrayendo», «Hecho»), a la derecha «0:42 · quedan ~1:10». Al terminar, el tic azul junto al nombre debe aparecer con un pequeño rebote antes de que la tarjeta se recargue.
5. **Ventana:** mueve y redimensiona, cierra y vuelve a abrir: debe aparecer donde la dejaste (también maximizada). Intenta hacerla más pequeña de 640x480: no debe dejar.
6. **Teclado:** con Tab el foco debe verse en los botones y en «Acerca de» (recuadro de foco de WinUI).
7. **Acerca de:** abre el diálogo del pie y pulsa el enlace de Rodaje: debe abrir `github.com/Dathinel/rodaje` en el navegador.
