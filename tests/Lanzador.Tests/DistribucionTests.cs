using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Rodaje.Packages.Download;
using Rodaje.TestServer;

namespace Lanzador.Tests;

/// <summary>build\catalogo-entrada.ps1 sobre un zip de prueba: la entrada «app» que produce la acepta SignedIndex con la huella y el peso reales.</summary>
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

            using var proceso = Process.Start(new ProcessStartInfo("powershell")
            {
                ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(EncontrarRaiz(), "build", "catalogo-entrada.ps1"), "-Version", "9.9.9", "-Zip", zip, "-Salida", salida },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            string consola = proceso.StandardOutput.ReadToEnd() + proceso.StandardError.ReadToEnd();
            proceso.WaitForExit();
            Assert.True(proceso.ExitCode == 0, consola);

            string entrada = File.ReadAllText(salida, Encoding.UTF8);
            byte[] indice = Encoding.UTF8.GetBytes($$"""{ "formatVersion": 1, "generated": "2026-10-24T12:00:00Z", "entries": [ {{entrada}} ] }""");
            using var firmante = new TestIndexSigner();
            using var clave = ECDsa.Create();
            clave.ImportSubjectPublicKeyInfo(firmante.PublicKey, out _);
            var problemas = new List<string>();

            IndexEntry e = Assert.Single(SignedIndex.Load(indice, firmante.SignFile(indice), clave, DownloadUrlPolicy.Default, problemas).Entries);

            Assert.Empty(problemas);
            Assert.Equal("lanzador", e.Id);
            Assert.Equal("9.9.9", e.Version.ToString());
            Assert.Equal(IndexEntryKind.App, e.Kind);
            Assert.Equal("https://github.com/Dathinel/lanzador/releases/download/v9.9.9/Lanzador-9.9.9-win-x64.zip", e.Url.OriginalString);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip))), e.Sha256);
            Assert.Equal(new FileInfo(zip).Length, e.Size);
            Assert.Null(e.MinApp);
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }
}
