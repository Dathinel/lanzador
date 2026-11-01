# Lanzador

Herramienta de IA aparte de Rodaje, con interfaz propia. Repositorio público `Dathinel/lanzador`, GPL-3.0-only. El plan común de las tres herramientas está en `..\PLAN.md` (carpeta `Herramientas-IA`, que no se versiona).

## Cómo se trabaja aquí

- **.NET:** siempre con `build\dotnet.cmd <args>`; `build\preparar-entorno.ps1` deja el SDK y FFmpeg en `.entorno\`. Los dos llaman a los scripts de Rodaje del submódulo `externo/rodaje`: no se copian.
- **Código de Rodaje:** por `ProjectReference` a `externo/rodaje/src/...`, nunca copiado. Mover el commit fijado del submódulo es un commit propio.
- **Worktrees** en `.claude\worktrees\` (ignorado). Un commit por tarea y push.
- **Publicar y distribuir:** `powershell -NoProfile -ExecutionPolicy Bypass -File build\distribuir.ps1` (publica App y Cli con Native AOT, empaqueta `dist\Lanzador-<v>-win-x64.zip`, genera `dist\catalogo-lanzador.json`, notas de versión, SHA256SUMS e instalador Inno Setup).
- **Lecciones de las hermanas que aquí ya se cumplen:** sin selectores WinRT (salen colapsados), `DispatcherQueueSynchronizationContext` antes de crear `App`, y ningún `async void` con `await` fuera de `try/catch`.
- **WinUI en AOT:** `AppWindow.Presenter is OverlappedPresenter` nunca acierta: `Presenter.Kind` + `As<OverlappedPresenter>()`. Los recursos `AccentButton*`/`HyperlinkButton*` no se pisan desde `App.Resources`: van en `Acento.xaml` mezclado donde se usan. Icono: `build\generar-iconos.ps1` regenera `assets\marca\lanzador.ico` desde los SVG.

## Estado (1-nov)

- Fases 0-3 y pulido cerrados (24-oct): `Lanzador.Core` (`Instalador` sobre `Rodaje.Packages`: índice firmado, descarga reanudable, módulos y modelos compartidos en `Comun\`), `Lanzador.Cli` (`lista | instalar | actualizar | quitar`) y `Lanzador.App` (WinUI 3, AOT, identidad azul eléctrico, `ventana.json`, «Acerca de» con la atribución a Rodaje); `build\distribuir.ps1` (zip, instalador Inno Setup, `catalogo-lanzador.json`, notas, `SHA256SUMS`), `ci.yml` y `release.yml` (borrador). Cifras en `docs/informe-fase-1.md` a `-3.md` e `informe-pulido.md`.
- **1.0.0 prerelease** (24-oct): release `v1.0.0` con zip, instalador y `SHA256SUMS`; prerelease porque la ventana no se había probado con ratón. Submódulo en el commit de Rodaje con `IndexEntryKind.App` (#445). Las apps encuentran `Comun\modulos` por `SUBTITULAR_MODULOS` y `RASTREAR_MODULOS`.
- **Unidos en `main`** (detalle en `CHANGELOG.md` «Sin publicar»): #1 calidad de vida 1.1 y CI arreglada, #2 aviso de versión nueva y «Actualizar a X», #3 revisión de bloque (los tres el 31-oct); #5 respaldo a `releases?per_page=10` cuando `releases/latest` da 404 y #4 sin red con instaladas, «Quitar» borra las descargas de la app, `InstaladaMasNueva`, CLI corta y `docs/compartido.md` (los dos el 1-nov). 104 pruebas; Core y pruebas corren también en Linux, la App solo en Windows. AOT republicado el 1-nov (`dist\Lanzador\Lanzador.exe` 8,2 MB, zip 28,0 MB).
- **Repaso con ratón (1-nov):** resultado al final de `docs/pruebas-a-mano.md`; lo que juzga Daniel está en los bloques 8-11.
- **Revisión de bloque de #4 y #5 (agy, 1-nov) → #6 unido el 1-nov** (codex en worktree, probado por Claude): `RecargarTarjetaAsync` usa `CargarTarjetasAsync` y conserva el modo sin conexión, el texto de «Quitar» suma las descargas de todas las versiones (`DescargasDeLaAppBytes`), y `OpcionesComunes.Parsear` es el único parseo de `--indice/--clave/--destino/--comun` (ventana y CLI; opción sin valor = `ErrorDeUso`). 117 pruebas; AOT y zip republicados (1-nov, 08:45). Límite aceptado: las descargas de versiones que ya no están en el catálogo quedan huérfanas en `Comun\descargas`.
- **Siguiente:** decisiones de Daniel: `docs/compartido.md` (paquete NuGet `Dathinel.Comun` o submódulo `externo/comun`; agy recomienda submódulo mientras la API cambie) y retención de `Comun\descargas`. Daniel firma las entradas `app` (`catalogo-subtitular.json`, `catalogo-rastrear.json`, `catalogo-lanzador.json`) con `firmar-indice.ps1` de `Dathinel/rodaje-catalogo`; hasta entonces el Lanzador real no lista nada. Después, pruebas a mano y quitar la marca de prerelease.
