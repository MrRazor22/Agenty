using System;
using System.ComponentModel;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Agenty.LLMCore.BuiltInTools
{
    public enum TempUnit
    {
        [Description("Celsius (°C)")] Celsius,
        [Description("Fahrenheit (°F)")] Fahrenheit
    }

    class WeatherTool
    {
        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        [Description("Get simple current weather for a city.")]
        public static async Task<string> GetCurrentWeather(
            [Description("City name")] string city,
            [Description("Temperature unit")] TempUnit unit = TempUnit.Celsius)
        {
            var coord = await GetCoordinates(city);
            if (coord == null) return $"City '{city}' not found.";

            var url = $"https://api.open-meteo.com/v1/forecast" +
                      $"?latitude={coord.Value.lat}&longitude={coord.Value.lon}" +
                      $"&current_weather=true&temperature_unit={GetTempUnit(unit)}";

            var json = JsonNode.Parse(await _http.GetStringAsync(url));
            var current = json?["current_weather"];
            if (current == null) return "Weather unavailable.";

            var temp = current["temperature"]?.ToString() ?? "?";
            var condition = GetCondition(current["weathercode"]?.GetValue<int>() ?? -1);

            return $"{city}: {temp}°{(unit == TempUnit.Celsius ? "C" : "F")} - {condition}";
        }

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

        private static string GetCondition(int code) => code switch
        {
            0 => "Clear",
            1 or 2 => "Partly cloudy",
            3 => "Overcast",
            45 or 48 => "Fog",
            51 or 53 or 55 => "Drizzle",
            61 or 63 or 65 => "Rain",
            71 or 73 or 75 => "Snow",
            95 => "Thunderstorm",
            _ => "Unknown"
        };
    }
}
