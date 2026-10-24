using System.IO.Compression;
using System.Text;
using Lanzador.Core;
using Rodaje.Packages;
using Rodaje.Packages.Download;
using Rodaje.TestServer;

namespace Lanzador.Tests;

public sealed class TarjetasYEspacioTests : IAsyncLifetime, IDisposable
{
    private readonly TestHttpServer _servidor = TestHttpServer.Start();
    private readonly TestIndexSigner _firmante = new();
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "lanzador-core-tests-" + Guid.NewGuid().ToString("N")[..10]);

    private readonly byte[] _modelo = TestPayloads.Deterministic(50_000, seed: 10);
    private readonly byte[] _modulo;
    private readonly byte[] _app1;
    private readonly byte[] _app2;

    public TarjetasYEspacioTests()
    {
        Directory.CreateDirectory(_temp);
        string modeloUrl = _servidor.UrlFor("modelo.bin").AbsoluteUri;
        _modulo = Zip(("paquete.json", $$"""
            { "formatVersion": 1, "id": "modulo-eco", "version": "2.0.0", "kind": "module", "name": "Eco", "author": "Dathinel", "license": "GPL-3.0-only",
              "repository": "https://github.com/Dathinel/modulo-eco", "apps": ["rodaje"],
              "module": { "exe": "Eco.exe", "models": [ { "id": "eco-pequeno", "file": "modelos/eco.bin", "size": {{_modelo.Length}}, "sha256": "{{TestPayloads.Sha256Hex(_modelo)}}", "url": "{{modeloUrl}}" } ] } }
            """), ("Eco.exe", "MZ"));
        _app1 = Zip(("Subtitular.exe", Encoding.Latin1.GetString(TestPayloads.Deterministic(100_000, seed: 1))),
            ("herramienta.json", """{ "modulos": ["modulo-eco"] }"""));
        _app2 = Zip(("Subtitular.exe", Encoding.Latin1.GetString(TestPayloads.Deterministic(120_000, seed: 2))),
            ("herramienta.json", """{ "modulos": ["modulo-eco"] }"""));

        _servidor.Map("modelo.bin", TestResponse.File(_modelo));
        _servidor.Map("modulo.paquete", TestResponse.File(_modulo));
        _servidor.Map("app1.zip", TestResponse.File(_app1));
        _servidor.Map("app2.zip", TestResponse.File(_app2));
        Publicar(Indice(Entrada("subtitular", "1.0.0", "app", "app1.zip", _app1)));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _servidor.DisposeAsync();
        Assert.Empty(_servidor.Faults);
    }

    public void Dispose()
    {
        _firmante.Dispose();
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private Carpetas CarpetasPrueba => new(Path.Combine(_temp, "Herramientas"), Path.Combine(_temp, "Comun"));

    private string Entrada(string id, string version, string kind, string ruta, byte[] contenido, string? sha = null) =>
        $$"""{ "id": "{{id}}", "version": "{{version}}", "kind": "{{kind}}", "url": "{{_servidor.UrlFor(ruta).AbsoluteUri}}", "sha256": "{{sha ?? TestPayloads.Sha256Hex(contenido)}}", "size": {{contenido.Length}} }""";

    private string Indice(params string[] entradas) => IndiceConFecha("2026-10-24T12:00:00Z", entradas);

    private string IndiceConFecha(string fecha, params string[] entradas) =>
        $$"""{ "formatVersion": 1, "generated": "{{fecha}}", "entries": [ {{string.Join(",", [.. entradas, Entrada("modulo-eco", "2.0.0", "module", "modulo.paquete", _modulo)])}} ] }""";

    private void Publicar(string indice, string? firma = null)
    {
        byte[] json = Encoding.UTF8.GetBytes(indice);
        _servidor.Map("indice.json", TestResponse.File(json));
        _servidor.Map("indice.json.sig", TestResponse.File(Encoding.ASCII.GetBytes(firma ?? _firmante.Sign(json))));
    }

    private static byte[] Zip(params (string Nombre, string Contenido)[] entradas)
    {
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string nombre, string contenido) in entradas)
            {
                using Stream s = zip.CreateEntry(nombre).Open();
                s.Write(Encoding.Latin1.GetBytes(contenido));
            }
        }

        return memoria.ToArray();
    }

    private Instalador CrearInstalador(HttpClient http) =>
        new(CarpetasPrueba, _servidor.UrlFor("indice.json").AbsoluteUri, _firmante.PublicKeyBase64, http);

    [Fact]
    public async Task ObtenerTarjetas_HerramientaNoInstalada()
    {
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        var tarjetas = await instalador.ObtenerTarjetasAsync();
        TarjetaHerramienta tarjeta = Assert.Single(tarjetas);

        Assert.Equal("subtitular", tarjeta.Id);
        Assert.Equal("Subtitular", tarjeta.Nombre);
        Assert.False(string.IsNullOrWhiteSpace(tarjeta.Descripcion));
        Assert.Equal(PackageVersion.Parse("1.0.0"), tarjeta.VersionDisponible);
        Assert.Null(tarjeta.VersionInstalada);
        Assert.Equal(_app1.Length, tarjeta.TamanoDescarga);
        Assert.Null(tarjeta.EspacioInstalada);
        Assert.Equal(EstadoTarjeta.NoInstalada, tarjeta.Estado);
        Assert.True(tarjeta.PuedeInstalar);
        Assert.False(tarjeta.PuedeAbrir);
        Assert.False(tarjeta.PuedeActualizar);
        Assert.False(tarjeta.PuedeQuitar);
    }

    [Fact]
    public async Task ObtenerTarjetas_HerramientaInstaladaYEspacioCalculado()
    {
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        var reportes = new List<ProgresoDetallado>();
        var progreso = new Progress<ProgresoDetallado>(reportes.Add);

        PackageVersion version = await instalador.InstalarAsync("subtitular", progreso, CancellationToken.None);
        Assert.Equal(PackageVersion.Parse("1.0.0"), version);

        var tarjetas = await instalador.ObtenerTarjetasAsync();
        TarjetaHerramienta tarjeta = Assert.Single(tarjetas);

        Assert.Equal(EstadoTarjeta.Instalada, tarjeta.Estado);
        Assert.Equal(version, tarjeta.VersionInstalada);
        Assert.False(tarjeta.PuedeInstalar);
        Assert.True(tarjeta.PuedeAbrir);
        Assert.False(tarjeta.PuedeActualizar);
        Assert.True(tarjeta.PuedeQuitar);

        Assert.NotNull(tarjeta.EspacioInstalada);
        Assert.True(tarjeta.EspacioInstalada.AppBytes > 0, "Debe tener bytes de app");
        Assert.True(tarjeta.EspacioInstalada.CompartidoBytes > 0, "Debe tener bytes compartidos (módulo + modelo)");
        Assert.Contains("compartido", tarjeta.EspacioInstalada.Formatear());

        // Verificar ejecutable detectado
        string? exe = instalador.BuscarEjecutable("subtitular");
        Assert.NotNull(exe);
        Assert.True(File.Exists(exe));
        Assert.EndsWith("Subtitular.exe", exe);

        // Verificar progreso detallado reportado
        Assert.NotEmpty(reportes);
        Assert.Contains(reportes, r => r.BytesTotales > 0 && r.ProgresoAgregado > 0);
    }

    [Fact]
    public async Task ObtenerTarjetas_Actualizable_CuandoHayNuevaVersion()
    {
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        await instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);

        // Publicar versión 1.1.0 en el catálogo con fecha posterior
        Publicar(IndiceConFecha("2026-10-24T13:00:00Z", Entrada("subtitular", "1.0.0", "app", "app1.zip", _app1), Entrada("subtitular", "1.1.0", "app", "app2.zip", _app2)));

        var tarjetas = await instalador.ObtenerTarjetasAsync(refrescar: true);
        TarjetaHerramienta tarjeta = Assert.Single(tarjetas);

        Assert.Equal(EstadoTarjeta.Actualizable, tarjeta.Estado);
        Assert.Equal(PackageVersion.Parse("1.0.0"), tarjeta.VersionInstalada);
        Assert.Equal(PackageVersion.Parse("1.1.0"), tarjeta.VersionDisponible);
        Assert.True(tarjeta.PuedeAbrir);
        Assert.True(tarjeta.PuedeActualizar);
        Assert.True(tarjeta.PuedeQuitar);
        Assert.False(tarjeta.PuedeInstalar);
    }

    [Fact]
    public async Task Cancelacion_DetieneInmediato_YNoDejaRestos()
    {
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancelado antes o justo al empezar

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, cts.Token);
        });

        // La carpeta de la app no debe existir
        string destino = Path.Combine(CarpetasPrueba.Herramientas, "subtitular", "1.0.0");
        Assert.False(Directory.Exists(destino), "La carpeta destino no debe quedar a medias");
    }

    [Fact]
    public void MensajesError_TraduceCorrectamenteSinTrazas()
    {
        Assert.Equal("La firma del catálogo o de los archivos descargados no es válida.",
            MensajesError.Describir(new DownloadVerificationException("Bad signature")));

        Assert.Equal("Sin conexión con el catálogo. Comprueba tu conexión a internet.",
            MensajesError.Describir(new HttpRequestException("No such host")));

        Assert.Equal("Operación cancelada.",
            MensajesError.Describir(new OperationCanceledException()));

        Assert.Equal("No hay suficiente espacio en disco para completar la descarga.",
            MensajesError.Describir(new IOException("Disk full", unchecked((int)0x80070070))));
    }

    [Fact]
    public void EspacioTotalDathinel_SumaArchivosExistentes()
    {
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        string carpetaDathinel = instalador.RaizDathinel;
        Directory.CreateDirectory(carpetaDathinel);
        File.WriteAllBytes(Path.Combine(carpetaDathinel, "test.dat"), new byte[1024]);

        long total = instalador.EspacioTotalDathinel();
        Assert.True(total >= 1024, $"Debe sumar al menos 1024 bytes, dio {total}");
    }
}
