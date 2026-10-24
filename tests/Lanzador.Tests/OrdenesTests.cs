using System.IO.Compression;
using System.Text;
using Lanzador.Core;
using Rodaje.TestServer;

namespace Lanzador.Tests;

/// <summary>Las cuatro órdenes contra un índice firmado con una clave de prueba y servido por un servidor HTTP local.</summary>
public sealed class OrdenesTests : IAsyncLifetime, IDisposable
{
    private readonly TestHttpServer _servidor = TestHttpServer.Start();
    private readonly TestIndexSigner _firmante = new();
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "lanzador-pruebas-" + Guid.NewGuid().ToString("N")[..10]);

    private readonly byte[] _modelo = TestPayloads.Deterministic(50_000, seed: 3);
    private readonly byte[] _modulo;
    private readonly byte[] _app1;
    private readonly byte[] _app2;

    public OrdenesTests()
    {
        Directory.CreateDirectory(_temp);
        string modeloUrl = _servidor.UrlFor("modelo.bin").AbsoluteUri;
        _modulo = Zip(("paquete.json", $$"""
            { "formatVersion": 1, "id": "modulo-eco", "version": "2.0.0", "kind": "module", "name": "Eco", "author": "Dathinel", "license": "GPL-3.0-only",
              "repository": "https://github.com/Dathinel/modulo-eco", "apps": ["rodaje"],
              "module": { "exe": "Eco.exe", "models": [ { "id": "eco-pequeno", "file": "modelos/eco.bin", "size": {{_modelo.Length}}, "sha256": "{{TestPayloads.Sha256Hex(_modelo)}}", "url": "{{modeloUrl}}" } ] } }
            """), ("Eco.exe", "MZ"));
        // Un archivo grande de datos aleatorios, para que un corte a mitad deje algo que reanudar.
        _app1 = Zip(("Subtitular.Cli.exe", Encoding.Latin1.GetString(TestPayloads.Deterministic(300_000, seed: 1))),
            ("herramienta.json", """{ "modulos": ["modulo-eco"] }"""));
        _app2 = Zip(("Subtitular.Cli.exe", Encoding.Latin1.GetString(TestPayloads.Deterministic(300_000, seed: 2))),
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

    private string Herramientas => Path.Combine(_temp, "Herramientas");

    private string Comun => Path.Combine(_temp, "Comun");

    private string Entrada(string id, string version, string kind, string ruta, byte[] contenido, string? sha = null) =>
        $$"""{ "id": "{{id}}", "version": "{{version}}", "kind": "{{kind}}", "url": "{{_servidor.UrlFor(ruta).AbsoluteUri}}", "sha256": "{{sha ?? TestPayloads.Sha256Hex(contenido)}}", "size": {{contenido.Length}} }""";

    private string Indice(params string[] entradas) =>
        $$"""{ "formatVersion": 1, "generated": "2026-10-24T12:00:00Z", "entries": [ {{string.Join(",", [.. entradas, Entrada("modulo-eco", "2.0.0", "module", "modulo.paquete", _modulo)])}} ] }""";

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

    private async Task<(int Codigo, string Salida, string Error)> Correr(params string[] args)
    {
        var salida = new StringWriter();
        var error = new StringWriter();
        int codigo = await Ordenes.EjecutarAsync([.. args, "--indice", _servidor.UrlFor("indice.json").AbsoluteUri, "--clave", _firmante.PublicKeyBase64, "--destino", Herramientas, "--comun", Comun], salida, error);
        return (codigo, salida.ToString(), error.ToString());
    }

    [Fact]
    public async Task Lista_muestra_version_peso_y_estado()
    {
        (int codigo, string salida, _) = await Correr("lista");
        Assert.Equal(0, codigo);
        Assert.Equal("subtitular\t1.0.0\t0.3 MB\tno instalada", salida.Trim());

        Assert.Equal(0, (await Correr("instalar", "subtitular")).Codigo);
        Assert.EndsWith("\tinstalada", (await Correr("lista")).Salida.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Instalar_deja_la_app_su_modulo_y_el_modelo_compartido()
    {
        (int codigo, string salida, string error) = await Correr("instalar", "subtitular");

        Assert.Equal(0, codigo);
        Assert.Contains("subtitular 1.0.0 instalada", salida, StringComparison.Ordinal);
        Assert.Contains("subtitular: 100 %", error, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(Herramientas, "subtitular", "1.0.0", "Subtitular.Cli.exe")));
        Assert.True(File.Exists(Path.Combine(Comun, "modulos", "modulo-eco", "2.0.0", "Eco.exe")));
        Assert.Equal(_modelo, File.ReadAllBytes(Path.Combine(Comun, "modulos", "modulo-eco", "2.0.0", "modelos", "eco.bin")));
        Assert.True(File.Exists(Path.Combine(Comun, "modelos", TestPayloads.Sha256Hex(_modelo))));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(Comun, "descargas"), "*.zip"));

        // Otra vez: nada que bajar.
        Assert.Equal(0, (await Correr("instalar", "subtitular")).Codigo);
        Assert.Equal(1, _servidor.RequestCount("app1.zip"));
        Assert.Equal(1, _servidor.RequestCount("modulo.paquete"));
        Assert.Equal(1, _servidor.RequestCount("modelo.bin"));
    }

    [Fact]
    public async Task Actualizar_cambia_la_version_y_no_vuelve_a_bajar_lo_comun()
    {
        Assert.Equal(0, (await Correr("instalar", "subtitular")).Codigo);
        Assert.Equal("Todo al día.", (await Correr("actualizar")).Salida.Trim());

        Publicar(Indice(Entrada("subtitular", "1.0.0", "app", "app1.zip", _app1), Entrada("subtitular", "1.1.0", "app", "app2.zip", _app2)));
        (int codigo, string salida, _) = await Correr("actualizar");

        Assert.Equal(0, codigo);
        Assert.Equal("subtitular: 1.0.0 → 1.1.0", salida.Trim());
        Assert.True(Directory.Exists(Path.Combine(Herramientas, "subtitular", "1.1.0")));
        Assert.False(Directory.Exists(Path.Combine(Herramientas, "subtitular", "1.0.0")));
        Assert.Equal(1, _servidor.RequestCount("modulo.paquete"));
        Assert.Equal(1, _servidor.RequestCount("modelo.bin"));
    }

    [Fact]
    public async Task Quitar_borra_solo_la_app()
    {
        Assert.Equal(0, (await Correr("instalar", "subtitular")).Codigo);
        (int codigo, string salida, _) = await Correr("quitar", "subtitular");

        Assert.Equal(0, codigo);
        Assert.Contains("quitada", salida, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(Herramientas, "subtitular")));
        Assert.True(File.Exists(Path.Combine(Comun, "modulos", "modulo-eco", "2.0.0", "Eco.exe")));
        Assert.True(File.Exists(Path.Combine(Comun, "modelos", TestPayloads.Sha256Hex(_modelo))));

        (codigo, _, string error) = await Correr("quitar", "subtitular");
        Assert.Equal(1, codigo);
        Assert.Equal("Error: «subtitular» no está instalada.", error.Trim());
    }

    [Fact]
    public async Task Firma_mala_se_rechaza_sin_dejar_archivos()
    {
        string json = Indice(Entrada("subtitular", "1.0.0", "app", "app1.zip", _app1));
        Publicar(json, _firmante.SignOtherData(Encoding.UTF8.GetBytes(json)));

        (int codigo, _, string error) = await Correr("instalar", "subtitular");

        Assert.Equal(2, codigo);
        Assert.Contains("firma", error, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Herramientas));
        Assert.Empty(Directory.Exists(Comun) ? Directory.EnumerateFiles(Comun, "*", SearchOption.AllDirectories) : Enumerable.Empty<string>());
        Assert.Equal(0, _servidor.RequestCount("app1.zip"));
    }

    [Fact]
    public async Task Huella_mala_se_rechaza_y_se_borra_lo_bajado()
    {
        Publicar(Indice(Entrada("subtitular", "1.0.0", "app", "app1.zip", _app1, sha: TestPayloads.Sha256Hex(_app2))));

        (int codigo, _, string error) = await Correr("instalar", "subtitular");

        Assert.Equal(2, codigo);
        Assert.Contains("no es lo que firma el catálogo", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Herramientas));
        Assert.Equal(["indice-visto.txt"], Directory.EnumerateFiles(Path.Combine(Comun, "descargas")).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Descarga_cortada_se_reanuda_con_range()
    {
        _servidor.Map("app1.zip", TestResponse.File(_app1) with { CutAfterBytes = 150_000 }, TestResponse.File(_app1));

        (int codigo, _, _) = await Correr("instalar", "subtitular");

        Assert.Equal(0, codigo);
        Assert.True(File.Exists(Path.Combine(Herramientas, "subtitular", "1.0.0", "Subtitular.Cli.exe")));
        var peticiones = _servidor.Requests.Where(r => r.Path == "/app1.zip").ToList();
        Assert.Equal(2, peticiones.Count);
        Assert.Null(peticiones[0].Range);
        Assert.Matches("^bytes=[1-9][0-9]*-$", peticiones[1].Range);
        Assert.Equal(206, peticiones[1].StatusCode);
    }

    [Theory]
    [InlineData(1, "instalar")]
    [InlineData(1, "quitar")]
    [InlineData(1, "instalar", "desconocida")]
    [InlineData(1, "bailar")]
    [InlineData(1, "lista", "--raro")]
    [InlineData(1, "actualizar", "desconocida")]
    public async Task Errores_de_uso_salen_con_1(int esperado, params string[] args)
    {
        (int codigo, _, string error) = await Correr(args);
        Assert.Equal(esperado, codigo);
        Assert.NotEmpty(error);
        Assert.DoesNotContain("Exception", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sin_servidor_sale_con_2()
    {
        var error = new StringWriter();
        int codigo = await Ordenes.EjecutarAsync(["lista", "--indice", "http://localhost:9/indice.json", "--clave", _firmante.PublicKeyBase64, "--destino", Herramientas, "--comun", Comun], new StringWriter(), error);
        Assert.Equal(2, codigo);
        Assert.StartsWith("Error: ", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"D:\no\existe\indice.json")]
    [InlineData("noexiste.json")]
    public async Task Indice_local_inexistente_sale_con_2_en_espanol(string indice)
    {
        var error = new StringWriter();
        int codigo = await Ordenes.EjecutarAsync(["lista", "--indice", indice, "--destino", Herramientas, "--comun", Comun], new StringWriter(), error);
        Assert.Equal(2, codigo);
        Assert.Equal($"Error: El índice «{indice}» no es una URL ni un archivo que exista.", error.ToString().Trim());
    }

    [Fact]
    public async Task Indice_local_sin_firma_sale_con_2_en_espanol()
    {
        string indice = Path.Combine(_temp, "indice.json");
        File.WriteAllText(indice, Indice());
        var error = new StringWriter();
        int codigo = await Ordenes.EjecutarAsync(["lista", "--indice", indice, "--clave", _firmante.PublicKeyBase64, "--destino", Herramientas, "--comun", Comun], new StringWriter(), error);
        Assert.Equal(2, codigo);
        Assert.Equal($"Error: No se pudo leer el índice «{indice}» o su firma «.sig».", error.ToString().Trim());
    }
}
