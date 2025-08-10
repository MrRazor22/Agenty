using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
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

    internal class GeoTools
    {
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static GeoTools()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "CountryInfoApp/1.0");
        }

        [Description("Get information about any country by name. Supports partial names, common names, and official names.")]
        public static async Task<CountryInfo> GetCountryInfo(
            [Description("Country name (e.g., 'India', 'United States', 'UK', 'Deutschland', etc.)")]
        string countryName)
        {
            if (string.IsNullOrWhiteSpace(countryName))
                throw new ArgumentException("Country name cannot be empty");

            // First try with the exact name using fullText=true
            var exactUrl = $"https://restcountries.com/v3.1/name/{Uri.EscapeDataString(countryName.Trim())}?fullText=true";

            try
            {
                var result = await TryGetCountryFromUrl(exactUrl);
                if (result != null) return result;
            }
            catch
            {
                // If exact match fails, try partial match
            }

            // Try partial name search (without fullText parameter)
            var partialUrl = $"https://restcountries.com/v3.1/name/{Uri.EscapeDataString(countryName.Trim())}";

            try
            {
                var result = await TryGetCountryFromUrl(partialUrl);
                if (result != null) return result;
            }
            catch
            {
                // If partial match fails, try alternative search methods
            }

            // Try searching by alternative spellings or codes
            var alternativeResult = await TryAlternativeSearch(countryName.Trim());
            if (alternativeResult != null) return alternativeResult;

            // If all searches fail, return error info
            throw new Exception($"Country '{countryName}' not found. Please check the spelling or try a different name format.");
        }

        [Description("Get information about multiple countries at once.")]
        public static async Task<List<CountryInfo>> GetMultipleCountriesInfo(
            [Description("Array of country names")]
        string[] countryNames)
        {
            var tasks = countryNames.Select(name => GetCountryInfo(name));
            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }

        [Description("Search for countries by region (e.g., 'Europe', 'Asia', 'Americas').")]
        public static async Task<List<CountryInfo>> GetCountriesByRegion(
            [Description("Region name (Europe, Asia, Africa, Americas, Oceania)")]
        string region)
        {
            var url = $"https://restcountries.com/v3.1/region/{Uri.EscapeDataString(region)}";

            try
            {
                var json = await _httpClient.GetStringAsync(url);
                var countries = JsonNode.Parse(json)?.AsArray();

                if (countries == null)
                    throw new Exception($"No countries found for region '{region}'");

                var results = new List<CountryInfo>();
                foreach (var countryData in countries)
                {
                    var countryInfo = ParseCountryData(countryData);
                    if (countryInfo != null)
                        results.Add(countryInfo);
                }

                return results;
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Failed to fetch countries for region '{region}': {ex.Message}", ex);
            }
        }

        private static async Task<CountryInfo?> TryGetCountryFromUrl(string url)
        {
            try
            {
                var json = await _httpClient.GetStringAsync(url);
                var countries = JsonNode.Parse(json)?.AsArray();

                if (countries == null || countries.Count == 0)
                    return null;

                // Return the first match
                return ParseCountryData(countries[0]);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<CountryInfo?> TryAlternativeSearch(string countryName)
        {
            // Try common alternative names
            var alternatives = GetAlternativeNames(countryName.ToLowerInvariant());

            foreach (var alternative in alternatives)
            {
                try
                {
                    var url = $"https://restcountries.com/v3.1/name/{Uri.EscapeDataString(alternative)}";
                    var result = await TryGetCountryFromUrl(url);
                    if (result != null) return result;
                }
                catch
                {
                    continue;
                }
            }

            // Try searching by country code if it's 2-3 characters
            if (countryName.Length >= 2 && countryName.Length <= 3)
            {
                try
                {
                    var codeUrl = $"https://restcountries.com/v3.1/alpha/{countryName.ToUpperInvariant()}";
                    return await TryGetCountryFromUrl(codeUrl);
                }
                catch
                {
                    // Ignore code search failures
                }
            }

            return null;
        }

        private static string[] GetAlternativeNames(string countryName)
        {
            var alternatives = new Dictionary<string, string[]>
            {
                ["usa"] = new[] { "United States", "United States of America", "US" },
                ["us"] = new[] { "United States", "United States of America", "USA" },
                ["uk"] = new[] { "United Kingdom", "Britain", "Great Britain" },
                ["britain"] = new[] { "United Kingdom", "UK" },
                ["deutschland"] = new[] { "Germany" },
                ["españa"] = new[] { "Spain" },
                ["france"] = new[] { "France" },
                ["italia"] = new[] { "Italy" },
                ["nippon"] = new[] { "Japan" },
                ["россия"] = new[] { "Russia" },
                ["中国"] = new[] { "China" },
                ["भारत"] = new[] { "India" }
            };

            return alternatives.ContainsKey(countryName) ? alternatives[countryName] : new[] { countryName };
        }

        private static CountryInfo? ParseCountryData(JsonNode? countryData)
        {
            if (countryData == null) return null;

            try
            {
                // Capital handling
                string capital = "N/A";
                var capitalArray = countryData["capital"]?.AsArray();
                if (capitalArray != null && capitalArray.Count > 0)
                    capital = capitalArray[0]?.ToString() ?? "N/A";

                // Languages handling
                var languagesNode = countryData["languages"]?.AsObject();
                string[] languages = languagesNode?.Select(kvp => kvp.Value?.ToString() ?? "")
                                                  .Where(s => !string.IsNullOrEmpty(s))
                                                  .ToArray() ?? Array.Empty<string>();

                // Currency handling
                var currenciesNode = countryData["currencies"]?.AsObject();
                string currencyName = "N/A";
                string currencySymbol = "";

                if (currenciesNode != null && currenciesNode.Count > 0)
                {
                    var firstCurrency = currenciesNode.First();
                    currencyName = firstCurrency.Value?["name"]?.ToString() ?? firstCurrency.Key;
                    currencySymbol = firstCurrency.Value?["symbol"]?.ToString() ?? "";
                }

                // Borders handling
                var bordersArray = countryData["borders"]?.AsArray();
                string[] borders = bordersArray?.Select(b => b?.ToString() ?? "")
                                              .Where(s => !string.IsNullOrEmpty(s))
                                              .ToArray() ?? Array.Empty<string>();

                return new CountryInfo
                {
                    Name = countryData["name"]?["common"]?.ToString() ?? "Unknown",
                    Capital = capital,
                    Region = countryData["region"]?.ToString() ?? "N/A",
                    Population = countryData["population"]?.GetValue<long>() ?? 0,
                    Languages = languages,
                    CurrencyName = currencyName,
                    CurrencySymbol = currencySymbol,
                    Flag = countryData["flag"]?.ToString() ?? "",
                    Area = countryData["area"]?.GetValue<double>() ?? 0,
                    Borders = borders
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing country data: {ex.Message}");
                return null;
            }
        }
    }
}
