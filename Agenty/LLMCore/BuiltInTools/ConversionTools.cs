using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Agenty.LLMCore.BuiltInTools
{
    internal class ConversionTools
    {
        [Description("Converts currency amount and returns detailed result.")]
        public static async Task<CurrencyConversionResult> ConvertCurrency(
        [Description("Currency conversion request")] CurrencyConversionRequest request)
        {
            var result = new CurrencyConversionResult
            {
                From = request.From.ToUpperInvariant(),
                To = request.To.ToUpperInvariant(),
                Amount = request.Amount,
                Success = false
            };

            try
            {
                using var client = new HttpClient();
                var url = $"https://open.er-api.com/v6/latest/{result.From}";
                var response = await client.GetStringAsync(url);

                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (root.TryGetProperty("result", out var resultProp) &&
                    resultProp.GetString() == "success" &&
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
                    result.ErrorMessage = root.GetProperty("error-type").GetString() ?? "Unknown error";
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        [Description("Converts a local time in a given timezone to UTC.")]
        public static string ConvertToUtc(
[Description("Time in local format (yyyy-MM-dd HH:mm)")] string localTime,
[Description("Timezone ID (e.g., Asia/Kolkata)")] string timezone)
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

        public override string ToString()
        {
            if (Success)
            {
                return $"{Amount} {From} = {ConvertedAmount:F4} {To} (Rate: {Rate:F6}, Retrieved: {TimestampUtc:u})";
            }
            else
            {
                return $"Conversion failed: {ErrorMessage ?? "Unknown error"}";
            }
        }
    }

}
