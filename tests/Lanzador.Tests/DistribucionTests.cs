using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Rodaje.Packages.Download;
using Rodaje.TestServer;

namespace Lanzador.Tests;

/// <summary>
/// build\catalogo-entrada.ps1 sobre un zip de prueba: la entrada «app» que produce tiene la forma que SignedIndex exige
/// (id, versión, tipo app, URL de la release, huella de 64 hexadecimales y peso mayor que cero) y la huella y el peso
/// son los del zip que se le dio. No depende de dist\ ni del zip de ninguna release.
/// </summary>
public sealed class DistribucionTests
{
    private static string EncontrarRaiz()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Directory.Build.props")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }

    /// <summary>pwsh 7 si está (CI y Linux); si no, Windows PowerShell 5.1. El script funciona en las dos.</summary>
    private static string PowerShell()
    {
        string[] carpetas = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (string nombre in OperatingSystem.IsWindows() ? ["pwsh.exe", "powershell.exe"] : new[] { "pwsh" })
        {
            if (carpetas.Any(c => File.Exists(Path.Combine(c, nombre))))
            {
                return nombre;
            }
        }

        return OperatingSystem.IsWindows() ? "powershell" : "pwsh";
    }

    [Fact]
    public void CatalogoEntrada_ProduceEntradaAppQueSignedIndexAceptaConLaHuellaRealDelZip()
    {
        string carpeta = Path.Combine(Path.GetTempPath(), "lanzador-catalogo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        try
        {
            string zip = Path.Combine(carpeta, "Lanzador-9.9.9-win-x64.zip");
            File.WriteAllBytes(zip, RandomNumberGenerator.GetBytes(4096));
            string salida = Path.Combine(carpeta, "catalogo-lanzador.json");

            using var proceso = Process.Start(new ProcessStartInfo(PowerShell())
            {
                ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(EncontrarRaiz(), "build", "catalogo-entrada.ps1"), "-Version", "9.9.9", "-Zip", zip, "-Salida", salida },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            string consola = proceso.StandardOutput.ReadToEnd() + proceso.StandardError.ReadToEnd();
            proceso.WaitForExit();
            Assert.True(proceso.ExitCode == 0, consola);
            Assert.True(File.Exists(salida), "El script no dejó el JSON de salida. " + consola);

            string entrada = File.ReadAllText(salida, Encoding.UTF8);
            byte[] indice = Encoding.UTF8.GetBytes($$"""{ "formatVersion": 1, "generated": "2026-10-24T12:00:00Z", "entries": [ {{entrada}} ] }""");
            using var firmante = new TestIndexSigner();
            using var clave = ECDsa.Create();
            clave.ImportSubjectPublicKeyInfo(firmante.PublicKey, out _);
            var problemas = new List<string>();

            // Forma: SignedIndex la acepta entera (una entrada mal escrita se omite y acaba en «problemas»).
            IndexEntry e = Assert.Single(SignedIndex.Load(indice, firmante.SignFile(indice), clave, DownloadUrlPolicy.Default, problemas).Entries);
            Assert.Empty(problemas);
            Assert.Equal("lanzador", e.Id);
            Assert.Equal("9.9.9", e.Version.ToString());
            Assert.Equal(IndexEntryKind.App, e.Kind);
            Assert.Equal("https://github.com/Dathinel/lanzador/releases/download/v9.9.9/Lanzador-9.9.9-win-x64.zip", e.Url.OriginalString);
            Assert.True(SignedIndex.IsSha256(e.Sha256), $"La huella debe ser 64 hexadecimales en minúsculas: «{e.Sha256}».");
            Assert.True(e.Size > 0, "El peso debe ser mayor que cero.");
            Assert.Null(e.MinApp);

            // Contenido: la huella y el peso son los del zip que se le dio, que es lo que el Lanzador comprobará al instalar.
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip))), e.Sha256);
            Assert.Equal(new FileInfo(zip).Length, e.Size);
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    [Fact]
    public void CatalogoEntrada_SinZipFallaConMensajeEnEspanol()
    {
        string carpeta = Path.Combine(Path.GetTempPath(), "lanzador-catalogo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        try
        {
            using var proceso = Process.Start(new ProcessStartInfo(PowerShell())
            {
                ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(EncontrarRaiz(), "build", "catalogo-entrada.ps1"), "-Version", "9.9.9", "-Zip", Path.Combine(carpeta, "no-existe.zip"), "-Salida", Path.Combine(carpeta, "salida.json") },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            string consola = proceso.StandardOutput.ReadToEnd() + proceso.StandardError.ReadToEnd();
            proceso.WaitForExit();

            Assert.Equal(1, proceso.ExitCode);
            // Sin acentos en lo que se busca: la consola de Windows PowerShell puede no sacarlos en UTF-8.
            Assert.Contains("Error: No se encontr", consola, StringComparison.Ordinal);
            Assert.Contains("el archivo zip de distribuci", consola, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(carpeta, "salida.json")));
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }
}
