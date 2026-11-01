# Historial de cambios de Lanzador

## Sin publicar

### Añadido
- Sin conexión, si hay herramientas instaladas la ventana las enseña igual, leídas del disco (versión, ejecutable y espacio), con «Abrir» y «Quitar» y sin «Instalar» ni «Actualizar», bajo una franja «Sin conexión» que dice que para instalar o buscar versiones nuevas hace falta red. Sin nada instalado sigue el panel «Sin conexión con el catálogo». Una firma inválida nunca se disimula con lo instalado.
- «Quitar» borra también lo que quedó en `Comun\descargas` de esa herramienta: los zips enteros y los `.parcial` de cualquiera de sus versiones publicadas; la confirmación lo dice cuando hay algo. Sin red se borra solo la carpeta (no se sabe de quién es cada descarga). Los módulos y modelos compartidos se quedan como siempre.
- Una herramienta instalada con versión mayor que la del catálogo (una compilación propia, un catálogo atrasado) tiene su propio estado, «Instalada 9.0.0, más nueva que el catálogo (1.0.0)», sin botones de instalar, actualizar ni reanudar; `lanzador lista` lo dice igual.
- `docs/compartido.md`: propuesta para dejar de copiar `FileDialogs` y `catalogo-entrada.ps1` entre Lanzador, Subtitular y Rastrear: paquete NuGet propio (recomendado) o submódulo, con ventajas y desventajas.

### Cambiado
- La CLI resume la firma inválida del catálogo en una línea («La firma del catálogo no es válida; no se instala nada.», código 2); el párrafo largo queda para la ventana.
- Aviso de versiones nuevas de las herramientas: al cargar el catálogo se compara la versión de cada entrada `app` con la instalada en el PC (la carpeta `Herramientas\<app>\<versión>`); la que tiene versión nueva muestra «Actualizar a 1.1.0» en el sitio de «Instalar», con el mismo estilo de botón, y al pulsarlo se descarga, se verifican firma y huella y se instala por encima como siempre. Si hay más de una, la cabecera dice discreto «2 actualizaciones»; con todo al día no cambia nada.
- Aviso de versión nueva del propio Lanzador: como mucho una vez cada 24 h (la última consulta queda en `%LOCALAPPDATA%\Dathinel\Lanzador\ajustes.json`) consulta la última release de `Dathinel/lanzador` en la API pública de GitHub, sin token, con `User-Agent` «Lanzador/<versión>» y 5 s de tiempo límite, fuera del hilo de la ventana; sin red o con una respuesta rara, silencio. Si el `tag_name` (`vX.Y.Z`) es mayor que la versión de la app sale una franja discreta arriba: «Lanzador 1.1.0 disponible», «Ver novedades» (abre la release en el navegador) y «Más tarde» (no vuelve a avisar de esa versión). En «Acerca de», el interruptor «Avisar cuando haya una versión nueva» (activado por omisión) y «Buscar actualizaciones», que consulta al momento también el catálogo y dice cuando no hay nada nuevo. Mientras solo haya prereleases publicadas, GitHub no devuelve ninguna «latest» y no se avisa.
- Cada tarjeta muestra la versión instalada junto a la del catálogo («Instalada 1.0.0 · Disponible 1.1.0») y el botón «Actualizar» solo aparece cuando la instalada es más vieja; una instalada más nueva que el catálogo no ofrece nada (y desde esta versión lo dice).
- «Buscar actualizaciones» al abrir (y con el botón de la cabecera, antes «Actualizar catálogo»): si hay versiones nuevas sale un aviso discreto que las nombra («Hay una actualización: Subtitular 1.0.0 → 1.1.0»).
- Progreso de descarga con velocidad y tiempo restante medidos a la velocidad real («12.0 MB de 28.0 MB (2.3 MB/s · quedan ~0:07)»), también en la CLI; la regla de tres sobre la fracción queda solo de respaldo.
- Reanudar una descarga cortada: al cancelar, lo bajado se queda en `Comun\descargas\<huella>.zip.parcial` y la tarjeta pasa a «Reanudar descarga» (o «Reanudar actualización») diciendo cuánto hay ya; la siguiente vez se pide solo lo que falta con `Range` (lo hace `FileDownloader` de Rodaje) y se comprueba la huella entera igual.
- «Abrir» solo aparece cuando la app instalada trae un ejecutable que abrir.
- «Quitar» pide confirmación diciendo qué carpeta se borra, cuánto ocupa y que los módulos y modelos compartidos se quedan; «Cancelar» es el botón por defecto.
- Un catálogo con firma inválida (o sin conexión) ya no deja la lista vacía en silencio: en su sitio sale un panel con el motivo en español («La firma del catálogo no es válida: el catálogo no es el que publicó Dathinel o llegó dañado. Por seguridad no se ofrece nada para instalar») y «Volver a intentar». La verificación de firma y huella al instalar no cambia.

### Corregido
- La tarjeta se recarga sin perder el estado sin conexión; «Quitar» informa de las descargas de todas las versiones y la ventana y la CLI comparten el parseo de opciones.
- La CI de `main` fallaba en la prueba de `catalogo-entrada.ps1`: el script usaba `Get-FileHash`, `ConvertTo-Json`, `Write-Host` y `New-Object`, que Windows PowerShell 5.1 no encuentra cuando lo lanza pwsh 7 (hereda un `PSModulePath` con el que no carga `Microsoft.PowerShell.Utility`). Ahora calcula la huella y escribe el JSON con .NET, y la prueba comprueba la forma de la entrada (que `SignedIndex` la acepte, huella de 64 hexadecimales, peso mayor que cero) y la huella del zip de prueba, sin depender de ninguna release.

## [1.0.0] - 2026-10-24

Primera versión, publicada como prerelease: probada con pruebas automáticas y el exe publicado, pero la ventana no se ha usado aún con ratón (lo que juzga Daniel está en `docs/pruebas-a-mano.md`).

Basado en el editor de video de código abierto de Dathinel / Rodaje (https://github.com/Dathinel/rodaje), un proyecto libre y gratuito: la descarga firmada de herramientas y modelos es código de Rodaje.

### Añadido
- **Lanzador.App**: interfaz gráfica moderna en WinUI 3 compilada con Native AOT (7,68 MB, arranque en frío en < 0,7 s).
  - Tarjetas interactivas por herramienta (Subtitular, Rastrear).
  - Comprobación estricta de firma del catálogo con ECDSA P-256 antes de permitir cualquier descarga.
  - Indicadores de estado: no instalada, instalada, actualizable.
  - Estimación y cálculo en tiempo real de espacio en disco (propio de la herramienta y compartido en `%LOCALAPPDATA%\Dathinel`).
  - Descargas reanudables con verificación de huella SHA-256 por archivo.
  - Progreso detallado y cancelación inmediata de instalaciones.
  - Apertura directa de herramientas configurando sus rutas de módulos compartidos (`SUBTITULAR_MODULOS`, `RASTREAR_MODULOS`).
- **Lanzador.Cli**: interfaz en línea de comandos compilada con Native AOT (6,2 MB) para scripts y automatización (`lista`, `instalar`, `actualizar`, `quitar`).
- **Lanzador.Core**: biblioteca base con cliente de catálogo firmado, instalador de herramientas, cálculo de almacenamiento y descriptores de error en español.
- **Distribución**: empaquetado zip autocontenido Native AOT para Windows x64, entrada de catálogo firmado de Rodaje e instalador opcional Inno Setup por usuario.
