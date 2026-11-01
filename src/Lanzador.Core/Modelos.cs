using Rodaje.Packages;

namespace Lanzador.Core;

/// <summary>Espacio que ocupa una herramienta instalada: lo exclusivo de la app y lo compartido en Comun\.</summary>
public sealed record EspacioInstalada(long AppBytes, long CompartidoBytes)
{
    public long TotalBytes => AppBytes + CompartidoBytes;

    public string Formatear()
    {
        if (CompartidoBytes <= 0)
        {
            return Instalador.Peso(AppBytes);
        }

        return $"{Instalador.Peso(TotalBytes)} ({Instalador.Peso(AppBytes)} app, {Instalador.Peso(CompartidoBytes)} compartido)";
    }
}

/// <summary>Estado de una herramienta respecto a lo instalado en el equipo.</summary>
public enum EstadoTarjeta
{
    NoInstalada,
    Instalada,
    Actualizable,
}

/// <summary>Información completa para pintar una tarjeta de herramienta en la interfaz.</summary>
/// <param name="Ejecutable">El exe que abre «Abrir», o <c>null</c> si no está instalada o el zip no traía ninguno.</param>
/// <param name="DescargaParcialBytes">Lo que quedó en <c>Comun\descargas</c> de una descarga cortada de la versión disponible; se reanuda desde ahí.</param>
/// <param name="ZipDescargado">El zip de la versión disponible ya está entero en <c>Comun\descargas</c> (falló la extracción): no hay nada que bajar.</param>
/// <param name="InstalacionIncompleta">La app está extraída pero faltan módulos o modelos del manifiesto (se cortó después del zip): «Completar instalación».</param>
public sealed record TarjetaHerramienta(
    string Id,
    string Nombre,
    string Descripcion,
    PackageVersion VersionDisponible,
    PackageVersion? VersionInstalada,
    long TamanoDescarga,
    EspacioInstalada? EspacioInstalada,
    EstadoTarjeta Estado,
    string? Ejecutable = null,
    long DescargaParcialBytes = 0,
    bool ZipDescargado = false,
    bool InstalacionIncompleta = false)
{
    public bool PuedeInstalar => Estado == EstadoTarjeta.NoInstalada;
    public bool PuedeAbrir => Estado != EstadoTarjeta.NoInstalada && Ejecutable is not null;
    public bool PuedeActualizar => Estado == EstadoTarjeta.Actualizable;
    public bool PuedeQuitar => Estado != EstadoTarjeta.NoInstalada;

    /// <summary>Hay una descarga a medias de la versión disponible: el botón dirá «Reanudar».</summary>
    public bool PuedeReanudar => Estado != EstadoTarjeta.Instalada && DescargaParcialBytes > 0;

    public string TextoVersion => Estado switch
    {
        EstadoTarjeta.NoInstalada => $"Versión {VersionDisponible}",
        EstadoTarjeta.Instalada => $"Versión {VersionInstalada} (al día)",
        EstadoTarjeta.Actualizable when InstalacionIncompleta => $"Versión {VersionInstalada} (faltan módulos o modelos)",
        EstadoTarjeta.Actualizable => $"Instalada {VersionInstalada} · Disponible {VersionDisponible}",
        _ => VersionDisponible.ToString()
    };

    public string TextoDescarga => ZipDescargado
        ? $"Descarga: {Instalador.Peso(TamanoDescarga)} · ya descargado"
        : PuedeReanudar
            ? $"Descarga: {Instalador.Peso(TamanoDescarga)} · {Instalador.Peso(Math.Min(DescargaParcialBytes, TamanoDescarga))} ya bajados, se reanudará"
            : $"Descarga: {Instalador.Peso(TamanoDescarga)}";

    public string TextoEspacio => EspacioInstalada switch
    {
        null => "No instalada",
        _ => $"En disco: {EspacioInstalada.Formatear()}"
    };

    public string TextoInstalar => ZipDescargado ? "Instalar (ya descargado)" : PuedeReanudar ? "Reanudar descarga" : "Instalar";

    public string TextoActualizar => InstalacionIncompleta ? "Completar instalación"
        : ZipDescargado ? "Actualizar (ya descargado)"
        : PuedeReanudar ? "Reanudar actualización"
        : "Actualizar";

    /// <summary>Lo que dice la tarjeta recién recargada tras cancelar: qué quedó y qué botón sigue.</summary>
    public string TextoCancelada => InstalacionIncompleta ? "Instalación cancelada. Lo ya bajado se guarda: pulsa «Completar instalación» para seguir."
        : ZipDescargado ? "Descarga cancelada. El zip ya estaba completo: pulsa «Instalar» para extraerlo."
        : PuedeReanudar ? "Descarga cancelada. Lo ya bajado se guarda: pulsa «Reanudar» para seguir donde quedó."
        : "Descarga cancelada.";

    /// <summary>Carpeta que borra «Quitar»: <c>Herramientas\&lt;id&gt;</c>.</summary>
    public string CarpetaInstalada(Carpetas carpetas)
    {
        ArgumentNullException.ThrowIfNull(carpetas);
        return Path.Combine(carpetas.Herramientas, Id);
    }

    /// <summary>Lo que se le pregunta antes de quitar: qué carpeta se borra, cuánto ocupa y qué se queda.</summary>
    public string TextoConfirmarQuitar(Carpetas carpetas)
    {
        ArgumentNullException.ThrowIfNull(carpetas);
        string carpeta = CarpetaInstalada(carpetas);
        string ocupa = EspacioInstalada is { AppBytes: > 0 } e ? $" ({Instalador.Peso(e.AppBytes)})" : "";
        string compartido = EspacioInstalada is { CompartidoBytes: > 0 } c
            ? $" Los módulos y modelos compartidos ({Instalador.Peso(c.CompartidoBytes)}) se quedan en «{carpetas.Comun}» para las demás herramientas."
            : $" Lo compartido en «{carpetas.Comun}» se queda para las demás herramientas.";
        return $"Se borrará la carpeta «{carpeta}»{ocupa} con {Nombre} {VersionInstalada}.{compartido}";
    }
}

