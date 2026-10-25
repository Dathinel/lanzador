# Fondo legal de Lanzador

No es asesoría legal: resume las licencias y lo que hay que comprobar antes de entregar un ejecutable a terceros. Las licencias de cada pieza están en [LICENCIAS.md](../LICENCIAS.md).

## Licencia

GPL-3.0-only, con el texto oficial completo en [LICENSE](../LICENSE). La razón: Lanzador compila código de Rodaje (la descarga firmada de complementos, en el submódulo `externo/rodaje`), que es GPLv3. Rodaje y este programa tienen un solo titular (Dathinel), así que la elección de licencia es libre; GPLv3 es la coherente con el resto.

**Términos adicionales de Rodaje.** El `LICENSE` de Rodaje añade, por la sección 7, un aviso de atribución en la interfaz («Basado en el editor de video de código abierto de Dathinel / Rodaje», con enlace). Ese término acompaña al código de Rodaje que se compila aquí (`Rodaje.Packages`). El Lanzador incluye atribución y enlace a Rodaje en `README.md` y en el `README.txt` del zip; **en la ventana todavía no** (pendiente, abajo).

**Marca.** El nombre «Rodaje», su logotipo y su icono no se licencian con el código (`TRADEMARK.md` de Rodaje). Lanzador tiene su propia denominación, identificadores e iconos independientes.

## Lista de comprobación para publicar

- [x] **`LICENSE` con la GPLv3 completa y cabecera oficial:** Presente en la raíz del repositorio y copiado en el zip de distribución y en el instalador.
- [x] **Crédito de Rodaje en la documentación (Sección 7, punto 2):** `README.md` y `README.txt` del zip con la frase y el enlace a `https://github.com/Dathinel/rodaje`.
- [ ] **Crédito de Rodaje en la ventana (Sección 7, punto 1):** falta el aviso con enlace en la interfaz de `Lanzador.App` (pie o «Acerca de»). Entra con el pulido visual.
- [x] **Marca propia:** Nombre «Lanzador», identificador único en catálogo (`lanzador`), sin activos ni logotipos protegidos de la marca Rodaje.
- [x] **`LICENCIAS.md` al día:** código propio, Rodaje por submódulo, .NET / Native AOT (MIT), Windows App SDK y WebView2 (términos de Microsoft, no MIT).
- [ ] **Windows App SDK frente a la GPLv3:** las DLL de Microsoft del zip llevan términos propios; si la excepción de «System Libraries» las cubre es la misma consulta al abogado pendiente en `docs/legal.md` de Rodaje.
- [x] **Módulos y modelos de IA descargados aparte:** El Lanzador no embebe modelos en su repositorio ni en sus artefactos de distribución. Instala y gestiona herramientas que descargan sus propios modelos fijados por SHA-256 e índice firmado.
- [x] **FFmpeg LGPL:** El Lanzador no distribuye FFmpeg directamente; las herramientas que gestiona reutilizan la compilación LGPL de Rodaje en la carpeta compartida `%LOCALAPPDATA%\Dathinel\Comun`.
- [x] **Sin código GPL ajeno copiado:** Todo el código fuente es original o referenciado por submódulo oficial sin copias no atribuidas.
- [x] **Sin datos personales ni secretos:** Sin claves privadas en el repositorio. La clave pública de verificación del catálogo es la única incorporada en el ensamblado; la firma del catálogo se realiza externamente en `Dathinel/rodaje-catalogo`.
- [x] **Reconstrucción reproducible:** La distribución se genera de forma determinista desde la etiqueta `v<versión>` con `build\distribuir.ps1`, produciendo el zip AOT, el JSON de catálogo y el manifiesto `SHA256SUMS`.
