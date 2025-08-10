using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Agenty.LLMCore.BuiltInTools
{
    public enum TemperatureUnit
    {
        [Description("Celsius (°C)")]
        Celsius,
        [Description("Fahrenheit (°F)")]
        Fahrenheit
    }

    public enum WindSpeedUnit
    {
        [Description("Kilometers per hour")]
        KmH,
        [Description("Miles per hour")]
        Mph,
        [Description("Meters per second")]
        Ms,
        [Description("Knots")]
        Kn
    }

    public enum PrecipitationUnit
    {
        [Description("Millimeters")]
        Mm,
        [Description("Inches")]
        Inch
    }

    public enum WeatherDetail
    {
        [Description("Basic temperature only")]
        Basic,
        [Description("Temperature and wind")]
        Standard,
        [Description("All weather details including precipitation, humidity, UV")]
        Detailed
    }

    public class WeatherTool
    {
        [Description("Gets weather forecast with detailed options using enums for better type safety.")]
        public static async Task<string> FetchWeather(
            [Description("Complex weather request with enum options")] WeatherRequest request)
        {
            if (request.Options.Days < 1 || request.Options.Days > 16)
                return "Days must be between 1 and 16.";

            string city = request.Location.City;
            string country = request.Location.CountryCode ?? "";

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            try
            {
                // Geocode with better error handling
                var coordinates = await GetCoordinates(client, city, country);
                if (coordinates == null)
                    return $"City '{city}' not found. Please check spelling or try with country code.";

                // Get weather data
                var weatherData = await GetWeatherData(client, coordinates.Value.lat, coordinates.Value.lon, request.Options);
                if (weatherData == null)
                    return "Failed to retrieve weather data.";

                // Format response
                return FormatWeatherResponse(weatherData, city, country, request.Options);
            }
            catch (Exception ex)
            {
                return $"Error fetching weather: {ex.Message}";
            }
        }

        [Description("Get simple current weather for a city.")]
        public static async Task<string> GetCurrentWeather(
            [Description("City name")] string city,
            [Description("Temperature unit")] TemperatureUnit temperatureUnit = TemperatureUnit.Celsius,
            [Description("Optional country code")] string? countryCode = null)
        {
            var request = new WeatherRequest
            {
                Location = new Location { City = city, CountryCode = countryCode },
                Options = new ForecastOptions
                {
                    TemperatureUnit = temperatureUnit,
                    Days = 1,
                    DetailLevel = WeatherDetail.Standard
                }
            };

            return await FetchWeather(request);
        }

        [Description("Compare weather between multiple cities.")]
        public static async Task<string> CompareWeather(
            [Description("Array of city names")] string[] cities,
            [Description("Temperature unit for comparison")] TemperatureUnit temperatureUnit = TemperatureUnit.Celsius)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Weather Comparison");
            sb.AppendLine(new string('=', 50));

            var tasks = cities.Select(async city =>
            {
                try
                {
                    var result = await GetCurrentWeather(city, temperatureUnit);
                    return $"\n {city.ToUpper()}:\n{result}";
                }
                catch
                {
                    return $"\n {city}: Unable to fetch weather data";
                }
            });

            var results = await Task.WhenAll(tasks);
            foreach (var result in results)
            {
                sb.AppendLine(result);
            }

            return sb.ToString();
        }

        [Description("Convert temperature between units.")]
        public static double ConvertTemperature(
            [Description("Temperature value")] double temperature,
            [Description("Source unit")] TemperatureUnit fromUnit,
            [Description("Target unit")] TemperatureUnit toUnit)
        {
            if (fromUnit == toUnit) return temperature;

            return (fromUnit, toUnit) switch
            {
                (TemperatureUnit.Celsius, TemperatureUnit.Fahrenheit) => temperature * 9 / 5 + 32,
                (TemperatureUnit.Fahrenheit, TemperatureUnit.Celsius) => (temperature - 32) * 5 / 9,
                _ => temperature
            };
        }

        private static async Task<(double lat, double lon)?> GetCoordinates(HttpClient client, string city, string country)
        {
            var geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=en";
            if (!string.IsNullOrEmpty(country))
                geoUrl += $"&country={Uri.EscapeDataString(country)}";

            var geoResponse = await client.GetStringAsync(geoUrl);
            var geo = JsonNode.Parse(geoResponse);
            var results = geo?["results"];

            if (results == null || results.AsArray().Count == 0)
                return null;

            var loc = results[0];
            var lat = loc?["latitude"]?.GetValue<double>();
            var lon = loc?["longitude"]?.GetValue<double>();

            if (!lat.HasValue || !lon.HasValue)
                return null;

            return (lat.Value, lon.Value);
        }

        private static async Task<JsonNode?> GetWeatherData(HttpClient client, double lat, double lon, ForecastOptions options)
        {
            var tempUnit = options.TemperatureUnit == TemperatureUnit.Fahrenheit ? "fahrenheit" : "celsius";
            var windUnit = GetWindSpeedUnit(options.WindSpeedUnit);
            var precipUnit = options.PrecipitationUnit == PrecipitationUnit.Inch ? "inch" : "mm";

            var weatherUrl = $"https://api.open-meteo.com/v1/forecast?" +
                           $"latitude={lat}&longitude={lon}&" +
                           $"current_weather=true&" +
                           $"temperature_unit={tempUnit}&" +
                           $"windspeed_unit={windUnit}&" +
                           $"precipitation_unit={precipUnit}&" +
                           $"forecast_days={options.Days}";

            // Add parameters based on detail level
            switch (options.DetailLevel)
            {
                case WeatherDetail.Basic:
                    weatherUrl += "&daily=temperature_2m_max,temperature_2m_min";
                    break;
                case WeatherDetail.Standard:
                    weatherUrl += "&daily=temperature_2m_max,temperature_2m_min,windspeed_10m_max";
                    break;
                case WeatherDetail.Detailed:
                    weatherUrl += "&daily=temperature_2m_max,temperature_2m_min,windspeed_10m_max,precipitation_sum,relative_humidity_2m,uv_index_max" +
                                "&hourly=temperature_2m,relative_humidity_2m,precipitation_probability";
                    break;
            }

            var weatherResponse = await client.GetStringAsync(weatherUrl);
            return JsonNode.Parse(weatherResponse);
        }

        private static string GetWindSpeedUnit(WindSpeedUnit unit) => unit switch
        {
            WindSpeedUnit.Mph => "mph",
            WindSpeedUnit.Ms => "ms",
            WindSpeedUnit.Kn => "kn",
            _ => "kmh"
        };

        private static string FormatWeatherResponse(JsonNode weather, string city, string country, ForecastOptions options)
        {
            var sb = new StringBuilder();
            var tempSymbol = options.TemperatureUnit == TemperatureUnit.Fahrenheit ? "°F" : "°C";
            var windUnit = GetWindUnitDisplay(options.WindSpeedUnit);

            sb.AppendLine($"Weather for {city}{(!string.IsNullOrEmpty(country) ? $", {country}" : "")}");
            sb.AppendLine(new string('─', 50));

            // Current weather
            var current = weather?["current_weather"];
            if (current != null)
            {
                var temp = current["temperature"]?.ToString() ?? "?";
                var code = current["weathercode"]?.GetValue<int>() ?? 0;
                var condition = GetWeatherCondition(code);

                sb.AppendLine($"Current: {temp}{tempSymbol} - {condition}");

                if (options.DetailLevel >= WeatherDetail.Standard)
                {
                    var wind = current["windspeed"]?.ToString() ?? "?";
                    var direction = current["winddirection"]?.GetValue<int>() ?? 0;
                    var directionText = GetWindDirection(direction);
                    sb.AppendLine($"Wind: {wind} {windUnit} {directionText}");
                }
            }

            // Daily forecast
            var daily = weather?["daily"];
            if (daily != null && options.Days > 1)
            {
                sb.AppendLine($"\n{options.Days}-Day Forecast:");

                var maxTemps = daily["temperature_2m_max"]?.AsArray();
                var minTemps = daily["temperature_2m_min"]?.AsArray();
                var winds = daily["windspeed_10m_max"]?.AsArray();
                var precipitation = daily["precipitation_sum"]?.AsArray();
                var uvIndex = daily["uv_index_max"]?.AsArray();

                for (int i = 0; i < Math.Min(options.Days, maxTemps?.Count ?? 0); i++)
                {
                    var dayName = i == 0 ? "Today" : DateTime.Now.AddDays(i).ToString("ddd");
                    var max = maxTemps?[i]?.ToString() ?? "?";
                    var min = minTemps?[i]?.ToString() ?? "?";

                    sb.Append($"  {dayName}: {max}/{min}{tempSymbol}");

                    if (options.DetailLevel == WeatherDetail.Detailed)
                    {
                        var wind = winds?[i]?.ToString() ?? "?";
                        var precip = precipitation?[i]?.ToString() ?? "?";
                        var uv = uvIndex?[i]?.ToString() ?? "?";
                        var precipUnit = options.PrecipitationUnit == PrecipitationUnit.Inch ? "in" : "mm";

                        sb.Append($", Wind: {wind} {windUnit}, Rain: {precip} {precipUnit}, UV: {uv}");
                    }

                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        private static string GetWindUnitDisplay(WindSpeedUnit unit) => unit switch
        {
            WindSpeedUnit.Mph => "mph",
            WindSpeedUnit.Ms => "m/s",
            WindSpeedUnit.Kn => "kn",
            _ => "km/h"
        };

        private static string GetWeatherCondition(int code) => code switch
        {
            0 => "Clear sky",
            1 => "Mainly clear",
            2 => "Partly cloudy",
            3 => "Overcast",
            45 or 48 => "Foggy",
            51 or 53 or 55 => "Drizzle",
            61 or 63 or 65 => "Rain",
            71 or 73 or 75 => "Snow",
            77 => "Snow grains",
            80 or 81 or 82 => "Rain showers",
            85 or 86 => "Snow showers",
            95 => "Thunderstorm",
            96 or 99 => "Thunderstorm with hail",
            _ => "Unknown"
        };

        private static string GetWindDirection(int degrees) => degrees switch
        {
            >= 0 and < 23 => "N ⬆️",
            >= 23 and < 68 => "NE ↗️",
            >= 68 and < 113 => "E",
            >= 113 and < 158 => "SE",
            >= 158 and < 203 => "S",
            >= 203 and < 248 => "SW",
            >= 248 and < 293 => "W",
            >= 293 and < 338 => "NW",
            >= 338 => "N",
            _ => ""
        };
    }

    public class Location
    {
        [Description("City name")]
        public string City { get; set; } = "";

        [Description("Optional: Country code or name (e.g., US, IN, India)")]
        public string? CountryCode { get; set; }
    }

    public class ForecastOptions
    {
        [Description("Temperature unit")]
        public TemperatureUnit TemperatureUnit { get; set; } = TemperatureUnit.Celsius;

        [Description("Wind speed unit")]
        public WindSpeedUnit WindSpeedUnit { get; set; } = WindSpeedUnit.KmH;

        [Description("Precipitation unit")]
        public PrecipitationUnit PrecipitationUnit { get; set; } = PrecipitationUnit.Mm;

        [Description("Level of weather detail to include")]
        public WeatherDetail DetailLevel { get; set; } = WeatherDetail.Standard;

        [Description("Number of forecast days (1-16)")]
        public int Days { get; set; } = 1;
    }

    public class WeatherRequest
    {
        [Description("Location information")]
        public Location Location { get; set; } = new();

        [Description("Forecast options with enum selections")]
        public ForecastOptions Options { get; set; } = new();
    }
}