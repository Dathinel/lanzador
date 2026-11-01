using System.Net.Sockets;
using Rodaje.Packages.Download;

namespace Lanzador.Core;

/// <summary>
/// El catálogo no pasó la verificación (firma que no corresponde, más viejo que uno ya visto o demasiado grande):
/// no se lee ni una entrada y no se ofrece nada para instalar. El mensaje ya va en español para la persona.
/// </summary>
public sealed class CatalogoNoVerificadoException : PackageDownloadException
{
    public const string Mensaje = "La firma del catálogo no es válida: el catálogo no es el que publicó Dathinel o llegó dañado. Por seguridad no se ofrece nada para instalar.";

    /// <summary>Para la línea de órdenes, que no tiene sitio para el párrafo de la ventana: una línea corta.</summary>
    public const string MensajeCorto = "La firma del catálogo no es válida; no se instala nada.";

    public CatalogoNoVerificadoException()
        : base(Mensaje)
    {
    }

    public CatalogoNoVerificadoException(string message)
        : base(message)
    {
    }

    public CatalogoNoVerificadoException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Envuelve el rechazo de Rodaje con el mensaje del Lanzador; el detalle técnico queda en <see cref="Exception.InnerException"/>.</summary>
    public static CatalogoNoVerificadoException Desde(DownloadVerificationException rechazo)
    {
        ArgumentNullException.ThrowIfNull(rechazo);
        return new CatalogoNoVerificadoException(Mensaje, rechazo);
    }
}

/// <summary>Convierte excepciones técnicas en mensajes claros en español para la interfaz.</summary>
public static class MensajesError
{
    public static string Describir(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        if (ex is CatalogoNoVerificadoException catalogo)
        {
            return catalogo.Message;
        }

        if (ex is DownloadVerificationException)
        {
            return "La firma del catálogo o de los archivos descargados no es válida.";
        }

        if (EsFalloDeRed(ex))
        {
            return "Sin conexión con el catálogo. Comprueba tu conexión a internet.";
        }

        if (ex is OperationCanceledException)
        {
            return "Operación cancelada.";
        }

        if (ex is ErrorDeUso deUso)
        {
            return deUso.Message;
        }

        if (ex is PackageDownloadException pkg)
        {
            return pkg.Message;
        }

        if (ex is IOException io)
        {
            // HResult 0x80070070 es ERROR_DISK_FULL
            if (io.HResult == unchecked((int)0x80070070))
            {
                return "No hay suficiente espacio en disco para completar la descarga.";
            }

            return "No se pudo leer o escribir en el disco.";
        }

        if (ex is UnauthorizedAccessException)
        {
            return "No hay permisos suficientes para escribir en la carpeta de destino.";
        }

        if (ex is System.Text.Json.JsonException)
        {
            return "El catálogo o manifiesto contiene un formato JSON no válido.";
        }

        return "Ocurrió un error al procesar la herramienta. Vuelve a intentarlo.";
    }

    /// <summary>
    /// Para el panel que ocupa el sitio de la lista cuando el catálogo no se pudo cargar: un título corto y el detalle
    /// con qué hacer. Nunca deja la lista vacía sin decir por qué.
    /// </summary>
    public static (string Titulo, string Detalle) DescribirFalloDelCatalogo(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        if (ex is CatalogoNoVerificadoException catalogo)
        {
            return ("El catálogo no se puede verificar", $"{catalogo.Message} Pulsa «Buscar actualizaciones» para volver a intentarlo.");
        }

        if (EsFalloDeRed(ex))
        {
            return ("Sin conexión con el catálogo", "No se pudo descargar el catálogo de Dathinel. Comprueba tu conexión a internet y pulsa «Buscar actualizaciones».");
        }

        return ("No se pudo leer el catálogo", $"{Describir(ex)} No se ofrecen aplicaciones para instalar hasta que se pueda leer.");
    }

    /// <summary>
    /// Una excepción de red, también cuando Rodaje la envolvió en <see cref="PackageDownloadException"/> («No hay conexión con GitHub»,
    /// «GitHub no respondió a tiempo»). Nunca un rechazo de firma: ese no se disimula con lo instalado.
    /// </summary>
    public static bool EsFalloDeRed(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return ex is HttpRequestException or SocketException
            || (ex is PackageDownloadException and not DownloadVerificationException && ex.InnerException is (HttpRequestException or SocketException or TaskCanceledException));
    }
}
