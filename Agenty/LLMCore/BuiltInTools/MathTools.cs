using System.ComponentModel;

namespace Agenty.LLMCore.BuiltInTools
{
    class MathTools
    {
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly ThreadLocal<Random> _random = new(() => new Random());

        // DTOs for structured outputs
        public record MathResult(string Expression, string Result);
        public record RandomIntResult(int Min, int Max, int Value);
        public record RandomIntsResult(int Min, int Max, int Count, List<int> Values);
        public record RandomDecimalResult(double Min, double Max, double Value);
        public record StatisticsResult(int Count, double Sum, double Mean, double Median, string Mode, double StdDev);
        public record ConversionResult(string Input, int FromBase, int ToBase, string Output);
        public record GcdResult(List<int> Numbers, int GCD);
        public record LcmResult(List<long> Numbers, long LCM);

        [Description("Evaluates a mathematical expression using MathJS API.")]
        public static async Task<MathResult> EvaluateMathAsync(
            [Description("Expression like '2+2*5', 'sqrt(16)', 'sin(pi/2)'.")] string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return new(expression, "Error: empty");

            var encoded = Uri.EscapeDataString(expression.Trim());
            var url = $"https://api.mathjs.org/v4/?expr={encoded}";

            using var response = await _httpClient.GetAsync(url);
            var result = await response.Content.ReadAsStringAsync();

            return new(expression, response.IsSuccessStatusCode ? result.Trim() : $"Error: {result}");
        }

        [Description("Generates a cryptographically secure random integer within the specified range.")]
        public static RandomIntResult GenerateRandomInt(
            [Description("Minimum value (inclusive)")] int min,
            [Description("Maximum value (inclusive)")] int max)
        {
            var value = _random.Value.Next(min, max + 1);
            return new(min, max, value);
        }

        [Description("Generates multiple random integers within the specified range.")]
        public static RandomIntsResult GenerateRandomInts(
            int min, int max, [Description("How many numbers to generate.")] int count = 1)
        {
            var rng = _random.Value;
            var values = Enumerable.Range(0, count).Select(_ => rng.Next(min, max + 1)).ToList();
            return new(min, max, count, values);
        }

        [Description("Generates a random decimal number within the specified range with configurable precision.")]
        public static RandomDecimalResult GenerateRandomDecimal(
            double min, double max, [Description("Number of decimal places")] int decimalPlaces = 2)
        {
            var rng = _random.Value;
            var value = Math.Round(min + rng.NextDouble() * (max - min), decimalPlaces);
            return new(min, max, value);
        }

        [Description("Calculates mean, median, mode, std dev for a list of numbers.")]
        public static StatisticsResult CalculateStatistics(
            [Description("Comma-separated numbers like '1,2,3'")] string numbers)
        {
            var list = numbers.Split(',').Select(double.Parse).ToList();
            var mean = list.Average();
            var median = list.Count % 2 == 0 ? (list.OrderBy(x => x).ElementAt(list.Count / 2 - 1) + list.OrderBy(x => x).ElementAt(list.Count / 2)) / 2 : list.OrderBy(x => x).ElementAt(list.Count / 2);
            var groups = list.GroupBy(x => x).ToList();
            var maxFreq = groups.Max(g => g.Count());
            var mode = maxFreq == 1 ? "No mode" : string.Join(",", groups.Where(g => g.Count() == maxFreq).Select(g => g.Key));
            var variance = list.Sum(x => Math.Pow(x - mean, 2)) / list.Count;
            var stdDev = Math.Sqrt(variance);

            return new(list.Count, list.Sum(), mean, median, mode, stdDev);
        }

        [Description("Converts numbers between bases (binary, octal, decimal, hex).")]
        public static ConversionResult ConvertBase(string number, int fromBase, int toBase)
        {
            var dec = Convert.ToInt64(number, fromBase);
            var result = Convert.ToString(dec, toBase);
            return new(number, fromBase, toBase, result);
        }

        [Description("Calculates the GCD of integers.")]
        public static GcdResult CalculateGCD(string numbers)
        {
            var list = numbers.Split(',').Select(int.Parse).ToList();
            int gcd = list.Aggregate((a, b) => { while (b != 0) { (a, b) = (b, a % b); } return a; });
            return new(list, gcd);
        }

        [Description("Calculates the LCM of integers.")]
        public static LcmResult CalculateLCM(string numbers)
        {
            var list = numbers.Split(',').Select(long.Parse).ToList();
            long lcm = list.Aggregate((a, b) => Math.Abs(a * b) / Gcd((int)a, (int)b));
            return new(list, lcm);
        }

        private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);

        // No Dispose method here – handled by host.
    }
}
