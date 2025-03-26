﻿using System.Text;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Models
{
    public class KeyLight(int id, string displayName, int port, string address)
        : IDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        public int Id { get; private set; } = id;

        public string DisplayName { get; } = displayName;
        private int Port { get; } = port;
        private string Address { get; } = address;

        private string Url => $"http://{Address}:{Port}/elgato/lights";

        private CancellationToken CancellationToken => this._cancellationTokenSource.Token;

        public bool On { get; set; }
        public int Brightness { get; set; } = 10;
        public int Temperature { get; set; } = 200;
        public int Hue { get; set; }
        public int Saturation { get; set; }
        public bool Ready { get; set; }

        private readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        public async Task InitDeviceAsync()
        {
            var retries = 3;

            while (!Ready && retries > 0)
            {
                retries--;

                try
                {
                    var response = await _httpClient.GetAsync(this.Url, CancellationToken);

                    response.EnsureSuccessStatusCode();

                    var responseContent = await response.Content.ReadAsStringAsync(CancellationToken);

                    SetLightData(JObject.Parse(responseContent));

                    Ready = true;
                }
                catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
                {
                    // Timeout aufgetreten
                    if (retries == 0)
                    {
                        throw new Exception("Request was canceled or timed out after retries", ex);
                    }
                }
                catch (HttpRequestException ex)
                {
                    // Andere HTTP-Anfragefehler behandeln
                    if (retries == 0)
                    {
                        throw new Exception("An error occurred during the web request after retries", ex);
                    }
                }
            }
        }

        public void Toggle()
        {
            SetDeviceState(!this.On);
        }

        public void SetBrightness(int brightness)
        {
            SetDeviceBrightness(brightness);
        }

        public void SetTemperature(int temperature)
        {
            SetDeviceTemperature(temperature);
        }

        public void SetHue(int hue)
        {
            SetDeviceHue(hue);
        }

        public void SetSaturation(int saturation)
        {
            SetDeviceSaturation(saturation);
        }

        private void SetDeviceState(bool on)
        {
            if (On == on)
            {
                return;
            }

            var jsonData = $"{{\"lights\":[{{\"on\":{Convert.ToInt32(on)}}}]}}";

            SendPutRequestAsync(jsonData).GetAwaiter().GetResult();

            On = on;
        }

        private void SetDeviceBrightness(int brightness)
        {
            if (Brightness == brightness)
            {
                return;
            }

            var jsonData = $"{{\"lights\":[{{\"brightness\":{brightness}}}]}}";

            SendPutRequestAsync(jsonData).GetAwaiter().GetResult();

            Brightness = brightness;
        }

        private void SetDeviceTemperature(int temperature)
        {
            if (Temperature == temperature)
            {
                return;
            }

            var jsonData = $"{{\"lights\":[{{\"temperature\":{temperature}}}]}}";

            SendPutRequestAsync(jsonData).GetAwaiter().GetResult();

            Temperature = temperature;
        }

        private void SetDeviceHue(int hue)
        {
            if (Hue == hue)
            {
                return;
            }

            var jsonData = $"{{\"lights\":[{{\"hue\":{hue}}}]}}";

            SendPutRequestAsync(jsonData).GetAwaiter().GetResult();

            Hue = hue;
        }

        private void SetDeviceSaturation(int saturation)
        {
            if (Saturation == saturation)
            {
                return;
            }

            var jsonData = $"{{\"lights\":[{{\"hue\":{saturation}}}]}}";

            SendPutRequestAsync(jsonData).GetAwaiter().GetResult();

            Saturation = saturation;
        }

        public async Task SendPutRequestAsync(string jsonData)
        {
            var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PutAsync(Url, content, CancellationToken);

                response.EnsureSuccessStatusCode();

                await response.Content.ReadAsStringAsync(CancellationToken);
            }
            catch
            {
                // ignored
            }
        }

        private void SetLightData(JObject json)
        {
            var light = json["lights"].First();

            On = light?["on"] != null && (Int32)light["on"] == 1;

            Brightness = light?["brightness"] != null ? (Int32)light["brightness"] : 0;
            Temperature = light?["temperature"] != null ? (Int32)light["temperature"] : 0;
            Hue = light?["hue"] != null ? (Int32)light["hue"] : 0;
            Saturation = light?["saturation"] != null ? (Int32)light["saturation"] : 0;
        }
        
        public void Dispose()
        {
            Dispose(true);
        }
        
        private void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            _cancellationTokenSource.Cancel();
        }
    }
}
