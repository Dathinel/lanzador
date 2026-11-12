using Lanzador.Core;
using Rodaje.Packages;

namespace Lanzador.Tests;

/// <summary>
/// La tabla completa de estados de <see cref="TarjetaHerramienta"/>: para cada combinación de instalada o no,
/// versión disponible menor, igual o mayor, exe presente o no, zip o parcial en disco, instalación a medias y
/// tarjeta solo de disco, se comprueban el estado, los cuatro «Puede…», el reanudar y todos los textos que se pintan.
/// </summary>
public sealed class TablaTarjetaTests
{
    private const long Tamano = 28L << 20;   // 28.0 MB
    private const long Parcial = 14L << 20;  // 14.0 MB

    /// <summary>Un caso de la tabla: la tarjeta y todo lo que se espera que pinte y permita.</summary>
    public sealed record Caso(
        string Nombre,
        TarjetaHerramienta Tarjeta,
        EstadoTarjeta Estado,
        bool PuedeInstalar,
        bool PuedeAbrir,
        bool PuedeActualizar,
        bool PuedeQuitar,
        bool PuedeReanudar,
        string TextoVersion,
        /// <summary>Lo que dice el botón de acción («Instalar» o «Actualizar»); <c>null</c> si no se ve ninguno.</summary>
        string? TextoBoton,
        string TextoDescarga,
        string TextoCancelada)
    {
        public override string ToString() => Nombre;
    }

    private static TarjetaHerramienta T(
        string disponible = "1.0.0",
        string? instalada = null,
        EstadoTarjeta? estado = null,
        string? ejecutable = null,
        long parcial = 0,
        bool zip = false,
        bool incompleta = false,
        bool sinCatalogo = false,
        long espacioApp = 0,
        long espacioCompartido = 0,
        long descargasApp = 0) => new(
            "subtitular",
            "Subtitular",
            "Descripción.",
            PackageVersion.Parse(disponible),
            instalada is null ? null : PackageVersion.Parse(instalada),
            Tamano,
            instalada is not null || espacioApp > 0 || espacioCompartido > 0 ? new EspacioInstalada(espacioApp, espacioCompartido) : null,
            estado ?? (instalada is null ? EstadoTarjeta.NoInstalada : EstadoTarjeta.Instalada),
            ejecutable,
            parcial,
            zip,
            incompleta,
            sinCatalogo,
            descargasApp);

    public static IEnumerable<object[]> Casos => TodosLosCasos().Select(c => new object[] { c });

