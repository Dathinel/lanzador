using System.Diagnostics;
using System.Net;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Lanzador.Core;
using Windows.Graphics;
using WinRT;

namespace Lanzador.App;

public sealed partial class MainWindow : Window
{
    private readonly HttpClient _http;
    private readonly Instalador _instalador;
    private EstadoVentana? _ventanaRestaurada;

    public MainWindow()
    {
        InitializeComponent();
        ColocarVentana();

        string indice = Instalador.IndiceDeDathinel.AbsoluteUri;
        string? clave = null;
        Carpetas carpetas = Carpetas.PorDefecto();

        string[] args = Environment.GetCommandLineArgs();
        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];
            string? val = i + 1 < args.Length ? args[i + 1] : null;
            if (arg == "--indice" && val is not null) { indice = val; i++; }
            else if (arg == "--clave" && val is not null) { clave = val; i++; }
            else if (arg == "--destino" && val is not null) { carpetas = carpetas with { Herramientas = val }; i++; }
            else if (arg == "--comun" && val is not null) { carpetas = carpetas with { Comun = val }; i++; }
        }

        _http = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = DecompressionMethods.None
        });
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"Lanzador/{Instalador.Version}");
        _instalador = new Instalador(carpetas, indice, clave, _http);

        CompositionTarget.Rendering += OnFirstFrame;
        _ = CargarCatalogoAsync(refrescar: false);
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint LoadIconW(nint hInstance, nint nombre);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetModuleHandleW(nint nombre);

    /// <summary>Icono del exe (ApplicationIcon), tamaño mínimo, y el tamaño y la posición de la última vez si siguen en pantalla.</summary>
    private void ColocarVentana()
    {
        nint icono = LoadIconW(GetModuleHandleW(0), 32512); // IDI_APPLICATION: el id con el que .NET incrusta ApplicationIcon
        if (icono != 0)
        {
            AppWindow.SetIcon(Win32Interop.GetIconIdFromIcon(icono));
        }

        double escala = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        // En AOT «Presenter is OverlappedPresenter» nunca acierta (hallazgo de Rastrear): se pregunta por Kind y se proyecta con As<>.
        OverlappedPresenter? presentador = AppWindow.Presenter.Kind == AppWindowPresenterKind.Overlapped ? AppWindow.Presenter.As<OverlappedPresenter>() : null;
        if (presentador is not null)
        {
            presentador.PreferredMinimumWidth = (int)(EstadoVentana.AnchoMinimo * escala);
            presentador.PreferredMinimumHeight = (int)(EstadoVentana.AltoMinimo * escala);
        }

        _ventanaRestaurada = EstadoVentana.Cargar(EstadoVentana.RutaPorDefecto);
        if (_ventanaRestaurada is { } v && DisplayArea.GetFromRect(new RectInt32(v.X, v.Y, v.Ancho, v.Alto), DisplayAreaFallback.None) is not null)
        {
            AppWindow.MoveAndResize(new RectInt32(v.X, v.Y, v.Ancho, v.Alto));
            if (v.Maximizada)
            {
                presentador?.Maximize();
            }
        }
        else
        {
            _ventanaRestaurada = null;
            AppWindow.Resize(new SizeInt32((int)(880 * escala), (int)(720 * escala)));
        }

        AppWindow.Changed += (w, e) =>
        {
            if ((e.DidSizeChange || e.DidPositionChange) && presentador?.State is null or OverlappedPresenterState.Restored)
            {
                _ventanaRestaurada = new EstadoVentana(w.Position.X, w.Position.Y, w.Size.Width, w.Size.Height, false);
            }
        };
        Closed += (_, _) =>
        {
            try
            {
                EstadoVentana r = _ventanaRestaurada ?? new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height, false);
                (r with { Maximizada = presentador?.State == OverlappedPresenterState.Maximized }).Guardar(EstadoVentana.RutaPorDefecto);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Perder la posición de la ventana no merece un error al cerrar.
            }
        };
    }

    private void OnFirstFrame(object? sender, object e)
    {
        CompositionTarget.Rendering -= OnFirstFrame;
        Program.FirstFrame();
    }

    public void MostrarErrorInesperado(string mensaje)
    {
        AvisoIndice.Severity = InfoBarSeverity.Error;
        AvisoIndice.Message = mensaje;
        AvisoIndice.IsOpen = true;
    }

    private async void OnRefrescarClick(object sender, RoutedEventArgs e)
    {
        await CargarCatalogoAsync(refrescar: true);
    }

    private void OnAbrirCarpetaClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _instalador.AbrirCarpetaDathinel();
        }
        catch (Exception ex)
        {
            MostrarErrorInesperado(MensajesError.Describir(ex));
        }
    }

    /// <summary>Crédito a Rodaje exigido por el término 1 de su licencia (Lanzador usa Rodaje.Packages), más nombre, versión y licencia propias.</summary>
    private async void OnAcercaDeClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            var contenido = new StackPanel { Spacing = 12 };
            contenido.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("ms-appx:///Acento.xaml") });
            contenido.Children.Add(Estilo(new TextBlock { Text = $"Lanzador {version}" }, "TituloTarjeta"));
            contenido.Children.Add(Estilo(new TextBlock { Text = "Las herramientas, listas. Software libre bajo la GPLv3, sin garantía." }, "TextoSecundario"));
            contenido.Children.Add(new HyperlinkButton
            {
                Content = new TextBlock { Text = "Basado en el editor de video de código abierto de Dathinel / Rodaje (https://github.com/Dathinel/rodaje)", TextWrapping = TextWrapping.Wrap },
                NavigateUri = new Uri("https://github.com/Dathinel/rodaje")
            });
            contenido.Children.Add(new HyperlinkButton
            {
                Content = "Código y licencia del Lanzador: github.com/Dathinel/lanzador",
                NavigateUri = new Uri("https://github.com/Dathinel/lanzador")
            });
            await new ContentDialog { Title = "Acerca de", Content = contenido, CloseButtonText = "Cerrar", XamlRoot = Content.XamlRoot }.ShowAsync();
        }
        catch (Exception ex)
        {
            MostrarErrorInesperado(MensajesError.Describir(ex));
        }
    }

    private void ActualizarEspacioTotal()
    {
        try
        {
            long bytes = _instalador.EspacioTotalDathinel();
            TextoEspacioTotal.Text = $"Espacio total en {_instalador.RaizDathinel}: {Instalador.Peso(bytes)}";
        }
        catch
        {
            TextoEspacioTotal.Text = $"Espacio total en {_instalador.RaizDathinel}: —";
        }
    }

    private async Task CargarCatalogoAsync(bool refrescar)
    {
        BotonRefrescar.IsEnabled = false;
        AvisoIndice.IsOpen = false;
        EstadoVacio.Visibility = Visibility.Collapsed;
        ListaTarjetas.Children.Clear();
        ActualizarEspacioTotal();

        try
        {
            IReadOnlyList<TarjetaHerramienta> tarjetas = await _instalador.ObtenerTarjetasAsync(refrescar);
            foreach (TarjetaHerramienta t in tarjetas)
            {
                ListaTarjetas.Children.Add(CrearTarjetaUI(t));
            }

            EstadoVacio.Visibility = tarjetas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            string desc = MensajesError.Describir(ex);
            AvisoIndice.Severity = InfoBarSeverity.Error;
            AvisoIndice.Message = $"No se puede verificar el catálogo: {desc}. No se ofrecen aplicaciones para instalar.";
            AvisoIndice.IsOpen = true;
        }
        finally
        {
            BotonRefrescar.IsEnabled = true;
        }
    }

    private static T Estilo<T>(T elemento, string estilo) where T : FrameworkElement
    {
        elemento.Style = (Style)Application.Current.Resources[estilo];
        return elemento;
    }

    private UIElement CrearTarjetaUI(TarjetaHerramienta tarjeta)
    {
        var contenedor = Estilo(new Border { Shadow = new ThemeShadow(), Translation = new Vector3(0, 0, 12) }, "Tarjeta");
        var panel = new StackPanel { Spacing = 16 };
        contenedor.Child = panel;

        // Fila 1: nombre, tic de «hecho» (aparece al terminar) y versión
        var cabecera = new Grid { ColumnSpacing = 12 };
        cabecera.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cabecera.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cabecera.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        cabecera.Children.Add(Estilo(new TextBlock { Text = tarjeta.Nombre, VerticalAlignment = VerticalAlignment.Center }, "TituloTarjeta"));

        var tic = Estilo(new FontIcon
        {
            Opacity = 0,
            Scale = new Vector3(0.4f, 0.4f, 1),
            CenterPoint = new Vector3(11, 11, 0),
            OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(350) },
            ScaleTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(350) },
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        }, "Tic");
        Grid.SetColumn(tic, 1);
        cabecera.Children.Add(tic);

        var version = Estilo(new TextBlock { Text = tarjeta.TextoVersion, VerticalAlignment = VerticalAlignment.Center }, "TextoSecundario");
        Grid.SetColumn(version, 2);
        cabecera.Children.Add(version);
        panel.Children.Add(cabecera);

        // Fila 2: descripción y pesos
        var detalle = new StackPanel { Spacing = 8 };
        detalle.Children.Add(Estilo(new TextBlock { Text = tarjeta.Descripcion }, "TextoSecundario"));
        var filaPesos = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
        filaPesos.Children.Add(Estilo(new TextBlock { Text = tarjeta.TextoDescarga }, "TextoSecundario"));
        filaPesos.Children.Add(Estilo(new TextBlock { Text = tarjeta.TextoEspacio }, "TextoSecundario"));
        detalle.Children.Add(filaPesos);
        panel.Children.Add(detalle);

        // Fila 3: progreso (etapa, barra animada, bytes y tiempos), oculto hasta que hay una operación
        var panelProgreso = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        var textoFase = Estilo(new TextBlock { Text = "Preparando…" }, "TextoAcento");
        var relleno = Estilo(new Border
        {
            Scale = new Vector3(0, 1, 1),
            ScaleTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(300) }
        }, "BarraRelleno");
        var pista = Estilo(new Border { Child = relleno }, "BarraPista");
        var filaTiempos = new Grid();
        filaTiempos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filaTiempos.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var textoBytes = Estilo(new TextBlock(), "TextoSecundario");
        var textoTiempos = Estilo(new TextBlock { Text = "0:00" }, "TextoSecundario");
        Grid.SetColumn(textoTiempos, 1);
        filaTiempos.Children.Add(textoBytes);
        filaTiempos.Children.Add(textoTiempos);
        panelProgreso.Children.Add(textoFase);
        panelProgreso.Children.Add(pista);
        panelProgreso.Children.Add(filaTiempos);
        panel.Children.Add(panelProgreso);

        // Fila 4: mensaje de error de la tarjeta (inicialmente cerrado)
        var avisoTarjeta = new InfoBar { IsOpen = false, IsClosable = true, Severity = InfoBarSeverity.Error };
        panel.Children.Add(avisoTarjeta);

        // Fila 5: botones de acción
        var filaBotones = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Style acento = (Style)Application.Current.Resources["AccentButtonStyle"];
        var btnInstalar = new Button { Content = "Instalar", Style = acento, Visibility = tarjeta.PuedeInstalar ? Visibility.Visible : Visibility.Collapsed };
        var btnAbrir = new Button { Content = "Abrir", Style = acento, Visibility = tarjeta.PuedeAbrir ? Visibility.Visible : Visibility.Collapsed };
        var btnActualizar = new Button { Content = "Actualizar", Visibility = tarjeta.PuedeActualizar ? Visibility.Visible : Visibility.Collapsed };
        var btnQuitar = new Button { Content = "Quitar", Visibility = tarjeta.PuedeQuitar ? Visibility.Visible : Visibility.Collapsed };
        var btnCancelar = new Button { Content = "Cancelar", Visibility = Visibility.Collapsed };
        filaBotones.Children.Add(btnInstalar);
        filaBotones.Children.Add(btnAbrir);
        filaBotones.Children.Add(btnActualizar);
        filaBotones.Children.Add(btnQuitar);
        filaBotones.Children.Add(btnCancelar);
        panel.Children.Add(filaBotones);

        CancellationTokenSource? ctsOperacion = null;
        var reloj = new Stopwatch();
        double fraccion = 0;
        var temporizador = DispatcherQueue.CreateTimer();
        temporizador.Interval = TimeSpan.FromSeconds(1);
        temporizador.Tick += (_, _) => textoTiempos.Text = Tiempos.Resumen(reloj.Elapsed, fraccion);

        void EstablecerOcupado(bool ocupado)
        {
            btnInstalar.IsEnabled = !ocupado;
            btnAbrir.IsEnabled = !ocupado;
            btnActualizar.IsEnabled = !ocupado;
            btnQuitar.IsEnabled = !ocupado;
            btnCancelar.Visibility = ocupado ? Visibility.Visible : Visibility.Collapsed;
            btnCancelar.IsEnabled = ocupado;
            panelProgreso.Visibility = ocupado ? Visibility.Visible : Visibility.Collapsed;
            if (ocupado)
            {
                reloj.Restart();
                temporizador.Start();
            }
            else
            {
                temporizador.Stop();
                reloj.Reset();
                fraccion = 0;
                relleno.Scale = new Vector3(0, 1, 1);
                textoTiempos.Text = "0:00";
            }
        }

        btnCancelar.Click += (_, _) => ctsOperacion?.Cancel();

        btnAbrir.Click += (_, _) =>
        {
            avisoTarjeta.IsOpen = false;
            try
            {
                _instalador.AbrirHerramienta(tarjeta.Id);
            }
            catch (Exception ex)
            {
                avisoTarjeta.Message = MensajesError.Describir(ex);
                avisoTarjeta.IsOpen = true;
            }
        };

        // Instalar y Actualizar hacen lo mismo: el Instalador ya sabe si hay versión previa.
        async void Ejecutar()
        {
            avisoTarjeta.IsOpen = false;
            ctsOperacion = new CancellationTokenSource();
            EstablecerOcupado(true);

            var progreso = new Progress<ProgresoDetallado>(p =>
            {
                fraccion = p.ProgresoAgregado;
                textoFase.Text = p.Fase;
                textoBytes.Text = p.BytesTotales > 0 ? p.TextoProgreso() : "";
                relleno.Scale = new Vector3((float)Math.Max(0.02, p.ProgresoAgregado), 1, 1);
            });

            try
            {
                await _instalador.InstalarAsync(tarjeta.Id, progreso, ctsOperacion.Token);
                temporizador.Stop();
                textoFase.Text = "Hecho";
                textoBytes.Text = "";
                textoTiempos.Text = Tiempos.Formatear(reloj.Elapsed);
                relleno.Scale = new Vector3(1, 1, 1);
                btnCancelar.Visibility = Visibility.Collapsed;
                tic.Opacity = 1;
                tic.Scale = new Vector3(1, 1, 1);
                await Task.Delay(900);
                await CargarCatalogoAsync(refrescar: false);
            }
            catch (Exception ex)
            {
                EstablecerOcupado(false);
                avisoTarjeta.Message = MensajesError.Describir(ex);
                avisoTarjeta.IsOpen = true;
                ActualizarEspacioTotal();
            }
            finally
            {
                ctsOperacion.Dispose();
                ctsOperacion = null;
            }
        }

        btnInstalar.Click += (_, _) => Ejecutar();
        btnActualizar.Click += (_, _) => Ejecutar();

        btnQuitar.Click += async (_, _) =>
        {
            avisoTarjeta.IsOpen = false;
            try
            {
                _instalador.Quitar(tarjeta.Id);
                await CargarCatalogoAsync(refrescar: false);
            }
            catch (Exception ex)
            {
                avisoTarjeta.Message = MensajesError.Describir(ex);
                avisoTarjeta.IsOpen = true;
            }
        };

        return contenedor;
    }
}
