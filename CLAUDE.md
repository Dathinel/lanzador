# Lanzador

Herramienta de IA aparte de Rodaje, con interfaz propia. Repositorio público `Dathinel/lanzador`, GPL-3.0-only. El plan común de las tres herramientas está en `..\PLAN.md` (carpeta `Herramientas-IA`, que no se versiona).

## Cómo se trabaja aquí

- **.NET:** siempre con `build\dotnet.cmd <args>`; `build\preparar-entorno.ps1` deja el SDK y FFmpeg en `.entorno\`. Los dos llaman a los scripts de Rodaje del submódulo `externo/rodaje`: no se copian.
- **Código de Rodaje:** por `ProjectReference` a `externo/rodaje/src/...`, nunca copiado. Mover el commit fijado del submódulo es un commit propio.
- **Worktrees** en `.claude\worktrees\` (ignorado). Un commit por tarea y push.
- **Publicar y distribuir:** `powershell -NoProfile -ExecutionPolicy Bypass -File build\distribuir.ps1` (publica App y Cli con Native AOT, empaqueta `dist\Lanzador-<v>-win-x64.zip`, genera `dist\catalogo-lanzador.json`, notas de versión, SHA256SUMS e instalador Inno Setup).
- **Lecciones de las hermanas que aquí ya se cumplen:** sin selectores WinRT (salen colapsados), `DispatcherQueueSynchronizationContext` antes de crear `App`, y ningún `async void` con `await` fuera de `try/catch`.
- **WinUI en AOT:** `AppWindow.Presenter is OverlappedPresenter` nunca acierta: `Presenter.Kind` + `As<OverlappedPresenter>()`. Los recursos `AccentButton*`/`HyperlinkButton*` no se pisan desde `App.Resources`: van en `Acento.xaml` mezclado donde se usan. Icono: `build\generar-iconos.ps1` regenera `assets\marca\lanzador.ico` desde los SVG.

## Estado (24-oct)

- Fase 0 cerrada: esqueleto, licencia y `src\Lanzador.slnx`.
- Fase 1 cerrada: `Lanzador.Core` (`Instalador` sobre `Rodaje.Packages`: índice firmado, descarga reanudable, módulos y modelos compartidos en `Comun\`) y `Lanzador.Cli` AOT (6,2 MB) con `lista | instalar | actualizar | quitar`; 17 pruebas contra un servidor local. Verificada por un agente aparte; cifras y pendientes en `docs/informe-fase-1.md`.
- Fase 2 cerrada: `Lanzador.App` (WinUI 3, AOT, 7,40 MB, 791 ms en frío), una ventana con tarjeta por herramienta, progreso, cancelación, mensajes en español y pie con el espacio de `%LOCALAPPDATA%\Dathinel`; la lógica (tarjetas, espacio propio y compartido, progreso detallado, `AbrirHerramienta`) en `Lanzador.Core` con 23 pruebas. Hecha por agy y verificada por Claude; cifras y pendientes en `docs/informe-fase-2.md`; lo que juzga Daniel en `docs/pruebas-a-mano.md`.
- Fase 3 cerrada: `build\distribuir.ps1` (46 s) deja zip autocontenido win-x64 (27,8 MB: 13,9 MB son los dos exe AOT y el resto el Windows App SDK), instalador Inno Setup por usuario (17,4 MB), `catalogo-lanzador.json` para `SignedIndex`, notas y `SHA256SUMS`; versión única en `Directory.Build.props`; `ci.yml` y `release.yml` (solo borrador). Hecha por agy y verificada por Claude; 24 pruebas; cifras en `docs/informe-fase-3.md`.
- Pulido visual cerrado: identidad propia (azul eléctrico, Bahnschrift del sistema, icono en `assets/marca`, lema), estado vacío ilustrado, progreso con tiempos y tic de «Hecho», ventana con mínimo y posición recordada (`ventana.json`), «Acerca de» con la atribución a Rodaje que exige su licencia. Exe 7,68 MB, 637 ms en frío, 31 pruebas; detalle y lo que queda en `docs/informe-pulido.md`; lo que juzga Daniel en `docs/pruebas-a-mano.md` (bloque 8).
- **Submódulo** en el commit de `main` de Rodaje con `IndexEntryKind.App` (#445).
- **Cómo encuentran las apps `Comun\modulos`:** el Lanzador las abre con `SUBTITULAR_MODULOS` y `RASTREAR_MODULOS` apuntando ahí (lo que ya leen sus `Modules.Find`).
- **Siguiente:** firma de los JSON de catálogo por Daniel en `Dathinel/rodaje-catalogo` y publicación de releases.
