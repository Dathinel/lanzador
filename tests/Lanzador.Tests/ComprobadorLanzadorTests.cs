using Lanzador.Core;
using Rodaje.Packages;
using Rodaje.TestServer;

namespace Lanzador.Tests;

/// <summary>
/// El aviso de versión nueva del propio Lanzador, con una fuente falsa en vez de GitHub: release nueva, igual y menor, tag
/// raro, error de red, versión descartada con «Más tarde», 24 h no cumplidas, interruptor apagado y los ajustes en disco.
/// </summary>
public sealed class ComprobadorLanzadorTests : IDisposable
{
    private static readonly PackageVersion Actual = PackageVersion.Parse("1.0.0");
    private static readonly DateTimeOffset Inicio = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _temp = Path.Combine(Path.GetTempPath(), "lanzador-ajustes-" + Guid.NewGuid().ToString("N")[..10]);
    private readonly RelojFalso _reloj = new(Inicio);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string Release(string tag, string url = "https://github.com/Dathinel/lanzador/releases/tag/v1.1.0") =>
        $$"""{ "url": "https://api.github.com/repos/Dathinel/lanzador/releases/1", "tag_name": "{{tag}}", "name": "Lanzador {{tag}}", "draft": false, "prerelease": false, "html_url": "{{url}}", "assets": [] }""";

    private ComprobadorLanzador Comprobador(FuenteFalsa fuente) => new(fuente, Actual, _reloj);

    // 1. Interpretar el JSON de releases/latest.

    [Fact]
    public void Interpreta_tag_vXYZ_y_html_url()
    {
        ReleaseLanzador? r = ComprobadorLanzador.Interpretar(Release("v1.1.0"));

        Assert.NotNull(r);
        Assert.Equal(PackageVersion.Parse("1.1.0"), r.Version);
        Assert.Equal(new Uri("https://github.com/Dathinel/lanzador/releases/tag/v1.1.0"), r.Url);
        Assert.Equal(PackageVersion.Parse("2.0.0"), ComprobadorLanzador.Interpretar(Release("2.0.0"))?.Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ esto no es json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("""{ "message": "Not Found" }""")]
    [InlineData("""{ "tag_name": "ffmpeg-7.1", "html_url": "https://github.com/x" }""")]
    [InlineData("""{ "tag_name": "v1.1", "html_url": "https://github.com/x" }""")]
    [InlineData("""{ "tag_name": "v1.1.0.5", "html_url": "https://github.com/x" }""")]
    [InlineData("""{ "tag_name": "v1.1.0" }""")]
    [InlineData("""{ "tag_name": "v1.1.0", "html_url": "javascript:alert(1)" }""")]
    [InlineData("""{ "tag_name": "v1.1.0", "html_url": "http://github.com/x" }""")]
    [InlineData("""{ "tag_name": "v1.1.0", "html_url": "https://github.com/x", "draft": true }""")]
    public void Tag_raro_url_rara_o_borrador_no_es_una_release(string json)
    {
        Assert.Null(ComprobadorLanzador.Interpretar(json));
    }

    // 2. Decisión: nueva, igual, menor, error de red.

    [Fact]
    public async Task Release_mas_nueva_avisa_con_la_franja_y_anota_la_consulta()
    {
        ComprobacionLanzador c = await Comprobador(new FuenteFalsa(Release("v1.1.0"))).ComprobarAsync(new Ajustes(), aMano: false);

        Assert.Equal(ResultadoLanzador.VersionNueva, c.Resultado);
        Assert.Equal("Lanzador 1.1.0 disponible", c.TextoFranja);
        Assert.Equal(new Uri("https://github.com/Dathinel/lanzador/releases/tag/v1.1.0"), c.Release!.Url);
        Assert.Equal(Inicio, c.Ajustes.UltimaConsultaLanzador);
    }

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("v0.9.9")]
    [InlineData("v1.0.0-beta.2")]
    public async Task Release_igual_o_mas_vieja_esta_al_dia_y_no_hay_franja(string tag)
    {
        ComprobacionLanzador c = await Comprobador(new FuenteFalsa(Release(tag))).ComprobarAsync(new Ajustes(), aMano: false);

        Assert.Equal(ResultadoLanzador.AlDia, c.Resultado);
        Assert.Null(c.TextoFranja);
        Assert.Equal(Inicio, c.Ajustes.UltimaConsultaLanzador);
    }

