# Fase 2: ventana WinUI 3 con Native AOT sobre Lanzador.Core (24-oct-2026)

Hecha por agy (gemini-3.8-flash) a partir de `..\encargos\fase2-lanzador-ui.md`; verificada por Claude (Fable 5.1), que midió las cifras de abajo.

- **`Lanzador.Core`** (`Modelos.cs`, `CalculadorEspacio.cs`, `MensajesError.cs`, `Instalador.cs`): `ObtenerTarjetasAsync` da una `TarjetaHerramienta` por entrada `app` del índice (estado `NoInstalada | Instalada | Actualizable`, versiones, peso de descarga, `EspacioInstalada` con lo propio de `Herramientas\<app>` y lo compartido de `Comun\` que declara su `herramienta.json`); `InstalarAsync`/`ActualizarAsync` aceptan `IProgress<ProgresoDetallado>` (archivo, bytes, velocidad suavizada y fracción agregada); `MensajesError.Describir` convierte red, firma, disco lleno, permisos y cancelación en frases en español; `AbrirHerramienta` lanza solo un `*.exe` de `Herramientas\<app>\<versión>` con `SUBTITULAR_MODULOS`/`RASTREAR_MODULOS` = `Comun\modulos`; `AbrirCarpetaDathinel` abre la carpeta real en el Explorador.
- **`Lanzador.App`**: una ventana, sin navegación; tarjetas construidas en código con Instalar/Abrir/Actualizar/Quitar/Cancelar, barra de progreso e `InfoBar` por tarjeta; si el índice no se verifica, `InfoBar` general y ninguna tarjeta. Pie con el espacio de `%LOCALAPPDATA%\Dathinel` y «Abrir carpeta». Mismos `--indice`, `--clave`, `--destino` y `--comun` que la CLI para probar contra el servidor local. La vista solo llama a `Lanzador.Core`: ninguna ruta se construye en ella. Sin selectores WinRT (no hace falta `IFileOpenDialog`); `Program.cs` instala `DispatcherQueueSynchronizationContext` antes de crear `App`; todo `async void` envuelve sus `await` en `try/catch`.

## Cifras (medidas por el verificador)

- `build\dotnet.cmd build src\Lanzador.slnx -c Release -warnaserror`: 0 advertencias, 0 errores.
- Pruebas: 23 de 23 en dos pasadas (`OrdenesTests` + `TarjetasYEspacioTests`: tarjeta no instalada, instalada con espacio propio y compartido, actualizable, cancelación sin restos, mensajes, espacio total), contra servidor local.
- `dotnet publish -c Release` con AOT: 0 avisos de recorte ni de CsWinRT. `Lanzador.exe` **7.758.848 bytes (7,40 MB)** frente a `Subtitular.exe` 7.771.648 y `Rastrear.App.exe` 7.835.648: la más ligera de las tres.
- Arranque hasta `MainWindowHandle` (sondeo cada 50 ms, 3 s viva, sin archivo de error): **791 ms en frío**, 135 y 158 ms en caliente.
- `Lanzador.Cli lista` contra el catálogo real (solo lectura): sin entradas `app` todavía (el PR #445 de Rodaje aún no está en el índice publicado).

## Pendiente

- El peso de descarga y el espacio «No instalada» solo cuentan el zip de la app: los módulos y modelos que declara no se conocen hasta abrir su `herramienta.json`. Hace falta que el índice lo diga (`modulos` en la entrada `app`) para estimarlo antes de instalar.
- «Actualizar catálogo» durante una instalación rehace las tarjetas y pierde el botón Cancelar de la que descargaba (la descarga sigue y termina bien).
- `Instalador.Peso(0)` muestra «0.1 MB» en el pie cuando la carpeta está vacía.
