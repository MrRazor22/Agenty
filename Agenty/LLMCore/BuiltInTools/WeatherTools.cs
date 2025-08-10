using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Agenty.LLMCore.BuiltInTools
{
    public class WeatherTool
    {

        [Description("Gets weather forecast with detailed options.")]
        public static async Task<string> FetchWeather(
        [Description("Complex weather request")] WeatherRequest request)
        {
            if (request.Options.Days < 1 || request.Options.Days > 7)
                return "Days must be between 1 and 7.";

            string city = request.Location.City;
            string country = request.Location.CountryCode ?? "";

            using var client = new HttpClient();

            // Geocode
            var geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1";
            if (!string.IsNullOrEmpty(country))
                geoUrl += $"&countrycode={country}";

            var geoResponse = await client.GetStringAsync(geoUrl);
            var geo = JsonNode.Parse(geoResponse);
            var results = geo?["results"];
            if (results == null || results.AsArray().Count == 0)
                return $"City '{city}' not found.";

            var loc = results[0];
            var lat = loc?["latitude"]?.GetValue<double>() ?? double.NaN;
            var lon = loc?["longitude"]?.GetValue<double>() ?? double.NaN;

            if (double.IsNaN(lat) || double.IsNaN(lon))
                return "Failed to determine coordinates.";

            // Prepare API call for forecast
            var tempUnit = request.Options.TemperatureUnit.ToLower() == "fahrenheit" ? "fahrenheit" : "celsius";
            var days = request.Options.Days;

            var weatherUrl = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current_weather=true&temperature_unit={tempUnit}&daily=temperature_2m_max,temperature_2m_min&forecast_days={days}";

            var weatherResponse = await client.GetStringAsync(weatherUrl);
            var weather = JsonNode.Parse(weatherResponse);

            // Compose output string
            var sb = new StringBuilder();
            sb.AppendLine($"Weather forecast for {city}, {country}:");

            var current = weather?["current_weather"];
            if (current != null)
            {
                var temp = current["temperature"]?.ToString() ?? "?";
                sb.AppendLine($"Current temperature: {temp}°{tempUnit[0]}");
                if (request.Options.IncludeWind)
                {
                    var wind = current["windspeed"]?.ToString() ?? "?";
                    sb.AppendLine($"Wind speed: {wind} km/h");
                }
            }

            var daily = weather?["daily"];
            if (daily != null)
            {
                var maxTemps = daily["temperature_2m_max"]?.AsArray();
                var minTemps = daily["temperature_2m_min"]?.AsArray();

                if (maxTemps != null && minTemps != null)
                {
                    sb.AppendLine("Daily forecast:");
                    for (int i = 0; i < days; i++)
                    {
                        sb.AppendLine($"Day {i + 1}: Max {maxTemps[i]}, Min {minTemps[i]} °{tempUnit[0]}");
                    }
                }
            }

            return sb.ToString();
        }
    }

    public class Location
    {
        [Description("City name")]
        public string City { get; set; } = "";

        [Description("Optional: Country code (e.g., US, IN)")]
        public string? CountryCode { get; set; }
    }

    public class ForecastOptions
    {
        [Description("Temperature unit (Celsius or Fahrenheit)")]
        public string TemperatureUnit { get; set; } = "Celsius";

        [Description("Whether to include wind info")]
        public bool IncludeWind { get; set; } = true;

        [Description("Number of forecast days (1-7)")]
        public int Days { get; set; } = 1;
    }

    public class WeatherRequest
    {
        [Description("Location info")]
        public Location Location { get; set; } = new();

        [Description("Forecast options")]
        public ForecastOptions Options { get; set; } = new();
    }
}