    [Fact]
    public async Task Sin_red_o_con_tiempo_agotado_silencio_y_se_vuelve_a_intentar_la_proxima_vez()
    {
        var sinRed = new FuenteFalsa(new HttpRequestException("No such host"));
        ComprobacionLanzador c = await Comprobador(sinRed).ComprobarAsync(new Ajustes(), aMano: false);
        Assert.Equal(ResultadoLanzador.SinRespuesta, c.Resultado);
        Assert.Null(c.TextoFranja);
        Assert.Null(c.Ajustes.UltimaConsultaLanzador);

        var tarda = new FuenteFalsa(new TaskCanceledException("5 s"));
        c = await Comprobador(tarda).ComprobarAsync(new Ajustes(), aMano: true);
        Assert.Equal(ResultadoLanzador.SinRespuesta, c.Resultado);

        // Un 404 de releases/latest (solo prereleases publicadas) también es silencio.
        var noHay = new FuenteFalsa(new HttpRequestException("404", null, System.Net.HttpStatusCode.NotFound));
        c = await Comprobador(noHay).ComprobarAsync(new Ajustes(), aMano: false);
        Assert.Equal(ResultadoLanzador.SinRespuesta, c.Resultado);
    }

    [Fact]
    public async Task Respuesta_rara_es_silencio_pero_cuenta_como_consulta()
    {
        ComprobacionLanzador c = await Comprobador(new FuenteFalsa("""{ "message": "API rate limit exceeded" }""")).ComprobarAsync(new Ajustes(), aMano: false);

        Assert.Equal(ResultadoLanzador.SinRespuesta, c.Resultado);
        Assert.Equal(Inicio, c.Ajustes.UltimaConsultaLanzador);
    }

    [Fact]
    public async Task La_cancelacion_de_la_ventana_si_se_propaga()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var fuente = new FuenteFalsa(new OperationCanceledException(cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Comprobador(fuente).ComprobarAsync(new Ajustes(), aMano: false, cts.Token));
    }

    // 3. Descartada con «Más tarde», 24 h e interruptor.

    [Fact]
    public async Task Mas_tarde_descarta_esa_version_pero_no_la_siguiente()
    {
        var fuente = new FuenteFalsa(Release("v1.1.0"));
        ComprobacionLanzador primera = await Comprobador(fuente).ComprobarAsync(new Ajustes(), aMano: false);
        Ajustes descartados = primera.Descartar();
        Assert.Equal("1.1.0", descartados.VersionDescartada);

        _reloj.Advance(TimeSpan.FromHours(25));
        ComprobacionLanzador segunda = await Comprobador(fuente).ComprobarAsync(descartados, aMano: false);
        Assert.Equal(ResultadoLanzador.Descartada, segunda.Resultado);
        Assert.Null(segunda.TextoFranja);

        _reloj.Advance(TimeSpan.FromHours(25));
        ComprobacionLanzador tercera = await Comprobador(new FuenteFalsa(Release("v1.2.0"))).ComprobarAsync(descartados, aMano: false);
        Assert.Equal(ResultadoLanzador.VersionNueva, tercera.Resultado);
        Assert.Equal("Lanzador 1.2.0 disponible", tercera.TextoFranja);
    }

    [Fact]
    public async Task No_consulta_dos_veces_en_24_horas_salvo_a_mano()
    {
        var fuente = new FuenteFalsa(Release("v1.1.0"));
        ComprobadorLanzador comprobador = Comprobador(fuente);
        ComprobacionLanzador primera = await comprobador.ComprobarAsync(new Ajustes(), aMano: false);
        Assert.Equal(1, fuente.Consultas);

        _reloj.Advance(TimeSpan.FromHours(23));
        ComprobacionLanzador segunda = await comprobador.ComprobarAsync(primera.Ajustes, aMano: false);
        Assert.Equal(ResultadoLanzador.NoTocaba, segunda.Resultado);
        Assert.Same(primera.Ajustes, segunda.Ajustes);
        Assert.Equal(1, fuente.Consultas);

        ComprobacionLanzador aMano = await comprobador.ComprobarAsync(primera.Ajustes, aMano: true);
        Assert.Equal(ResultadoLanzador.VersionNueva, aMano.Resultado);
        Assert.Equal(2, fuente.Consultas);
        Assert.Equal(Inicio.AddHours(23), aMano.Ajustes.UltimaConsultaLanzador);

        _reloj.Advance(TimeSpan.FromHours(24));
        ComprobacionLanzador alDiaSiguiente = await comprobador.ComprobarAsync(aMano.Ajustes, aMano: false);
        Assert.Equal(ResultadoLanzador.VersionNueva, alDiaSiguiente.Resultado);
        Assert.Equal(3, fuente.Consultas);
    }