    private static IEnumerable<Caso> TodosLosCasos()
    {
        yield return new("sin instalar, limpia", T(), EstadoTarjeta.NoInstalada, true, false, false, false, false,
            "Versión 1.0.0", "Instalar", "Descarga: 28.0 MB", "Descarga cancelada.");
        yield return new("sin instalar, parcial", T(parcial: Parcial), EstadoTarjeta.NoInstalada, true, false, false, false, true,
            "Versión 1.0.0", "Reanudar descarga", "Descarga: 28.0 MB · 14.0 MB ya bajados, se reanudará",
            "Descarga cancelada. Lo ya bajado se guarda: pulsa «Reanudar» para seguir donde quedó.");
        yield return new("sin instalar, zip entero", T(zip: true), EstadoTarjeta.NoInstalada, true, false, false, false, false,
            "Versión 1.0.0", "Instalar (ya descargado)", "Descarga: 28.0 MB · ya descargado",
            "Descarga cancelada. El zip ya estaba completo: pulsa «Instalar» para extraerlo.");
        yield return new("sin instalar, zip y parcial: manda el zip", T(zip: true, parcial: Parcial), EstadoTarjeta.NoInstalada, true, false, false, false, true,
            "Versión 1.0.0", "Instalar (ya descargado)", "Descarga: 28.0 MB · ya descargado",
            "Descarga cancelada. El zip ya estaba completo: pulsa «Instalar» para extraerlo.");
        yield return new("instalada al día, con exe", T(instalada: "1.0.0", ejecutable: "Subtitular.exe"), EstadoTarjeta.Instalada, false, true, false, true, false,
            "Versión 1.0.0 (al día)", null, "Descarga: 28.0 MB", "Descarga cancelada.");
        yield return new("instalada al día, sin exe", T(instalada: "1.0.0"), EstadoTarjeta.Instalada, false, false, false, true, false,
            "Versión 1.0.0 (al día)", null, "Descarga: 28.0 MB", "Descarga cancelada.");
        yield return new("instalada al día con parcial colgado", T(instalada: "1.0.0", ejecutable: "e.exe", parcial: Parcial), EstadoTarjeta.Instalada, false, true, false, true, false,
            "Versión 1.0.0 (al día)", null, "Descarga: 28.0 MB", "Descarga cancelada.");
        yield return new("instalada más nueva que el catálogo", T(disponible: "1.0.0", instalada: "2.0.0", estado: EstadoTarjeta.InstaladaMasNueva, ejecutable: "e.exe"), EstadoTarjeta.InstaladaMasNueva, false, true, false, true, false,
            "Instalada 2.0.0, más nueva que el catálogo (1.0.0)", null, "Descarga: 28.0 MB", "Descarga cancelada.");
        yield return new("instalada más nueva con parcial: no reanuda", T(disponible: "1.0.0", instalada: "2.0.0", estado: EstadoTarjeta.InstaladaMasNueva, parcial: Parcial), EstadoTarjeta.InstaladaMasNueva, false, false, false, true, false,
            "Instalada 2.0.0, más nueva que el catálogo (1.0.0)", null, "Descarga: 28.0 MB", "Descarga cancelada.");
        yield return new("actualizable, limpia", T(disponible: "1.1.0", instalada: "1.0.0", estado: EstadoTarjeta.Actualizable, ejecutable: "e.exe"), EstadoTarjeta.Actualizable, false, true, true, true, false,
            "Instalada 1.0.0 · Disponible 1.1.0", "Actualizar a 1.1.0", "Descarga: 28.0 MB", "Descarga cancelada.");
        yield return new("actualizable, parcial", T(disponible: "1.1.0", instalada: "1.0.0", estado: EstadoTarjeta.Actualizable, ejecutable: "e.exe", parcial: Parcial), EstadoTarjeta.Actualizable, false, true, true, true, true,
            "Instalada 1.0.0 · Disponible 1.1.0", "Reanudar actualización a 1.1.0", "Descarga: 28.0 MB · 14.0 MB ya bajados, se reanudará",
            "Descarga cancelada. Lo ya bajado se guarda: pulsa «Reanudar» para seguir donde quedó.");
        yield return new("actualizable, zip entero", T(disponible: "1.1.0", instalada: "1.0.0", estado: EstadoTarjeta.Actualizable, ejecutable: "e.exe", zip: true), EstadoTarjeta.Actualizable, false, true, true, true, false,
            "Instalada 1.0.0 · Disponible 1.1.0", "Actualizar a 1.1.0 (ya descargado)", "Descarga: 28.0 MB · ya descargado",
            "Descarga cancelada. El zip ya estaba completo: pulsa «Instalar» para extraerlo.");
        yield return new("actualizable, instalación a medias", T(disponible: "1.1.0", instalada: "1.0.0", estado: EstadoTarjeta.Actualizable, incompleta: true), EstadoTarjeta.Actualizable, false, false, true, true, false,
            "Versión 1.0.0 (faltan módulos o modelos)", "Completar instalación", "Descarga: 28.0 MB",
            "Instalación cancelada. Lo ya bajado se guarda: pulsa «Completar instalación» para seguir.");
        yield return new("instalación a medias manda sobre zip y parcial", T(disponible: "1.1.0", instalada: "1.0.0", estado: EstadoTarjeta.Actualizable, incompleta: true, zip: true, parcial: Parcial), EstadoTarjeta.Actualizable, false, false, true, true, true,
            "Versión 1.0.0 (faltan módulos o modelos)", "Completar instalación", "Descarga: 28.0 MB · ya descargado",
            "Instalación cancelada. Lo ya bajado se guarda: pulsa «Completar instalación» para seguir.");
        yield return new("sin catálogo: tarjeta solo del disco", T(instalada: "1.0.0", ejecutable: "e.exe", sinCatalogo: true), EstadoTarjeta.Instalada, false, true, false, true, false,
            "Versión 1.0.0 (instalada)", null, "Sin conexión: no se sabe si hay una versión nueva", "Descarga cancelada.");
    }

    [Theory]
    [MemberData(nameof(Casos))]
    public void Cada_combinacion_pinta_estado_botones_y_textos_exactos(Caso c)
    {
        TarjetaHerramienta t = c.Tarjeta;
        Assert.Equal(c.Estado, t.Estado);
        Assert.Equal(c.PuedeInstalar, t.PuedeInstalar);
        Assert.Equal(c.PuedeAbrir, t.PuedeAbrir);
        Assert.Equal(c.PuedeActualizar, t.PuedeActualizar);
        Assert.Equal(c.PuedeQuitar, t.PuedeQuitar);
        Assert.Equal(c.PuedeReanudar, t.PuedeReanudar);
        Assert.Equal(c.TextoVersion, t.TextoVersion);
        Assert.Equal(c.TextoDescarga, t.TextoDescarga);
        Assert.Equal(c.TextoCancelada, t.TextoCancelada);

        string? boton = t.PuedeInstalar ? t.TextoInstalar : t.PuedeActualizar ? t.TextoActualizar : null;
        Assert.Equal(c.TextoBoton, boton);
    }

