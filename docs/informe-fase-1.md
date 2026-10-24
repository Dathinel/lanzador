# Fase 1: CLI que lista, instala, actualiza y quita herramientas (24-oct-2026)

Hecha por Claude (Fable 5.1) a partir de `..\encargos\fase1-lanzador-cli.md`.

- **Requisito en Rodaje** (worktree `claude-indice-app`, rama `claude/indice-app`, commit `913828a4`, sin push): `IndexEntryKind.App` se lee de `"kind": "app"` en `indice.json`; `AvailableVersionsAsync` lo omite como a los modelos, así Rodaje no intenta instalar apps. Prueba `Reads_app_entries_that_rodaje_ignores` en `SignedIndexTests`. El submódulo `externo/rodaje` apunta a ese commit; cuando el PR se una, se mueve al de `main`.
- **`Lanzador.Core`** (`IsAotCompatible`, JSON con generador de código): `Instalador` reutiliza de `Rodaje.Packages` `GitHubReleaseDownloader` (índice firmado, tope de tamaño, no acepta un índice más viejo que el último visto), `FileDownloader` (hash al vuelo, `Range`, `.parcial`), `ModelStore` (modelos por hash, enlace duro) y `SafePaths` (zip sin rutas peligrosas). La app va a `Herramientas\<app>\<versión>`; sus módulos (`herramienta.json`: `{ "modulos": ["modulo-eco"] }`) a `Comun\modulos\<id>\<versión>` y sus modelos (`module.models` del `paquete.json`) al almacén `Comun\modelos`. La clave pública del catálogo se incrusta desde el submódulo, no se copia.
- **`Lanzador.Cli`**: `lista | instalar <app> | actualizar [<app>] | quitar <app>` con `--indice <url|ruta>`, `--destino`, `--comun` y `--clave`. Progreso en stderr. Salida 0 bien, 1 error de la persona, 2 red, firma, huella o disco; mensajes en español, sin trazas.

## Cifras

- `build\dotnet.cmd build src\Lanzador.slnx -c Release -warnaserror`: 0 advertencias. Rodaje (worktree): 0 advertencias; 159 pruebas de `Rodaje.Packages.Tests.Download` pasan.
- Pruebas: 14 de 14 (`OrdenesTests`, servidor `HttpListener` local y clave de prueba generada en la prueba): lista con versión, peso y estado; instalar deja app, módulo y modelo compartido y no vuelve a bajar nada; actualizar cambia 1.0.0 → 1.1.0, borra la vieja y no vuelve a bajar lo común; quitar borra solo la app; firma mala → 2 sin ningún archivo; huella mala → 2 sin restos (solo `indice-visto.txt`); corte a mitad → segunda petición con `Range: bytes=N-` y 206; seis errores de uso → 1; sin servidor → 2.
- Publicación Native AOT sin advertencias (`-o dist\Lanzador`, `OptimizationPreference=Size`, `UseSystemResourceKeys`): `Lanzador.Cli.exe` 6.137.856 bytes; `--help` en 0,12 s; `lista` contra el catálogo real de Dathinel (sin apps todavía): firma verificada, 0 entradas, 1,1 s.

## Pendiente

- **Peso:** 6,1 MB frente a los 4,0 MB de `Subtitular.Cli`; lo que pesa es HttpClient con TLS, System.Text.Json, zip y ECDSA. No es la más ligera de las tres por ahora.
- Las apps instaladas todavía no saben dónde están los módulos (`Comun\modulos`): hay que pasárselo (variable `SUBTITULAR_MODULOS` o equivalente) en la fase 2 o 3.
- `--indice <ruta>` (archivo local) está implementado pero sin prueba; una app solo declara módulos (sus modelos vienen del `paquete.json` del módulo), no modelos propios.
- Fase 2: ventana WinUI 3 con una tarjeta por herramienta. Fase 3: entradas `app` en el catálogo firmado por Daniel.