    [Fact]
    public void Toca_si_nunca_se_consulto_pasadas_24_h_o_con_el_reloj_atrasado()
    {
        Assert.True(ComprobadorLanzador.Toca(new Ajustes(), Inicio));
        Assert.False(ComprobadorLanzador.Toca(new Ajustes(UltimaConsultaLanzador: Inicio), Inicio.AddHours(23).AddMinutes(59)));
        Assert.True(ComprobadorLanzador.Toca(new Ajustes(UltimaConsultaLanzador: Inicio), Inicio.AddHours(24)));
        Assert.True(ComprobadorLanzador.Toca(new Ajustes(UltimaConsultaLanzador: Inicio.AddDays(3)), Inicio));
    }

    [Fact]
    public async Task Con_el_aviso_apagado_no_consulta_salvo_a_mano()
    {
        var fuente = new FuenteFalsa(Release("v1.1.0"));
        var apagado = new Ajustes(AvisarVersionNueva: false);

        ComprobacionLanzador c = await Comprobador(fuente).ComprobarAsync(apagado, aMano: false);
        Assert.Equal(ResultadoLanzador.Desactivado, c.Resultado);
        Assert.Equal(0, fuente.Consultas);

        c = await Comprobador(fuente).ComprobarAsync(apagado, aMano: true);
        Assert.Equal(ResultadoLanzador.VersionNueva, c.Resultado);
        Assert.False(c.Ajustes.AvisarVersionNueva);
    }

    [Fact]
    public async Task A_mano_avisa_aunque_la_version_estuviera_descartada()
    {
        ComprobacionLanzador c = await Comprobador(new FuenteFalsa(Release("v1.1.0"))).ComprobarAsync(new Ajustes(VersionDescartada: "1.1.0"), aMano: true);

        Assert.Equal(ResultadoLanzador.VersionNueva, c.Resultado);
        Assert.Equal("Lanzador 1.1.0 disponible", c.TextoFranja);
    }

    // 4. Lo que dice el aviso tras «Buscar actualizaciones».

    [Fact]
    public void Resumen_de_la_busqueda_dice_cuando_no_hay_nada_nuevo()
    {
        var alDia = new ComprobacionLanzador(ResultadoLanzador.AlDia, null, new Ajustes());
        var nueva = new ComprobacionLanzador(ResultadoLanzador.VersionNueva, new ReleaseLanzador(PackageVersion.Parse("1.1.0"), new Uri("https://github.com/x")), new Ajustes());
        var sinRespuesta = new ComprobacionLanzador(ResultadoLanzador.SinRespuesta, null, new Ajustes());
        TarjetaHerramienta instalada = new("subtitular", "Subtitular", "d", PackageVersion.Parse("1.0.0"), PackageVersion.Parse("1.0.0"), 1, null, EstadoTarjeta.Instalada);
        TarjetaHerramienta vieja = instalada with { VersionDisponible = PackageVersion.Parse("1.1.0"), Estado = EstadoTarjeta.Actualizable };

        Assert.Equal("No hay nada nuevo: el Lanzador y las herramientas están al día.", ComprobadorLanzador.ResumenBusqueda([instalada], alDia));
        Assert.Equal("No hay nada nuevo: el Lanzador y las herramientas están al día.", ComprobadorLanzador.ResumenBusqueda([], alDia));
        Assert.Null(ComprobadorLanzador.ResumenBusqueda([instalada], nueva));
        Assert.Equal("Las herramientas están al día. No se pudo consultar si hay un Lanzador nuevo; inténtalo más tarde.", ComprobadorLanzador.ResumenBusqueda([instalada], sinRespuesta));
        Assert.Equal("Hay una actualización: Subtitular 1.0.0 → 1.1.0.", ComprobadorLanzador.ResumenBusqueda([vieja], alDia));
        Assert.Equal("Hay una actualización: Subtitular 1.0.0 → 1.1.0.", ComprobadorLanzador.ResumenBusqueda([vieja], nueva));
    }

    // 5. Ajustes en disco.