/// <summary>Aviso discreto de «hay actualizaciones» al abrir o al buscar.</summary>
public static class Actualizaciones
{
    /// <summary>«Hay una actualización: Subtitular 1.0.0 → 1.1.0.» o <c>null</c> si todo está al día.</summary>
    public static string? Aviso(IReadOnlyList<TarjetaHerramienta> tarjetas)
    {
        ArgumentNullException.ThrowIfNull(tarjetas);
        // Una instalación a medias es Actualizable para que el botón vuelva a entrar, pero no es una versión nueva.
        var pendientes = tarjetas.Where(t => t.Estado == EstadoTarjeta.Actualizable && !t.InstalacionIncompleta).ToList();
        if (pendientes.Count == 0)
        {
            return null;
        }

        string lista = string.Join(", ", pendientes.Select(t => $"{t.Nombre} {t.VersionInstalada} → {t.VersionDisponible}"));
        return pendientes.Count == 1 ? $"Hay una actualización: {lista}." : $"Hay {pendientes.Count} actualizaciones: {lista}.";
    }
}

/// <summary>Nombres y descripciones amigables para las herramientas del catálogo de Dathinel.</summary>
public static class CatalogoMetadatos
{
    public static (string Nombre, string Descripcion) Obtener(string id) => id.ToLowerInvariant() switch
    {
        "subtitular" => ("Subtitular", "Subtítulos automáticos, traducción a otros idiomas y lectura en voz alta."),
        "rastrear" => ("Rastrear", "Seguimiento y visión por IA para video: puntos, fondo, rostros y encuadre."),
        "rodaje" => ("Rodaje", "Editor de video no lineal con IA local para Windows."),
        _ => (char.ToUpperInvariant(id[0]) + id[1..], $"Herramienta {id} del catálogo firmado.")
    };
}

/// <summary>Progreso detallado de una descarga o instalación con velocidad y progreso agregado.</summary>
public sealed record ProgresoDetallado(
    string Fase,
    string Archivo,
    long BytesRecibidos,
    long BytesTotales,
    double VelocidadBytesPorSegundo,
    long BytesAgregadosRecibidos,
    long BytesAgregadosTotales,
    double ProgresoAgregado)
{
    public double ProgresoArchivo => BytesTotales <= 0 ? 0 : Math.Clamp(BytesRecibidos / (double)BytesTotales, 0, 1);

    /// <summary>Lo que falta de todo lo que hay que bajar a la velocidad medida, o <c>null</c> sin velocidad aún.</summary>
    public TimeSpan? TiempoRestante
    {
        get
        {
            long pendientes = BytesAgregadosTotales - BytesAgregadosRecibidos;
            if (VelocidadBytesPorSegundo <= 0 || pendientes <= 0)
            {
                return null;
            }

            double segundos = pendientes / VelocidadBytesPorSegundo;
            return segundos > TimeSpan.MaxValue.TotalSeconds / 2 ? null : TimeSpan.FromSeconds(segundos);
        }
    }

    public string FormatearVelocidad() => $"{Instalador.Peso((long)Math.Max(0, VelocidadBytesPorSegundo))}/s";

    /// <summary>« (2.3 MB/s · quedan ~0:07)», « (2.3 MB/s)» sin tiempo, o vacío sin velocidad: el único sitio que arma ese sufijo.</summary>
    public string TextoRitmo() => VelocidadBytesPorSegundo <= 0 ? ""
        : TiempoRestante is { } restante ? $" ({FormatearVelocidad()} · quedan ~{Tiempos.Formatear(restante)})"
        : $" ({FormatearVelocidad()})";

    /// <summary>«subtitular: 12.0 MB de 28.0 MB (2.3 MB/s · quedan ~0:07)», o solo la fase si no hay bytes.</summary>
    public string TextoProgreso() => BytesTotales <= 0
        ? Fase
        : $"{Archivo}: {Instalador.Peso(BytesRecibidos)} de {Instalador.Peso(BytesTotales)}{TextoRitmo()}";
}
