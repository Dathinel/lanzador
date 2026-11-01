using System.IO.Compression;
using System.Text;
using Lanzador.Core;
using Rodaje.Packages;
using Rodaje.TestServer;

namespace Lanzador.Tests;

/// <summary>
/// Avisos de versiones nuevas de las herramientas: qué decide la tarjeta según la versión instalada en el PC y la del
/// catálogo (nueva, igual, menor, no instalada), el texto del botón «Actualizar a X» y el recuento de la cabecera.
/// </summary>
public sealed class ActualizacionesTests : IAsyncLifetime, IDisposable
{
    private readonly TestHttpServer _servidor = TestHttpServer.Start();
    private readonly TestIndexSigner _firmante = new();
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "lanzador-act-tests-" + Guid.NewGuid().ToString("N")[..10]);
    private readonly byte[] _app = Zip(("Subtitular.exe", "MZ"));
    private readonly byte[] _otra = Zip(("Rastrear.exe", "MZ"));

    public ActualizacionesTests()
    {
        Directory.CreateDirectory(_temp);
        _servidor.Map("app.zip", TestResponse.File(_app));
        _servidor.Map("otra.zip", TestResponse.File(_otra));
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

    // 1. Decisión por app: la versión instalada se lee de Herramientas\<id>\<versión>, como ya hace el Instalador.

    [Theory]
    [InlineData("1.1.0", "1.0.0", EstadoTarjeta.Actualizable)]
    [InlineData("1.0.0", "1.0.0", EstadoTarjeta.Instalada)]
    [InlineData("0.9.0", "1.0.0", EstadoTarjeta.Instalada)]
    [InlineData("1.1.0", null, EstadoTarjeta.NoInstalada)]
    public async Task La_tarjeta_compara_la_version_del_catalogo_con_la_instalada(string catalogo, string? instalada, EstadoTarjeta esperado)
    {
        Publicar(Entrada("subtitular", catalogo, "app.zip", _app));
        if (instalada is not null)
        {
            Directory.CreateDirectory(Path.Combine(CarpetasPrueba.Herramientas, "subtitular", instalada));
        }

        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        TarjetaHerramienta t = Assert.Single(await instalador.ObtenerTarjetasAsync());

        Assert.Equal(esperado, t.Estado);
        Assert.Equal(esperado == EstadoTarjeta.Actualizable, t.PuedeActualizar);
        Assert.Equal(esperado == EstadoTarjeta.NoInstalada, t.PuedeInstalar);
        if (esperado == EstadoTarjeta.Actualizable)
        {
            Assert.Equal($"Actualizar a {catalogo}", t.TextoActualizar);
            Assert.Equal($"Instalada {instalada} · Disponible {catalogo}", t.TextoVersion);
        }
    }

    [Fact]
    public void El_boton_dice_a_que_version_va_tambien_al_reanudar()
    {
        TarjetaHerramienta t = Tarjeta("subtitular", "1.0.0", "1.1.0", EstadoTarjeta.Actualizable);
        Assert.Equal("Actualizar a 1.1.0", t.TextoActualizar);
        Assert.Equal("Reanudar actualización a 1.1.0", (t with { DescargaParcialBytes = 1_000 }).TextoActualizar);
        Assert.Equal("Reanudar actualización a 1.1.0", t.TextoReanudarActualizacion);
        Assert.Equal("Reanudar descarga", TarjetaHerramienta.TextoReanudarDescarga);
    }

    // 2. Recuento discreto arriba solo con más de una; con todas al día, nada.

    [Fact]
    public void Recuento_solo_con_mas_de_una_actualizacion()
    {
        TarjetaHerramienta nueva = Tarjeta("subtitular", "1.0.0", "1.1.0", EstadoTarjeta.Actualizable);
        TarjetaHerramienta alDia = Tarjeta("rastrear", "2.0.0", "2.0.0", EstadoTarjeta.Instalada);
        TarjetaHerramienta ninguna = Tarjeta("rodaje", null, "3.0.0", EstadoTarjeta.NoInstalada);

        Assert.Null(Actualizaciones.Recuento([]));
        Assert.Null(Actualizaciones.Recuento([alDia, ninguna]));
        Assert.Null(Actualizaciones.Recuento([nueva, alDia, ninguna]));
        Assert.Equal("2 actualizaciones", Actualizaciones.Recuento([nueva, alDia with { VersionDisponible = PackageVersion.Parse("2.1.0"), Estado = EstadoTarjeta.Actualizable }, ninguna]));
    }

    [Fact]
    public async Task Con_dos_herramientas_viejas_el_recuento_y_el_aviso_las_cuentan()
    {
        Publicar(Entrada("subtitular", "1.1.0", "app.zip", _app), Entrada("rastrear", "2.1.0", "otra.zip", _otra));
        Directory.CreateDirectory(Path.Combine(CarpetasPrueba.Herramientas, "subtitular", "1.0.0"));
        Directory.CreateDirectory(Path.Combine(CarpetasPrueba.Herramientas, "rastrear", "2.0.0"));

        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        IReadOnlyList<TarjetaHerramienta> tarjetas = await instalador.ObtenerTarjetasAsync();

        Assert.Equal("2 actualizaciones", Actualizaciones.Recuento(tarjetas));
        Assert.Equal("Hay 2 actualizaciones: Rastrear 2.0.0 → 2.1.0, Subtitular 1.0.0 → 1.1.0.", Actualizaciones.Aviso(tarjetas));
        Assert.All(tarjetas, t => Assert.StartsWith("Actualizar a ", t.TextoActualizar, StringComparison.Ordinal));
    }

    private static TarjetaHerramienta Tarjeta(string id, string? instalada, string disponible, EstadoTarjeta estado) =>
        new(id, CatalogoMetadatos.Obtener(id).Nombre, "desc", PackageVersion.Parse(disponible), instalada is null ? null : PackageVersion.Parse(instalada),
            28_000_000, instalada is null ? null : new EspacioInstalada(30_000_000, 0), estado, instalada is null ? null : $"{id}.exe");

    private string Entrada(string id, string version, string ruta, byte[] contenido) =>
        $$"""{ "id": "{{id}}", "version": "{{version}}", "kind": "app", "url": "{{_servidor.UrlFor(ruta).AbsoluteUri}}", "sha256": "{{TestPayloads.Sha256Hex(contenido)}}", "size": {{contenido.Length}} }""";

    private void Publicar(params string[] entradas)
    {
        byte[] json = Encoding.UTF8.GetBytes($$"""{ "formatVersion": 1, "generated": "2026-11-01T12:00:00Z", "entries": [ {{string.Join(",", entradas)}} ] }""");
        _servidor.Map("indice.json", TestResponse.File(json));
        _servidor.Map("indice.json.sig", TestResponse.File(Encoding.ASCII.GetBytes(_firmante.Sign(json))));
    }

    private Instalador CrearInstalador(HttpClient http) =>
        new(CarpetasPrueba, _servidor.UrlFor("indice.json").AbsoluteUri, _firmante.PublicKeyBase64, http);

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
}