    [Fact]
    public void Ajustes_se_guardan_y_se_recuperan_y_sin_archivo_o_rotos_son_los_de_fabrica()
    {
        string ruta = Path.Combine(_temp, "sub", "ajustes.json");
        Assert.Equal(new Ajustes(), Ajustes.Cargar(ruta));
        Assert.True(Ajustes.Cargar(ruta).AvisarVersionNueva);

        var ajustes = new Ajustes(AvisarVersionNueva: false, UltimaConsultaLanzador: Inicio, VersionDescartada: "1.1.0");
        ajustes.Guardar(ruta);
        Assert.Equal(ajustes, Ajustes.Cargar(ruta));

        File.WriteAllText(ruta, "{ roto");
        Assert.Equal(new Ajustes(), Ajustes.Cargar(ruta));
    }

    [Fact]
    public void La_fuente_real_apunta_a_la_api_publica_sin_token_y_con_5_segundos()
    {
        Assert.Equal("https://api.github.com/repos/Dathinel/lanzador/releases/latest", FuenteReleasesGitHub.Url.AbsoluteUri);
        Assert.Equal(TimeSpan.FromSeconds(5), FuenteReleasesGitHub.TiempoLimite);
        Assert.Equal(TimeSpan.FromHours(24), ComprobadorLanzador.Intervalo);
    }

    // 6. La fuente real contra un servidor local: cabeceras, 404 y tiempo límite.

    [Fact]
    public async Task La_fuente_real_manda_el_user_agent_sin_token_y_lee_la_release()
    {
        await using TestHttpServer servidor = TestHttpServer.Start();
        servidor.Map("releases/latest", TestResponse.File(System.Text.Encoding.UTF8.GetBytes(Release("v1.1.0"))) with { ContentType = "application/json; charset=utf-8" });
        using var http = new HttpClient();
        var fuente = new FuenteReleasesGitHub(http, Actual, servidor.UrlFor("releases/latest"));

        ComprobacionLanzador c = await new ComprobadorLanzador(fuente, Actual, _reloj).ComprobarAsync(new Ajustes(), aMano: false);

        Assert.Equal(ResultadoLanzador.VersionNueva, c.Resultado);
        RecordedRequest peticion = Assert.Single(servidor.Requests);
        Assert.Equal("Lanzador/1.0.0", peticion.Headers["User-Agent"]);
        Assert.False(peticion.Headers.ContainsKey("Authorization"));
        Assert.Contains("application/vnd.github+json", peticion.Headers["Accept"], StringComparison.Ordinal);
        Assert.Empty(servidor.Faults);
    }

    [Fact]
    public async Task La_fuente_real_con_404_o_servidor_colgado_es_silencio()
    {
        await using TestHttpServer servidor = TestHttpServer.Start();
        servidor.Map("releases/latest", TestResponse.Status(404, """{ "message": "Not Found" }"""));
        servidor.Map("colgado", TestResponse.File(System.Text.Encoding.UTF8.GetBytes(Release("v1.1.0"))) with { StallAfterBytes = 0 });
        using var http = new HttpClient();

        ComprobacionLanzador c = await new ComprobadorLanzador(new FuenteReleasesGitHub(http, Actual, servidor.UrlFor("releases/latest")), Actual, _reloj).ComprobarAsync(new Ajustes(), aMano: true);
        Assert.Equal(ResultadoLanzador.SinRespuesta, c.Resultado);

        var colgado = new FuenteReleasesGitHub(http, Actual, servidor.UrlFor("colgado"), TimeSpan.FromMilliseconds(300));
        c = await new ComprobadorLanzador(colgado, Actual, _reloj).ComprobarAsync(new Ajustes(), aMano: true);
        Assert.Equal(ResultadoLanzador.SinRespuesta, c.Resultado);
        Assert.Null(c.Ajustes.UltimaConsultaLanzador);
    }

    /// <summary>Un reloj que solo avanza cuando la prueba lo dice.</summary>
    private sealed class RelojFalso(DateTimeOffset ahora) : TimeProvider
    {
        private DateTimeOffset _ahora = ahora;

        public override DateTimeOffset GetUtcNow() => _ahora;

        public void Advance(TimeSpan cuanto) => _ahora += cuanto;
    }

    /// <summary>Devuelve un JSON fijo o lanza la excepción dada, y cuenta las consultas.</summary>
    private sealed class FuenteFalsa : IFuenteReleases
    {
        private readonly string? _json;
        private readonly Exception? _error;

        public FuenteFalsa(string json) => _json = json;

        public FuenteFalsa(Exception error) => _error = error;

        public int Consultas { get; private set; }

        public Task<string> ObtenerUltimaAsync(CancellationToken ct)
        {
            Consultas++;
            return _error is null ? Task.FromResult(_json!) : Task.FromException<string>(_error);
        }
    }
}
