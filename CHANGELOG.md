# Historial de cambios de Lanzador

## [1.0.0] - 2026-10-24

### Añadido
- **Lanzador.App**: interfaz gráfica moderna en WinUI 3 compilada con Native AOT (7,4 MB, arranque en frío en < 0,8 s).
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
