using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lanzador.Core;

/// <summary>Posición y tamaño de la ventana en píxeles físicos, para recordarla entre sesiones.</summary>
public sealed record EstadoVentana(int X, int Y, int Ancho, int Alto, bool Maximizada)
{
    public const int AnchoMinimo = 640;
    public const int AltoMinimo = 480;

    public static string RutaPorDefecto { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dathinel", "Lanzador", "ventana.json");

    /// <summary>Lee el estado guardado; null si no hay archivo, no se puede leer o el tamaño no es razonable.</summary>
    public static EstadoVentana? Cargar(string ruta)
    {
        try
        {
            if (!File.Exists(ruta))
            {
                return null;
            }

            EstadoVentana? e = JsonSerializer.Deserialize(File.ReadAllText(ruta), VentanaJson.Default.EstadoVentana);
            return e is { Ancho: >= AnchoMinimo and <= 16384, Alto: >= AltoMinimo and <= 16384 } ? e : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Guardar(string ruta)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, JsonSerializer.Serialize(this, VentanaJson.Default.EstadoVentana));
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(EstadoVentana))]
internal sealed partial class VentanaJson : JsonSerializerContext;
