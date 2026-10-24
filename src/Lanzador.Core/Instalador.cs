using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rodaje.Packages;
using Rodaje.Packages.Download;

namespace Lanzador.Core;

/// <summary>Error que corrige la persona (app desconocida, no instalada); el mensaje se muestra tal cual.</summary>
public sealed class ErrorDeUso(string message) : Exception(message);

/// <summary>Dónde van las herramientas y lo que comparten (descargas, módulos y modelos, para no bajarlos dos veces).</summary>
public sealed record Carpetas(string Herramientas, string Comun)
{
    public static Carpetas PorDefecto()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new(Path.Combine(local, "Dathinel", "Herramientas"), Path.Combine(local, "Dathinel", "Comun"));
    }

    public string Descargas => Path.Combine(Comun, "descargas");
    public string Modulos => Path.Combine(Comun, "modulos");
    public string Modelos => Path.Combine(Comun, "modelos");
}

/// <summary>Una herramienta del índice: la versión más nueva publicada y la instalada, si la hay.</summary>
public sealed record Herramienta(string Id, PackageVersion Version, long Size, PackageVersion? Instalada);

/// <summary><c>herramienta.json</c> dentro del zip de una app: los módulos del catálogo que necesita.</summary>
internal sealed record ManifiestoHerramienta(string[]? Modulos);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(ManifiestoHerramienta))]
internal sealed partial class ManifiestoJson : JsonSerializerContext;

/// <summary>
/// Lista, instala, actualiza y quita herramientas desde el índice firmado. Una app es una entrada <c>app</c> del índice (un zip
/// autocontenido) que va a <c>Herramientas\&lt;app&gt;\&lt;versión&gt;</c>; sus módulos van a <c>Comun\modulos\&lt;id&gt;\&lt;versión&gt;</c>
/// y los modelos de cada módulo al almacén <c>Comun\modelos</c>, enlazados donde el módulo los busca.
/// </summary>
public sealed class Instalador : IDisposable
{
    public const string ManifiestoNombre = "herramienta.json";

    private readonly Carpetas _carpetas;
    private readonly GitHubReleaseDownloader _indice;
    private readonly FileDownloader _archivos;
    private readonly string? _indiceLocal;
    private readonly string? _clave;
    private readonly DownloadUrlPolicy _politica;

    /// <param name="indice">URL del <c>indice.json</c> (la firma es la misma dirección con <c>.sig</c>) o la ruta de un archivo local.</param>
    /// <param name="clavePublica">Clave P-256 en base64; por defecto la del catálogo de Dathinel que viaja en el exe.</param>
    public Instalador(Carpetas carpetas, string indice, string? clavePublica, HttpClient http)
    {
        _carpetas = carpetas ?? throw new ArgumentNullException(nameof(carpetas));
        ArgumentException.ThrowIfNullOrWhiteSpace(indice);
        ArgumentNullException.ThrowIfNull(http);
        // Mensaje propio: el del framework sale en inglés.
        if (!Uri.TryCreate(File.Exists(indice) ? Path.GetFullPath(indice) : indice, UriKind.Absolute, out Uri? url) || (url.IsFile && !File.Exists(url.LocalPath)))
        {
            throw new PackageDownloadException($"El índice «{indice}» no es una URL ni un archivo que exista.");
        }

        _indiceLocal = url.IsFile ? url.LocalPath : null;
        _clave = clavePublica ?? ClaveDelCatalogo();
        _archivos = new FileDownloader(http);
        // Un índice de este equipo (pruebas, un servidor local) puede apuntar a archivos de este equipo.
        _politica = url.IsLoopback || url.IsFile ? DownloadUrlPolicy.WithLoopback("Dathinel") : DownloadUrlPolicy.Default;
        _indice = new GitHubReleaseDownloader(new GitHubReleaseOptions
        {
            IndexUrl = url,
            PublicKey = _clave,
            DownloadFolder = _carpetas.Descargas,
            AppVersion = Version,
            Policy = _politica,
        }, http) { Files = _archivos };
    }

    public static Uri IndiceDeDathinel { get; } = new("https://github.com/Dathinel/rodaje-catalogo/releases/latest/download/indice.json");

