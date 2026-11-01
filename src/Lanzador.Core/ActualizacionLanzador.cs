using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rodaje.Packages;

namespace Lanzador.Core;

/// <summary>
/// Ajustes del Lanzador en <c>%LOCALAPPDATA%\Dathinel\Lanzador\ajustes.json</c>: si avisa de versiones nuevas del propio
/// Lanzador, cuándo consultó GitHub por última vez y qué versión descartó con «Más tarde».
/// </summary>
public sealed record Ajustes(bool AvisarVersionNueva = true, DateTimeOffset? UltimaConsultaLanzador = null, string? VersionDescartada = null)
{
    public static string RutaPorDefecto { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dathinel", "Lanzador", "ajustes.json");

    /// <summary>Lee los ajustes; los de fábrica si no hay archivo o no se puede leer.</summary>
    public static Ajustes Cargar(string ruta)
    {
        try
        {
            if (!File.Exists(ruta))
            {
                return new Ajustes();
            }

            return JsonSerializer.Deserialize(File.ReadAllText(ruta), AjustesJson.Default.Ajustes) ?? new Ajustes();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Ajustes();
        }
    }

    public void Guardar(string ruta)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, JsonSerializer.Serialize(this, AjustesJson.Default.Ajustes));
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(Ajustes))]
internal sealed partial class AjustesJson : JsonSerializerContext;

/// <summary>La última release publicada del Lanzador en GitHub: versión del <c>tag_name</c> (<c>vX.Y.Z</c>) y página de novedades.</summary>
public sealed record ReleaseLanzador(PackageVersion Version, Uri Url);

/// <summary>De dónde sale el JSON de la última release; en la app, la API pública de GitHub; en las pruebas, datos falsos.</summary>
public interface IFuenteReleases
{
    /// <summary>El JSON de la última release publicada; <c>null</c> si no hay ninguna.</summary>
    /// <exception cref="HttpRequestException">Sin red o respuesta que no es 200.</exception>
    /// <exception cref="OperationCanceledException">Pasó el tiempo límite.</exception>
    Task<string?> ObtenerUltimaAsync(CancellationToken ct);
}

/// <summary>Consulta <c>api.github.com</c> sin token, con <c>User-Agent</c> «Lanzador/versión» y 5 s de tiempo límite.</summary>
/// <param name="url">Solo para las pruebas: un servidor local en vez de GitHub.</param>
/// <param name="tiempoLimite">Solo para las pruebas: un plazo más corto que los 5 s.</param>
public sealed class FuenteReleasesGitHub(HttpClient http, PackageVersion version, Uri? url = null, TimeSpan? tiempoLimite = null) : IFuenteReleases
{
    public static Uri Url { get; } = new("https://api.github.com/repos/Dathinel/lanzador/releases/latest");

    public static TimeSpan TiempoLimite { get; } = TimeSpan.FromSeconds(5);

    /// <summary>Una respuesta de GitHub ronda los 10 KB; más de esto no es una release.</summary>
    private const int MaximoBytes = 1 << 20;

    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));
    private readonly string _agente = $"Lanzador/{version ?? throw new ArgumentNullException(nameof(version))}";
    private readonly Uri _url = url ?? Url;
    private readonly TimeSpan _tiempoLimite = tiempoLimite ?? TiempoLimite;

    public async Task<string?> ObtenerUltimaAsync(CancellationToken ct)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(_tiempoLimite);
        try
        {
            return await ObtenerJsonAsync(_url, limite.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // latest excluye las prereleases: si no hay una estable, consultar las publicadas.
        }

        string json = await ObtenerJsonAsync(new Uri(_url, "../releases?per_page=10"), limite.Token).ConfigureAwait(false);
        using JsonDocument documento = JsonDocument.Parse(json);
        if (documento.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("La lista de releases de GitHub no es un array.");
        }

        foreach (JsonElement release in documento.RootElement.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object
                || !release.TryGetProperty("draft", out JsonElement borrador)
                || borrador.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new JsonException("La release de GitHub no indica si es un borrador.");
            }

            if (!borrador.GetBoolean())
            {
                return release.GetRawText();
            }
        }

        return null;
    }

    private async Task<string> ObtenerJsonAsync(Uri url, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Get, url);
        peticion.Headers.UserAgent.Clear();
        peticion.Headers.UserAgent.ParseAdd(_agente);
        peticion.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using HttpResponseMessage respuesta = await _http.SendAsync(peticion, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        respuesta.EnsureSuccessStatusCode();
        if (respuesta.Content.Headers.ContentLength > MaximoBytes)
        {
            throw new HttpRequestException("La respuesta de GitHub es demasiado grande para ser una release.");
        }

        await using Stream cuerpo = await respuesta.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var memoria = new MemoryStream();
        byte[] trozo = new byte[16 * 1024];
        int leidos;
        while ((leidos = await cuerpo.ReadAsync(trozo, ct).ConfigureAwait(false)) > 0)
        {
            if (memoria.Length + leidos > MaximoBytes)
            {
                throw new HttpRequestException("La respuesta de GitHub es demasiado grande para ser una release.");
            }

            memoria.Write(trozo, 0, leidos);
        }

        return System.Text.Encoding.UTF8.GetString(memoria.GetBuffer(), 0, (int)memoria.Length);
    }
}

