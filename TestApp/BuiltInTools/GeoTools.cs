using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Agenty.LLMCore.BuiltInTools
{
    public class CountryInfo
    {
        public string Name { get; set; } = "";
        public string Capital { get; set; } = "";
        public string Region { get; set; } = "";
        public long Population { get; set; }
        public string[] Languages { get; set; } = Array.Empty<string>();
        public string CurrencyName { get; set; } = "";
        public string CurrencySymbol { get; set; } = "";
        public string Flag { get; set; } = "";
        public double Area { get; set; }
        public string[] Borders { get; set; } = Array.Empty<string>();

        // Human readable only, not part of schema
        public override string ToString() =>
            $"{Name} {Flag}\n" +
            $"Capital: {Capital}\n" +
            $"Region: {Region}\n" +
            $"Population: {Population:N0}\n" +
            $"Languages: {string.Join(", ", Languages)}\n" +
            $"Currency: {CurrencyName} ({CurrencySymbol})\n" +
            $"Area: {Area:N0} km²\n" +
            $"Borders: {(Borders.Length > 0 ? string.Join(", ", Borders) : "None")}";
    }
    class GeoTools
    {
        private static readonly HttpClient _http = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static GeoTools()
        {
            _http.DefaultRequestHeaders.Add("User-Agent", "Agenty.LLMCore/1.0");
        }

        [Description("Get information about any country by name.")]
        public static async Task<CountryInfo> GetCountryInfo(
            [Description("Country name (e.g., 'India', 'United States', 'UK')")] string countryName)
        {
            if (string.IsNullOrWhiteSpace(countryName))
                throw new ArgumentException("Country name cannot be empty");

            var url = $"https://restcountries.com/v3.1/name/{Uri.EscapeDataString(countryName.Trim())}";
            var json = await _http.GetStringAsync(url);
            var countries = JsonNode.Parse(json)?.AsArray();
            if (countries == null || countries.Count == 0)
                throw new Exception($"Country '{countryName}' not found.");

            return ParseCountry(countries[0]);
        }

        [Description("Search for countries by region (e.g., 'Europe', 'Asia').")]
        public static async Task<List<CountryInfo>> GetCountriesByRegion(
            [Description("Region name")] string region)
        {
            var url = $"https://restcountries.com/v3.1/region/{Uri.EscapeDataString(region)}";
            var json = await _http.GetStringAsync(url);
            var arr = JsonNode.Parse(json)?.AsArray() ?? throw new Exception("No countries found");
            return arr.Select(ParseCountry).ToList();
        }

        private static CountryInfo ParseCountry(JsonNode? data)
        {
            if (data == null) return new CountryInfo();

            return new CountryInfo
            {
                Name = data["name"]?["common"]?.ToString() ?? "Unknown",
                Capital = data["capital"]?.AsArray()?.FirstOrDefault()?.ToString() ?? "N/A",
                Region = data["region"]?.ToString() ?? "N/A",
                Population = data["population"]?.GetValue<long>() ?? 0,
                Languages = data["languages"]?.AsObject()?
                            .Select(kvp => kvp.Value?.ToString() ?? "")
                            .Where(s => !string.IsNullOrEmpty(s))
                            .ToArray()
                            ?? Array.Empty<string>(),
                CurrencyName = data["currencies"]?.AsObject()?.First().Value?["name"]?.ToString() ?? "N/A",
                CurrencySymbol = data["currencies"]?.AsObject()?.First().Value?["symbol"]?.ToString() ?? "",
                Flag = data["flag"]?.ToString() ?? "",
                Area = data["area"]?.GetValue<double>() ?? 0,
                Borders = data["borders"]?.AsArray()?.Select(b => b?.ToString() ?? "").ToArray()
                    ?? Array.Empty<string>()
            };
        }
    }

}
