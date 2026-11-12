using Lanzador.Core;

namespace Lanzador.Tests;

public sealed class OpcionesComunesTests
{
    [Fact]
    public void Conserva_rutas_con_espacios_y_tildes_y_argumentos_de_la_orden()
    {
        OpcionesComunes opciones = OpcionesComunes.Parsear([
            "instalar", "--indice", @"C:\Catálogos firmados\índice.json", "subtitular",
            "--destino", @"C:\Mis herramientas\Vídeo", "--comun", @"C:\Datos comunes\Módulos", "--clave", "abc=="]);

        Assert.Equal(@"C:\Catálogos firmados\índice.json", opciones.Indice);
        Assert.Equal(@"C:\Mis herramientas\Vídeo", opciones.Carpetas.Herramientas);
        Assert.Equal(@"C:\Datos comunes\Módulos", opciones.Carpetas.Comun);
        Assert.Equal("abc==", opciones.Clave);
        Assert.Equal(new[] { "instalar", "subtitular" }, opciones.Argumentos);
    }

    [Fact]
    public void Sin_opciones_usa_los_valores_por_defecto()
    {
        OpcionesComunes opciones = OpcionesComunes.Parsear([]);
        Assert.Equal(Instalador.IndiceDeDathinel.AbsoluteUri, opciones.Indice);
        Assert.Null(opciones.Clave);
        Assert.Equal(Carpetas.PorDefecto(), opciones.Carpetas);
        Assert.Empty(opciones.Argumentos);
    }

    [Theory]
    [InlineData("--indice")]
    [InlineData("--clave")]
    [InlineData("--destino")]
    [InlineData("--comun")]
    public void Opcion_repetida_conserva_el_ultimo_valor(string opcion)
    {
        OpcionesComunes opciones = OpcionesComunes.Parsear([opcion, "primero", opcion, "último"]);
        string? valor = opcion switch
        {
            "--indice" => opciones.Indice,
            "--clave" => opciones.Clave,
            "--destino" => opciones.Carpetas.Herramientas,
            _ => opciones.Carpetas.Comun,
        };
        Assert.Equal("último", valor);
        Assert.Empty(opciones.Argumentos);
    }

    [Theory]
    [InlineData("--indice")]
    [InlineData("--clave")]
    [InlineData("--destino")]
    [InlineData("--comun")]
    public async Task Opcion_sin_valor_da_error_tambien_si_sigue_otra_opcion(string opcion)
    {
        foreach (string[] args in new[] { new[] { "lista", opcion }, new[] { "lista", opcion, "--comun", "carpeta" } })
        {
            Assert.Equal($"Falta el valor de {opcion}.", Assert.Throws<ErrorDeUso>(() => OpcionesComunes.Parsear(args)).Message);
            var error = new StringWriter();
            Assert.Equal(1, await Ordenes.EjecutarAsync(args, new StringWriter(), error));
            Assert.Equal($"Error: Falta el valor de {opcion}.", error.ToString().Trim());
        }
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("-")]
    [InlineData("--raro")]
    public void Valor_que_empieza_por_guion_no_vale_como_valor(string valor)
    {
        var ex = Assert.Throws<ErrorDeUso>(() => OpcionesComunes.Parsear(["--indice", valor]));
        Assert.Equal("Falta el valor de --indice.", ex.Message);
    }

    [Fact]
    public void Indice_vacio_se_guarda_tal_cual_lo_rechaza_el_instalador()
    {
        OpcionesComunes opciones = OpcionesComunes.Parsear(["--indice", ""]);
        Assert.Equal("", opciones.Indice);
        Assert.Empty(opciones.Argumentos);
    }

    [Theory]
    [InlineData("--modo")]
    [InlineData("--INDICE")]
    [InlineData("--indice=x")]
    [InlineData("--")]
    public void Opcion_desconocida_no_se_traga_queda_en_argumentos(string desconocida)
    {
        OpcionesComunes opciones = OpcionesComunes.Parsear([desconocida, "x"]);
        Assert.Equal(new[] { desconocida, "x" }, opciones.Argumentos);
    }

    [Fact]
    public void Opciones_y_argumentos_intercalados_se_reconocen_en_cualquier_orden()
    {
        OpcionesComunes opciones = OpcionesComunes.Parsear(["subtitular", "--destino", "d", "instalar", "--comun", "c"]);
        Assert.Equal(new[] { "subtitular", "instalar" }, opciones.Argumentos);
        Assert.Equal("d", opciones.Carpetas.Herramientas);
        Assert.Equal("c", opciones.Carpetas.Comun);
    }
}