/// <summary>Lo que salió de comprobar si hay un Lanzador nuevo.</summary>
public enum ResultadoLanzador
{
    /// <summary>Hay una release más nueva que esta versión: la franja la anuncia.</summary>
    VersionNueva,

    /// <summary>No hay releases publicadas, o la última es esta versión o más vieja.</summary>
    AlDia,

    /// <summary>Hay versión nueva, pero se pulsó «Más tarde» para esa misma versión: silencio hasta la siguiente.</summary>
    Descartada,

    /// <summary>No han pasado 24 h desde la última consulta: no se ha preguntado a GitHub.</summary>
    NoTocaba,

    /// <summary>El interruptor «Avisar cuando haya una versión nueva» está apagado.</summary>
    Desactivado,

    /// <summary>Sin red, tiempo límite, error de GitHub o un JSON raro: silencio.</summary>
    SinRespuesta,
}

/// <param name="Ajustes">Los ajustes tal como quedan después de comprobar (última consulta al día); la ventana los guarda.</param>
public sealed record ComprobacionLanzador(ResultadoLanzador Resultado, ReleaseLanzador? Release, Ajustes Ajustes)
{
    /// <summary>«Lanzador 1.1.0 disponible», o <c>null</c> si no hay franja que mostrar.</summary>
    public string? TextoFranja => Resultado == ResultadoLanzador.VersionNueva && Release is not null ? $"Lanzador {Release.Version} disponible" : null;

    /// <summary>«Más tarde»: los ajustes con esa versión descartada, para no volver a avisar de ella.</summary>
    public Ajustes Descartar() => Release is null ? Ajustes : Ajustes with { VersionDescartada = Release.Version.ToString() };
}

/// <summary>
/// Decide si hay un Lanzador nuevo: compara el <c>tag_name</c> de la última release con la versión de la app, respeta el
/// interruptor, las 24 h entre consultas y la versión descartada con «Más tarde». A mano («Buscar actualizaciones») consulta
/// siempre y lo dice aunque esa versión se hubiera descartado.
/// </summary>
public sealed class ComprobadorLanzador(IFuenteReleases fuente, PackageVersion versionActual, TimeProvider? reloj = null)
{
    public static TimeSpan Intervalo { get; } = TimeSpan.FromHours(24);

    private readonly IFuenteReleases _fuente = fuente ?? throw new ArgumentNullException(nameof(fuente));
    private readonly PackageVersion _actual = versionActual ?? throw new ArgumentNullException(nameof(versionActual));
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;

