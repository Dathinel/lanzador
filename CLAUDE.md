# Lanzador

Herramienta de IA aparte de Rodaje, con interfaz propia. Repositorio público `Dathinel/lanzador`, GPL-3.0-only. El plan común de las tres herramientas está en `..\PLAN.md` (carpeta `Herramientas-IA`, que no se versiona).

## Cómo se trabaja aquí

- **.NET:** siempre con `build\dotnet.cmd <args>`; `build\preparar-entorno.ps1` deja el SDK y FFmpeg en `.entorno\`. Los dos llaman a los scripts de Rodaje del submódulo `externo/rodaje`: no se copian.
- **Código de Rodaje:** por `ProjectReference` a `externo/rodaje/src/...`, nunca copiado. Mover el commit fijado del submódulo es un commit propio.
- **Worktrees** en `.claude\worktrees\` (ignorado). Un commit por tarea y push.
- **Publicar:** `build\dotnet.cmd publish src\Lanzador.Cli\Lanzador.Cli.csproj -c Release -o dist\Lanzador`.

## Estado (24-oct)

- Fase 0 cerrada: esqueleto, licencia y `src\Lanzador.slnx`.
- Fase 1 cerrada: `Lanzador.Core` (`Instalador` sobre `Rodaje.Packages`: índice firmado, descarga reanudable, módulos y modelos compartidos en `Comun\`) y `Lanzador.Cli` AOT (6,2 MB) con `lista | instalar | actualizar | quitar`; 17 pruebas contra un servidor local. Verificada por un agente aparte; cifras y pendientes en `docs/informe-fase-1.md`.
- **Submódulo** en `913828a4` (rama `claude/indice-app` de Rodaje, `IndexEntryKind.App`, sin push ni PR todavía): cuando ese PR se una, mover el submódulo al commit de `main`.
- **Siguiente:** fase 2, ventana WinUI 3 sobre `Lanzador.Core`; antes, decidir cómo las apps encuentran `Comun\modulos` (`..\PLAN.md`).
