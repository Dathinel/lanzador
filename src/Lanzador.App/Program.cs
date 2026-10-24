using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Lanzador.App;

/// <summary>Entrada propia (DISABLE_XAML_GENERATED_MAIN) para medir el arranque hasta el primer cuadro.</summary>
internal static class Program
{
    private static readonly Stopwatch Startup = Stopwatch.StartNew();
    private static string? _measureFile;

    public static App? Instance { get; private set; }

    [STAThread]
    private static int Main(string[] args)
    {
        // --medir <archivo>: escribe los milisegundos hasta el primer cuadro y cierra.
        int i = Array.IndexOf(args, "--medir");
        if (i >= 0 && i + 1 < args.Length)
        {
            _measureFile = args[i + 1];
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(static _ =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            Instance = new App();
        });
        return 0;
    }

    public static void FirstFrame()
    {
        if (_measureFile is null)
        {
            return;
        }

        File.WriteAllText(_measureFile, Startup.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
        Application.Current.Exit();
    }
}
