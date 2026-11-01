# Paquete común para lo que se repite entre Lanzador, Subtitular y Rastrear

Propuesta para dejar de copiar a mano `FileDialogs` y `catalogo-entrada.ps1` entre las tres herramientas. Recomienda un paquete NuGet propio y deja el submódulo git como alternativa, con lo bueno y lo malo de cada uno. No decide nada: lo decide Daniel.

## Qué se repite hoy

| Pieza | Dónde nace | Qué hace | Quién la copia |
|---|---|---|---|
| `FileDialogs` | `externo/rodaje/src/Rodaje.App/Shell/FileDialogs.cs` (190 líneas, `internal static unsafe`) | Diálogos de abrir y guardar de Windows (`IFileOpenDialog` e `IFileSaveDialog`) por su tabla virtual con `TerraFX.Interop.Windows`: funcionan sin paquete MSIX, con Native AOT y como administrador, cosa que los selectores de WinRT no aceptan (lección apuntada en `CLAUDE.md`). | Subtitular y Rastrear. `Rodaje.App` es un exe WinUI y no se puede referenciar como `Rodaje.Packages`, así que lo único posible hoy es copiar el archivo. El Lanzador no abre archivos y no lo necesita todavía. |
| `catalogo-entrada.ps1` | `build/catalogo-entrada.ps1` (100 líneas) | Genera la entrada `app` del catálogo firmado (`id`, `version`, `kind`, `url`, `sha256`, `size`) a partir del zip de distribución y de `Directory.Build.props`. | Las tres, cada una con su `id` y su URL de release cambiados a mano. |

El coste no es el tamaño sino la deriva: el arreglo del 31-oct (sin cmdlets de `Microsoft.PowerShell.Utility`, porque Windows PowerShell 5.1 no los carga cuando lo lanza pwsh 7) hay que llevarlo a mano a cada copia, y nada avisa de que una se quedó atrás. Lo mismo pasará con cada cambio de `FileDialogs` (un filtro nuevo, un flag de `IFileDialog`, un cambio de versión de TerraFX).

## Recomendación: un paquete NuGet propio

Un repositorio nuevo, `Dathinel/comun`, que publica un paquete `Dathinel.Comun` con:

- **`Dathinel.Comun.Ventana`** (`net10.0-windows10.0.19041`, `IsAotCompatible`, `AllowUnsafeBlocks`): `FileDialogs` como clase `public`, con sus pruebas (hoy no tiene ninguna fuera de Rodaje) y una publicación AOT de muestra en la CI para que no se rompa con el enlazador. Dependencia única: `TerraFX.Interop.Windows`, fijada en el paquete.
- **`tools/catalogo-entrada.ps1`** parametrizado: `-Id`, `-Version`, `-Zip`, `-Repositorio` (`Dathinel/lanzador`) y `-Salida`, con la misma regla de hoy (solo .NET y `Microsoft.PowerShell.Management`, para que corra igual en 5.1 y en 7) y la prueba de `DistribucionTests` movida al repositorio común.
- **`build/Dathinel.Comun.targets`**, que NuGet importa solo: un objetivo `EntradaCatalogo` que llama al script con `$(Version)`, `$(CatalogoId)` y `$(CatalogoRepositorio)` del proyecto. Cada herramienta deja de tener su copia y `distribuir.ps1` hace `dotnet msbuild -t:EntradaCatalogo`.

Cómo lo consumirían las tres:

```xml
<PackageReference Include="Dathinel.Comun" Version="0.1.0" />
```

con `packages.lock.json` y `--locked-mode` en la CI, como ya hace Rodaje con todo lo demás, para que la huella quede fijada. El Lanzador solo necesita el script y el `.targets`; Subtitular y Rastrear, además, `FileDialogs`. Para no arrastrar `TerraFX` a quien no lo usa, el paquete puede partirse en dos (`Dathinel.Comun.Build` y `Dathinel.Comun.Ventana`) desde el primer día.

Dónde publicarlo:

- **nuget.org** (recomendado): público, gratis, sin token para restaurar; se reserva el prefijo `Dathinel.*` una vez. A cambio, una versión publicada no se puede borrar (solo ocultar), así que las pruebas se hacen con prerelease (`0.1.0-alfa.1`) o con una carpeta local como fuente.
- **GitHub Packages** de Dathinel: queda junto al código, pero hasta los paquetes públicos piden un token para leerlos; en Actions sirve `GITHUB_TOKEN`, en el PC de cada cual hace falta un PAT en `nuget.config`. Más fricción para la primera descarga, que es justo lo que el Lanzador quiere evitar.

### Ventajas

