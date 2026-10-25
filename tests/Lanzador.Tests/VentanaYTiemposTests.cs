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
