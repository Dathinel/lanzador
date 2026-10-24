using System.Text.Json;
using Rodaje.Packages;
using Rodaje.Packages.Download;

namespace Lanzador.Core;

/// <summary>Calcula el espacio en disco usado por las herramientas y lo compartido en Comun\.</summary>
public static class CalculadorEspacio
{
    /// <summary>
    /// Calcula el espacio exclusivo de la app en Herramientas\&lt;id&gt; y el compartido en Comun\
    /// (módulos declarados y sus modelos en el almacén). Devuelve <c>null</c> si no está instalada.
    /// </summary>
    public static EspacioInstalada? CalcularEspacioInstalada(string id, Carpetas carpetas)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(carpetas);

        if (!Identifiers.IsValid(id))
        {
            return null;
        }

        string carpetaApp = Path.Combine(carpetas.Herramientas, id);
        if (!Directory.Exists(carpetaApp))
        {
            return null;
        }

        long appBytes = SumarCarpeta(carpetaApp);
        var archivosComunes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Buscar manifiestos herramienta.json en las versiones instaladas
        foreach (string versionDir in Directory.EnumerateDirectories(carpetaApp))
        {
            string manifiesto = Path.Combine(versionDir, Instalador.ManifiestoNombre);
            if (!File.Exists(manifiesto))
            {
                continue;
            }

            try
            {
                byte[] json = File.ReadAllBytes(manifiesto);
                ManifiestoHerramienta? m = JsonSerializer.Deserialize(json, ManifiestoJson.Default.ManifiestoHerramienta);
                foreach (string modulo in m?.Modulos ?? [])
                {
                    if (!Identifiers.IsValid(modulo))
                    {
                        continue;
                    }

                    string moduloDir = Path.Combine(carpetas.Modulos, modulo);
                    if (!Directory.Exists(moduloDir))
                    {
                        continue;
                    }

                    // Sumar archivos del módulo
                    foreach (string archivo in Directory.EnumerateFiles(moduloDir, "*", SearchOption.AllDirectories))
                    {
                        archivosComunes.Add(Path.GetFullPath(archivo));
                    }

                    // Sumar modelos declarados en el paquete.json del módulo
                    foreach (string sub in Directory.EnumerateDirectories(moduloDir))
                    {
                        string paqueteJson = Path.Combine(sub, "paquete.json");
                        if (!File.Exists(paqueteJson))
                        {
                            continue;
                        }

                        try
                        {
                            foreach (ModuleModel model in ModuleModels.Read(paqueteJson))
                            {
                                string modeloRuta = Path.Combine(carpetas.Modelos, model.Sha256);
                                if (File.Exists(modeloRuta))
                                {
                                    archivosComunes.Add(Path.GetFullPath(modeloRuta));
                                }
                            }
                        }
                        catch
                        {
                            // Ignorar manifiestos de módulo no válidos al medir espacio
                        }
                    }
                }
            }
            catch
            {
                // Ignorar manifiesto corrupto
            }
        }

        long compartidoBytes = 0;
        foreach (string ruta in archivosComunes)
        {
            try
            {
                var fi = new FileInfo(ruta);
                if (fi.Exists)
                {
                    compartidoBytes += fi.Length;
                }
            }
            catch (IOException)
            {
            }
        }

        return new EspacioInstalada(appBytes, compartidoBytes);
    }

    /// <summary>Suma recursivamente el tamaño de todos los archivos de una carpeta. Si no existe, devuelve 0.</summary>
    public static long SumarCarpeta(string carpeta)
    {
        if (string.IsNullOrWhiteSpace(carpeta) || !Directory.Exists(carpeta))
        {
            return 0;
        }

        long total = 0;
        try
        {
            foreach (string archivo in Directory.EnumerateFiles(carpeta, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(archivo).Length;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return total;
    }
}
