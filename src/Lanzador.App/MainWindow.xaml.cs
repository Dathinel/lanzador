using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Lanzador.Core;
using Windows.Graphics;

namespace Lanzador.App;

public sealed partial class MainWindow : Window
{
    private readonly HttpClient _http;
    private readonly Instalador _instalador;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(860, 700));

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
        ListaTarjetas.Children.Clear();
        ActualizarEspacioTotal();

        try
        {
            IReadOnlyList<TarjetaHerramienta> tarjetas = await _instalador.ObtenerTarjetasAsync(refrescar);
            if (tarjetas.Count == 0)
            {
                var vacio = new TextBlock
                {
                    Text = "No hay herramientas disponibles en el catálogo para esta versión.",
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    Margin = new Thickness(0, 20, 0, 0)
                };
                ListaTarjetas.Children.Add(vacio);
            }
            else
            {
                foreach (TarjetaHerramienta t in tarjetas)
                {
                    ListaTarjetas.Children.Add(CrearTarjetaUI(t));
                }
            }
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

    private UIElement CrearTarjetaUI(TarjetaHerramienta tarjeta)
    {
        var contenedor = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
            Padding = new Thickness(16)
        };

        var panel = new StackPanel { Spacing = 10 };
        contenedor.Child = panel;

        // Fila 1: Título y estado
        var cabecera = new Grid();
        cabecera.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cabecera.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titulo = new TextBlock
        {
            Text = tarjeta.Nombre,
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"]
        };
        cabecera.Children.Add(titulo);

        var estadoPill = new TextBlock
        {
            Text = tarjeta.TextoVersion,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(estadoPill, 1);
        cabecera.Children.Add(estadoPill);
        panel.Children.Add(cabecera);

        // Fila 2: Descripción
        var descripcion = new TextBlock
        {
            Text = tarjeta.Descripcion,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };
        panel.Children.Add(descripcion);

        // Fila 3: Pesos (Descarga y Espacio en disco)
        var filaPesos = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        var textoDescarga = new TextBlock
        {
            Text = tarjeta.TextoDescarga,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };
        var textoEspacio = new TextBlock
        {
            Text = tarjeta.TextoEspacio,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };
        filaPesos.Children.Add(textoDescarga);
        filaPesos.Children.Add(textoEspacio);
        panel.Children.Add(filaPesos);

        // Fila 4: Progreso (inicialmente oculto)
        var panelProgreso = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
        var textoProgreso = new TextBlock
        {
            Text = "Descargando…",
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };
        var barraProgreso = new ProgressBar { Maximum = 100, Value = 0 };
        panelProgreso.Children.Add(textoProgreso);
        panelProgreso.Children.Add(barraProgreso);
        panel.Children.Add(panelProgreso);

        // Fila 5: Mensaje de error de la tarjeta (inicialmente cerrado)
        var avisoTarjeta = new InfoBar { IsOpen = false, IsClosable = true, Severity = InfoBarSeverity.Error };
        panel.Children.Add(avisoTarjeta);

        // Fila 6: Botones de acción
        var filaBotones = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        var btnInstalar = new Button
        {
            Content = "Instalar",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            Visibility = tarjeta.PuedeInstalar ? Visibility.Visible : Visibility.Collapsed
        };

        var btnAbrir = new Button
        {
            Content = "Abrir",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            Visibility = tarjeta.PuedeAbrir ? Visibility.Visible : Visibility.Collapsed
        };

        var btnActualizar = new Button
        {
            Content = "Actualizar",
            Visibility = tarjeta.PuedeActualizar ? Visibility.Visible : Visibility.Collapsed
        };

        var btnQuitar = new Button
        {
            Content = "Quitar",
            Visibility = tarjeta.PuedeQuitar ? Visibility.Visible : Visibility.Collapsed
        };

        var btnCancelar = new Button
        {
            Content = "Cancelar",
            Visibility = Visibility.Collapsed
        };

        filaBotones.Children.Add(btnInstalar);
        filaBotones.Children.Add(btnAbrir);
        filaBotones.Children.Add(btnActualizar);
        filaBotones.Children.Add(btnQuitar);
        filaBotones.Children.Add(btnCancelar);
        panel.Children.Add(filaBotones);

        CancellationTokenSource? ctsOperacion = null;

        void EstablecerOcupado(bool ocupado)
        {
            btnInstalar.IsEnabled = !ocupado;
            btnAbrir.IsEnabled = !ocupado;
            btnActualizar.IsEnabled = !ocupado;
            btnQuitar.IsEnabled = !ocupado;
            btnCancelar.Visibility = ocupado ? Visibility.Visible : Visibility.Collapsed;
            btnCancelar.IsEnabled = ocupado;
            panelProgreso.Visibility = ocupado ? Visibility.Visible : Visibility.Collapsed;
            if (!ocupado)
            {
                barraProgreso.Value = 0;
            }
        }

        btnCancelar.Click += (_, _) =>
        {
            ctsOperacion?.Cancel();
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
                avisoTarjeta.Message = MensajesError.Describir(ex);
                avisoTarjeta.IsOpen = true;
            }
        };

        btnInstalar.Click += async (_, _) =>
        {
            avisoTarjeta.IsOpen = false;
            ctsOperacion = new CancellationTokenSource();
            EstablecerOcupado(true);

            var progreso = new Progress<ProgresoDetallado>(p =>
            {
                textoProgreso.Text = p.TextoProgreso();
                barraProgreso.Value = (int)(p.ProgresoAgregado * 100);
            });

            try
            {
                await _instalador.InstalarAsync(tarjeta.Id, progreso, ctsOperacion.Token);
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
        };

        btnActualizar.Click += async (_, _) =>
        {
            avisoTarjeta.IsOpen = false;
            ctsOperacion = new CancellationTokenSource();
            EstablecerOcupado(true);

            var progreso = new Progress<ProgresoDetallado>(p =>
            {
                textoProgreso.Text = p.TextoProgreso();
                barraProgreso.Value = (int)(p.ProgresoAgregado * 100);
            });

            try
            {
                await _instalador.InstalarAsync(tarjeta.Id, progreso, ctsOperacion.Token);
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
        };

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
