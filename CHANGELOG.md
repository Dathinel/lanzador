# Historial de cambios de Lanzador

## Sin publicar

### Corregido
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