    /// <summary>Lee <c>tag_name</c> (<c>vX.Y.Z</c>, también sin la «v») y <c>html_url</c>; <c>null</c> con cualquier cosa rara.</summary>
    public static ReleaseLanzador? Interpretar(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        ReleaseGitHub? release;
        try
        {
            release = JsonSerializer.Deserialize(json, ReleaseJson.Default.ReleaseGitHub);
        }
        catch (JsonException)
        {
            return null;
        }

        if (release is not { TagName: { } tag, HtmlUrl: { } html } || release.Draft)
        {
            return null;
        }

        tag = tag.Trim();
        if (tag.StartsWith('v') || tag.StartsWith('V'))
        {
            tag = tag[1..];
        }

        if (!PackageVersion.TryParse(tag, out PackageVersion? version)
            || !Uri.TryCreate(html, UriKind.Absolute, out Uri? url)
            || url.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        return new ReleaseLanzador(version, url);
    }

    /// <summary>Toca consultar si nunca se consultó o si han pasado 24 h (un reloj atrasado respecto a la última consulta también cuenta).</summary>
    public static bool Toca(Ajustes ajustes, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(ajustes);
        return ajustes.UltimaConsultaLanzador is not { } ultima || ahora - ultima >= Intervalo || ultima > ahora;
    }

    /// <param name="aMano">«Buscar actualizaciones»: se consulta aunque no toque, esté apagado el aviso o la versión se hubiera descartado.</param>
    public async Task<ComprobacionLanzador> ComprobarAsync(Ajustes ajustes, bool aMano, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ajustes);
        if (!aMano && !ajustes.AvisarVersionNueva)
        {
            return new ComprobacionLanzador(ResultadoLanzador.Desactivado, null, ajustes);
        }

        DateTimeOffset ahora = _reloj.GetUtcNow();
        if (!aMano && !Toca(ajustes, ahora))
        {
            return new ComprobacionLanzador(ResultadoLanzador.NoTocaba, null, ajustes);
        }

        string? json;
        try
        {
            json = await _fuente.ObtenerUltimaAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or JsonException)
        {
            // Sin red, tiempo límite o GitHub que no responde: no se anota la consulta, para volver a intentarlo al abrir.
            return new ComprobacionLanzador(ResultadoLanzador.SinRespuesta, null, ajustes);
        }

        Ajustes consultados = ajustes with { UltimaConsultaLanzador = ahora };
        if (json is null)
        {
            return new ComprobacionLanzador(ResultadoLanzador.AlDia, null, consultados);
        }

        ReleaseLanzador? release = Interpretar(json);
        if (release is null)
        {
            return new ComprobacionLanzador(ResultadoLanzador.SinRespuesta, null, consultados);
        }

        if (release.Version <= _actual)
        {
            return new ComprobacionLanzador(ResultadoLanzador.AlDia, release, consultados);
        }

        if (!aMano && string.Equals(consultados.VersionDescartada, release.Version.ToString(), StringComparison.Ordinal))
        {
            return new ComprobacionLanzador(ResultadoLanzador.Descartada, release, consultados);
        }

        return new ComprobacionLanzador(ResultadoLanzador.VersionNueva, release, consultados);
    }

    /// <summary>
    /// Lo que dice el aviso tras «Buscar actualizaciones»: las herramientas con versión nueva si las hay; si no, que no hay
    /// nada nuevo (o que el Lanzador no se pudo consultar). <c>null</c> cuando la franja del Lanzador ya lo dice todo.
    /// </summary>
    public static string? ResumenBusqueda(IReadOnlyList<TarjetaHerramienta> tarjetas, ComprobacionLanzador lanzador)
    {
        ArgumentNullException.ThrowIfNull(lanzador);
        if (Actualizaciones.Aviso(tarjetas) is { } herramientas)
        {
            return herramientas;
        }

        return lanzador.Resultado switch
        {
            ResultadoLanzador.VersionNueva => null,
            ResultadoLanzador.AlDia => "No hay nada nuevo: el Lanzador y las herramientas están al día.",
            _ => "Las herramientas están al día. No se pudo consultar si hay un Lanzador nuevo; inténtalo más tarde.",
        };
    }
}

/// <summary>Los campos de <c>releases/latest</c> que importan.</summary>
internal sealed record ReleaseGitHub(
    [property: JsonPropertyName("tag_name")] string? TagName,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("draft")] bool Draft);

[JsonSourceGenerationOptions(ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(ReleaseGitHub))]
internal sealed partial class ReleaseJson : JsonSerializerContext;
