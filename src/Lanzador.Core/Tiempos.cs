namespace Lanzador.Core;

/// <summary>Tiempos de una operación para la interfaz: transcurrido y estimación lineal de lo que queda.</summary>
public static class Tiempos
{
    public static string Formatear(TimeSpan t) => t.TotalHours >= 1
        ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
        : $"{t.Minutes}:{t.Seconds:00}";

    /// <summary>«0:42 · quedan ~1:10», o solo lo transcurrido mientras no haya con qué estimar (menos del 2 % o de 2 s).</summary>
    public static string Resumen(TimeSpan transcurrido, double fraccion)
    {
        string texto = Formatear(transcurrido);
        if (fraccion is < 0.02 or >= 1 || transcurrido.TotalSeconds < 2)
        {
            return texto;
        }

        TimeSpan restante = TimeSpan.FromSeconds(transcurrido.TotalSeconds * (1 - fraccion) / fraccion);
        return $"{texto} · quedan ~{Formatear(restante)}";
    }
}
