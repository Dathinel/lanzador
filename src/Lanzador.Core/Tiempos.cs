namespace Lanzador.Core;

/// <summary>Tiempos de una operación para la interfaz: transcurrido y estimación de lo que queda.</summary>
public static class Tiempos
{
    public static string Formatear(TimeSpan t) => t.TotalHours >= 1
        ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
        : $"{t.Minutes}:{t.Seconds:00}";

    /// <summary>«0:42 · quedan ~1:10», o solo lo transcurrido mientras no haya con qué estimar (menos del 2 % o de 2 s).</summary>
    public static string Resumen(TimeSpan transcurrido, double fraccion) => Resumen(transcurrido, fraccion, restanteMedido: null);

    /// <summary>
    /// Como <see cref="Resumen(TimeSpan, double)"/>, pero si la descarga midió lo que queda a su velocidad real
    /// (<see cref="ProgresoDetallado.TiempoRestante"/>) se usa eso en vez de la regla de tres sobre la fracción.
    /// </summary>
    public static string Resumen(TimeSpan transcurrido, double fraccion, TimeSpan? restanteMedido)
    {
        string texto = Formatear(transcurrido);
        if (transcurrido.TotalSeconds < 2 || fraccion >= 1)
        {
            return texto;
        }

        if (restanteMedido is { } medido && medido > TimeSpan.Zero)
        {
            return $"{texto} · quedan ~{Formatear(medido)}";
        }

        if (fraccion < 0.02)
        {
            return texto;
        }

        TimeSpan restante = TimeSpan.FromSeconds(transcurrido.TotalSeconds * (1 - fraccion) / fraccion);
        return $"{texto} · quedan ~{Formatear(restante)}";
    }
}
