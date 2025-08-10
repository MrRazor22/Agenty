using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Agenty.LLMCore.BuiltInTools
{
    internal class MathTools
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly ThreadLocal<Random> _random = new ThreadLocal<Random>(() => new Random());

        static MathTools()
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Agenty.LLMCore/1.0");
        }

        [Description("Evaluates a mathematical expression using MathJS API. Supports basic arithmetic, functions, constants, and complex expressions.")]
        public static async Task<string> EvaluateMathAsync(
            [Description("Mathematical expression (e.g., '2+2*5', 'sqrt(16)', 'sin(pi/2)', '2^3')")] string expression,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return "Error: Expression cannot be empty.";

            try
            {
                var encodedExpression = Uri.EscapeDataString(expression.Trim());
                var url = $"https://api.mathjs.org/v4/?expr={encodedExpression}";

                using var response = await _httpClient.GetAsync(url, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsStringAsync(cancellationToken);
                    return $"{expression} = {result.Trim()}";
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    return $"Error evaluating '{expression}': {response.StatusCode} - {errorContent}";
                }
            }
            catch (TaskCanceledException)
            {
                return $"Error: Request timed out while evaluating '{expression}'.";
            }
            catch (HttpRequestException ex)
            {
                return $"Error: Network error while evaluating '{expression}': {ex.Message}";
            }
            catch (Exception ex)
            {
                return $"Error: Failed to evaluate '{expression}': {ex.Message}";
            }
        }

        [Description("Generates a cryptographically secure random integer within the specified range.")]
        public static string GenerateRandomInt(
            [Description("Minimum value (inclusive)")] int min,
            [Description("Maximum value (inclusive)")] int max)
        {
            if (min > max)
                return $"Error: Minimum value ({min}) cannot be greater than maximum value ({max}).";

            if (min == max)
                return $"Random number between {min} and {max}: {min}";

            try
            {
                var value = _random.Value.Next(min, max + 1);
                return $"Random number between {min} and {max}: {value}";
            }
            catch (ArgumentException ex)
            {
                return $"Error: Invalid range [{min}, {max}]: {ex.Message}";
            }
        }

        [Description("Generates multiple random integers within the specified range.")]
        public static string GenerateRandomInts(
            [Description("Minimum value (inclusive)")] int min,
            [Description("Maximum value (inclusive)")] int max,
            [Description("Number of random integers to generate")] int count = 1)
        {
            if (min > max)
                return $"Error: Minimum value ({min}) cannot be greater than maximum value ({max}).";

            if (count <= 0)
                return "Error: Count must be greater than 0.";

            if (count > 1000)
                return "Error: Count cannot exceed 1000 for performance reasons.";

            try
            {
                var values = new List<int>(count);
                var rng = _random.Value;

                for (int i = 0; i < count; i++)
                {
                    values.Add(rng.Next(min, max + 1));
                }

                var valuesStr = string.Join(", ", values);
                return $"{count} random number{(count > 1 ? "s" : "")} between {min} and {max}: [{valuesStr}]";
            }
            catch (Exception ex)
            {
                return $"Error generating random numbers: {ex.Message}";
            }
        }

        [Description("Generates a random decimal number within the specified range with configurable precision.")]
        public static string GenerateRandomDecimal(
            [Description("Minimum value (inclusive)")] double min,
            [Description("Maximum value (exclusive)")] double max,
            [Description("Number of decimal places (default: 2)")] int decimalPlaces = 2)
        {
            if (min >= max)
                return $"Error: Minimum value ({min}) must be less than maximum value ({max}).";

            if (decimalPlaces < 0 || decimalPlaces > 10)
                return "Error: Decimal places must be between 0 and 10.";

            try
            {
                var rng = _random.Value;
                var value = min + (rng.NextDouble() * (max - min));
                var roundedValue = Math.Round(value, decimalPlaces);

                return $"Random decimal between {min} and {max}: {roundedValue.ToString($"F{decimalPlaces}", CultureInfo.InvariantCulture)}";
            }
            catch (Exception ex)
            {
                return $"Error generating random decimal: {ex.Message}";
            }
        }

        [Description("Calculates basic statistical measures (mean, median, mode, std dev) for a list of numbers.")]
        public static string CalculateStatistics(
            [Description("Comma-separated list of numbers (e.g., '1,2,3,4,5')")] string numbers)
        {
            if (string.IsNullOrWhiteSpace(numbers))
                return "Error: Numbers list cannot be empty.";

            try
            {
                var numberList = numbers.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Select(double.Parse)
                    .ToList();

                if (!numberList.Any())
                    return "Error: No valid numbers found.";

                var count = numberList.Count;
                var sum = numberList.Sum();
                var mean = sum / count;

                // Median
                var sorted = numberList.OrderBy(x => x).ToList();
                var median = count % 2 == 0
                    ? (sorted[count / 2 - 1] + sorted[count / 2]) / 2
                    : sorted[count / 2];

                // Mode
                var frequencyGroups = numberList.GroupBy(x => x).ToList();
                var maxFrequency = frequencyGroups.Max(g => g.Count());
                var modes = frequencyGroups.Where(g => g.Count() == maxFrequency).Select(g => g.Key).ToList();
                var modeStr = maxFrequency == 1 ? "No mode" : string.Join(", ", modes);

                // Standard deviation
                var variance = numberList.Sum(x => Math.Pow(x - mean, 2)) / count;
                var stdDev = Math.Sqrt(variance);

                return $"Statistics for [{string.Join(", ", numberList)}]:\n" +
                       $"Count: {count}\n" +
                       $"Sum: {sum:F2}\n" +
                       $"Mean: {mean:F2}\n" +
                       $"Median: {median:F2}\n" +
                       $"Mode: {modeStr}\n" +
                       $"Standard Deviation: {stdDev:F2}";
            }
            catch (FormatException)
            {
                return "Error: Invalid number format. Please use comma-separated decimal numbers.";
            }
            catch (Exception ex)
            {
                return $"Error calculating statistics: {ex.Message}";
            }
        }

        [Description("Converts numbers between different bases (binary, octal, decimal, hexadecimal).")]
        public static string ConvertBase(
            [Description("Number to convert")] string number,
            [Description("Source base (2, 8, 10, or 16)")] int fromBase,
            [Description("Target base (2, 8, 10, or 16)")] int toBase)
        {
            var validBases = new[] { 2, 8, 10, 16 };

            if (!validBases.Contains(fromBase) || !validBases.Contains(toBase))
                return "Error: Only bases 2, 8, 10, and 16 are supported.";

            if (string.IsNullOrWhiteSpace(number))
                return "Error: Number cannot be empty.";

            try
            {
                // Convert from source base to decimal
                var decimalValue = Convert.ToInt64(number.Trim(), fromBase);

                // Convert from decimal to target base
                var result = Convert.ToString(decimalValue, toBase);

                var fromBaseName = GetBaseName(fromBase);
                var toBaseName = GetBaseName(toBase);

                return $"{number} ({fromBaseName}) = {result} ({toBaseName})";
            }
            catch (FormatException)
            {
                return $"Error: '{number}' is not a valid number in base {fromBase}.";
            }
            catch (OverflowException)
            {
                return $"Error: Number '{number}' is too large to convert.";
            }
            catch (Exception ex)
            {
                return $"Error converting base: {ex.Message}";
            }
        }

        private static string GetBaseName(int baseValue) => baseValue switch
        {
            2 => "binary",
            8 => "octal",
            10 => "decimal",
            16 => "hexadecimal",
            _ => $"base-{baseValue}"
        };

        [Description("Calculates the greatest common divisor (GCD) of two or more integers.")]
        public static string CalculateGCD(
            [Description("Comma-separated list of integers (e.g., '12,18,24')")] string numbers)
        {
            if (string.IsNullOrWhiteSpace(numbers))
                return "Error: Numbers list cannot be empty.";

            try
            {
                var numberList = numbers.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Select(s => Math.Abs(int.Parse(s))) // Use absolute values
                    .Where(n => n > 0) // Exclude zeros
                    .ToList();

                if (numberList.Count < 2)
                    return "Error: At least two positive integers are required.";

                var gcd = numberList.Aggregate(GCD);
                return $"GCD of [{string.Join(", ", numberList)}] = {gcd}";
            }
            catch (FormatException)
            {
                return "Error: Invalid integer format. Please use comma-separated integers.";
            }
            catch (OverflowException)
            {
                return "Error: One or more numbers are too large.";
            }
            catch (Exception ex)
            {
                return $"Error calculating GCD: {ex.Message}";
            }
        }

        [Description("Calculates the least common multiple (LCM) of two or more integers.")]
        public static string CalculateLCM(
            [Description("Comma-separated list of integers (e.g., '4,6,8')")] string numbers)
        {
            if (string.IsNullOrWhiteSpace(numbers))
                return "Error: Numbers list cannot be empty.";

            try
            {
                var numberList = numbers.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Select(s => Math.Abs(long.Parse(s))) // Use long for larger results
                    .Where(n => n > 0) // Exclude zeros
                    .ToList();

                if (numberList.Count < 2)
                    return "Error: At least two positive integers are required.";

                var lcm = numberList.Aggregate(LCM);
                return $"LCM of [{string.Join(", ", numberList)}] = {lcm}";
            }
            catch (FormatException)
            {
                return "Error: Invalid integer format. Please use comma-separated integers.";
            }
            catch (OverflowException)
            {
                return "Error: Result is too large or one of the input numbers is too large.";
            }
            catch (Exception ex)
            {
                return $"Error calculating LCM: {ex.Message}";
            }
        }

        private static int GCD(int a, int b)
        {
            while (b != 0)
            {
                var temp = b;
                b = a % b;
                a = temp;
            }
            return a;
        }

        private static long LCM(long a, long b)
        {
            return Math.Abs(a * b) / GCD((int)a, (int)b);
        }

        // Cleanup resources
        public static void Dispose()
        {
            _httpClient?.Dispose();
            _random?.Dispose();
        }
    }
}