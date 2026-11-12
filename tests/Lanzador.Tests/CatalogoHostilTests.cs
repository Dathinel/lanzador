using System.Globalization;
using System.IO.Compression;
using System.Text;
using Lanzador.Core;
using Rodaje.Packages;
using Rodaje.Packages.Download;
using Rodaje.TestServer;

namespace Lanzador.Tests;

/// <summary>
/// Un catálogo firmado pero con entradas hostiles o un JSON roto: cada entrada mal escrita se omite sin tumbar a las
/// demás, lo que no se puede leer en absoluto sale con el mensaje en español, y nada de lo malicioso llega a instalarse.
/// </summary>
public sealed class CatalogoHostilTests : IAsyncLifetime, IDisposable
{
    private readonly TestHttpServer _servidor = TestHttpServer.Start();
    private readonly TestIndexSigner _firmante = new();
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "lanzador-hostil-tests-" + Guid.NewGuid().ToString("N")[..10]);
    private readonly byte[] _app;

    public CatalogoHostilTests()
    {
        Directory.CreateDirectory(_temp);
        _app = Zip(("Subtitular.exe", "MZ" + Encoding.Latin1.GetString(TestPayloads.Deterministic(20_000, seed: 5))));
        _servidor.Map("app.zip", TestResponse.File(_app));
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

    private string Entrada(
        string id = "\"subtitular\"",
        string version = "\"1.0.0\"",
        string kind = "\"app\"",
        string? url = null,
        string? sha = null,
        string? size = null,
        string camposExtra = "") =>
        $$"""{ "id": {{id}}, "version": {{version}}, "kind": {{kind}}, "url": {{url ?? $"\"{_servidor.UrlFor("app.zip").AbsoluteUri}\""}}, "sha256": {{sha ?? $"\"{TestPayloads.Sha256Hex(_app)}\""}}, "size": {{size ?? _app.Length.ToString(CultureInfo.InvariantCulture)}}{{camposExtra}} }""";

    private string Indice(params string[] entradas) => IndiceConFecha("2026-11-01T12:00:00Z", entradas);

    private string IndiceConFecha(string fecha, params string[] entradas) =>
        $$"""{ "formatVersion": 1, "generated": "{{fecha}}", "entries": [ {{string.Join(",", entradas)}} ] }""";

    private void Publicar(string indice, string? firma = null)
    {
        byte[] json = Encoding.UTF8.GetBytes(indice);
        _servidor.Map("indice.json", TestResponse.File(json));
        _servidor.Map("indice.json.sig", TestResponse.File(Encoding.ASCII.GetBytes(firma ?? _firmante.Sign(json))));
    }

    private Instalador CrearInstalador(HttpClient http) =>
        new(CarpetasPrueba, _servidor.UrlFor("indice.json").AbsoluteUri, _firmante.PublicKeyBase64, http);

    /// <summary>Una entrada de otra app («rastrear») con un campo roto: si se aceptara, saldría una segunda tarjeta.</summary>
    private string Rota(string? version = null, string? url = null, string? sha = null, string? size = null, string camposExtra = "") =>
        Entrada(id: "\"rastrear\"", version: version ?? "\"1.0.0\"", url: url, sha: sha, size: size, camposExtra: camposExtra);

    /// <summary>Con la entrada control y la hostil juntas, la tarjeta tiene que ser solo la de «subtitular».</summary>
    private async Task<TarjetaHerramienta> TarjetaDeControlAsync(string entradaHostil)
    {
        Publicar(Indice(Entrada(), entradaHostil));
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        TarjetaHerramienta tarjeta = Assert.Single(await instalador.ObtenerTarjetasAsync());
        Assert.Equal("subtitular", tarjeta.Id);
        return tarjeta;
    }

    private async Task AssertIndiceNoSeLee(string indice)
    {
        Publicar(indice);
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        var ex = await Assert.ThrowsAsync<PackageDownloadException>(() => instalador.ObtenerTarjetasAsync());
        Assert.IsNotType<CatalogoNoVerificadoException>(ex);
        Assert.DoesNotContain("Exception", ex.Message, StringComparison.Ordinal);
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

    // 1. Ids que no pueden ser carpeta ni identificador: la entrada se omite, la buena queda.

    [Theory]
    [InlineData("\"..\"")]
    [InlineData("\"../x\"")]
    [InlineData("\"a/b\"")]
    [InlineData("\"a\\\\b\"")]
    [InlineData("\"a:b\"")]
    [InlineData("\"CON\"")]
    [InlineData("\"NUL\"")]
    [InlineData("\"con.txt\"")]
    [InlineData("\"Subtitular\"")]
    [InlineData("\"sub titular\"")]
    [InlineData("\"-subtitular\"")]
    [InlineData("\"subtitular-\"")]
    [InlineData("\"a..b\"")]
    [InlineData("\"\"")]
    [InlineData("7")]
    [InlineData("true")]
    [InlineData("null")]
    public async Task Entrada_con_id_hostil_se_omite_y_no_llega_a_la_lista(string id)
    {
        await TarjetaDeControlAsync(Entrada(id: id));
    }

    [Fact]
    public async Task Entrada_con_id_hostil_tampoco_se_puede_instalar()
    {
        Publicar(Indice(Entrada(id: "\"../subtitular\"")));
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        Assert.Empty(await instalador.ObtenerTarjetasAsync());
        var ex = await Assert.ThrowsAsync<ErrorDeUso>(() => instalador.InstalarAsync("../subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None));
        Assert.Contains("no tiene ninguna herramienta llamada", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Id_con_espacios_al_borde_se_recorta_y_es_valido()
    {
        Publicar(Indice(Entrada(id: "\"rastrear \"")));
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        TarjetaHerramienta tarjeta = Assert.Single(await instalador.ObtenerTarjetasAsync());
        Assert.Equal("rastrear", tarjeta.Id);
        Assert.Equal("Rastrear", tarjeta.Nombre);
    }

    // 2. Versiones que no son mayor.menor.parche se omiten; las SemVer válidas se aceptan tal cual.

    [Theory]
    [InlineData("\"1\"")]
    [InlineData("\"1.0\"")]
    [InlineData("\"v1.0.0\"")]
    [InlineData("\"\"")]
    [InlineData("\"1.0.0.5\"")]
    [InlineData("\"01.0.0\"")]
    [InlineData("\"1.0.0-\"")]
    [InlineData("\"99999999999.0.0\"")]
    [InlineData("7")]
    [InlineData("null")]
    public async Task Entrada_con_version_rota_se_omite(string version)
    {
        await TarjetaDeControlAsync(Rota(version: version));
    }

    [Theory]
    [InlineData("\"1.0.0-beta\"", "1.0.0-beta")]
    [InlineData("\"1.0.0+build5\"", "1.0.0+build5")]
    [InlineData("\" 0.2.3 \"", "0.2.3")]
    public async Task Entrada_con_version_semver_valida_se_acepta_tal_cual(string version, string esperada)
    {
        Publicar(Indice(Entrada(version: version)));
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        TarjetaHerramienta t = Assert.Single(await instalador.ObtenerTarjetasAsync());
        Assert.Equal(PackageVersion.Parse(esperada), t.VersionDisponible);
        Assert.Equal($"Versión {esperada}", t.TextoVersion);
    }

    // 3. Tamaños que no cuadran: la entrada se omite, o la descarga se rechaza antes de escribir.

    [Theory]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("1.5")]
    [InlineData("\"123\"")]
    [InlineData("99999999999999")]
    [InlineData("1e3")]
    [InlineData("null")]
    public async Task Entrada_con_tamano_roto_se_omite(string size)
    {
        await TarjetaDeControlAsync(Rota(size: size));
    }

    [Fact]
    public async Task Tamano_menor_que_el_archivo_real_se_rechaza_al_instante()
    {
        Publicar(Indice(Entrada(size: (_app.Length - 1).ToString(CultureInfo.InvariantCulture))));
        var error = new StringWriter();
        int codigo = await Ordenes.EjecutarAsync(
            ["instalar", "subtitular", "--indice", _servidor.UrlFor("indice.json").AbsoluteUri, "--clave", _firmante.PublicKeyBase64, "--destino", CarpetasPrueba.Herramientas, "--comun", CarpetasPrueba.Comun],
            new StringWriter(), error);

        Assert.Equal(2, codigo);
        Assert.Contains("más grande que el publicado", error.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(CarpetasPrueba.Herramientas, "subtitular")));
    }

    // 4. URLs que no son de donde toca: la entrada se omite antes de pedir nada.

    [Theory]
    [InlineData("http://evil.example.com/app.zip")]
    [InlineData("https://evil.example.com/app.zip")]
    [InlineData("ftp://127.0.0.1/app.zip")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://github.com/OtroDueño/repo/releases/download/v1/app.zip")]
    [InlineData("https://github.com/Dathinel/repo/releases/download/v1/app.zip?trampa=1")]
    [InlineData("https://github.com:8443/Dathinel/repo/releases/download/v1/app.zip")]
    [InlineData("/relativo/app.zip")]
    [InlineData("")]
    [InlineData("no-es-url")]
    public async Task Entrada_con_url_no_permitida_se_omite(string url)
    {
        await TarjetaDeControlAsync(Rota(url: $"\"{url}\""));
    }

    // El control de todas las «Rota»: la misma entrada sin nada roto sí sale como segunda tarjeta.
    [Fact]
    public async Task Entrada_bien_escrita_de_otra_app_si_aparece()
    {
        Publicar(Indice(Entrada(), Rota()));
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        Assert.Equal(["rastrear", "subtitular"], (await instalador.ObtenerTarjetasAsync()).Select(t => t.Id));
    }

    // 5. «kind» que no es «app» en minúsculas no lo ve el Lanzador.

    [Theory]
    [InlineData("\"App\"")]
    [InlineData("\"APP\"")]
    [InlineData("\"widget\"")]
    [InlineData("\"module\"")]
    [InlineData("7")]
    [InlineData("null")]
    public async Task Entrada_con_kind_que_no_es_app_no_aparece(string kind)
    {
        await TarjetaDeControlAsync(Entrada(id: "\"rastrear\"", kind: kind));
    }

    [Fact]
    public async Task Entrada_sin_kind_es_contenido_y_no_aparece()
    {
        const string entrada = """{ "id": "rastrear", "version": "1.0.0", "url": "http://localhost:1/app.zip", "sha256": "0000000000000000000000000000000000000000000000000000000000000000", "size": 1 }""";
        await TarjetaDeControlAsync(entrada);
    }

    // 6. Huellas que no son 64 hexadecimales se omiten; en mayúsculas se acepta y se instala.

    [Theory]
    [InlineData(63)]
    [InlineData(65)]
    public async Task Entrada_con_huella_de_longitud_mala_se_omite(int largos)
    {
        await TarjetaDeControlAsync(Rota(sha: $"\"{new string('a', largos)}\""));
    }

    [Fact]
    public async Task Entrada_con_huella_no_hexadecimal_se_omite()
    {
        await TarjetaDeControlAsync(Rota(sha: $"\"{new string('g', 64)}\""));
    }

    [Fact]
    public async Task Huella_en_mayusculas_se_acepta_e_instala()
    {
        Publicar(Indice(Entrada(sha: $"\"{TestPayloads.Sha256Hex(_app).ToUpperInvariant()}\"")));
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        await instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(CarpetasPrueba.Herramientas, "subtitular", "1.0.0", "Subtitular.exe")));
    }

    // 7. minApp mal escrito omite la entrada; uno legible pero por encima del Lanzador la filtra y lo dice.

    [Theory]
    [InlineData("2")]
    [InlineData("\"abc\"")]
    [InlineData("true")]
    public async Task Entrada_con_minapp_roto_se_omite(string minApp)
    {
        await TarjetaDeControlAsync(Rota(camposExtra: $", \"minApp\": {minApp}"));
    }

    [Fact]
    public async Task Minapp_por_encima_del_lanzador_filtra_y_lo_explica()
    {
        Publicar(Indice(Entrada(camposExtra: ", \"minApp\": \"99.0.0\"")));
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        Assert.Empty(await instalador.ObtenerTarjetasAsync());
        var ex = await Assert.ThrowsAsync<ErrorDeUso>(() => instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None));
        Assert.Equal("«subtitular» necesita el Lanzador 99.0.0 o posterior.", ex.Message);
    }

    // 8. La misma entrada dos veces: la primera gana y no hay duplicados.

    [Fact]
    public async Task Dos_entradas_identicas_dejan_una_sola_tarjeta()
    {
        Publicar(Indice(Entrada(), Entrada()));
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        Assert.Single(await instalador.ObtenerTarjetasAsync());
    }

    [Fact]
    public async Task Misma_version_con_dos_huellas_instala_la_primera()
    {
        byte[] otra = Zip(("Subtitular.exe", "OTRA"));
        _servidor.Map("otra.zip", TestResponse.File(otra));
        string segunda = $$"""{ "id": "subtitular", "version": "1.0.0", "kind": "app", "url": "{{_servidor.UrlFor("otra.zip").AbsoluteUri}}", "sha256": "{{TestPayloads.Sha256Hex(otra)}}", "size": {{otra.Length}} }""";
        Publicar(Indice(Entrada(), segunda));
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        Assert.Single(await instalador.ObtenerTarjetasAsync());
        await instalador.InstalarAsync("subtitular", (IProgress<ProgresoDetallado>?)null, CancellationToken.None);

        Assert.Equal(1, _servidor.RequestCount("app.zip"));
        Assert.Equal(0, _servidor.RequestCount("otra.zip"));
        Assert.Equal("MZ" + Encoding.Latin1.GetString(TestPayloads.Deterministic(20_000, seed: 5)),
            File.ReadAllText(Path.Combine(CarpetasPrueba.Herramientas, "subtitular", "1.0.0", "Subtitular.exe"), Encoding.Latin1));
    }

    // 9. El JSON del índice roto o con forma que no es: error en español, nunca una lista inventada.

    [Theory]
    [InlineData("""{"formatVersion":1,"entries":[{""")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ no es json")]
    [InlineData("[]")]
    [InlineData("123")]
    [InlineData("\"texto\"")]
    [InlineData("{}")]
    [InlineData("""{"formatVersion":"1","entries":[]}""")]
    [InlineData("""{"formatVersion":1.5,"entries":[]}""")]
    [InlineData("""{"formatVersion":2,"entries":[]}""")]
    public async Task Indice_con_forma_rota_no_se_lee(string indice)
    {
        await AssertIndiceNoSeLee(indice);
    }

    [Theory]
    [InlineData("""{"formatVersion":1,"entries":{"rastrear":{}}}""")]
    [InlineData("""{"formatVersion":1,"generated":"ayer","entries":[42,"x",null,true]}""")]
    [InlineData("""{"formatVersion":1}""")]
    public async Task Indice_legible_sin_entradas_usables_da_lista_vacia(string indice)
    {
        Publicar(indice);
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        Assert.Empty(await instalador.ObtenerTarjetasAsync());
    }

    [Fact]
    public async Task Indice_con_utf8_roto_no_se_lee()
    {
        byte[] json = Encoding.UTF8.GetBytes(Indice(Entrada()));
        json[json.IndexOf((byte)'"') + 30] = 0xFF;
        _servidor.Map("indice.json", TestResponse.File(json));
        _servidor.Map("indice.json.sig", TestResponse.File(Encoding.ASCII.GetBytes(_firmante.Sign(json))));
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        var ex = await Assert.ThrowsAsync<PackageDownloadException>(() => instalador.ObtenerTarjetasAsync());
        Assert.Contains("UTF-8", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Indice_con_sustituto_suelto_escapado_no_se_lee()
    {
        // El JSON lleva el escape \ud800 (un sustituto suelto): ningún campo llega a leerse.
        await AssertIndiceNoSeLee("{ \"formatVersion\": 1, \"entries\": [ { \"id\": \"subtitular\\ud800x\" } ] }");
    }

    [Fact]
    public async Task Indice_vacio_de_entradas_da_lista_vacia_no_error()
    {
        Publicar(Indice());
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        Assert.Empty(await instalador.ObtenerTarjetasAsync());
        Assert.Empty(await instalador.ListaAsync(CancellationToken.None));
    }

    // 10. Un índice más viejo que el último visto se rechaza como firma inválida; un «visto» corrupto no estorba.

    [Fact]
    public async Task Indice_mas_viejo_que_el_visto_es_catalogo_no_verificado()
    {
        Publicar(IndiceConFecha("2026-11-01T13:00:00Z", Entrada()));
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        Assert.Single(await instalador.ObtenerTarjetasAsync());

        Publicar(IndiceConFecha("2026-11-01T12:00:00Z", Entrada(version: "\"9.9.9\"")));

        var ex = await Assert.ThrowsAsync<CatalogoNoVerificadoException>(() => instalador.ObtenerTarjetasAsync(refrescar: true));
        Assert.Equal(CatalogoNoVerificadoException.Mensaje, ex.Message);
        Assert.False(MensajesError.EsFalloDeRed(ex));
    }

    [Fact]
    public async Task Archivo_de_visto_corrupto_se_ignora_y_el_indice_sirve()
    {
        Publicar(Indice(Entrada()));
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);
        Assert.Single(await instalador.ObtenerTarjetasAsync());

        string visto = Path.Combine(CarpetasPrueba.Descargas, "indice-visto.txt");
        File.WriteAllText(visto, "esto no es una fecha");

        Assert.Single(await instalador.ObtenerTarjetasAsync(refrescar: true));
    }

    // 11. La firma del índice, lejos del formato esperado.

    [Fact]
    public async Task Firma_remota_que_no_llega_es_fallo_legible_no_sin_conexion()
    {
        Publicar(Indice(Entrada()));
        _servidor.Map("indice.json.sig", TestResponse.Status(404, "no está"));
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        var ex = await Assert.ThrowsAsync<PackageDownloadException>(() => instalador.ObtenerTarjetasAsync());
        Assert.IsNotType<CatalogoNoVerificadoException>(ex);
        Assert.False(MensajesError.EsFalloDeRed(ex));
    }

    [Fact]
    public async Task Firma_remota_enorme_es_catalogo_no_verificado()
    {
        Publicar(Indice(Entrada()));
        _servidor.Map("indice.json.sig", TestResponse.File(new byte[IndexSignature.MaxSignatureBytes + 1]));
        using var http = new HttpClient();
        using var instalador = CrearInstalador(http);

        await Assert.ThrowsAsync<CatalogoNoVerificadoException>(() => instalador.ObtenerTarjetasAsync());
    }

    [Fact]
    public async Task Indice_local_con_firma_enorme_no_se_usa()
    {
        string indice = Path.Combine(_temp, "indice-local.json");
        File.WriteAllText(indice, Indice(Entrada()));
        File.WriteAllBytes(indice + ".sig", new byte[IndexSignature.MaxSignatureBytes + 1]);
        using var http = new HttpClient();
        using var instalador = new Instalador(CarpetasPrueba, indice, _firmante.PublicKeyBase64, http);

        var ex = await Assert.ThrowsAsync<PackageDownloadException>(() => instalador.ObtenerTarjetasAsync());
        Assert.Contains("demasiado grande", ex.Message, StringComparison.Ordinal);
    }

    // 12. Lo que pide el manifiesto de la app también puede estar mal.

    [Fact]
    public async Task App_que_pide_modulo_inexistente_sale_con_2_explicando_cual()
    {
        byte[] app = Zip(("Subtitular.exe", "MZ"), ("herramienta.json", """{ "modulos": ["modulo-que-no-existe"] }"""));
        _servidor.Map("app.zip", TestResponse.File(app));
        Publicar(Indice(Entrada(sha: $"\"{TestPayloads.Sha256Hex(app)}\"", size: app.Length.ToString(CultureInfo.InvariantCulture))));
        var error = new StringWriter();

        int codigo = await Ordenes.EjecutarAsync(
            ["instalar", "subtitular", "--indice", _servidor.UrlFor("indice.json").AbsoluteUri, "--clave", _firmante.PublicKeyBase64, "--destino", CarpetasPrueba.Herramientas, "--comun", CarpetasPrueba.Comun],
            new StringWriter(), error);

        Assert.Equal(2, codigo);
        Assert.Contains("no tiene el módulo «modulo-que-no-existe»", error.ToString(), StringComparison.Ordinal);
        // La app ya extraída se queda: es instalación a medias, no un fracaso total.
        Assert.True(Directory.Exists(Path.Combine(CarpetasPrueba.Herramientas, "subtitular", "1.0.0")));
    }

    [Fact]
    public async Task Manifiesto_que_no_es_json_valido_sale_con_2_en_espanol()
    {
        byte[] app = Zip(("Subtitular.exe", "MZ"), ("herramienta.json", """{ "modulos": "esto-no-es-una-lista" }"""));
        _servidor.Map("app.zip", TestResponse.File(app));
        Publicar(Indice(Entrada(sha: $"\"{TestPayloads.Sha256Hex(app)}\"", size: app.Length.ToString(CultureInfo.InvariantCulture))));
        var error = new StringWriter();

        int codigo = await Ordenes.EjecutarAsync(
            ["instalar", "subtitular", "--indice", _servidor.UrlFor("indice.json").AbsoluteUri, "--clave", _firmante.PublicKeyBase64, "--destino", CarpetasPrueba.Herramientas, "--comun", CarpetasPrueba.Comun],
            new StringWriter(), error);

        Assert.Equal(2, codigo);
        Assert.Contains("«herramienta.json» de la herramienta no es JSON válido", error.ToString(), StringComparison.Ordinal);
    }
}
