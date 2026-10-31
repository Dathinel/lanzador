using System.IO.Compression;
using System.Net;
using System.Text;
using Lanzador.Core;
using Rodaje.Packages;
using Rodaje.Packages.Download;
using Rodaje.TestServer;

namespace Lanzador.Tests;

/// <summary>
/// Lo que la ventana muestra sin tocar la UI: versiones y «Actualizar» solo cuando difieren, aviso de actualizaciones,
/// velocidad y tiempo restante, reanudar una descarga cortada, «Abrir» solo con ejecutable, confirmación de «Quitar»
/// con la carpeta, y un catálogo con firma inválida que se explica en español.
/// </summary>
public sealed class CalidadDeVidaTests : IAsyncLifetime, IDisposable
{
    private readonly TestHttpServer _servidor = TestHttpServer.Start();
    private readonly TestIndexSigner _firmante = new();
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "lanzador-cdv-tests-" + Guid.NewGuid().ToString("N")[..10]);

    private readonly byte[] _modelo = TestPayloads.Deterministic(50_000, seed: 20);
    private readonly byte[] _modulo;
    private readonly byte[] _app1;
    private readonly byte[] _app2;
    private readonly byte[] _sinExe;

    public CalidadDeVidaTests()
    {
        Directory.CreateDirectory(_temp);
        string modeloUrl = _servidor.UrlFor("modelo.bin").AbsoluteUri;
        _modulo = Zip(("paquete.json", $$"""
            { "formatVersion": 1, "id": "modulo-eco", "version": "2.0.0", "kind": "module", "name": "Eco", "author": "Dathinel", "license": "GPL-3.0-only",
              "repository": "https://github.com/Dathinel/modulo-eco", "apps": ["rodaje"],
              "module": { "exe": "Eco.exe", "models": [ { "id": "eco-pequeno", "file": "modelos/eco.bin", "size": {{_modelo.Length}}, "sha256": "{{TestPayloads.Sha256Hex(_modelo)}}", "url": "{{modeloUrl}}" } ] } }
            """), ("Eco.exe", "MZ"));
        // Datos aleatorios grandes, para que una cancelación a mitad deje algo que reanudar.
        _app1 = Zip(("Subtitular.exe", Encoding.Latin1.GetString(TestPayloads.Deterministic(300_000, seed: 1))),
            ("herramienta.json", """{ "modulos": ["modulo-eco"] }"""));
        _app2 = Zip(("Subtitular.exe", Encoding.Latin1.GetString(TestPayloads.Deterministic(300_000, seed: 2))),
            ("herramienta.json", """{ "modulos": ["modulo-eco"] }"""));
        _sinExe = Zip(("LEEME.txt", "sin ejecutable"));

        _servidor.Map("modelo.bin", TestResponse.File(_modelo));
        _servidor.Map("modulo.paquete", TestResponse.File(_modulo));
        _servidor.Map("app1.zip", TestResponse.File(_app1));
        _servidor.Map("app2.zip", TestResponse.File(_app2));
        _servidor.Map("sinexe.zip", TestResponse.File(_sinExe));
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

    private Instalador CrearInstalador(HttpClient http) =>
        new(CarpetasPrueba, _servidor.UrlFor("indice.json").AbsoluteUri, _firmante.PublicKeyBase64, http);

    private static TarjetaHerramienta Tarjeta(string instalada, string disponible, EstadoTarjeta estado, string? exe = "Subtitular.exe", long parcial = 0) =>
        new("subtitular", "Subtitular", "desc", PackageVersion.Parse(disponible), PackageVersion.Parse(instalada), 28_000_000, new EspacioInstalada(30_000_000, 900_000_000), estado, exe, parcial);

    // 1. Versión instalada junto a la del catálogo; «Actualizar» solo cuando difieren.

    [Fact]
    public void Version_instalada_y_disponible_juntas_y_actualizar_solo_si_difieren()
    {
        TarjetaHerramienta alDia = Tarjeta("1.0.0", "1.0.0", EstadoTarjeta.Instalada);
        Assert.Equal("Versión 1.0.0 (al día)", alDia.TextoVersion);
        Assert.False(alDia.PuedeActualizar);
        Assert.Equal("Actualizar", alDia.TextoActualizar);

        TarjetaHerramienta vieja = Tarjeta("1.0.0", "1.1.0", EstadoTarjeta.Actualizable);
        Assert.Equal("Instalada 1.0.0 · Disponible 1.1.0", vieja.TextoVersion);
        Assert.True(vieja.PuedeActualizar);

        TarjetaHerramienta ninguna = Tarjeta("1.0.0", "1.1.0", EstadoTarjeta.NoInstalada) with { VersionInstalada = null, EspacioInstalada = null };
        Assert.Equal("Versión 1.1.0", ninguna.TextoVersion);
        Assert.False(ninguna.PuedeActualizar);
    }

    [Fact]
    public async Task Una_version_instalada_mas_nueva_que_el_catalogo_no_ofrece_actualizar()
    {
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        Directory.CreateDirectory(Path.Combine(CarpetasPrueba.Herramientas, "subtitular", "9.0.0"));

        TarjetaHerramienta t = Assert.Single(await instalador.ObtenerTarjetasAsync());

        Assert.Equal(EstadoTarjeta.Instalada, t.Estado);
        Assert.Equal(PackageVersion.Parse("9.0.0"), t.VersionInstalada);
        Assert.False(t.PuedeActualizar);
    }

    // 2. Buscar actualizaciones: aviso discreto solo si hay.

    [Fact]
    public void Aviso_de_actualizaciones_solo_cuando_hay_y_las_nombra()
    {
        Assert.Null(Actualizaciones.Aviso([]));
        Assert.Null(Actualizaciones.Aviso([Tarjeta("1.0.0", "1.0.0", EstadoTarjeta.Instalada)]));

        Assert.Equal("Hay una actualización: Subtitular 1.0.0 → 1.1.0.",
            Actualizaciones.Aviso([Tarjeta("1.0.0", "1.1.0", EstadoTarjeta.Actualizable), Tarjeta("1.0.0", "1.0.0", EstadoTarjeta.Instalada)]));

        TarjetaHerramienta rastrear = Tarjeta("2.0.0", "2.1.0", EstadoTarjeta.Actualizable) with { Id = "rastrear", Nombre = "Rastrear" };
        Assert.Equal("Hay 2 actualizaciones: Subtitular 1.0.0 → 1.1.0, Rastrear 2.0.0 → 2.1.0.",
            Actualizaciones.Aviso([Tarjeta("1.0.0", "1.1.0", EstadoTarjeta.Actualizable), rastrear]));
    }

    [Fact]
    public async Task Al_refrescar_el_catalogo_sale_el_aviso_si_hay_version_nueva()
    {
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        await instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);
        Assert.Null(Actualizaciones.Aviso(await instalador.ObtenerTarjetasAsync()));

        Publicar(IndiceConFecha("2026-10-24T13:00:00Z", Entrada("subtitular", "1.0.0", "app", "app1.zip", _app1), Entrada("subtitular", "1.1.0", "app", "app2.zip", _app2)));

        Assert.Equal("Hay una actualización: Subtitular 1.0.0 → 1.1.0.", Actualizaciones.Aviso(await instalador.ObtenerTarjetasAsync(refrescar: true)));
    }

    // 3. Progreso con velocidad y tiempo restante; reanudar una descarga cortada.

    [Fact]
    public void Progreso_calcula_lo_que_queda_a_la_velocidad_medida()
    {
        var p = new ProgresoDetallado("Descargando", "subtitular", 12_000_000, 28_000_000, 2_000_000, 12_000_000, 40_000_000, 0.3);
        Assert.Equal(TimeSpan.FromSeconds(14), p.TiempoRestante);
        Assert.Equal("subtitular: 11.4 MB de 26.7 MB (1.9 MB/s · quedan ~0:14)", p.TextoProgreso());

        var sinVelocidad = p with { VelocidadBytesPorSegundo = 0 };
        Assert.Null(sinVelocidad.TiempoRestante);
        Assert.Equal("subtitular: 11.4 MB de 26.7 MB", sinVelocidad.TextoProgreso());

        var terminado = p with { BytesAgregadosRecibidos = 40_000_000 };
        Assert.Null(terminado.TiempoRestante);
    }

    [Fact]
    public void Resumen_de_tiempos_prefiere_lo_medido_sobre_la_regla_de_tres()
    {
        Assert.Equal("0:42 · quedan ~0:14", Tiempos.Resumen(TimeSpan.FromSeconds(42), 0.375, TimeSpan.FromSeconds(14)));
        Assert.Equal("0:42 · quedan ~1:10", Tiempos.Resumen(TimeSpan.FromSeconds(42), 0.375, null));
        Assert.Equal("0:10 · quedan ~0:05", Tiempos.Resumen(TimeSpan.FromSeconds(10), 0, TimeSpan.FromSeconds(5)));
        Assert.Equal("0:01", Tiempos.Resumen(TimeSpan.FromSeconds(1), 0.5, TimeSpan.FromSeconds(5)));
        Assert.Equal("0:10", Tiempos.Resumen(TimeSpan.FromSeconds(10), 1, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Descarga_cancelada_queda_para_reanudar_y_la_tarjeta_lo_dice()
    {
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        using var cts = new CancellationTokenSource();
        var velocidades = new List<double>();
        var progreso = new ProgresoSincrono(p =>
        {
            velocidades.Add(p.VelocidadBytesPorSegundo);
            if (p.Archivo == "subtitular" && p.BytesRecibidos > 60_000)
            {
                cts.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => instalador.InstalarAsync("subtitular", progreso, cts.Token));

        string parcial = Path.Combine(CarpetasPrueba.Descargas, TestPayloads.Sha256Hex(_app1) + ".zip" + FileDownloader.PartialSuffix);
        Assert.True(File.Exists(parcial), "Lo descargado debe quedar en un .parcial para reanudar.");
        long bytesParciales = new FileInfo(parcial).Length;
        Assert.InRange(bytesParciales, 1, _app1.Length - 1);

        TarjetaHerramienta t = Assert.Single(await instalador.ObtenerTarjetasAsync());
        Assert.Equal(EstadoTarjeta.NoInstalada, t.Estado);
        Assert.Equal(bytesParciales, t.DescargaParcialBytes);
        Assert.True(t.PuedeReanudar);
        Assert.Equal("Reanudar descarga", t.TextoInstalar);
        Assert.Contains("ya bajados, se reanudará", t.TextoDescarga, StringComparison.Ordinal);

        // La segunda vez pide solo lo que falta con Range y termina.
        await instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);
        var peticiones = _servidor.Requests.Where(r => r.Path == "/app1.zip").ToList();
        Assert.Equal(2, peticiones.Count);
        Assert.Null(peticiones[0].Range);
        Assert.Equal($"bytes={bytesParciales}-", peticiones[1].Range);
        Assert.Equal(206, peticiones[1].StatusCode);
        Assert.False(File.Exists(parcial));

        t = Assert.Single(await instalador.ObtenerTarjetasAsync());
        Assert.Equal(EstadoTarjeta.Instalada, t.Estado);
        Assert.False(t.PuedeReanudar);
        Assert.Equal("Instalar", t.TextoInstalar);
    }

    // 4. «Abrir» solo cuando hay un ejecutable que abrir.

    [Fact]
    public async Task Abrir_solo_si_la_app_instalada_trae_ejecutable()
    {
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        Publicar(Indice(Entrada("subtitular", "1.0.0", "app", "app1.zip", _app1), Entrada("sinexe", "1.0.0", "app", "sinexe.zip", _sinExe)));
        await instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);
        await instalador.InstalarAsync("sinexe", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);

        var tarjetas = await instalador.ObtenerTarjetasAsync();

        TarjetaHerramienta con = Assert.Single(tarjetas, t => t.Id == "subtitular");
        Assert.True(con.PuedeAbrir);
        Assert.Equal(Path.Combine(CarpetasPrueba.Herramientas, "subtitular", "1.0.0", "Subtitular.exe"), con.Ejecutable);

        TarjetaHerramienta sin = Assert.Single(tarjetas, t => t.Id == "sinexe");
        Assert.Equal(EstadoTarjeta.Instalada, sin.Estado);
        Assert.Null(sin.Ejecutable);
        Assert.False(sin.PuedeAbrir);
        Assert.True(sin.PuedeQuitar);
        Assert.Contains("ejecutable", Assert.Throws<ErrorDeUso>(() => instalador.AbrirHerramienta("sinexe")).Message, StringComparison.Ordinal);
    }

    // 5. Quitar con confirmación que dice qué carpeta se borra.

    [Fact]
    public async Task Confirmacion_de_quitar_dice_la_carpeta_lo_que_ocupa_y_lo_que_se_queda()
    {
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        await instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);
        TarjetaHerramienta t = Assert.Single(await instalador.ObtenerTarjetasAsync());

        string carpeta = Path.Combine(CarpetasPrueba.Herramientas, "subtitular");
        Assert.Equal(carpeta, t.CarpetaInstalada(instalador.Carpetas));
        Assert.Equal(carpeta, instalador.CarpetaInstalada("subtitular"));

        string texto = t.TextoConfirmarQuitar(instalador.Carpetas);
        Assert.StartsWith($"Se borrará la carpeta «{carpeta}» (", texto, StringComparison.Ordinal);
        Assert.Contains("con Subtitular 1.0.0.", texto, StringComparison.Ordinal);
        Assert.Contains($"se quedan en «{CarpetasPrueba.Comun}»", texto, StringComparison.Ordinal);

        instalador.Quitar("subtitular");
        Assert.False(Directory.Exists(carpeta));
        Assert.True(File.Exists(Path.Combine(CarpetasPrueba.Modulos, "modulo-eco", "2.0.0", "Eco.exe")));
    }

    [Fact]
    public void Confirmacion_de_quitar_sin_espacio_medido_sigue_diciendo_la_carpeta()
    {
        TarjetaHerramienta t = Tarjeta("1.0.0", "1.0.0", EstadoTarjeta.Instalada) with { EspacioInstalada = null };
        var carpetas = new Carpetas(@"C:\H", @"C:\C");
        Assert.Equal($"Se borrará la carpeta «{Path.Combine(@"C:\H", "subtitular")}» con Subtitular 1.0.0. Lo compartido en «C:\\C» se queda para las demás herramientas.", t.TextoConfirmarQuitar(carpetas));
    }

    // 6. Catálogo con firma inválida: mensaje claro en español, nunca una lista vacía en silencio.

    [Fact]
    public async Task Catalogo_con_firma_invalida_se_explica_en_espanol()
    {
        string json = Indice(Entrada("subtitular", "1.0.0", "app", "app1.zip", _app1));
        Publicar(json, _firmante.SignOtherData(Encoding.UTF8.GetBytes(json)));
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        var ex = await Assert.ThrowsAsync<CatalogoNoVerificadoException>(() => instalador.ObtenerTarjetasAsync());

        Assert.Equal(CatalogoNoVerificadoException.Mensaje, ex.Message);
        Assert.IsType<DownloadVerificationException>(ex.InnerException);
        Assert.Equal(ex.Message, MensajesError.Describir(ex));
        Assert.Contains("firma del catálogo no es válida", ex.Message, StringComparison.Ordinal);
        Assert.Contains("no se ofrece nada para instalar", ex.Message, StringComparison.Ordinal);

        (string titulo, string detalle) = MensajesError.DescribirFalloDelCatalogo(ex);
        Assert.Equal("El catálogo no se puede verificar", titulo);
        Assert.Contains("«Buscar actualizaciones»", detalle, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", detalle, StringComparison.Ordinal);
        Assert.Equal(0, _servidor.RequestCount("app1.zip"));
    }

    [Fact]
    public async Task Indice_local_con_firma_invalida_tambien_es_catalogo_no_verificado()
    {
        string indice = Path.Combine(_temp, "indice.json");
        string json = Indice(Entrada("subtitular", "1.0.0", "app", "app1.zip", _app1));
        File.WriteAllText(indice, json);
        File.WriteAllText(indice + ".sig", _firmante.SignOtherData(Encoding.UTF8.GetBytes(json)));
        using var http = new HttpClient();
        using var instalador = new Instalador(CarpetasPrueba, indice, _firmante.PublicKeyBase64, http);

        await Assert.ThrowsAsync<CatalogoNoVerificadoException>(() => instalador.ListaAsync(CancellationToken.None));
    }

    [Fact]
    public void Fallo_de_red_del_catalogo_se_distingue_de_la_firma()
    {
        (string titulo, string detalle) = MensajesError.DescribirFalloDelCatalogo(new PackageDownloadException("No hay conexión con GitHub: x", new HttpRequestException("x")));
        Assert.Equal("Sin conexión con el catálogo", titulo);
        Assert.Contains("conexión a internet", detalle, StringComparison.Ordinal);
        Assert.Equal("Sin conexión con el catálogo. Comprueba tu conexión a internet.", MensajesError.Describir(new PackageDownloadException("x", new HttpRequestException("x"))));

        (titulo, _) = MensajesError.DescribirFalloDelCatalogo(new PackageDownloadException("El índice usa el formato 9."));
        Assert.Equal("No se pudo leer el catálogo", titulo);

        // Un archivo descargado con huella mala no es «catálogo no verificado»: el mensaje de siempre.
        Assert.Equal("La firma del catálogo o de los archivos descargados no es válida.", MensajesError.Describir(new DownloadVerificationException("x")));
    }

    [Fact]
    public async Task Cli_con_firma_invalida_dice_firma_y_sale_con_2()
    {
        string json = Indice(Entrada("subtitular", "1.0.0", "app", "app1.zip", _app1));
        Publicar(json, _firmante.SignOtherData(Encoding.UTF8.GetBytes(json)));
        var error = new StringWriter();

        int codigo = await Ordenes.EjecutarAsync(["lista", "--indice", _servidor.UrlFor("indice.json").AbsoluteUri, "--clave", _firmante.PublicKeyBase64, "--destino", CarpetasPrueba.Herramientas, "--comun", CarpetasPrueba.Comun], new StringWriter(), error);

        Assert.Equal(2, codigo);
        Assert.Equal("Error: " + CatalogoNoVerificadoException.Mensaje, error.ToString().Trim());
    }

    /// <summary>Informa en el mismo hilo de la descarga (Progress&lt;T&gt; lo pospone), para cancelar en el acto.</summary>
    private sealed class ProgresoSincrono(Action<ProgresoDetallado> informar) : IProgress<ProgresoDetallado>
    {
        public void Report(ProgresoDetallado value) => informar(value);
    }
}