    [Theory]
    [MemberData(nameof(Casos))]
    public void Ninguna_combinacion_permite_cosas_imposibles(Caso c)
    {
        TarjetaHerramienta t = c.Tarjeta;
        Assert.False(t.PuedeInstalar && t.PuedeActualizar, "«Instalar» y «Actualizar» no pueden salir a la vez.");
        Assert.False(t.PuedeInstalar && (t.PuedeQuitar || t.PuedeAbrir), "Sin instalar no hay «Abrir» ni «Quitar».");
        if (t.PuedeActualizar)
        {
            Assert.True(t.PuedeQuitar, "Lo que se puede actualizar también se puede quitar.");
        }

        if (t.AlDia)
        {
            Assert.False(t.PuedeReanudar, "Al día no hay descarga que reanudar aunque quede un .parcial.");
            Assert.False(t.PuedeInstalar);
        }

        if (t.PuedeReanudar && !t.ZipDescargado && !t.SinCatalogo)
        {
            Assert.Contains("se reanudará", t.TextoDescarga, StringComparison.Ordinal);
        }

        if (t.PuedeAbrir)
        {
            Assert.NotNull(t.Ejecutable);
        }

        if (t.ZipDescargado && t.PuedeInstalar)
        {
            Assert.Equal("Instalar (ya descargado)", t.TextoInstalar);
        }
    }

    // El aviso de espacio y el diálogo de «Quitar», que no caben en la tabla.

    [Fact]
    public void Texto_espacio_distingue_no_instalada_app_sola_y_compartido()
    {
        Assert.Equal("No instalada", T().TextoEspacio);
        Assert.Equal("En disco: 2.0 MB", T(instalada: "1.0.0", espacioApp: 2L << 20).TextoEspacio);
        Assert.Equal("En disco: 3.0 MB (2.0 MB app, 1.0 MB compartido)",
            T(instalada: "1.0.0", espacioApp: 2L << 20, espacioCompartido: 1L << 20).TextoEspacio);
    }

    [Fact]
    public void Confirmar_quitar_nombra_carpeta_peso_descargas_y_lo_que_se_queda()
    {
        var carpetas = new Carpetas(@"C:\Datos\Herramientas", @"C:\Datos\Comun");
        TarjetaHerramienta t = T(instalada: "1.0.0", espacioApp: 2L << 20, espacioCompartido: 1L << 20, descargasApp: 3L << 20);

        Assert.Equal(
            "Se borrará la carpeta «C:\\Datos\\Herramientas\\subtitular» (2.0 MB) con Subtitular 1.0.0." +
            " También se borra lo descargado de Subtitular que quedó en «C:\\Datos\\Comun\\descargas» (3.0 MB)." +
            " Los módulos y modelos compartidos (1.0 MB) se quedan en «C:\\Datos\\Comun» para las demás herramientas.",
            t.TextoConfirmarQuitar(carpetas));
    }

    [Fact]
    public void Confirmar_quitar_sin_descargas_solo_dice_la_carpeta_y_lo_compartido()
    {
        var carpetas = new Carpetas(@"C:\Datos\Herramientas", @"C:\Datos\Comun");
        TarjetaHerramienta t = T(instalada: "1.0.0", espacioApp: 2L << 20);

        Assert.Equal(
            "Se borrará la carpeta «C:\\Datos\\Herramientas\\subtitular» (2.0 MB) con Subtitular 1.0.0." +
            " Lo compartido en «C:\\Datos\\Comun» se queda para las demás herramientas.",
            t.TextoConfirmarQuitar(carpetas));
        Assert.DoesNotContain("También se borra", t.TextoConfirmarQuitar(carpetas), StringComparison.Ordinal);
    }

    [Fact]
    public void Confirmar_quitar_sin_peso_no_pone_parentesis()
    {
        var carpetas = new Carpetas(@"C:\Datos\Herramientas", @"C:\Datos\Comun");
        TarjetaHerramienta t = T(instalada: "1.0.0");

        Assert.Equal(
            "Se borrará la carpeta «C:\\Datos\\Herramientas\\subtitular» con Subtitular 1.0.0." +
            " Lo compartido en «C:\\Datos\\Comun» se queda para las demás herramientas.",
            t.TextoConfirmarQuitar(carpetas));
    }

    [Theory]
    [InlineData(0, "0.1 MB")]
    [InlineData(1L << 20, "1.0 MB")]
    [InlineData(1073689395, "1023.9 MB")] // el último byte que todavía se dice en MB
    [InlineData(1073689396, "1.0 GB")]    // 1023.95 MB: redondeado sería «1024.0 MB»
    [InlineData((1L << 30) - 1, "1.0 GB")]
    [InlineData(1L << 30, "1.0 GB")]
    [InlineData(1L << 40, "1024.0 GB")]
    public void Peso_formatea_bordes_de_mb_y_gb(long bytes, string esperado)
    {
        Assert.Equal(esperado, Instalador.Peso(bytes));
    }
}
