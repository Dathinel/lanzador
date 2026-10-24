using System.Net.Sockets;
using Rodaje.Packages.Download;

namespace Lanzador.Core;

/// <summary>Convierte excepciones técnicas en mensajes claros en español para la interfaz.</summary>
public static class MensajesError
{
    public static string Describir(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        if (ex is DownloadVerificationException)
        {
            return "La firma del catálogo o de los archivos descargados no es válida.";
        }

        if (ex is HttpRequestException || ex is SocketException)
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
}
