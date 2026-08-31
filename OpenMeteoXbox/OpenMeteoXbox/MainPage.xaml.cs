using System;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Media.SpeechSynthesis;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace OpenMeteoXbox
{
    /// <summary>
    /// Página principal de la aplicación del clima.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private string _speechText = string.Empty;

        public MainPage()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Manejador global de teclas y botones del mando en la página.
        /// </summary>
        public void Page_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (keyboardOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Windows.System.VirtualKey.GamepadB || e.Key == Windows.System.VirtualKey.Escape)
                {
                    btnCancelKeyboard_Click(this, null);
                    e.Handled = true;
                }
                else if (e.Key == Windows.System.VirtualKey.GamepadX)
                {
                    // Atajo: [X] = Espacio
                    txtKeyboardDisplay.Text += " ";
                    e.Handled = true;
                }
                else if (e.Key == Windows.System.VirtualKey.GamepadY)
                {
                    // Atajo: [Y] = Borrar
                    if (txtKeyboardDisplay.Text.Length > 0)
                    {
                        txtKeyboardDisplay.Text = txtKeyboardDisplay.Text.Substring(0, txtKeyboardDisplay.Text.Length - 1);
                    }
                    e.Handled = true;
                }
                else if (e.Key == Windows.System.VirtualKey.GamepadMenu)
                {
                    // Atajo: [Start] = Aceptar y buscar
                    btnAcceptKeyboard_Click(this, null);
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// Manejador de eventos de teclado y mando en el cuadro de búsqueda.
        /// </summary>
        public void OnKeyboardInput(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter || e.Key == Windows.System.VirtualKey.GamepadMenu)
            {
                btnSearch_Click(this, null);
            }
            else if (e.Key == Windows.System.VirtualKey.GamepadA)
            {
                OpenVirtualKeyboard();
            }
        }

        public void btnOpenKeyboard_Click(object sender, RoutedEventArgs e)
        {
            OpenVirtualKeyboard();
        }

        private void OpenVirtualKeyboard()
        {
            txtKeyboardDisplay.Text = txtCity.Text ?? string.Empty;
            keyboardOverlay.Visibility = Visibility.Visible;
            btnFirstKey.Focus(FocusState.Programmatic);

            // Intentar también abrir el teclado táctil de Windows/Xbox si está disponible
            Windows.UI.ViewManagement.InputPane.GetForCurrentView().TryShow();
        }

        public void KeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                if (tag == "BACKSPACE")
                {
                    if (txtKeyboardDisplay.Text.Length > 0)
                    {
                        txtKeyboardDisplay.Text = txtKeyboardDisplay.Text.Substring(0, txtKeyboardDisplay.Text.Length - 1);
                    }
                }
                else if (tag == "SPACE")
                {
                    txtKeyboardDisplay.Text += " ";
                }
                else if (tag == "CLEAR")
                {
                    txtKeyboardDisplay.Text = string.Empty;
                }
                else
                {
                    txtKeyboardDisplay.Text += tag;
                }
            }
        }

        public void btnAcceptKeyboard_Click(object sender, RoutedEventArgs e)
        {
            txtCity.Text = txtKeyboardDisplay.Text;
            keyboardOverlay.Visibility = Visibility.Collapsed;
            btnSearch_Click(this, null);
        }

        public void btnCancelKeyboard_Click(object sender, RoutedEventArgs e)
        {
            keyboardOverlay.Visibility = Visibility.Collapsed;
            txtCity.Focus(FocusState.Programmatic);
        }

        /// <summary>
        /// Manejador del botón de búsqueda de clima.
        /// </summary>
        public async void btnSearch_Click(object sender, RoutedEventArgs e)
        {
            string city = txtCity.Text?.Trim();
            if (string.IsNullOrWhiteSpace(city))
            {
                ShowError("Por favor, introduce el nombre de una ciudad.");
                return;
            }

            SetLoading(true);

            try
            {
                // 1. Obtener coordenadas geográficas de la ciudad
                string geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=es&format=json";
                string geoResponse = await _httpClient.GetStringAsync(new Uri(geoUrl));

                if (!JsonObject.TryParse(geoResponse, out JsonObject geoJson) ||
                    !geoJson.ContainsKey("results") ||
                    geoJson.GetNamedArray("results").Count == 0)
                {
                    ShowError($"No se encontraron resultados para '{city}'.");
                    return;
                }

                JsonObject locationObj = geoJson.GetNamedArray("results").GetObjectAt(0);
                string cityName = locationObj.GetNamedString("name");
                string country = locationObj.ContainsKey("country") ? locationObj.GetNamedString("country") : string.Empty;
                double latitude = locationObj.GetNamedNumber("latitude");
                double longitude = locationObj.GetNamedNumber("longitude");

                // 2. Obtener pronóstico meteorológico actual
                string latStr = latitude.ToString(CultureInfo.InvariantCulture);
                string lonStr = longitude.ToString(CultureInfo.InvariantCulture);
                string weatherUrl = $"https://api.open-meteo.com/v1/forecast?latitude={latStr}&longitude={lonStr}&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m";

                string weatherResponse = await _httpClient.GetStringAsync(new Uri(weatherUrl));
                if (!JsonObject.TryParse(weatherResponse, out JsonObject weatherJson) ||
                    !weatherJson.ContainsKey("current"))
                {
                    ShowError("Error al obtener los datos del clima.");
                    return;
                }

                JsonObject current = weatherJson.GetNamedObject("current");
                double temp = current.GetNamedNumber("temperature_2m");
                double apparentTemp = current.GetNamedNumber("apparent_temperature");
                double humidity = current.GetNamedNumber("relative_humidity_2m");
                double windSpeed = current.GetNamedNumber("wind_speed_10m");
                int weatherCode = (int)current.GetNamedNumber("weather_code");

                WeatherDetails weather = GetWeatherInfo(weatherCode);

                // 3. Actualizar la interfaz de usuario
                txtLocation.Text = string.IsNullOrEmpty(country) ? cityName : $"{cityName}, {country}";
                txtWeatherEmoji.Text = weather.Emoji;
                txtWeatherDescription.Text = weather.Description;
                txtTemperature.Text = $"{temp:0.#} °C";
                txtApparentTemp.Text = $"{apparentTemp:0.#} °C";
                txtHumidity.Text = $"{humidity:0}%";
                txtWindSpeed.Text = $"{windSpeed:0.#} km/h";

                _speechText = $"En {cityName}, el clima actual es {weather.Description} con una temperatura de {temp:0.#} grados Celsius y una sensación térmica de {apparentTemp:0.#} grados.";

                txtError.Visibility = Visibility.Collapsed;
                weatherCard.Visibility = Visibility.Visible;
            }
            catch (HttpRequestException)
            {
                ShowError("Error de conexión. Comprueba tu conexión a internet.");
            }
            catch (Exception ex)
            {
                ShowError($"Ocurrió un error: {ex.Message}");
            }
            finally
            {
                SetLoading(false);
            }
        }

        /// <summary>
        /// Síntesis de voz para leer el pronóstico actual en voz alta.
        /// </summary>
        public async void btnSpeak_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_speechText)) return;

            try
            {
                MediaElement mediaElement = new MediaElement();
                using (var synth = new SpeechSynthesizer())
                {
                    SpeechSynthesisStream stream = await synth.SynthesizeTextToStreamAsync(_speechText);
                    mediaElement.SetSource(stream, stream.ContentType);
                    mediaElement.Play();
                }
            }
            catch
            {
                // Si falla el sintetizador de voz en la plataforma, no interrumpir la experiencia de usuario
            }
        }

        private void SetLoading(bool isLoading)
        {
            progressRing.IsActive = isLoading;
            progressRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            btnSearch.IsEnabled = !isLoading;
            txtCity.IsEnabled = !isLoading;

            if (isLoading)
            {
                txtError.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowError(string message)
        {
            txtError.Text = message;
            txtError.Visibility = Visibility.Visible;
            weatherCard.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Traduce el código de clima WMO de Open-Meteo a emoji y descripción en español.
        /// </summary>
        private WeatherDetails GetWeatherInfo(int code)
        {
            switch (code)
            {
                case 0:
                    return new WeatherDetails("☀️", "Cielo despejado");
                case 1:
                    return new WeatherDetails("🌤️", "Mayormente despejado");
                case 2:
                    return new WeatherDetails("⛅", "Parcialmente nublado");
                case 3:
                    return new WeatherDetails("☁️", "Nublado");
                case 45:
                case 48:
                    return new WeatherDetails("🌫️", "Niebla");
                case 51:
                case 53:
                case 55:
                    return new WeatherDetails("🌦️", "Llovizna");
                case 56:
                case 57:
                    return new WeatherDetails("🌨️", "Llovizna helada");
                case 61:
                case 63:
                case 65:
                    return new WeatherDetails("🌧️", "Lluvia");
                case 66:
                case 67:
                    return new WeatherDetails("🌨️", "Lluvia helada");
                case 71:
                case 73:
                case 75:
                case 77:
                    return new WeatherDetails("❄️", "Nevada");
                case 80:
                case 81:
                case 82:
                    return new WeatherDetails("🌧️", "Chubascos de lluvia");
                case 85:
                case 86:
                    return new WeatherDetails("🌨️", "Chubascos de nieve");
                case 95:
                    return new WeatherDetails("⛈️", "Tormenta eléctrica");
                case 96:
                case 99:
                    return new WeatherDetails("⛈️", "Tormenta eléctrica con granizo");
                default:
                    return new WeatherDetails("🌡️", "Condición variable");
            }
        }


    }

    /// <summary>
    /// Modelo auxiliar para la información del estado del tiempo.
    /// </summary>
    public class WeatherDetails
    {
        public string Emoji { get; }
        public string Description { get; }

        public WeatherDetails(string emoji, string description)
        {
            Emoji = emoji;
            Description = description;
        }
    }
}