    /// <summary>Versión del Lanzador, comparada con el <c>minApp</c> de las entradas <c>app</c>.</summary>
    public static PackageVersion Version { get; } = PackageVersion.Parse(typeof(Instalador).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");

    public static string? ClaveDelCatalogo()
    {
        using Stream? stream = typeof(Instalador).Assembly.GetManifestResourceStream("Lanzador.clave-catalogo.txt");
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        string text = reader.ReadToEnd().Trim();
        return text.Length == 0 ? null : text;
    }

    /// <exception cref="PackageDownloadException">No se pudo leer el índice o su firma no es válida.</exception>
    public async Task<SignedIndex> IndiceAsync(CancellationToken ct)
    {
        if (_indiceLocal is null)
        {
            return await _indice.GetIndexAsync(cancellationToken: ct).ConfigureAwait(false);
        }

        using ECDsa clave = IndexSignature.TryImportPublicKey(_clave) ?? throw new PackageDownloadException("La clave pública del catálogo no es válida.");
        try
        {
            byte[] json = await File.ReadAllBytesAsync(_indiceLocal, ct).ConfigureAwait(false);
            byte[] firma = await File.ReadAllBytesAsync(_indiceLocal + ".sig", ct).ConfigureAwait(false);
            return SignedIndex.Load(json, firma, clave, _politica);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PackageDownloadException($"No se pudo leer el índice «{_indiceLocal}» o su firma «.sig».", ex);
        }
        catch (FormatException ex)
        {
            throw new PackageDownloadException(ex.Message, ex);
        }
    }

    public async Task<IReadOnlyList<Herramienta>> ListaAsync(CancellationToken ct)
    {
        SignedIndex indice = await IndiceAsync(ct).ConfigureAwait(false);
        return [.. indice.Entries.Where(e => e.Kind == IndexEntryKind.App && e.WorksWith(Version))
            .GroupBy(e => e.Id, StringComparer.Ordinal)
            .Select(g => g.MaxBy(e => e.Version)!)
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .Select(e => new Herramienta(e.Id, e.Version, e.Size, Instalada(e.Id)))];
    }

    /// <summary>La versión instalada más nueva de <paramref name="id"/>, o <c>null</c>.</summary>
    public PackageVersion? Instalada(string id) => VersionesInstaladas(id).MaxBy(v => v);

    /// <summary>Ids de las herramientas con alguna versión instalada.</summary>
    public IReadOnlyList<string> Instaladas() =>
        Directory.Exists(_carpetas.Herramientas)
            ? [.. Directory.EnumerateDirectories(_carpetas.Herramientas).Select(Path.GetFileName).Where(id => Identifiers.IsValid(id) && Instalada(id!) is not null).Select(id => id!).Order(StringComparer.Ordinal)]
            : [];

    /// <summary>Instala la versión más nueva de <paramref name="id"/> (y sus módulos y modelos); si ya está, no hace nada. Devuelve la versión que queda.</summary>
    /// <exception cref="ErrorDeUso">El índice no tiene esa app.</exception>
    /// <exception cref="PackageDownloadException">Red, firma o huella (lo bajado con huella mala ya se borró).</exception>
    public async Task<PackageVersion> InstalarAsync(string id, IProgress<string>? progreso, CancellationToken ct)
    {
        SignedIndex indice = await IndiceAsync(ct).ConfigureAwait(false);
        IndexEntry entrada = indice.Latest(id, Version, IndexEntryKind.App)
            ?? throw new ErrorDeUso(indice.Newest(id) is { MinApp: { } min }
                ? $"«{id}» necesita el Lanzador {min} o posterior."
                : $"El catálogo no tiene ninguna herramienta llamada «{id}». Mira «lanzador lista».");

        string destino = Path.Combine(_carpetas.Herramientas, id, entrada.Version.ToString());
        if (!Directory.Exists(destino))
        {
            string zip = Path.Combine(_carpetas.Descargas, entrada.Sha256 + ".zip");
            await DescargarAsync(id, entrada, zip, progreso, ct).ConfigureAwait(false);
            Extraer(zip, destino, ct);
            File.Delete(zip);
        }

        string manifiesto = Path.Combine(destino, ManifiestoNombre);
        if (File.Exists(manifiesto))
        {
            await using FileStream stream = File.OpenRead(manifiesto);
            ManifiestoHerramienta? leido = await JsonSerializer.DeserializeAsync(stream, ManifiestoJson.Default.ManifiestoHerramienta, ct).ConfigureAwait(false);
            foreach (string modulo in leido?.Modulos ?? [])
            {
                await InstalarModuloAsync(indice, modulo, progreso, ct).ConfigureAwait(false);
            }
        }

        foreach (PackageVersion vieja in VersionesInstaladas(id).Where(v => !v.Equals(entrada.Version)))
        {
            Directory.Delete(Path.Combine(_carpetas.Herramientas, id, vieja.ToString()), recursive: true);
        }

        return entrada.Version;
    }

    /// <summary>Actualiza <paramref name="id"/> o, sin id, todas las instaladas. Devuelve las que cambiaron de versión.</summary>
    public async Task<IReadOnlyList<(string Id, PackageVersion De, PackageVersion A)>> ActualizarAsync(string? id, IProgress<string>? progreso, CancellationToken ct)
    {
        IReadOnlyList<string> ids = id is null ? Instaladas() : [id];
        var cambios = new List<(string, PackageVersion, PackageVersion)>();
        foreach (string app in ids)
        {
            PackageVersion antes = Instalada(app) ?? throw new ErrorDeUso($"«{app}» no está instalada; usa «lanzador instalar {app}».");
            PackageVersion ahora = await InstalarAsync(app, progreso, ct).ConfigureAwait(false);
            if (!ahora.Equals(antes))
            {
                cambios.Add((app, antes, ahora));
            }
        }

        return cambios;
    }

    /// <summary>Borra solo la carpeta de esa app; los módulos y modelos comunes se quedan.</summary>
    /// <exception cref="ErrorDeUso">No está instalada.</exception>
    public void Quitar(string id)
    {
        string carpeta = Path.Combine(_carpetas.Herramientas, id);
        if (!Identifiers.IsValid(id) || !Directory.Exists(carpeta))
        {
            throw new ErrorDeUso($"«{id}» no está instalada.");
        }

        Directory.Delete(carpeta, recursive: true);
    }

    public void Dispose() => _indice.Dispose();

    private IEnumerable<PackageVersion> VersionesInstaladas(string id)
    {
        string carpeta = Path.Combine(_carpetas.Herramientas, id);
        if (!Identifiers.IsValid(id) || !Directory.Exists(carpeta))
        {
            yield break;
        }

        foreach (string sub in Directory.EnumerateDirectories(carpeta))
        {
            if (PackageVersion.TryParse(Path.GetFileName(sub), out PackageVersion? v))
            {
                yield return v;
            }
        }
    }

    private async Task InstalarModuloAsync(SignedIndex indice, string id, IProgress<string>? progreso, CancellationToken ct)
    {
        // El minApp de un módulo habla de Rodaje, no del Lanzador: se toma el más nuevo publicado.
        IndexEntry entrada = indice.Entries.Where(e => e.Id == id && e.Kind == IndexEntryKind.Module).MaxBy(e => e.Version)
            ?? throw new PackageDownloadException($"El catálogo no tiene el módulo «{id}» que pide la herramienta.");
        string carpeta = Path.Combine(_carpetas.Modulos, id, entrada.Version.ToString());
        if (!Directory.Exists(carpeta))
        {
            string paquete = Path.Combine(_carpetas.Descargas, entrada.Sha256 + PackageManifest.FileExtension);
            await DescargarAsync(id, entrada, paquete, progreso, ct).ConfigureAwait(false);
            Extraer(paquete, carpeta, ct);
            File.Delete(paquete);
        }

        var almacen = new ModelStore(_carpetas.Modelos, _archivos, _politica);
        foreach (ModuleModel modelo in ModuleModels.Read(Path.Combine(carpeta, "paquete.json")))
        {
            await almacen.PlaceAsync(modelo, carpeta, Progreso(modelo.Id, modelo.Size, progreso), ct).ConfigureAwait(false);
        }
    }

    private async Task DescargarAsync(string id, IndexEntry entrada, string destino, IProgress<string>? progreso, CancellationToken ct)
    {
        try
        {
            await _archivos.DownloadAsync(new DownloadTarget(entrada.Url, entrada.Sha256, entrada.Size), destino, Progreso(id, entrada.Size, progreso), ct).ConfigureAwait(false);
        }
        catch (DownloadVerificationException ex)
        {
            throw new DownloadVerificationException($"«{id}» no es lo que firma el catálogo: {ex.Message}", ex);
        }
    }

    private static IProgress<DownloadProgress>? Progreso(string id, long total, IProgress<string>? progreso) =>
        progreso is null ? null : new Reporte(p => progreso.Report($"{id}: {(int)(p.Fraction * 100)} % de {Peso(total)}"));

    public static string Peso(long bytes) => bytes >= 1L << 30
        ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 30):F1} GB")
        : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0.1, bytes / (double)(1 << 20)):F1} MB");

    /// <summary>Descomprime a una carpeta temporal al lado y la renombra al final: el destino nunca existe a medias.</summary>
    private static void Extraer(string zip, string destino, CancellationToken ct)
    {
        string temporal = destino + ".instalando";
        if (Directory.Exists(temporal))
        {
            Directory.Delete(temporal, recursive: true);
        }

        try
        {
            using ZipArchive archivo = ZipFile.OpenRead(zip);
            foreach (ZipArchiveEntry entrada in archivo.Entries)
            {
                ct.ThrowIfCancellationRequested();
                bool carpeta = entrada.FullName.EndsWith('/') || entrada.FullName.EndsWith('\\');
                if (!SafePaths.TryNormalizeRelative(entrada.FullName.TrimEnd('/', '\\'), out string? relativa))
                {
                    throw new InvalidDataException($"El zip tiene una ruta no permitida: «{entrada.FullName}».");
                }

                string ruta = Path.GetFullPath(Path.Combine(temporal, relativa));
                if (!SafePaths.IsInside(ruta, temporal))
                {
                    throw new InvalidDataException($"El zip intenta escribir fuera de su carpeta: «{entrada.FullName}».");
                }

                if (carpeta)
                {
                    Directory.CreateDirectory(ruta);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
                entrada.ExtractToFile(ruta, overwrite: false);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            Directory.Move(temporal, destino);
        }
        catch
        {
            if (Directory.Exists(temporal))
            {
                Directory.Delete(temporal, recursive: true);
            }

            throw;
        }
    }

    private sealed class Reporte(Action<DownloadProgress> informar) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => informar(value);
    }
}
