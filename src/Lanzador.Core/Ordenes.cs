using System.Net;
using Rodaje.Packages;
using Rodaje.Packages.Download;

namespace Lanzador.Core;

/// <summary>
/// La línea de órdenes: <c>lista | instalar &lt;app&gt; | actualizar [&lt;app&gt;] | quitar &lt;app&gt;</c> con <c>--indice</c>, <c>--destino</c>,
/// <c>--comun</c> y <c>--clave</c>. Sale con 0 si fue bien, 1 si el error es de la persona (orden, argumentos, app desconocida) y
/// 2 si falló la red, la firma, la huella o el disco. Mensajes en español, sin trazas.
/// </summary>
public static class Ordenes
{
    public static async Task<int> EjecutarAsync(string[] args, TextWriter salida, TextWriter error, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(salida);
        ArgumentNullException.ThrowIfNull(error);

        string? orden = null;
        string? app = null;
        string indice = Instalador.IndiceDeDathinel.AbsoluteUri;
        string? clave = null;
        Carpetas carpetas = Carpetas.PorDefecto();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string? valor = i + 1 < args.Length ? args[i + 1] : null;
            switch (arg)
            {
                case "--indice" when valor is not null:
                    indice = valor;
                    i++;
                    break;
                case "--destino" when valor is not null:
                    carpetas = carpetas with { Herramientas = valor };
                    i++;
                    break;
                case "--comun" when valor is not null:
                    carpetas = carpetas with { Comun = valor };
                    i++;
                    break;
                case "--clave" when valor is not null:
                    clave = valor;
                    i++;
                    break;
                case "--help" or "-h":
                    Uso(salida);
                    return 0;
                case var _ when arg.StartsWith('-'):
                    return Fallo(error, $"Opción desconocida: {arg}", 1);
                case var _ when orden is null:
                    orden = arg;
                    break;
                case var _ when app is null:
                    app = arg;
                    break;
                default:
                    return Fallo(error, $"Sobra el argumento «{arg}».", 1);
            }
        }

        if (orden is not ("lista" or "instalar" or "actualizar" or "quitar"))
        {
            Uso(error);
            return 1;
        }

        if (orden is "instalar" or "quitar" && app is null)
        {
            return Fallo(error, $"Falta la herramienta: lanzador {orden} <app>.", 1);
        }

        if (orden is "lista" && app is not null)
        {
            return Fallo(error, "«lista» no lleva argumentos.", 1);
        }

        using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 10, AutomaticDecompression = DecompressionMethods.None });
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Lanzador/{Instalador.Version}");
        var progreso = new Progreso(error);
        try
        {
            using var instalador = new Instalador(carpetas, indice, clave, http);
            switch (orden)
            {
                case "lista":
                    foreach (Herramienta h in await instalador.ListaAsync(ct).ConfigureAwait(false))
                    {
                        string estado = h.Instalada is null ? "no instalada" : h.Instalada.Equals(h.Version) ? "instalada" : $"instalada {h.Instalada}, hay {h.Version}";
                        await salida.WriteLineAsync($"{h.Id}\t{h.Version}\t{Instalador.Peso(h.Size)}\t{estado}").ConfigureAwait(false);
                    }

                    break;
                case "instalar":
                    PackageVersion version = await instalador.InstalarAsync(app!, progreso, ct).ConfigureAwait(false);
                    progreso.Cerrar();
                    await salida.WriteLineAsync($"{app} {version} instalada en {Path.Combine(carpetas.Herramientas, app!, version.ToString())}").ConfigureAwait(false);
                    break;
                case "actualizar":
                    var cambios = await instalador.ActualizarAsync(app, progreso, ct).ConfigureAwait(false);
                    progreso.Cerrar();
                    await salida.WriteLineAsync(cambios.Count == 0 ? "Todo al día." : string.Join(Environment.NewLine, cambios.Select(c => $"{c.Id}: {c.De} → {c.A}"))).ConfigureAwait(false);
                    break;
                default:
                    instalador.Quitar(app!);
                    await salida.WriteLineAsync($"{app} quitada; los módulos y modelos comunes se quedan en {carpetas.Comun}.").ConfigureAwait(false);
                    break;
            }

            return 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            progreso.Cerrar();
            return Fallo(error, "Cancelado; lo descargado se guarda para seguir más tarde.", 2);
        }
        catch (ErrorDeUso ex)
        {
            progreso.Cerrar();
            return Fallo(error, ex.Message, 1);
        }
        catch (Exception ex) when (ex is PackageDownloadException or IOException or UnauthorizedAccessException or UriFormatException or System.Text.Json.JsonException)
        {
            progreso.Cerrar();
            return Fallo(error, ex.Message, 2);
        }
    }

    private static int Fallo(TextWriter error, string mensaje, int codigo)
    {
        error.WriteLine("Error: " + mensaje);
        return codigo;
    }

    private static void Uso(TextWriter w)
    {
        w.WriteLine("Uso: lanzador <orden> [opciones]");
        w.WriteLine("  lista                 Herramientas del catálogo: versión, peso y si está instalada");
        w.WriteLine("  instalar <app>        Instala la versión más nueva con sus módulos y modelos");
        w.WriteLine("  actualizar [<app>]    Actualiza esa herramienta o todas las instaladas");
        w.WriteLine("  quitar <app>          Borra esa herramienta; lo común se queda");
        w.WriteLine("Opciones:");
        w.WriteLine("  --indice <url|ruta>   Índice firmado (por defecto el catálogo de Dathinel)");
        w.WriteLine("  --destino <carpeta>   Dónde van las herramientas (por defecto %LOCALAPPDATA%\\Dathinel\\Herramientas)");
        w.WriteLine("  --comun <carpeta>     Descargas, módulos y modelos compartidos (por defecto %LOCALAPPDATA%\\Dathinel\\Comun)");
        w.WriteLine("  --clave <base64>      Otra clave pública del índice (pruebas)");
    }

    /// <summary>Una línea de progreso en stderr que se sobrescribe; <see cref="Cerrar"/> la termina si se escribió algo.</summary>
    private sealed class Progreso(TextWriter error) : IProgress<string>
    {
        private string _ultimo = "";

        public void Report(string value)
        {
            if (value != _ultimo)
            {
                _ultimo = value;
                error.Write("\r" + value.PadRight(60));
            }
        }

        public void Cerrar()
        {
            if (_ultimo.Length > 0)
            {
                error.WriteLine();
                _ultimo = "";
            }
        }
    }
}
