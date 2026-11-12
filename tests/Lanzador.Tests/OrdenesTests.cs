using System.IO.Compression;
using System.Text;
using Lanzador.Core;
using Rodaje.Packages.Download;
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
    [InlineData(1, "")]
    [InlineData(1, "instalar", "")]
    public async Task Errores_de_uso_salen_con_1(int esperado, params string[] args)
    {
        (int codigo, _, string error) = await Correr(args);
        Assert.Equal(esperado, codigo);
        Assert.NotEmpty(error);
        Assert.DoesNotContain("Exception", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Zip_con_ruta_prohibida_sale_con_2_en_espanol()
    {
        byte[] malo = Zip(("../malicioso.txt", "x"));
        _servidor.Map("malo.zip", TestResponse.File(malo));
        Publicar(Indice(Entrada("subtitular", "1.0.0", "app", "malo.zip", malo)));

        (int codigo, _, string error) = await Correr("instalar", "subtitular");

        Assert.Equal(2, codigo);
        Assert.Contains("ruta no permitida", error, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(Herramientas, "subtitular")));
    }

    [Fact]
    public async Task Version_instalada_mas_nueva_que_el_catalogo_no_se_revierte()
    {
        string masNueva = Path.Combine(Herramientas, "subtitular", "9.0.0");
        Directory.CreateDirectory(masNueva);

        (int codigo, _, _) = await Correr("instalar", "subtitular");

        Assert.Equal(0, codigo);
        Assert.True(Directory.Exists(masNueva));
        Assert.False(Directory.Exists(Path.Combine(Herramientas, "subtitular", "1.0.0")));
        Assert.Equal(0, _servidor.RequestCount("app1.zip"));
    }

    [Fact]
    public async Task Indice_local_enorme_sale_con_2_en_espanol()
    {
        string indice = Path.Combine(_temp, "indice.json");
        File.WriteAllBytes(indice, new byte[SignedIndex.MaxBytes + 1]);
        File.WriteAllText(indice + ".sig", "firma");
        var error = new StringWriter();

        int codigo = await Ordenes.EjecutarAsync(["lista", "--indice", indice, "--clave", _firmante.PublicKeyBase64, "--destino", Herramientas, "--comun", Comun], new StringWriter(), error);

        Assert.Equal(2, codigo);
        Assert.Contains("demasiado grande", error.ToString(), StringComparison.Ordinal);
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
        Assert.Equal($"Error: El índice «{indice}» no es una URL http(s) ni un archivo que exista.", error.ToString().Trim());
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

    // 9. Argumentos de la orden: cuentan hasta dos, cada uno tiene su error, y las mayúsculas no valen.

    [Fact]
    public async Task Lista_con_argumento_sale_con_1()
    {
        (int codigo, _, string error) = await Correr("lista", "extra");
        Assert.Equal(1, codigo);
        Assert.Equal("Error: «lista» no lleva argumentos.", error.Trim());
    }

    [Fact]
    public async Task Tercer_argumento_sobra()
    {
        (int codigo, _, string error) = await Correr("instalar", "a", "b");
        Assert.Equal(1, codigo);
        Assert.Equal("Error: Sobra el argumento «b».", error.Trim());
    }

    [Fact]
    public async Task La_orden_dos_veces_se_trata_como_nombre_de_app()
    {
        (int codigo, _, string error) = await Correr("instalar", "instalar");
        Assert.Equal(1, codigo);
        Assert.Equal("Error: El catálogo no tiene ninguna herramienta llamada «instalar». Mira «lanzador lista».", error.Trim());
    }

    [Fact]
    public async Task Orden_en_mayusculas_no_se_reconoce()
    {
        (int codigo, _, string error) = await Correr("LISTA");
        Assert.Equal(1, codigo);
        Assert.StartsWith("Uso: lanzador", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--INDICE")]
    [InlineData("--")]
    [InlineData("--indice=x")]
    public async Task Argumento_con_guion_desconocido_sale_con_1(string raro)
    {
        (int codigo, _, string error) = await Correr("lista", raro);
        Assert.Equal(1, codigo);
        Assert.Equal($"Error: Opción desconocida: {raro}", error.Trim());
    }

    [Fact]
    public async Task Opcion_desconocida_despues_de_la_orden_sale_con_1()
    {
        (int codigo, _, string error) = await Correr("instalar", "-x");
        Assert.Equal(1, codigo);
        Assert.Equal("Error: Opción desconocida: -x", error.Trim());
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public async Task Ayuda_sale_con_0_y_el_uso_va_a_la_salida(string ayuda)
    {
        (int codigo, string salida, string error) = await Correr(ayuda);
        Assert.Equal(0, codigo);
        Assert.StartsWith("Uso: lanzador", salida, StringComparison.Ordinal);
        Assert.Empty(error.Trim());
    }

    [Fact]
    public async Task Ayuda_despues_de_la_orden_tambien_sale_con_0()
    {
        (int codigo, string salida, string error) = await Correr("lista", "--help");
        Assert.Equal(0, codigo);
        Assert.StartsWith("Uso: lanzador", salida, StringComparison.Ordinal);
        Assert.Empty(error.Trim());
    }

    [Fact]
    public async Task Actualizar_sin_nada_instalado_dice_todo_al_dia()
    {
        (int codigo, string salida, _) = await Correr("actualizar");
        Assert.Equal(0, codigo);
        Assert.Equal("Todo al día.", salida.Trim());
    }

    [Fact]
    public async Task Actualizar_app_que_no_esta_instalada_sale_con_1()
    {
        (int codigo, _, string error) = await Correr("actualizar", "fantasma");
        Assert.Equal(1, codigo);
        Assert.Equal("Error: «fantasma» no está instalada; usa «lanzador instalar fantasma».", error.Trim());
    }

    [Theory]
    [InlineData("subtitular/1.0.0")]
    [InlineData("..")]
    [InlineData("a\\b")]
    [InlineData("con")]
    public async Task Quitar_id_que_no_es_identificador_sale_con_1(string id)
    {
        (int codigo, _, string error) = await Correr("quitar", id);
        Assert.Equal(1, codigo);
        Assert.Equal($"Error: «{id}» no está instalada.", error.Trim());
    }

    // 10. El índice y la clave también pueden venir mal.

    [Fact]
    public async Task Clave_basura_con_indice_local_sale_con_2_en_espanol()
    {
        string indice = Path.Combine(_temp, "indice-clave.json");
        File.WriteAllText(indice, Indice());
        File.WriteAllText(indice + ".sig", _firmante.Sign(Encoding.UTF8.GetBytes(Indice())));
        var error = new StringWriter();

        int codigo = await Ordenes.EjecutarAsync(["lista", "--indice", indice, "--clave", "esto-no-es-base64", "--destino", Herramientas, "--comun", Comun], new StringWriter(), error);

        Assert.Equal(2, codigo);
        Assert.Equal("Error: La clave pública del catálogo no es válida.", error.ToString().Trim());
    }

    [Fact]
    public async Task Clave_basura_con_indice_remoto_sale_con_2()
    {
        var error = new StringWriter();
        int codigo = await Ordenes.EjecutarAsync(["lista", "--indice", _servidor.UrlFor("indice.json").AbsoluteUri, "--clave", "esto-no-es-base64", "--destino", Herramientas, "--comun", Comun], new StringWriter(), error);
        Assert.Equal(2, codigo);
        Assert.StartsWith("Error: ", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("clave", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, _servidor.RequestCount("indice.json"));
    }

    [Fact]
    public async Task Indice_que_es_una_carpeta_sale_con_2()
    {
        var error = new StringWriter();
        int codigo = await Ordenes.EjecutarAsync(["lista", "--indice", _temp, "--destino", Herramientas, "--comun", Comun], new StringWriter(), error);
        Assert.Equal(2, codigo);
        Assert.Equal($"Error: El índice «{_temp}» no es una URL http(s) ni un archivo que exista.", error.ToString().Trim());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Indice_vacio_o_espacios_sale_con_2_en_espanol(string indice)
    {
        var error = new StringWriter();
        int codigo = await Ordenes.EjecutarAsync(["lista", "--indice", indice, "--destino", Herramientas, "--comun", Comun], new StringWriter(), error);
        Assert.Equal(2, codigo);
        Assert.Equal($"Error: El índice «{indice}» no es una URL http(s) ni un archivo que exista.", error.ToString().TrimEnd());
    }

    [Theory]
    [InlineData("ftp://ejemplo.com/indice.json")]
    [InlineData("mailto:alguien@ejemplo.com")]
    public async Task Indice_con_esquema_que_no_es_http_sale_con_2_en_espanol(string indice)
    {
        var error = new StringWriter();
        int codigo = await Ordenes.EjecutarAsync(["lista", "--indice", indice, "--destino", Herramientas, "--comun", Comun], new StringWriter(), error);
        Assert.Equal(2, codigo);
        Assert.Equal($"Error: El índice «{indice}» no es una URL http(s) ni un archivo que exista.", error.ToString().TrimEnd());
    }

    // 11. «actualizar» sin app recorre todo lo instalado, también lo que el catálogo ya no ofrece.

    [Fact]
    public async Task Actualizar_todas_no_se_para_en_una_que_el_catalogo_ya_no_ofrece()
    {
        Assert.Equal(0, (await Correr("instalar", "subtitular")).Codigo);
        // Las dos van antes que «subtitular»: una retirada del catálogo y otra que pide un Lanzador más nuevo.
        Directory.CreateDirectory(Path.Combine(Herramientas, "retirada", "1.0.0"));
        Directory.CreateDirectory(Path.Combine(Herramientas, "rastrear", "1.0.0"));
        string exigente = Entrada("rastrear", "2.0.0", "app", "app2.zip", _app2).Replace(" }", ", \"minApp\": \"99.0.0\" }", StringComparison.Ordinal);
        Publicar(Indice(exigente, Entrada("subtitular", "1.0.0", "app", "app1.zip", _app1), Entrada("subtitular", "1.1.0", "app", "app2.zip", _app2)));

        (int codigo, string salida, string error) = await Correr("actualizar");

        Assert.Equal(0, codigo);
        Assert.Equal("subtitular: 1.0.0 → 1.1.0", salida.Trim());
        Assert.DoesNotContain("Error:", error, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.Combine(Herramientas, "retirada", "1.0.0")));
        Assert.True(Directory.Exists(Path.Combine(Herramientas, "rastrear", "1.0.0")));

        // Pedida por su nombre sí se dice por qué no se puede.
        (codigo, _, error) = await Correr("actualizar", "rastrear");
        Assert.Equal(1, codigo);
        Assert.Equal("Error: «rastrear» necesita el Lanzador 99.0.0 o posterior.", error.Trim());
    }
}
