using System.IO.Compression;
using System.Text;
using Lanzador.Core;
using Rodaje.Packages;
using Rodaje.Packages.Download;
using Rodaje.TestServer;

namespace Lanzador.Tests;

/// <summary>
/// Sin red se enseñan las instaladas (solo «Abrir» y «Quitar») con la franja «Sin conexión»; «Quitar» se lleva también los zips
/// y «.parcial» de la app; una instalada más nueva que el catálogo lo dice; y la CLI resume en una línea la firma inválida.
/// </summary>
public sealed class SinRedYQuitarTests : IAsyncLifetime, IDisposable
{
    /// <summary>Un puerto cerrado de este equipo: la conexión se rechaza en el acto, como sin red.</summary>
    private const string IndiceSinRed = "http://localhost:9/indice.json";

    private readonly TestHttpServer _servidor = TestHttpServer.Start();
    private readonly TestIndexSigner _firmante = new();
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "lanzador-sinred-tests-" + Guid.NewGuid().ToString("N")[..10]);

    private readonly byte[] _modelo = TestPayloads.Deterministic(50_000, seed: 30);
    private readonly byte[] _modulo;
    private readonly byte[] _app1;
    private readonly byte[] _app2;

    public SinRedYQuitarTests()
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

    private string Entrada(string id, string version, string kind, string ruta, byte[] contenido) =>
        $$"""{ "id": "{{id}}", "version": "{{version}}", "kind": "{{kind}}", "url": "{{_servidor.UrlFor(ruta).AbsoluteUri}}", "sha256": "{{TestPayloads.Sha256Hex(contenido)}}", "size": {{contenido.Length}} }""";

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

    private Instalador CrearInstalador(HttpClient http, string? indice = null) =>
        new(CarpetasPrueba, indice ?? _servidor.UrlFor("indice.json").AbsoluteUri, _firmante.PublicKeyBase64, http);

    private async Task InstalarConRedAsync(HttpClient http)
    {
        using Instalador conRed = CrearInstalador(http);
        await conRed.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);
    }

    private string Descarga(byte[] zip, string sufijo = "") => Path.Combine(CarpetasPrueba.Descargas, TestPayloads.Sha256Hex(zip) + ".zip" + sufijo);

    // 1. Sin red: las instaladas con «Abrir» y «Quitar» y la franja «Sin conexión».

    [Fact]
    public async Task Sin_red_salen_las_instaladas_con_abrir_y_quitar_y_la_franja()
    {
        using var http = new HttpClient();
        await InstalarConRedAsync(http);
        using Instalador sinRed = CrearInstalador(http, IndiceSinRed);

        CargaDeTarjetas carga = await sinRed.CargarTarjetasAsync();

        Assert.True(carga.SinConexion);
        Assert.Equal(CargaDeTarjetas.AvisoSinConexion, carga.Aviso);
        Assert.Contains("«Buscar actualizaciones»", carga.Aviso, StringComparison.Ordinal);
        TarjetaHerramienta t = Assert.Single(carga.Tarjetas);
        Assert.Equal("subtitular", t.Id);
        Assert.Equal("Subtitular", t.Nombre);
        Assert.True(t.SinCatalogo);
        Assert.Equal(EstadoTarjeta.Instalada, t.Estado);
        Assert.Equal(PackageVersion.Parse("1.0.0"), t.VersionInstalada);
        Assert.Equal(t.VersionInstalada, t.VersionDisponible);
        Assert.True(t.PuedeAbrir);
        Assert.Equal(Path.Combine(CarpetasPrueba.Herramientas, "subtitular", "1.0.0", "Subtitular.exe"), t.Ejecutable);
        Assert.True(t.PuedeQuitar);
        Assert.False(t.PuedeInstalar);
        Assert.False(t.PuedeActualizar);
        Assert.False(t.PuedeReanudar);
        Assert.Equal("Versión 1.0.0 (instalada)", t.TextoVersion);
        Assert.Equal("Sin conexión: no se sabe si hay una versión nueva", t.TextoDescarga);
        Assert.NotNull(t.EspacioInstalada);
        Assert.True(t.EspacioInstalada.CompartidoBytes > 0, "El espacio compartido se mide del disco, sin catálogo.");
        Assert.StartsWith("En disco:", t.TextoEspacio, StringComparison.Ordinal);
        Assert.Null(Actualizaciones.Aviso(carga.Tarjetas));
        Assert.Null(Actualizaciones.Recuento(carga.Tarjetas));
    }

    [Fact]
    public async Task Recargar_tarjeta_sin_red_conserva_estado_y_acciones()
    {
        using var http = new HttpClient();
        await InstalarConRedAsync(http);
        using Instalador sinRed = CrearInstalador(http, IndiceSinRed);
        CargaDeTarjetas inicial = await sinRed.CargarTarjetasAsync();

        CargaDeTarjetas recarga = await sinRed.CargarTarjetasAsync(refrescar: false);
        TarjetaHerramienta tarjeta = Assert.IsType<TarjetaHerramienta>(recarga.TarjetaParaRepintar("subtitular"));

        Assert.Equal(inicial.TarjetaParaRepintar("subtitular"), tarjeta);
        Assert.True(recarga.SinConexion);
        Assert.True(tarjeta.SinCatalogo);
        Assert.True(tarjeta.PuedeAbrir);
        Assert.True(tarjeta.PuedeQuitar);
        Assert.False(tarjeta.PuedeActualizar);
        Assert.Null(recarga.TarjetaParaRepintar("ausente"));
    }

    [Fact]
    public async Task Sin_red_y_sin_nada_instalado_sigue_saliendo_el_panel_de_sin_conexion()
    {
        using var http = new HttpClient();
        using Instalador sinRed = CrearInstalador(http, IndiceSinRed);

        var ex = await Assert.ThrowsAsync<PackageDownloadException>(() => sinRed.CargarTarjetasAsync());

        Assert.True(MensajesError.EsFalloDeRed(ex));
        Assert.Equal("Sin conexión con el catálogo", MensajesError.DescribirFalloDelCatalogo(ex).Titulo);
        Assert.Empty(sinRed.TarjetasInstaladas());
    }

    [Fact]
    public async Task Con_red_la_carga_trae_el_catalogo_sin_franja()
    {
        using var http = new HttpClient();
        using Instalador instalador = CrearInstalador(http);

        CargaDeTarjetas carga = await instalador.CargarTarjetasAsync();

        Assert.False(carga.SinConexion);
        Assert.Null(carga.Aviso);
        TarjetaHerramienta t = Assert.Single(carga.Tarjetas);
        Assert.False(t.SinCatalogo);
        Assert.Equal(EstadoTarjeta.NoInstalada, t.Estado);
        Assert.Equal("Descarga: 0.1 MB", t.TextoDescarga);
    }

    [Fact]
    public async Task La_firma_invalida_no_se_disimula_con_lo_instalado()
    {
        using var http = new HttpClient();
        await InstalarConRedAsync(http);
        string json = Indice(Entrada("subtitular", "1.0.0", "app", "app1.zip", _app1));
        Publicar(json, _firmante.SignOtherData(Encoding.UTF8.GetBytes(json)));
        using Instalador instalador = CrearInstalador(http);

        var ex = await Assert.ThrowsAsync<CatalogoNoVerificadoException>(() => instalador.CargarTarjetasAsync());

        Assert.False(MensajesError.EsFalloDeRed(ex));
    }

    [Fact]
    public void Un_tiempo_de_espera_agotado_tambien_es_fallo_de_red()
    {
        Assert.True(MensajesError.EsFalloDeRed(new PackageDownloadException("GitHub no respondió a tiempo.", new TaskCanceledException())));
        Assert.False(MensajesError.EsFalloDeRed(new PackageDownloadException("El índice usa el formato 9.")));
        Assert.False(MensajesError.EsFalloDeRed(new DownloadVerificationException("x", new HttpRequestException("x"))));
    }

    // 2. «Quitar» borra también los zips y «.parcial» de esa app.

    [Fact]
    public async Task Quitar_borra_los_zips_y_parciales_de_la_app_y_deja_los_ajenos()
    {
        using var http = new HttpClient();
        await InstalarConRedAsync(http);
        Publicar(IndiceConFecha("2026-10-24T13:00:00Z", Entrada("subtitular", "1.0.0", "app", "app1.zip", _app1), Entrada("subtitular", "1.1.0", "app", "app2.zip", _app2)));
        using Instalador instalador = CrearInstalador(http);
        Directory.CreateDirectory(CarpetasPrueba.Descargas);
        string parcialNueva = Descarga(_app2, FileDownloader.PartialSuffix);
        string zipVieja = Descarga(_app1);
        string ajeno = Path.Combine(CarpetasPrueba.Descargas, new string('a', 64) + ".zip");
        File.WriteAllBytes(parcialNueva, _app2[..40_000]);
        File.WriteAllBytes(zipVieja, _app1);
        File.WriteAllBytes(ajeno, [1, 2, 3]);

        TarjetaHerramienta t = Assert.Single(await instalador.ObtenerTarjetasAsync(refrescar: true));
        Assert.Equal(EstadoTarjeta.Actualizable, t.Estado);
        Assert.True(t.PuedeReanudar);
        Assert.Contains($"También se borra lo descargado de Subtitular que quedó en «{CarpetasPrueba.Descargas}» ({Instalador.Peso(_app1.Length + 40_000)}).", t.TextoConfirmarQuitar(instalador.Carpetas), StringComparison.Ordinal);
        Assert.Equal(new[] { zipVieja, parcialNueva }.Order(StringComparer.Ordinal), instalador.DescargasDeLaApp("subtitular", await instalador.IndiceAsync(CancellationToken.None)).Order(StringComparer.Ordinal));

        await instalador.QuitarAsync("subtitular");

        Assert.False(Directory.Exists(Path.Combine(CarpetasPrueba.Herramientas, "subtitular")));
        Assert.False(File.Exists(parcialNueva));
        Assert.False(File.Exists(zipVieja));
        Assert.True(File.Exists(ajeno), "Las descargas de otras entradas no se tocan.");
        Assert.True(File.Exists(Path.Combine(CarpetasPrueba.Modulos, "modulo-eco", "2.0.0", "Eco.exe")));
        Assert.True(File.Exists(Path.Combine(CarpetasPrueba.Modelos, TestPayloads.Sha256Hex(_modelo))));

        t = Assert.Single(await instalador.ObtenerTarjetasAsync());
        Assert.Equal(EstadoTarjeta.NoInstalada, t.Estado);
        Assert.False(t.PuedeReanudar);
        Assert.False(t.ZipDescargado);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Confirmacion_incluye_descargas_aunque_solo_quede_una_version_antigua(bool variasVersiones)
    {
        using var http = new HttpClient();
        await InstalarConRedAsync(http);
        if (variasVersiones)
        {
            Publicar(Indice(Entrada("subtitular", "1.0.0", "app", "app1.zip", _app1), Entrada("subtitular", "1.1.0", "app", "app2.zip", _app2)));
        }

        Directory.CreateDirectory(CarpetasPrueba.Descargas);
        File.WriteAllBytes(Descarga(_app1), _app1);
        using Instalador instalador = CrearInstalador(http);
        TarjetaHerramienta tarjeta = Assert.Single(await instalador.ObtenerTarjetasAsync());

        Assert.Equal(_app1.Length, tarjeta.DescargasDeLaAppBytes);
        string texto = tarjeta.TextoConfirmarQuitar(CarpetasPrueba);
        Assert.Contains($"También se borra lo descargado de Subtitular que quedó en «{CarpetasPrueba.Descargas}» ({Instalador.Peso(_app1.Length)}).", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("de la versión", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Quitar_sin_red_borra_la_carpeta_y_deja_las_descargas_para_la_proxima()
    {
        using var http = new HttpClient();
        await InstalarConRedAsync(http);
        Directory.CreateDirectory(CarpetasPrueba.Descargas);
        string parcial = Descarga(_app2, FileDownloader.PartialSuffix);
        File.WriteAllBytes(parcial, _app2[..40_000]);
        using Instalador sinRed = CrearInstalador(http, IndiceSinRed);

        await sinRed.QuitarAsync("subtitular");

        Assert.False(Directory.Exists(Path.Combine(CarpetasPrueba.Herramientas, "subtitular")));
        Assert.True(File.Exists(parcial), "Sin catálogo no se sabe de quién es el .parcial: se queda.");
        Assert.Empty(sinRed.DescargasDeLaApp("subtitular", null));
        var ex = await Assert.ThrowsAsync<PackageDownloadException>(() => sinRed.CargarTarjetasAsync());
        Assert.True(MensajesError.EsFalloDeRed(ex));
    }

    [Fact]
    public void Confirmar_quitar_sin_descargas_no_habla_de_ellas()
    {
        var t = new TarjetaHerramienta("subtitular", "Subtitular", "desc", PackageVersion.Parse("1.0.0"), PackageVersion.Parse("1.0.0"), 1, null, EstadoTarjeta.Instalada);
        Assert.DoesNotContain("También se borra", t.TextoConfirmarQuitar(CarpetasPrueba), StringComparison.Ordinal);
        Assert.Contains("También se borra", (t with { ZipDescargado = true }).TextoConfirmarQuitar(CarpetasPrueba), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_quitar_borra_tambien_las_descargas_de_la_app()
    {
        using var http = new HttpClient();
        await InstalarConRedAsync(http);
        Directory.CreateDirectory(CarpetasPrueba.Descargas);
        string parcial = Descarga(_app1, FileDownloader.PartialSuffix);
        File.WriteAllBytes(parcial, _app1[..20_000]);
        var salida = new StringWriter();

        int codigo = await Ordenes.EjecutarAsync(["quitar", "subtitular", "--indice", _servidor.UrlFor("indice.json").AbsoluteUri, "--clave", _firmante.PublicKeyBase64, "--destino", CarpetasPrueba.Herramientas, "--comun", CarpetasPrueba.Comun], salida, new StringWriter());

        Assert.Equal(0, codigo);
        Assert.Contains("quitada", salida.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(parcial));
    }

    // 3. Instalada más nueva que el catálogo.

    [Fact]
    public async Task Instalada_mas_nueva_que_el_catalogo_lo_dice_y_no_ofrece_nada()
    {
        using var http = new HttpClient();
        using Instalador instalador = CrearInstalador(http);
        string carpeta = Path.Combine(CarpetasPrueba.Herramientas, "subtitular", "9.0.0");
        Directory.CreateDirectory(carpeta);
        File.WriteAllText(Path.Combine(carpeta, "Subtitular.exe"), "MZ");
        // Un «.parcial» y un zip de la versión del catálogo (más vieja) no son nada que reanudar ni instalar.
        Directory.CreateDirectory(CarpetasPrueba.Descargas);
        File.WriteAllBytes(Descarga(_app1, FileDownloader.PartialSuffix), _app1[..10_000]);

        var tarjetas = await instalador.ObtenerTarjetasAsync();
        TarjetaHerramienta t = Assert.Single(tarjetas);

        Assert.Equal(EstadoTarjeta.InstaladaMasNueva, t.Estado);
        Assert.True(t.AlDia);
        Assert.Equal("Instalada 9.0.0, más nueva que el catálogo (1.0.0)", t.TextoVersion);
        Assert.Equal(PackageVersion.Parse("1.0.0"), t.VersionDisponible);
        Assert.False(t.PuedeInstalar);
        Assert.False(t.PuedeActualizar);
        Assert.False(t.PuedeReanudar);
        Assert.Equal(0, t.DescargaParcialBytes);
        Assert.False(t.ZipDescargado);
        Assert.True(t.PuedeAbrir);
        Assert.True(t.PuedeQuitar);
        Assert.Equal("Descarga: 0.1 MB", t.TextoDescarga);
        Assert.Null(Actualizaciones.Aviso(tarjetas));
        Assert.Null(Actualizaciones.Recuento(tarjetas));
    }

    [Fact]
    public async Task Cli_lista_dice_cuando_la_instalada_es_mas_nueva()
    {
        Directory.CreateDirectory(Path.Combine(CarpetasPrueba.Herramientas, "subtitular", "9.0.0"));
        var salida = new StringWriter();

        int codigo = await Ordenes.EjecutarAsync(["lista", "--indice", _servidor.UrlFor("indice.json").AbsoluteUri, "--clave", _firmante.PublicKeyBase64, "--destino", CarpetasPrueba.Herramientas, "--comun", CarpetasPrueba.Comun], salida, new StringWriter());

        Assert.Equal(0, codigo);
        Assert.Equal("subtitular\t1.0.0\t0.1 MB\tinstalada 9.0.0, más nueva que el catálogo (1.0.0)", salida.ToString().Trim());
    }

    // 4. La CLI resume la firma inválida en una línea.

    [Fact]
    public async Task Cli_con_firma_invalida_lo_dice_en_una_linea_corta()
    {
        string json = Indice(Entrada("subtitular", "1.0.0", "app", "app1.zip", _app1));
        Publicar(json, _firmante.SignOtherData(Encoding.UTF8.GetBytes(json)));
        var error = new StringWriter();

        int codigo = await Ordenes.EjecutarAsync(["instalar", "subtitular", "--indice", _servidor.UrlFor("indice.json").AbsoluteUri, "--clave", _firmante.PublicKeyBase64, "--destino", CarpetasPrueba.Herramientas, "--comun", CarpetasPrueba.Comun], new StringWriter(), error);

        string texto = error.ToString().Trim();
        Assert.Equal(2, codigo);
        Assert.Equal("Error: " + CatalogoNoVerificadoException.MensajeCorto, texto);
        Assert.DoesNotContain('\n', texto);
        Assert.InRange(texto.Length, 1, 80);
        Assert.Contains("firma", texto, StringComparison.Ordinal);
        // La ventana sigue con el párrafo largo.
        Assert.NotEqual(CatalogoNoVerificadoException.Mensaje, CatalogoNoVerificadoException.MensajeCorto);
        Assert.Equal(0, _servidor.RequestCount("app1.zip"));
    }
}
