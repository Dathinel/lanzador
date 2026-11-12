using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Lanzador.Core;
using Rodaje.Packages;
using Rodaje.Packages.Download;
using Rodaje.TestServer;

namespace Lanzador.Tests;

/// <summary>
/// Estados del disco en los que queda una instalación: carpetas de versión a medias o con nombres raros, zips y
/// «.parcial» que no cuadran, rutas difíciles de Windows (espacios, tildes, ñ, muy largas) y archivos de solo lectura.
/// </summary>
public sealed class EstadosDiscoTests : IAsyncLifetime, IDisposable
{
    private readonly TestHttpServer _servidor = TestHttpServer.Start();
    private readonly TestIndexSigner _firmante = new();
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "lanzador-disco-tests-" + Guid.NewGuid().ToString("N")[..10]);
    private readonly byte[] _app;

    public EstadosDiscoTests()
    {
        Directory.CreateDirectory(_temp);
        _app = Zip(("Subtitular.exe", "MZ" + Encoding.Latin1.GetString(TestPayloads.Deterministic(30_000, seed: 6))));
        _servidor.Map("app.zip", TestResponse.File(_app));
        Publicar(Indice(Entrada("subtitular", "1.0.0", "app.zip", _app)));
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
            QuitarSoloLectura(_temp);
            Directory.Delete(_temp, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static void QuitarSoloLectura(string carpeta)
    {
        foreach (string entrada in Directory.EnumerateFileSystemEntries(carpeta, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(entrada, File.GetAttributes(entrada) & ~FileAttributes.ReadOnly);
        }
    }

    /// <summary>Una unión (junction) en Windows, que no pide permisos; un enlace simbólico en los demás sistemas.</summary>
    private static void CrearEnlace(string enlace, string destino)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(enlace, destino);
            return;
        }

        using Process mklink = Process.Start(new ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", enlace, destino])
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
        mklink.WaitForExit();
        Assert.Equal(0, mklink.ExitCode);
    }

    private Carpetas CarpetasPrueba => new(Path.Combine(_temp, "Herramientas"), Path.Combine(_temp, "Comun"));

    private string Herramientas => CarpetasPrueba.Herramientas;

    private string Descarga(byte[] zip, string sufijo = "") => Path.Combine(CarpetasPrueba.Descargas, TestPayloads.Sha256Hex(zip) + ".zip" + sufijo);

    private string[] Cli(params string[] args) =>
        [.. args, "--indice", _servidor.UrlFor("indice.json").AbsoluteUri, "--clave", _firmante.PublicKeyBase64, "--destino", Herramientas, "--comun", CarpetasPrueba.Comun];

    private string Entrada(string id, string version, string ruta, byte[] contenido) =>
        $$"""{ "id": "{{id}}", "version": "{{version}}", "kind": "app", "url": "{{_servidor.UrlFor(ruta).AbsoluteUri}}", "sha256": "{{TestPayloads.Sha256Hex(contenido)}}", "size": {{contenido.Length.ToString(CultureInfo.InvariantCulture)}} }""";

    private string Indice(params string[] entradas) =>
        $$"""{ "formatVersion": 1, "generated": "2026-11-01T12:00:00Z", "entries": [ {{string.Join(",", entradas)}} ] }""";

    private void Publicar(string indice)
    {
        byte[] json = Encoding.UTF8.GetBytes(indice);
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

    // 1. Carpetas de versión que no son lo que parecen.

    [Fact]
    public async Task Carpeta_de_version_vacia_cuenta_como_instalada_sin_ejecutable()
    {
        Directory.CreateDirectory(Path.Combine(Herramientas, "subtitular", "1.0.0"));
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        TarjetaHerramienta t = Assert.Single(await instalador.ObtenerTarjetasAsync());

        Assert.Equal(EstadoTarjeta.Instalada, t.Estado);
        Assert.Equal(PackageVersion.Parse("1.0.0"), t.VersionInstalada);
        Assert.Null(t.Ejecutable);
        Assert.False(t.PuedeAbrir);
        Assert.True(t.PuedeQuitar);
        Assert.Equal("Versión 1.0.0 (al día)", t.TextoVersion);
        Assert.Null(instalador.BuscarEjecutable("subtitular"));
    }

    [Theory]
    [InlineData("beta")]
    [InlineData("v1.0.0")]
    [InlineData("1.0")]
    [InlineData("1.0.0.instalando")]
    public async Task Carpeta_que_no_es_version_no_cuenta_como_instalada(string nombre)
    {
        Directory.CreateDirectory(Path.Combine(Herramientas, "subtitular", nombre));
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        Assert.Null(instalador.Instalada("subtitular"));
        Assert.Empty(instalador.Instaladas());
        TarjetaHerramienta t = Assert.Single(await instalador.ObtenerTarjetasAsync());
        Assert.Equal(EstadoTarjeta.NoInstalada, t.Estado);
    }

    [Fact]
    public async Task Sobrante_punto_instalando_se_limpia_en_la_siguiente_instalacion()
    {
        string temporal = Path.Combine(Herramientas, "subtitular", "1.0.0.instalando");
        Directory.CreateDirectory(temporal);
        File.WriteAllText(Path.Combine(temporal, "resto.tmp"), "x");
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        await instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);

        Assert.False(Directory.Exists(temporal), "El «.instalando» de una extracción cortada se borra antes de empezar.");
        Assert.True(File.Exists(Path.Combine(Herramientas, "subtitular", "1.0.0", "Subtitular.exe")));
    }

    [Fact]
    public async Task Con_dos_versiones_instaladas_manda_la_mayor()
    {
        Directory.CreateDirectory(Path.Combine(Herramientas, "subtitular", "0.9.0"));
        string mayor = Path.Combine(Herramientas, "subtitular", "2.0.0");
        Directory.CreateDirectory(mayor);
        File.WriteAllText(Path.Combine(mayor, "Subtitular.exe"), "MZ");
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        Assert.Equal(PackageVersion.Parse("2.0.0"), instalador.Instalada("subtitular"));
        Assert.Equal(Path.Combine(mayor, "Subtitular.exe"), instalador.BuscarEjecutable("subtitular"));
        TarjetaHerramienta t = Assert.Single(await instalador.ObtenerTarjetasAsync());
        Assert.Equal(EstadoTarjeta.InstaladaMasNueva, t.Estado);
        Assert.Equal("Instalada 2.0.0, más nueva que el catálogo (1.0.0)", t.TextoVersion);
    }

    [Theory]
    [InlineData("Subtitular")]
    [InlineData("sub titular")]
    [InlineData("-subtitular")]
    public void Carpeta_con_nombre_que_no_es_id_se_ignora(string id)
    {
        Directory.CreateDirectory(Path.Combine(Herramientas, id, "1.0.0"));
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        Assert.Empty(instalador.Instaladas());
        Assert.Null(instalador.Instalada(id));
        Assert.Null(instalador.BuscarEjecutable(id));
    }

    [Fact]
    public async Task Un_archivo_donde_iba_la_carpeta_de_la_app_no_es_instalacion()
    {
        Directory.CreateDirectory(Herramientas);
        File.WriteAllText(Path.Combine(Herramientas, "subtitular"), "no es una carpeta");
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        Assert.Null(instalador.Instalada("subtitular"));
        Assert.Empty(instalador.Instaladas());
    }

    // 2. Zips y «.parcial» que no cuadran.

    [Fact]
    public async Task Parcial_de_cero_bytes_no_es_descarga_que_reanudar()
    {
        Directory.CreateDirectory(CarpetasPrueba.Descargas);
        File.WriteAllBytes(Descarga(_app, FileDownloader.PartialSuffix), []);
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        TarjetaHerramienta t = Assert.Single(await instalador.ObtenerTarjetasAsync());

        Assert.Equal(0, t.DescargaParcialBytes);
        Assert.False(t.PuedeReanudar);
        Assert.Equal("Instalar", t.TextoInstalar);
        Assert.DoesNotContain("reanudará", t.TextoDescarga, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Parcial_mas_largo_que_el_zip_no_se_fia_y_se_baja_entero()
    {
        Directory.CreateDirectory(CarpetasPrueba.Descargas);
        File.WriteAllBytes(Descarga(_app, FileDownloader.PartialSuffix), TestPayloads.Deterministic(_app.Length + 1_000, seed: 9));
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        // Un .parcial más largo que el zip no puede ser parte de él: la tarjeta no promete reanudar nada.
        TarjetaHerramienta t = Assert.Single(await instalador.ObtenerTarjetasAsync());
        Assert.Equal(0, t.DescargaParcialBytes);
        Assert.False(t.PuedeReanudar);
        Assert.Equal("Instalar", t.TextoInstalar);
        Assert.DoesNotContain("reanudará", t.TextoDescarga, StringComparison.Ordinal);

        await instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);

        // Y la descarga lo borra y pide el zip de cero, sin Range.
        Assert.Null(Assert.Single(_servidor.Requests, r => r.Path == "/app.zip").Range);
        Assert.Equal(EstadoTarjeta.Instalada, Assert.Single(await instalador.ObtenerTarjetasAsync()).Estado);
    }

    [Fact]
    public async Task Zip_descargado_corrupto_se_descarta_y_se_vuelve_a_bajar()
    {
        Directory.CreateDirectory(CarpetasPrueba.Descargas);
        File.WriteAllBytes(Descarga(_app), TestPayloads.Corrupt(_app, 100));
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        TarjetaHerramienta t = Assert.Single(await instalador.ObtenerTarjetasAsync());
        Assert.True(t.ZipDescargado, "Solo se mira que el archivo pese lo publicado; la huella se comprueba al usarlo.");
        Assert.Equal("Instalar (ya descargado)", t.TextoInstalar);

        await instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);

        Assert.Equal(1, _servidor.RequestCount("app.zip"));
        Assert.Equal("MZ" + Encoding.Latin1.GetString(TestPayloads.Deterministic(30_000, seed: 6)),
            File.ReadAllText(Path.Combine(Herramientas, "subtitular", "1.0.0", "Subtitular.exe"), Encoding.Latin1));
    }

    [Fact]
    public async Task Zip_descargado_de_cero_bytes_no_cuenta_como_descargado_y_se_vuelve_a_bajar()
    {
        Directory.CreateDirectory(CarpetasPrueba.Descargas);
        File.WriteAllBytes(Descarga(_app), []);
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        TarjetaHerramienta t = Assert.Single(await instalador.ObtenerTarjetasAsync());
        Assert.False(t.ZipDescargado, "Un zip que no pesa lo publicado no es el zip.");
        Assert.Equal("Instalar", t.TextoInstalar);

        await instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);
        Assert.Equal(1, _servidor.RequestCount("app.zip"));
        Assert.Equal(EstadoTarjeta.Instalada, Assert.Single(await instalador.ObtenerTarjetasAsync()).Estado);
    }

    [Fact]
    public async Task Una_carpeta_con_el_nombre_del_zip_es_un_fallo_de_disco_sin_instalar_nada()
    {
        Directory.CreateDirectory(Descarga(_app));
        using (var http = new HttpClient())
        using (var instalador = CrearInstalador(http))
        {
            Assert.False(Assert.Single(await instalador.ObtenerTarjetasAsync()).ZipDescargado);
        }

        // El .parcial sí se puede escribir, pero el renombrado final a «<sha>.zip» choca con la carpeta.
        var error = new StringWriter();
        int codigo = await Ordenes.EjecutarAsync(Cli("instalar", "subtitular"), new StringWriter(), error);

        Assert.Equal(2, codigo);
        Assert.Contains("Error: No se pudo leer o escribir en el disco", error.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(Herramientas, "subtitular")));
    }

    // 3. Rutas difíciles de Windows.

    [Fact]
    public async Task Instalar_en_ruta_con_espacios_tildes_y_enie()
    {
        var carpetas = new Carpetas(
            Path.Combine(_temp, "Mis Herramientas áéíóú ñandú"),
            Path.Combine(_temp, "Datos Comunes versión ñ"));
        using var http = new HttpClient();
        using var instalador = new Instalador(carpetas, _servidor.UrlFor("indice.json").AbsoluteUri, _firmante.PublicKeyBase64, http);

        PackageVersion v = await instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);

        Assert.Equal(PackageVersion.Parse("1.0.0"), v);
        Assert.True(File.Exists(Path.Combine(carpetas.Herramientas, "subtitular", "1.0.0", "Subtitular.exe")));
        Assert.NotNull(instalador.BuscarEjecutable("subtitular"));

        instalador.Quitar("subtitular");
        Assert.False(Directory.Exists(Path.Combine(carpetas.Herramientas, "subtitular")));
    }

    [Fact]
    public async Task Instalar_en_ruta_muy_larga()
    {
        string profunda = Path.Combine([_temp, .. Enumerable.Repeat("subcarpeta-nombre-largo", 12)]);
        var carpetas = new Carpetas(Path.Combine(profunda, "Herramientas"), Path.Combine(profunda, "Comun"));
        Assert.True(Path.Combine(carpetas.Herramientas, "subtitular", "1.0.0", "Subtitular.exe").Length > 260,
            "La prueba pide una ruta que supera MAX_PATH.");

        using var http = new HttpClient();
        using var instalador = new Instalador(carpetas, _servidor.UrlFor("indice.json").AbsoluteUri, _firmante.PublicKeyBase64, http);
        await instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(carpetas.Herramientas, "subtitular", "1.0.0", "Subtitular.exe")));
        Assert.Equal(EstadoTarjeta.Instalada, Assert.Single(await instalador.ObtenerTarjetasAsync()).Estado);
    }

    // 4. Solo lectura: la carpeta tiene que irse igual, al quitar y al actualizar, sin salir de ella por un enlace.

    /// <summary>Marca de solo lectura todo lo instalado de «subtitular», también una subcarpeta con un archivo dentro.</summary>
    private void MarcarSoloLectura()
    {
        string sub = Path.Combine(Herramientas, "subtitular", "1.0.0", "datos");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "ajustes.ini"), "x");
        foreach (string entrada in Directory.EnumerateFileSystemEntries(Path.Combine(Herramientas, "subtitular"), "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(entrada, File.GetAttributes(entrada) | FileAttributes.ReadOnly);
        }
    }

    [Fact]
    public async Task Quitar_borra_la_carpeta_aunque_sus_archivos_sean_de_solo_lectura()
    {
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        await instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);
        MarcarSoloLectura();

        instalador.Quitar("subtitular");

        Assert.False(Directory.Exists(Path.Combine(Herramientas, "subtitular")), "«Quitar» debe borrar aunque la app dejara archivos de solo lectura.");
    }

    [Fact]
    public async Task Actualizar_borra_la_version_vieja_aunque_sea_de_solo_lectura()
    {
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        await instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);
        MarcarSoloLectura();
        byte[] nueva = Zip(("Subtitular.exe", "MZ nueva"));
        _servidor.Map("nueva.zip", TestResponse.File(nueva));
        Publicar(Indice(Entrada("subtitular", "1.0.0", "app.zip", _app), Entrada("subtitular", "1.1.0", "nueva.zip", nueva)));
        await instalador.IndiceAsync(refresh: true);

        var cambios = await instalador.ActualizarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);

        Assert.Equal(("subtitular", PackageVersion.Parse("1.0.0"), PackageVersion.Parse("1.1.0")), Assert.Single(cambios));
        Assert.Equal(["1.1.0"], Directory.EnumerateDirectories(Path.Combine(Herramientas, "subtitular")).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Quitar_no_sale_de_la_carpeta_por_un_enlace_ni_toca_su_destino()
    {
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        await instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);
        string fuera = Path.Combine(_temp, "fuera");
        Directory.CreateDirectory(fuera);
        string ajeno = Path.Combine(fuera, "ajeno.txt");
        File.WriteAllText(ajeno, "no es de la herramienta");
        File.SetAttributes(ajeno, FileAttributes.ReadOnly);
        MarcarSoloLectura();
        CrearEnlace(Path.Combine(Herramientas, "subtitular", "enlace"), fuera);

        instalador.Quitar("subtitular");

        Assert.False(Directory.Exists(Path.Combine(Herramientas, "subtitular")));
        Assert.True(File.Exists(ajeno), "Lo que hay al otro lado del enlace no es de la herramienta.");
        Assert.True(File.GetAttributes(ajeno).HasFlag(FileAttributes.ReadOnly), "Ni se le quita el solo lectura.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Quitar_una_herramienta_que_es_un_enlace_borra_el_enlace_y_no_su_destino(bool soloLaVersion)
    {
        string fuera = Path.Combine(_temp, "fuera");
        string ajeno = Path.Combine(fuera, "1.0.0", "ajeno.txt");
        Directory.CreateDirectory(Path.Combine(fuera, "1.0.0"));
        File.WriteAllText(ajeno, "no es de la herramienta");
        Directory.CreateDirectory(soloLaVersion ? Path.Combine(Herramientas, "subtitular") : Herramientas);
        if (soloLaVersion)
        {
            CrearEnlace(Path.Combine(Herramientas, "subtitular", "1.0.0"), Path.Combine(fuera, "1.0.0"));
        }
        else
        {
            CrearEnlace(Path.Combine(Herramientas, "subtitular"), fuera);
        }

        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        Assert.Equal(PackageVersion.Parse("1.0.0"), instalador.Instalada("subtitular"));

        instalador.Quitar("subtitular");

        Assert.False(Directory.Exists(Path.Combine(Herramientas, "subtitular")));
        Assert.True(File.Exists(ajeno), "Lo que hay al otro lado del enlace no es de la herramienta.");
    }
}
