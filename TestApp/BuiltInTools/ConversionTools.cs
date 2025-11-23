using Agenty.Tools;
using System.ComponentModel;
using System.Text.Json;

namespace Agenty.LLMCore.BuiltInTools
{
    class ConversionTools
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        [Tool]
        [Description("Converts currency amount using live exchange rates and returns a detailed result.")]
        public static async Task<CurrencyConversionResult> ConvertCurrency(
            [Description("Currency conversion request with source, target, and amount.")]
            CurrencyConversionRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.From) || string.IsNullOrWhiteSpace(request.To))
            {
                return new CurrencyConversionResult
                {
                    Success = false,
                    ErrorMessage = "From/To currency codes cannot be empty."
                };
            }

            var result = new CurrencyConversionResult
            {
                From = request.From.ToUpperInvariant(),
                To = request.To.ToUpperInvariant(),
                Amount = request.Amount,
                Success = false
            };

            try
            {
                var url = $"https://open.er-api.com/v6/latest/{result.From}";
                var response = await _httpClient.GetStringAsync(url);

                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (root.TryGetProperty("result", out var status) &&
                    status.GetString() == "success" &&
                    root.TryGetProperty("rates", out var rates) &&
                    rates.TryGetProperty(result.To, out var toRate))
                {
                    result.Rate = toRate.GetDecimal();
                    result.ConvertedAmount = result.Amount * result.Rate;
                    result.TimestampUtc = DateTime.UtcNow;
                    result.Success = true;
                }
                else
                {
                    result.ErrorMessage = root.TryGetProperty("error-type", out var errorProp)
                        ? errorProp.GetString()
                        : "Unknown error from exchange API.";
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Conversion failed: {ex.Message}";
            }

            return result;
        }
        [Tool]
        [Description("Converts a local time in a given timezone to UTC.")]
        public static string ConvertToUtc(
            [Description("Local time string in format yyyy-MM-dd HH:mm")] string localTime,
            [Description("Timezone ID, e.g., 'Asia/Kolkata' or 'America/New_York'")] string timezone)
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
                var local = DateTime.Parse(localTime);
                var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
                return $"{localTime} in {timezone} = {utc:yyyy-MM-dd HH:mm} UTC";
            }
            catch (Exception ex)
            {
                return $"Conversion failed: {ex.Message}";
            }
        }
    }

    public class CurrencyConversionRequest
    {
        [Description("Currency code to convert from (e.g., USD)")]
        public string From { get; set; } = "";

        [Description("Currency code to convert to (e.g., EUR)")]
        public string To { get; set; } = "";

        [Description("Amount to convert")]
        public decimal Amount { get; set; } = 1m;
    }

    public class CurrencyConversionResult
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public decimal Amount { get; set; }
        public decimal ConvertedAmount { get; set; }
        public decimal Rate { get; set; }
        public DateTime TimestampUtc { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }

        public override string ToString() =>
            Success
                ? $"{Amount} {From} = {ConvertedAmount:F4} {To} (Rate: {Rate:F6}, Retrieved: {TimestampUtc:u})"
                : $"Conversion failed: {ErrorMessage ?? "Unknown error"}";
    }
}