- **Versión explícita.** Cada herramienta elige cuándo sube (`0.1.0` → `0.2.0`) y el `CHANGELOG` del paquete dice qué cambió. Un submódulo solo ofrece un hash.
- **Nada que inicializar.** `dotnet restore` ya lo hace todo; no hay `git submodule update --init` que olvidar en la CI, en `dotnet.cmd` ni en un clon nuevo.
- **Se compila una vez.** Las tres restauran binarios; el enlazador AOT y las pruebas del paquete corren en su repositorio, no tres veces.
- **Lo compartido tiene dueño.** Pruebas, CI, licencia (GPL-3.0-only, como las tres) y documentación en un solo sitio; una copia atrasada deja de ser invisible: es una versión vieja en un `csproj`.
- **Encaja con lo que ya hay.** `.entorno`, `--locked-mode` y `Directory.Build.props` siguen igual; solo cambia de dónde sale el código.

### Desventajas

- **Hay que montar la publicación.** Un flujo `dotnet pack` + `dotnet nuget push` al etiquetar, una clave de API como secreto y el prefijo reservado. Es trabajo de una tarde, pero es trabajo.
- **Un cambio son dos PR.** Primero el paquete, después cada herramienta que lo sube. Para probar algo sin publicar: `dotnet pack` a una carpeta y esa carpeta como fuente en `nuget.config` (o una prerelease).
- **La red entra en la ecuación.** Restaurar necesita llegar al feed; con `--locked-mode` y la caché de NuGet (`~/.nuget/packages`) no se vuelve a bajar, pero el primer `restore` de un clon limpio sí.
- **El script va dentro del paquete.** Es una indirección que hoy no existe: para leerlo hay que ir a la caché de NuGet o al repositorio común, no a `build\` del proyecto.
- **Rodaje sigue con su copia.** `Rodaje.App` podría consumir el paquete también, pero es una decisión suya; mientras tanto el original y el paquete pueden divergir. Lo honesto es que el paquete nazca del archivo de Rodaje y que Rodaje lo adopte después, o que se quede como la única fuente y Rodaje lo referencie.

## Alternativa: submódulo git con `ProjectReference`

Lo mismo que hace este repositorio con Rodaje: `externo/comun` apuntando a `Dathinel/comun`, `ProjectReference` a `externo/comun/src/Dathinel.Comun.Ventana` y `build/catalogo-entrada.ps1` reducido a un envoltorio que llama a `externo/comun/build/catalogo-entrada.ps1` con el `id` y el repositorio (el patrón de `build/preparar-entorno.ps1`).

### Ventajas

- **Nada que publicar.** El cambio y quien lo usa van en el mismo PR si se trabaja con worktrees; se depura con el código fuente a la vista.
- **Ya se sabe hacer.** `dotnet.cmd` y `preparar-entorno.ps1` ya inicializan `externo/rodaje`; añadir otro submódulo es seguir el mismo camino.
- **Funciona sin red** una vez clonado, y sin feeds, tokens ni prefijos.

### Desventajas

- **Lo de siempre con los submódulos.** `HEAD` suelto, clones sin `--recursive`, mover el commit fijado es un commit propio, la CI tiene que inicializarlo, y tres repositorios con tres hashes distintos sin que nadie lo vea.
- **Se compila tres veces**, enlazador AOT incluido, y cada repositorio tiene que cargar con `TerraFX` aunque no use `FileDialogs` (el Lanzador, por ejemplo, cuyas pruebas de Core corren en Linux: un proyecto `net10.0-windows` en el submódulo obliga a separar bien lo que se referencia).
- **Sin versiones.** Un hash no dice si el cambio rompe algo; habría que mantener un `CHANGELOG` por commit a mano.
- **Lo compartido y Rodaje se mezclan.** Hoy `externo/rodaje` ya trae `Rodaje.App`; un segundo submódulo con una copia de `FileDialogs` deja dos originales a la vista en el mismo árbol.

## Lo que haría yo

1. Crear `Dathinel/comun` con `Dathinel.Comun.Ventana` (nacido del `FileDialogs` de Rodaje, público, con pruebas y publicación AOT de muestra) y `Dathinel.Comun.Build` (`catalogo-entrada.ps1` parametrizado, su prueba y el `.targets`).
2. Publicar `0.1.0` en nuget.org con el prefijo reservado; etiqueta `v0.1.0`, release con `CHANGELOG`.
3. Subtitular y Rastrear cambian sus copias por `Dathinel.Comun.Ventana` y `Dathinel.Comun.Build`; el Lanzador solo `Dathinel.Comun.Build` y borra `build/catalogo-entrada.ps1` (queda el envoltorio de `distribuir.ps1`). Un PR por herramienta, con `packages.lock.json`.
4. Proponer a Rodaje que referencie el paquete en lugar de su copia, para que haya un solo `FileDialogs`.

Si Daniel prefiere no publicar nada todavía, el submódulo es el camino corto: el mismo repositorio `Dathinel/comun`, consumido como `externo/comun`, y el salto a NuGet queda para cuando la deriva vuelva a doler. Lo que no conviene es seguir copiando archivos.
