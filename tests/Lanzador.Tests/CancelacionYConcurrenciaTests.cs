using System.Globalization;
using System.IO.Compression;
using System.Text;
using Lanzador.Core;
using Rodaje.Packages;
using Rodaje.Packages.Download;
using Rodaje.TestServer;

namespace Lanzador.Tests;

/// <summary>
/// Cancelar a mitad de cada fase (descarga, extracción, modelo) y dos operaciones a la vez sobre la misma app:
/// lo que queda en disco se puede reanudar o reinstalar sin descargar de más, y la carpeta nunca queda corrupta.
/// </summary>
public sealed class CancelacionYConcurrenciaTests : IAsyncLifetime, IDisposable
{
    private readonly TestHttpServer _servidor = TestHttpServer.Start();
    private readonly TestIndexSigner _firmante = new();
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "lanzador-cancela-tests-" + Guid.NewGuid().ToString("N")[..10]);
    private readonly byte[] _modelo = TestPayloads.Deterministic(200_000, seed: 8);
    private readonly byte[] _modulo;
    private readonly byte[] _app;

    public CancelacionYConcurrenciaTests()
    {
        Directory.CreateDirectory(_temp);
        string modeloUrl = _servidor.UrlFor("modelo.bin").AbsoluteUri;
        _modulo = Zip(("paquete.json", $$"""
            { "formatVersion": 1, "id": "modulo-eco", "version": "2.0.0", "kind": "module", "name": "Eco", "author": "Dathinel", "license": "GPL-3.0-only",
              "repository": "https://github.com/Dathinel/modulo-eco", "apps": ["rodaje"],
              "module": { "exe": "Eco.exe", "models": [ { "id": "eco-pequeno", "file": "modelos/eco.bin", "size": {{_modelo.Length}}, "sha256": "{{TestPayloads.Sha256Hex(_modelo)}}", "url": "{{modeloUrl}}" } ] } }
            """), ("Eco.exe", "MZ"));
        _app = Zip(("Subtitular.exe", Encoding.Latin1.GetString(TestPayloads.Deterministic(300_000, seed: 1))),
            ("herramienta.json", """{ "modulos": ["modulo-eco"] }"""));
        _servidor.Map("modelo.bin", TestResponse.File(_modelo));
        _servidor.Map("modulo.paquete", TestResponse.File(_modulo));
        _servidor.Map("app.zip", TestResponse.File(_app));
        Publicar();
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
        $$"""{ "id": "{{id}}", "version": "{{version}}", "kind": "{{kind}}", "url": "{{_servidor.UrlFor(ruta).AbsoluteUri}}", "sha256": "{{TestPayloads.Sha256Hex(contenido)}}", "size": {{contenido.Length.ToString(CultureInfo.InvariantCulture)}} }""";

    private void Publicar()
    {
        string indice = $$"""{ "formatVersion": 1, "generated": "2026-11-01T12:00:00Z", "entries": [ {{Entrada("subtitular", "1.0.0", "app", "app.zip", _app)}}, {{Entrada("modulo-eco", "2.0.0", "module", "modulo.paquete", _modulo)}} ] }""";
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

    /// <summary>Cancela cuando el progreso llega a la fase pedida (o al byte pedido dentro de ella).</summary>
    private sealed class Cancelador : IProgress<ProgresoDetallado>
    {
        private readonly CancellationTokenSource _cts;
        private readonly Func<ProgresoDetallado, bool> _cuando;
        private int _disparado;

        public Cancelador(CancellationTokenSource cts, Func<ProgresoDetallado, bool> cuando)
        {
            _cts = cts;
            _cuando = cuando;
        }

        public int Disparado => _disparado;

        public void Report(ProgresoDetallado value)
        {
            if (_cuando(value))
            {
                Interlocked.Increment(ref _disparado);
                _cts.Cancel();
            }
        }
    }

    /// <summary>Progreso que avisa en el mismo hilo que informa (<see cref="Progress{T}"/> lo haría más tarde).</summary>
    private sealed class Aviso(Action<ProgresoDetallado> alInformar) : IProgress<ProgresoDetallado>
    {
        public void Report(ProgresoDetallado value) => alInformar(value);
    }

    private string Exe => Path.Combine(CarpetasPrueba.Herramientas, "subtitular", "1.0.0", "Subtitular.exe");

    // 1. Cancelar durante la descarga deja un .parcial que se reanuda, no se repite.

    [Fact]
    public async Task Cancelar_durante_la_descarga_guarda_el_parcial_y_se_reanuda()
    {
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        using var cts = new CancellationTokenSource();
        var progreso = new Cancelador(cts, p => p.Fase == "Descargando" && p.BytesRecibidos > 0);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => instalador.InstalarAsync("subtitular", progreso, cts.Token));

        Assert.True(progreso.Disparado > 0);
        TarjetaHerramienta t = Assert.Single(await instalador.ObtenerTarjetasAsync());
        Assert.Equal(EstadoTarjeta.NoInstalada, t.Estado);
        Assert.True(t.PuedeReanudar);
        Assert.True(t.DescargaParcialBytes > 0 && t.DescargaParcialBytes < _app.Length);
        Assert.Equal("Reanudar descarga", t.TextoInstalar);
        Assert.False(File.Exists(Exe));

        // La segunda vez continúa donde quedó: un Range y ya está instalada.
        await instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);

        Assert.Equal(2, _servidor.RequestCount("app.zip"));
        var peticiones = _servidor.Requests.Where(r => r.Path == "/app.zip").ToList();
        Assert.Null(peticiones[0].Range);
        Assert.Matches("^bytes=[1-9][0-9]*-$", peticiones[1].Range);
        Assert.Equal(EstadoTarjeta.Instalada, Assert.Single(await instalador.ObtenerTarjetasAsync()).Estado);
        Assert.Equal(TestPayloads.Deterministic(300_000, seed: 1), File.ReadAllBytes(Exe));
    }

    // 2. Cancelar durante la extracción deja el zip entero y reinstalar no descarga nada.

    [Fact]
    public async Task Cancelar_durante_la_extraccion_guarda_el_zip_y_reinstalar_no_descarga()
    {
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        using var cts = new CancellationTokenSource();
        var progreso = new Cancelador(cts, p => p.Fase == "Extrayendo");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => instalador.InstalarAsync("subtitular", progreso, cts.Token));

        TarjetaHerramienta t = Assert.Single(await instalador.ObtenerTarjetasAsync());
        Assert.Equal(EstadoTarjeta.NoInstalada, t.Estado);
        Assert.True(t.ZipDescargado);
        Assert.False(t.PuedeReanudar);
        Assert.Equal("Instalar (ya descargado)", t.TextoInstalar);
        Assert.False(Directory.Exists(Path.Combine(CarpetasPrueba.Herramientas, "subtitular", "1.0.0.instalando")));
        Assert.False(File.Exists(Exe));

        await instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);

        Assert.True(_servidor.RequestCount("app.zip") == 1, "El zip ya estaba entero: no se pidió otra vez.");
        Assert.Equal(EstadoTarjeta.Instalada, Assert.Single(await instalador.ObtenerTarjetasAsync()).Estado);
        Assert.True(File.Exists(Exe));
    }

    // 3. Cancelar durante el modelo deja la app y el módulo puestos: falta solo el modelo.

    [Fact]
    public async Task Cancelar_durante_el_modelo_deja_instalacion_incompleta_que_se_completa()
    {
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        using var cts = new CancellationTokenSource();
        var progreso = new Cancelador(cts, p => p.Fase == "Descargando modelo" && p.BytesRecibidos > 0 && p.BytesRecibidos < p.BytesTotales);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => instalador.InstalarAsync("subtitular", progreso, cts.Token));

        Assert.True(Directory.Exists(Path.Combine(CarpetasPrueba.Herramientas, "subtitular", "1.0.0")), "La app extraída se queda.");
        Assert.True(Directory.Exists(Path.Combine(CarpetasPrueba.Comun, "modulos", "modulo-eco", "2.0.0")), "El módulo extraído se queda.");
        TarjetaHerramienta t = Assert.Single(await instalador.ObtenerTarjetasAsync());
        Assert.Equal(EstadoTarjeta.Actualizable, t.Estado);
        Assert.True(t.InstalacionIncompleta);
        Assert.Equal("Completar instalación", t.TextoActualizar);
        Assert.Equal("Versión 1.0.0 (faltan módulos o modelos)", t.TextoVersion);

        await instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);

        Assert.Equal(1, _servidor.RequestCount("app.zip"));
        Assert.Equal(1, _servidor.RequestCount("modulo.paquete"));
        Assert.Equal(2, _servidor.RequestCount("modelo.bin"));
        Assert.Matches("^bytes=[1-9][0-9]*-$", _servidor.Requests.Last(r => r.Path == "/modelo.bin").Range);
        Assert.Equal(EstadoTarjeta.Instalada, Assert.Single(await instalador.ObtenerTarjetasAsync()).Estado);
        Assert.Equal(_modelo, File.ReadAllBytes(Path.Combine(CarpetasPrueba.Comun, "modulos", "modulo-eco", "2.0.0", "modelos", "eco.bin")));
    }

    // 4. Dos instalaciones a la vez van en turno: la segunda espera a la primera y ya no tiene nada que bajar.

    [Fact]
    public async Task Dos_instalaciones_a_la_vez_van_en_turno_y_no_bajan_nada_dos_veces()
    {
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        PackageVersion[] versiones = await Task.WhenAll(
            instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None),
            instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None));

        Assert.Equal([PackageVersion.Parse("1.0.0"), PackageVersion.Parse("1.0.0")], versiones);
        Assert.Equal(1, _servidor.RequestCount("app.zip"));
        Assert.Equal(1, _servidor.RequestCount("modulo.paquete"));
        Assert.Equal(1, _servidor.RequestCount("modelo.bin"));
        Assert.Equal(TestPayloads.Deterministic(300_000, seed: 1), File.ReadAllBytes(Exe));
        Assert.Equal(EstadoTarjeta.Instalada, Assert.Single(await instalador.ObtenerTarjetasAsync()).Estado);
    }

    [Fact]
    public async Task La_que_espera_su_turno_lo_dice_y_se_puede_cancelar()
    {
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        var dentro = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var seguir = new ManualResetEventSlim();
        // La primera se queda parada a mitad de la descarga, con el turno cogido, hasta que la prueba la suelta.
        Task<PackageVersion> primera = instalador.InstalarAsync("subtitular", new Aviso(p =>
        {
            if (p.Fase == "Descargando" && dentro.TrySetResult())
            {
                seguir.Wait();
            }
        }), CancellationToken.None);
        try
        {
            await Task.WhenAny(dentro.Task, primera);
            var fases = new List<string>();
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => instalador.InstalarAsync("subtitular", new Aviso(p => fases.Add(p.Fase)), cts.Token));

            Assert.Equal(["Esperando a que termine otra instalación"], fases);
        }
        finally
        {
            seguir.Set();
        }

        Assert.Equal(PackageVersion.Parse("1.0.0"), await primera);
        Assert.Equal(1, _servidor.RequestCount("app.zip"));
    }

    // 5. Muchas lecturas de tarjetas a la vez sobre el mismo Instalador (y su HttpClient) no se estorban.

    [Fact]
    public async Task Lecturas_de_tarjetas_en_paralelo_dan_todas_el_mismo_estado()
    {
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        await instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);

        var lecturas = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => instalador.ObtenerTarjetasAsync()));

        Assert.All(lecturas, tarjetas =>
        {
            TarjetaHerramienta t = Assert.Single(tarjetas);
            Assert.Equal(EstadoTarjeta.Instalada, t.Estado);
            Assert.Equal(Exe, t.Ejecutable);
        });
    }

    // 5b. Leer tarjetas mientras desaparecen y reaparecen descargas (el .parcial se renombra al acabar; «Quitar» lo
    // borra) no lanza: el peso de cada descarga se mide con una sola consulta al disco, no «existe» y luego «cuánto pesa».

    [Fact]
    public async Task Leer_tarjetas_mientras_desaparecen_las_descargas_no_lanza()
    {
        // Catálogo con muchas versiones de la misma app: DescargasDeLaApp enumera un .parcial por cada una.
        var entradas = new List<string>();
        string descargas = Path.Combine(_temp, "Comun", "descargas");
        Directory.CreateDirectory(descargas);
        var parciales = new List<string>();
        for (int i = 0; i < 60; i++)
        {
            byte[] relleno = TestPayloads.Deterministic(64, seed: 100 + i);
            entradas.Add($$"""{ "id": "subtitular", "version": "1.0.{{i.ToString(CultureInfo.InvariantCulture)}}", "kind": "app", "url": "{{_servidor.UrlFor("app.zip").AbsoluteUri}}", "sha256": "{{TestPayloads.Sha256Hex(relleno)}}", "size": 64 }""");
            string parcial = Path.Combine(descargas, TestPayloads.Sha256Hex(relleno) + ".zip" + FileDownloader.PartialSuffix);
            File.WriteAllBytes(parcial, relleno);
            parciales.Add(parcial);
        }

        string indice = $$"""{ "formatVersion": 1, "generated": "2026-11-01T12:00:00Z", "entries": [ {{string.Join(", ", entradas)}} ] }""";
        byte[] json = Encoding.UTF8.GetBytes(indice);
        _servidor.Map("indice.json", TestResponse.File(json));
        _servidor.Map("indice.json.sig", TestResponse.File(Encoding.ASCII.GetBytes(_firmante.Sign(json))));

        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        // Sacar y volver a meter todos los .parcial sin pausa: lo mismo que hace el renombrado final
        // de una descarga o el borrado al quitar, pero miles de veces en el rato que dura la prueba.
        using var cierre = new CancellationTokenSource();
        Task mover = Task.Run(() =>
        {
            string[] temporales = [.. parciales.Select(p => p + ".tmp")];
            while (!cierre.IsCancellationRequested)
            {
                for (int i = 0; i < parciales.Count; i++)
                {
                    if (File.Exists(parciales[i]))
                    {
                        File.Move(parciales[i], temporales[i]);
                    }
                }
                for (int i = 0; i < parciales.Count; i++)
                {
                    if (File.Exists(temporales[i]))
                    {
                        File.Move(temporales[i], parciales[i]);
                    }
                }
            }
        });

        try
        {
            for (int i = 0; i < 25; i++)
            {
                TarjetaHerramienta tarjeta = Assert.Single(await instalador.ObtenerTarjetasAsync());
                Assert.Equal(EstadoTarjeta.NoInstalada, tarjeta.Estado);
            }
        }
        finally
        {
            await cierre.CancelAsync();
            await mover;
        }
    }

    // 6. La cancelación del propio Lanzador llega hasta la lectura del catálogo.

    [Fact]
    public async Task Leer_el_catalogo_con_cancelacion_previa_lanza_operation_canceled()
    {
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => instalador.ListaAsync(cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => instalador.CargarTarjetasAsync(ct: cts.Token));
    }
}
