using Agenty.LLMCore.ToolHandling;
using System;
using System.ComponentModel;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Agenty.LLMCore.BuiltInTools
{
    [Description("Temperature Units")]
    public enum TempUnit
    {
        Celsius,
        Fahrenheit
    }

    class WeatherTool
    {
        private static readonly HttpClient _http = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        [Tool]
        [Description("Get simple current weather for a city.")]
        public static async Task<string> GetCurrentWeather(
            [Description("City name")] string city,
            [Description("Temperature unit")] TempUnit unit = TempUnit.Celsius,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var coord = await GetCoordinates(city);
            ct.ThrowIfCancellationRequested();

            if (coord == null) return $"City '{city}' not found.";

            var url = $"https://api.open-meteo.com/v1/forecast" +
                      $"?latitude={coord.Value.lat}&longitude={coord.Value.lon}" +
                      $"&current_weather=true&temperature_unit={GetTempUnit(unit)}";

            var json = JsonNode.Parse(await _http.GetStringAsync(url, ct));
            ct.ThrowIfCancellationRequested();

            var current = json?["current_weather"];
            if (current == null) return "Weather unavailable.";

            var temp = current["temperature"]?.ToString() ?? "?";
            var condition = GetCondition(current["weathercode"]?.GetValue<int>() ?? -1);

            return $"{city}: {temp}°{(unit == TempUnit.Celsius ? "C" : "F")} - {condition}";
        }
        [Tool]
        [Description("Get forecast for multiple days (1–7).")]
        public static async Task<string> GetForecast(
            [Description("City name")] string city,
            [Description("Number of days (1–7)")] int days = 3,
            [Description("Temperature unit")] TempUnit unit = TempUnit.Celsius)
        {
            if (days < 1 || days > 7) return "Days must be 1–7.";

            var coord = await GetCoordinates(city);
            if (coord == null) return $"City '{city}' not found.";

            var url = $"https://api.open-meteo.com/v1/forecast" +
                      $"?latitude={coord.Value.lat}&longitude={coord.Value.lon}" +
                      $"&daily=temperature_2m_max,temperature_2m_min" +
                      $"&temperature_unit={GetTempUnit(unit)}&forecast_days={days}";

            var json = JsonNode.Parse(await _http.GetStringAsync(url));
            var daily = json?["daily"];
            if (daily == null) return "Forecast unavailable.";

            var maxT = daily["temperature_2m_max"]?.AsArray();
            var minT = daily["temperature_2m_min"]?.AsArray();

            var sb = new StringBuilder();
            sb.AppendLine($"{days}-day forecast for {city}:");
            for (int i = 0; i < Math.Min(days, maxT?.Count ?? 0); i++)
            {
                var day = DateTime.Now.AddDays(i).ToString("ddd");
                sb.AppendLine($"{day}: {minT?[i]}–{maxT?[i]}°{(unit == TempUnit.Celsius ? "C" : "F")}");
            }

            return sb.ToString();
        }
        [Tool]
        [Description("Compare current weather across cities.")]
        public static async Task<string> CompareWeather(
            [Description("Array of cities")] string[] cities,
            [Description("Temperature unit")] TempUnit unit = TempUnit.Celsius)
        {
            var sb = new StringBuilder("Weather comparison:\n");
            foreach (var city in cities)
            {
                var res = await GetCurrentWeather(city, unit);
                sb.AppendLine(res);
            }
            return sb.ToString();
        }
        [Tool]
        [Description("Convert temperature between Celsius and Fahrenheit.")]
        public static double ConvertTemperature(
            [Description("Temperature value")] double value,
            [Description("From unit")] TempUnit from,
            [Description("To unit")] TempUnit to)
        {
            if (from == to) return value;
            return from == TempUnit.Celsius
                ? value * 9 / 5 + 32
                : (value - 32) * 5 / 9;
        }

        private static async Task<(double lat, double lon)?> GetCoordinates(string city)
        {
            var geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1";
            var json = JsonNode.Parse(await _http.GetStringAsync(geoUrl));
            var first = json?["results"]?[0];
            if (first == null) return null;
            return (first["latitude"]!.GetValue<double>(), first["longitude"]!.GetValue<double>());
        }

        private static string GetTempUnit(TempUnit u) => u == TempUnit.Fahrenheit ? "fahrenheit" : "celsius";

        private static string GetCondition(int code)
        {
            switch (code)
            {
                case 0: return "Clear sky";
                case 1: return "Mainly clear";
                case 2: return "Partly cloudy";
                case 3: return "Overcast";

                case 45:
                case 48: return "Fog / Depositing rime fog";

                case 51: return "Light drizzle";
                case 53: return "Moderate drizzle";
                case 55: return "Dense drizzle";

                case 56: return "Light freezing drizzle";
                case 57: return "Dense freezing drizzle";

                case 61: return "Slight rain";
                case 63: return "Moderate rain";
                case 65: return "Heavy rain";

                case 66: return "Light freezing rain";
                case 67: return "Heavy freezing rain";

                case 71: return "Slight snow fall";
                case 73: return "Moderate snow fall";
                case 75: return "Heavy snow fall";

                case 77: return "Snow grains";

                case 80: return "Slight rain showers";
                case 81: return "Moderate rain showers";
                case 82: return "Violent rain showers";

                case 85: return "Slight snow showers";
                case 86: return "Heavy snow showers";

                case 95: return "Thunderstorm (slight or moderate)";
                case 96: return "Thunderstorm with slight hail";
                case 99: return "Thunderstorm with heavy hail";

                default: return "";
            }
        }


    }
}
