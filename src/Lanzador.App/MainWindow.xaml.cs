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
    private readonly ComprobadorLanzador _comprobador;
    // Operaciones de instalación en marcha por herramienta: una tarjeta repintada en un refresco recupera su «Cancelar» y no puede lanzar una segunda sobre el mismo destino.
    private readonly Dictionary<string, CancellationTokenSource> _operaciones = new(StringComparer.Ordinal);
    private Ajustes _ajustes;
    private ComprobacionLanzador? _ultimaComprobacion;
    private EstadoVentana? _ventanaRestaurada;

    public MainWindow()
    {
        InitializeComponent();
        ColocarVentana();

        OpcionesComunes opciones = OpcionesComunes.Parsear(Environment.GetCommandLineArgs()[1..]);

        _http = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = DecompressionMethods.None
        });
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"Lanzador/{Instalador.Version}");
        _instalador = new Instalador(opciones.Carpetas, opciones.Indice, opciones.Clave, _http);
        _ajustes = Ajustes.Cargar(Ajustes.RutaPorDefecto);
        _comprobador = new ComprobadorLanzador(new FuenteReleasesGitHub(_http, Instalador.Version), Instalador.Version);

        CompositionTarget.Rendering += OnFirstFrame;
        _ = BuscarActualizacionesAsync(aMano: false);
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
        await BuscarActualizacionesAsync(aMano: true);
    }

    /// <summary>«Más tarde»: esa versión del Lanzador no vuelve a avisar; la siguiente sí.</summary>
    private void OnMasTardeClick(object sender, RoutedEventArgs e)
    {
        FranjaLanzador.Visibility = Visibility.Collapsed;
        if (_ultimaComprobacion is { } comprobacion)
        {
            GuardarAjustes(comprobacion.Descartar());
        }
    }

    private void GuardarAjustes(Ajustes ajustes)
    {
        _ajustes = ajustes;
        try
        {
            ajustes.Guardar(Ajustes.RutaPorDefecto);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Perder los ajustes no merece un error en la ventana: se vuelven a aplicar los de fábrica la próxima vez.
        }
    }

    /// <summary>
    /// Al abrir y con «Buscar actualizaciones»: carga el catálogo y, después, mira si hay un Lanzador nuevo (al abrir solo si
    /// tocan las 24 h y el aviso está activado; a mano siempre, y dice cuando no hay nada nuevo). Sin red o con respuesta rara, silencio.
    /// </summary>
    private async Task BuscarActualizacionesAsync(bool aMano)
    {
        IReadOnlyList<TarjetaHerramienta>? tarjetas = await CargarCatalogoAsync(refrescar: aMano);
        try
        {
            ComprobacionLanzador comprobacion = await _comprobador.ComprobarAsync(_ajustes, aMano);
            _ultimaComprobacion = comprobacion;
            if (!ReferenceEquals(comprobacion.Ajustes, _ajustes))
            {
                GuardarAjustes(comprobacion.Ajustes);
            }

            if (comprobacion.TextoFranja is { } texto && comprobacion.Release is { } release)
            {
                TextoFranja.Text = texto;
                EnlaceNovedades.NavigateUri = release.Url;
                FranjaLanzador.Visibility = Visibility.Visible;
            }

            if (aMano && tarjetas is not null && ComprobadorLanzador.ResumenBusqueda(tarjetas, comprobacion) is { } resumen)
            {
                AvisoActualizaciones.Message = resumen;
                AvisoActualizaciones.IsOpen = true;
            }
        }
        catch (Exception ex)
        {
            MostrarErrorInesperado(MensajesError.Describir(ex));
        }
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

            // Versiones nuevas del propio Lanzador: el interruptor (activado por omisión) y buscar ahora mismo, también el catálogo.
            var interruptor = new ToggleSwitch { Header = "Avisar cuando haya una versión nueva", IsOn = _ajustes.AvisarVersionNueva, OnContent = "Activado", OffContent = "Desactivado" };
            interruptor.Toggled += (_, _) => GuardarAjustes(_ajustes with { AvisarVersionNueva = interruptor.IsOn });
            contenido.Children.Add(interruptor);
            var buscar = new HyperlinkButton { Content = "Buscar actualizaciones" };
            contenido.Children.Add(buscar);

            var dialogo = new ContentDialog { Title = "Acerca de", Content = contenido, CloseButtonText = "Cerrar", XamlRoot = Content.XamlRoot };
            buscar.Click += (_, _) =>
            {
                dialogo.Hide();
                _ = BuscarActualizacionesAsync(aMano: true);
            };
            await dialogo.ShowAsync();
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

    /// <summary>
    /// Lee el catálogo (al abrir y al pulsar «Buscar actualizaciones»), pinta las tarjetas y avisa discreto si hay versiones nuevas.
    /// Sin red pero con herramientas instaladas, las pinta desde el disco (solo «Abrir» y «Quitar») bajo la franja «Sin conexión».
    /// </summary>
    /// <returns>Las tarjetas del catálogo, o <c>null</c> si no se pudo leer (el panel o la franja «Sin conexión» ya lo dicen).</returns>
    private async Task<IReadOnlyList<TarjetaHerramienta>?> CargarCatalogoAsync(bool refrescar)
    {
        BotonRefrescar.IsEnabled = false;
        BotonReintentar.IsEnabled = false;
        AvisoIndice.IsOpen = false;
        AvisoSinConexion.IsOpen = false;
        AvisoActualizaciones.IsOpen = false;
        TextoRecuento.Visibility = Visibility.Collapsed;
        EstadoVacio.Visibility = Visibility.Collapsed;
        EstadoCatalogo.Visibility = Visibility.Collapsed;
        ListaTarjetas.Children.Clear();
        ActualizarEspacioTotal();

        try
        {
            CargaDeTarjetas carga = await _instalador.CargarTarjetasAsync(refrescar);
            IReadOnlyList<TarjetaHerramienta> tarjetas = carga.Tarjetas;
            foreach (TarjetaHerramienta t in tarjetas)
            {
                ListaTarjetas.Children.Add(CrearTarjetaUI(t));
            }

            if (carga.Aviso is { } sinConexion)
            {
                // Sin catálogo no se sabe si están al día: ni recuento, ni aviso, ni el resumen de «Buscar actualizaciones».
                AvisoSinConexion.Message = sinConexion;
                AvisoSinConexion.IsOpen = true;
                return null;
            }

            EstadoVacio.Visibility = tarjetas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            // «2 actualizaciones» junto al botón de la cabecera, solo si hay más de una; con una basta el botón de su tarjeta.
            if (Actualizaciones.Recuento(tarjetas) is { } recuento)
            {
                TextoRecuento.Text = recuento;
                TextoRecuento.Visibility = Visibility.Visible;
            }

            if (Actualizaciones.Aviso(tarjetas) is { } aviso)
            {
                AvisoActualizaciones.Message = aviso;
                AvisoActualizaciones.IsOpen = true;
            }

            return tarjetas;
        }
        catch (Exception ex)
        {
            // Con firma inválida (o sin red) la lista no se queda vacía en silencio: el panel dice por qué y qué hacer.
            (string titulo, string detalle) = MensajesError.DescribirFalloDelCatalogo(ex);
            TituloEstadoCatalogo.Text = titulo;
            TextoEstadoCatalogo.Text = detalle;
            EstadoCatalogo.Visibility = Visibility.Visible;
            return null;
        }
        finally
        {
            BotonRefrescar.IsEnabled = true;
            BotonReintentar.IsEnabled = true;
        }
    }

    private static T Estilo<T>(T elemento, string estilo) where T : FrameworkElement
    {
        elemento.Style = (Style)Application.Current.Resources[estilo];
        return elemento;
    }

    /// <summary>Vuelve a pintar una tarjeta con lo que hay en disco ahora (tras cancelar), con un aviso informativo encima.</summary>
    private async Task RecargarTarjetaAsync(string id, UIElement vieja)
    {
        try
        {
            CargaDeTarjetas carga = await _instalador.CargarTarjetasAsync(refrescar: false);
            TarjetaHerramienta? nueva = carga.TarjetaParaRepintar(id);
            int indice = ListaTarjetas.Children.IndexOf(vieja);
            if (nueva is null || indice < 0)
            {
                await CargarCatalogoAsync(refrescar: false);
                return;
            }

            ListaTarjetas.Children[indice] = CrearTarjetaUI(nueva, nueva.TextoCancelada);
        }
        catch (Exception ex)
        {
            MostrarErrorInesperado(MensajesError.Describir(ex));
        }
    }

    private UIElement CrearTarjetaUI(TarjetaHerramienta tarjeta, string? avisoInicial = null)
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
        var avisoTarjeta = new InfoBar { IsOpen = avisoInicial is not null, IsClosable = true, Severity = avisoInicial is null ? InfoBarSeverity.Error : InfoBarSeverity.Informational, Message = avisoInicial ?? "" };
        panel.Children.Add(avisoTarjeta);

        // Fila 5: botones de acción
        var filaBotones = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Style acento = (Style)Application.Current.Resources["AccentButtonStyle"];
        // Los textos («Reanudar descarga», «Instalar (ya descargado)», «Completar instalación») los decide la tarjeta; «Actualizar a X»
        // ocupa el sitio de «Instalar» (mismo estilo de acento) solo cuando la instalada es más vieja que la del catálogo.
        var btnInstalar = new Button { Content = tarjeta.TextoInstalar, Style = acento, Visibility = tarjeta.PuedeInstalar ? Visibility.Visible : Visibility.Collapsed };
        var btnActualizar = new Button { Content = tarjeta.TextoActualizar, Style = acento, Visibility = tarjeta.PuedeActualizar ? Visibility.Visible : Visibility.Collapsed };
        var btnAbrir = new Button { Content = "Abrir", Style = acento, Visibility = tarjeta.PuedeAbrir ? Visibility.Visible : Visibility.Collapsed };
        var btnQuitar = new Button { Content = "Quitar", Visibility = tarjeta.PuedeQuitar ? Visibility.Visible : Visibility.Collapsed };
        var btnCancelar = new Button { Content = "Cancelar", Visibility = Visibility.Collapsed };
        filaBotones.Children.Add(btnInstalar);
        filaBotones.Children.Add(btnActualizar);
        filaBotones.Children.Add(btnAbrir);
        filaBotones.Children.Add(btnQuitar);
        filaBotones.Children.Add(btnCancelar);
        panel.Children.Add(filaBotones);

        CancellationTokenSource? ctsOperacion = _operaciones.TryGetValue(tarjeta.Id, out CancellationTokenSource? enCurso) ? enCurso : null;
        var reloj = new Stopwatch();
        double fraccion = 0;
        TimeSpan? restanteMedido = null;
        var temporizador = DispatcherQueue.CreateTimer();
        temporizador.Interval = TimeSpan.FromSeconds(1);
        // Lo que queda sale de la velocidad real de la descarga; mientras no la haya, de la regla de tres sobre la fracción.
        temporizador.Tick += (_, _) => textoTiempos.Text = Tiempos.Resumen(reloj.Elapsed, fraccion, restanteMedido);

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
                restanteMedido = null;
                relleno.Scale = new Vector3(0, 1, 1);
                textoTiempos.Text = "0:00";
            }
        }

        // Si la operación sigue en marcha pero la tarjeta se repintó en un refresco, se muestra ocupada y «Cancelar» apunta a su origen.
        if (enCurso is not null)
        {
            EstablecerOcupado(true);
        }

        btnCancelar.Click += (_, _) =>
        {
            try
            {
                ctsOperacion?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        };

        btnAbrir.Click += (_, _) =>
        {
            avisoTarjeta.IsOpen = false;
            try
            {
                _instalador.AbrirHerramienta(tarjeta.Id);
            }
            catch (Exception ex)
            {
                avisoTarjeta.Severity = InfoBarSeverity.Error;
                avisoTarjeta.Message = MensajesError.Describir(ex);
                avisoTarjeta.IsOpen = true;
            }
        };

        // Instalar y Actualizar hacen lo mismo: el Instalador ya sabe si hay versión previa.
        async void Ejecutar()
        {
            avisoTarjeta.IsOpen = false;
            var cts = new CancellationTokenSource();
            if (!_operaciones.TryAdd(tarjeta.Id, cts))
            {
                // Ya hay una operación sobre esta herramienta (la tarjeta se repintó en un refresco).
                cts.Dispose();
                return;
            }

            ctsOperacion = cts;
            EstablecerOcupado(true);

            var progreso = new Progress<ProgresoDetallado>(p =>
            {
                fraccion = p.ProgresoAgregado;
                restanteMedido = p.TiempoRestante;
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
            catch (OperationCanceledException)
            {
                // Lo bajado queda en Comun\descargas; la tarjeta recargada dice si hay algo que reanudar o completar.
                EstablecerOcupado(false);
                ActualizarEspacioTotal();
                await RecargarTarjetaAsync(tarjeta.Id, contenedor);
            }
            catch (Exception ex)
            {
                EstablecerOcupado(false);
                avisoTarjeta.Severity = InfoBarSeverity.Error;
                avisoTarjeta.Message = MensajesError.Describir(ex);
                avisoTarjeta.IsOpen = true;
                ActualizarEspacioTotal();
            }
            finally
            {
                _operaciones.Remove(tarjeta.Id);
                ctsOperacion?.Dispose();
                ctsOperacion = null;
            }
        }

        btnInstalar.Click += (_, _) => Ejecutar();
        btnActualizar.Click += (_, _) => Ejecutar();

        // Quitar pide confirmación diciendo qué carpeta se borra, qué descargas de la app se van y qué se queda; «Cancelar» es el botón por defecto.
        btnQuitar.Click += async (_, _) =>
        {
            avisoTarjeta.IsOpen = false;
            try
            {
                var confirmacion = new ContentDialog
                {
                    Title = $"¿Quitar {tarjeta.Nombre}?",
                    Content = new TextBlock { Text = tarjeta.TextoConfirmarQuitar(_instalador.Carpetas), TextWrapping = TextWrapping.Wrap },
                    PrimaryButtonText = "Quitar",
                    CloseButtonText = "Cancelar",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = Content.XamlRoot,
                };
                if (await confirmacion.ShowAsync() != ContentDialogResult.Primary)
                {
                    return;
                }

                // Con catálogo borra también los zips y «.parcial» de la app en Comun\descargas; sin red, solo la carpeta.
                await _instalador.QuitarAsync(tarjeta.Id);
                await CargarCatalogoAsync(refrescar: false);
            }
            catch (Exception ex)
            {
                avisoTarjeta.Severity = InfoBarSeverity.Error;
                avisoTarjeta.Message = MensajesError.Describir(ex);
                avisoTarjeta.IsOpen = true;
            }
        };

        return contenedor;
    }
}
