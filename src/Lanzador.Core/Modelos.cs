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
public sealed record TarjetaHerramienta(
    string Id,
    string Nombre,
    string Descripcion,
    PackageVersion VersionDisponible,
    PackageVersion? VersionInstalada,
    long TamanoDescarga,
    EspacioInstalada? EspacioInstalada,
    EstadoTarjeta Estado)
{
    public bool PuedeInstalar => Estado == EstadoTarjeta.NoInstalada;
    public bool PuedeAbrir => Estado != EstadoTarjeta.NoInstalada;
    public bool PuedeActualizar => Estado == EstadoTarjeta.Actualizable;
    public bool PuedeQuitar => Estado != EstadoTarjeta.NoInstalada;

    public string TextoVersion => Estado switch
    {
        EstadoTarjeta.NoInstalada => $"Versión {VersionDisponible}",
        EstadoTarjeta.Instalada => $"Versión {VersionInstalada} (al día)",
        EstadoTarjeta.Actualizable => $"Instalada {VersionInstalada} · Disponible {VersionDisponible}",
        _ => VersionDisponible.ToString()
    };

    public string TextoDescarga => $"Descarga: {Instalador.Peso(TamanoDescarga)}";

    public string TextoEspacio => EspacioInstalada switch
    {
        null => "No instalada",
        _ => $"En disco: {EspacioInstalada.Formatear()}"
    };
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

    public string FormatearVelocidad() => $"{Instalador.Peso((long)Math.Max(0, VelocidadBytesPorSegundo))}/s";

    public string TextoProgreso()
    {
        if (BytesTotales <= 0)
        {
            return Fase;
        }

        return $"{Archivo}: {Instalador.Peso(BytesRecibidos)} de {Instalador.Peso(BytesTotales)} ({FormatearVelocidad()})";
    }
}
