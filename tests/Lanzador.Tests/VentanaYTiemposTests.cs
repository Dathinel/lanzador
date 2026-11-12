using Lanzador.Core;

namespace Lanzador.Tests;

public sealed class VentanaYTiemposTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "lanzador-ventana-" + Guid.NewGuid().ToString("N")[..10]);

    private string Ruta => Path.Combine(_temp, "sub", "ventana.json");

    [Fact]
    public void Ventana_se_guarda_y_se_recupera_igual()
    {
        var estado = new EstadoVentana(10, 20, 900, 700, Maximizada: true);
        estado.Guardar(Ruta);

        Assert.Equal(estado, EstadoVentana.Cargar(Ruta));
    }

    [Fact]
    public void Ventana_sin_archivo_corrupta_o_diminuta_da_null()
    {
        Assert.Null(EstadoVentana.Cargar(Ruta));

        Directory.CreateDirectory(Path.GetDirectoryName(Ruta)!);
        File.WriteAllText(Ruta, "{ esto no es json");
        Assert.Null(EstadoVentana.Cargar(Ruta));

        new EstadoVentana(0, 0, 10, 10, false).Guardar(Ruta);
        Assert.Null(EstadoVentana.Cargar(Ruta));
    }

    [Theory]
    [InlineData(640, 480, true)]      // justo el mínimo
    [InlineData(16384, 16384, true)]  // justo el máximo
    [InlineData(639, 480, false)]
    [InlineData(640, 479, false)]
    [InlineData(16385, 480, false)]
    [InlineData(640, 16385, false)]
    [InlineData(0, 0, false)]
    [InlineData(-100, -100, false)]
    public void Ventana_fuera_de_rango_no_se_usa(int ancho, int alto, bool sirve)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Ruta)!);
        new EstadoVentana(-32000, -32000, ancho, alto, Maximizada: true).Guardar(Ruta);

        EstadoVentana? cargada = EstadoVentana.Cargar(Ruta);
        Assert.Equal(sirve, cargada is not null);
        if (cargada is not null)
        {
            Assert.Equal(-32000, cargada.X);
            Assert.True(cargada.Maximizada);
        }
    }

    [Theory]
    [InlineData("""{ "ancho": "800", "alto": 600 }""")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("""{ "maximizada": true }""")]
    [InlineData("""{ "ancho": 800.5, "alto": 600 }""")]
    public void Ventana_con_json_raro_da_null(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Ruta)!);
        File.WriteAllText(Ruta, json);
        Assert.Null(EstadoVentana.Cargar(Ruta));
    }

    [Fact]
    public void Ventana_con_campos_de_mas_se_lee_igual()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Ruta)!);
        File.WriteAllText(Ruta, """{ "x": 5, "y": 6, "ancho": 800, "alto": 600, "maximizada": false, "monitor": "izquierdo" }""");

        Assert.Equal(new EstadoVentana(5, 6, 800, 600, Maximizada: false), EstadoVentana.Cargar(Ruta));
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(42, "0:42")]
    [InlineData(65, "1:05")]
    [InlineData(3723, "1:02:03")]
    public void Duracion_se_formatea_como_reloj(int segundos, string esperado)
    {
        Assert.Equal(esperado, Tiempos.Formatear(TimeSpan.FromSeconds(segundos)));
    }

    [Fact]
    public void Resumen_estima_lo_que_queda_solo_cuando_hay_base()
    {
        Assert.Equal("0:42 · quedan ~1:10", Tiempos.Resumen(TimeSpan.FromSeconds(42), 0.375));
        Assert.Equal("0:01", Tiempos.Resumen(TimeSpan.FromSeconds(1), 0.5));
        Assert.Equal("0:10", Tiempos.Resumen(TimeSpan.FromSeconds(10), 0));
        Assert.Equal("0:10", Tiempos.Resumen(TimeSpan.FromSeconds(10), 1));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }
}
