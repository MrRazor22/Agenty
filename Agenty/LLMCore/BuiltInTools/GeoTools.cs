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
    public enum Country
    {
        [Description("India")]
        India,
        [Description("United States")]
        USA,
        [Description("United Kingdom")]
        UK,
        [Description("Germany")]
        Germany,
        [Description("Japan")]
        Japan,
        [Description("Australia")]
        Australia
    }

    public class CountryInfo
    {
        public string Name { get; set; } = "";
        public string Capital { get; set; } = "";
        public string Region { get; set; } = "";
        public long Population { get; set; }
        public string[] Languages { get; set; } = Array.Empty<string>();
        public string CurrencyName { get; set; } = "";
        public string CurrencySymbol { get; set; } = "";

        public override string ToString() =>
            $"{Name} - Capital: {Capital}, Region: {Region}, Population: {Population:N0}, Languages: {string.Join(", ", Languages)}, Currency: {CurrencyName} ({CurrencySymbol})";
    }
    internal class GeoTools
    {
        [Description("Get information about a country.")]
        public static async Task<CountryInfo> GetCountryInfo(
        [Description("Select country")] Country country)
        {
            var url = "https://restcountries.com/v3.1/all";
            try
            {
                using var client = new HttpClient();
                var json = await client.GetStringAsync(url);
                var countries = JsonNode.Parse(json)?.AsArray();

                if (countries == null)
                    throw new Exception("Failed to parse country data.");

                string targetName = country switch
                {
                    Country.India => "India",
                    Country.USA => "United States",
                    Country.UK => "United Kingdom",
                    Country.Germany => "Germany",
                    Country.Japan => "Japan",
                    Country.Australia => "Australia",
                    _ => throw new ArgumentException("Unsupported country")
                };

                var countryData = countries.FirstOrDefault(c =>
                    c["name"]?["common"]?.ToString().Equals(targetName, StringComparison.OrdinalIgnoreCase) == true);

                if (countryData == null)
                    throw new Exception($"Country data for {targetName} not found.");

                // Capital is an array of strings, get first or "N/A"
                string capital = "N/A";
                var capitalArray = countryData["capital"]?.AsArray();
                if (capitalArray != null && capitalArray.Count > 0)
                    capital = capitalArray[0]?.ToString() ?? "N/A";

                var languagesNode = countryData["languages"]?.AsObject();
                string[] languages = languagesNode?.Select(kvp => kvp.Value?.ToString() ?? "")
                                                   .Where(s => !string.IsNullOrEmpty(s))
                                                   .ToArray() ?? Array.Empty<string>();

                var currenciesNode = countryData["currencies"]?.AsObject();
                string currencyName = "N/A";
                string currencySymbol = "";

                if (currenciesNode != null && currenciesNode.Count > 0)
                {
                    var firstCurrency = currenciesNode.First();
                    currencyName = firstCurrency.Key;
                    currencySymbol = firstCurrency.Value?["symbol"]?.ToString() ?? "";
                }

                return new CountryInfo
                {
                    Name = countryData["name"]?["common"]?.ToString() ?? targetName,
                    Capital = capital,
                    Region = countryData["region"]?.ToString() ?? "N/A",
                    Population = countryData["population"]?.GetValue<long>() ?? 0,
                    Languages = languages,
                    CurrencyName = currencyName,
                    CurrencySymbol = currencySymbol
                };

            }
            catch (Exception ex)
            {
                return new CountryInfo
                {
                    Name = country.ToString(),
                    Capital = "N/A",
                    Region = "N/A",
                    Population = 0,
                    Languages = Array.Empty<string>(),
                    CurrencyName = "N/A",
                    CurrencySymbol = "",
                };
            }
        }
    }
}
