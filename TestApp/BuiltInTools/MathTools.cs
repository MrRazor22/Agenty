using Agenty.ToolHandling;
using System.ComponentModel;

namespace Agenty.BuiltInTools
{
    class MathTools
    {
        private static readonly HttpClient _httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly ThreadLocal<Random> _random = new ThreadLocal<Random>(() => new Random());

        // DTOs for structured outputs
        public sealed class MathResult
        {
            public string Expression { get; }
            public string Result { get; }

            public MathResult(string expression, string result)
            {
                Expression = expression;
                Result = result;
            }
        }

        public sealed class RandomIntResult
        {
            public int Min { get; }
            public int Max { get; }
            public int Value { get; }

            public RandomIntResult(int min, int max, int value)
            {
                Min = min;
                Max = max;
                Value = value;
            }
        }

        public sealed class RandomIntsResult
        {
            public int Min { get; }
            public int Max { get; }
            public int Count { get; }
            public List<int> Values { get; }

            public RandomIntsResult(int min, int max, int count, List<int> values)
            {
                Min = min;
                Max = max;
                Count = count;
                Values = values;
            }
        }

        public sealed class RandomDecimalResult
        {
            public double Min { get; }
            public double Max { get; }
            public double Value { get; }

            public RandomDecimalResult(double min, double max, double value)
            {
                Min = min;
                Max = max;
                Value = value;
            }
        }

        public sealed class StatisticsResult
        {
            public int Count { get; }
            public double Sum { get; }
            public double Mean { get; }
            public double Median { get; }
            public string Mode { get; }
            public double StdDev { get; }

            public StatisticsResult(int count, double sum, double mean, double median, string mode, double stdDev)
            {
                Count = count;
                Sum = sum;
                Mean = mean;
                Median = median;
                Mode = mode;
                StdDev = stdDev;
            }
        }

        public sealed class ConversionResult
        {
            public string Input { get; }
            public int FromBase { get; }
            public int ToBase { get; }
            public string Output { get; }

            public ConversionResult(string input, int fromBase, int toBase, string output)
            {
                Input = input;
                FromBase = fromBase;
                ToBase = toBase;
                Output = output;
            }
        }

        public sealed class GcdResult
        {
            public List<int> Numbers { get; }
            public int GCD { get; }

            public GcdResult(List<int> numbers, int gcd)
            {
                Numbers = numbers;
                GCD = gcd;
            }
        }

        public sealed class LcmResult
        {
            public List<long> Numbers { get; }
            public long LCM { get; }

            public LcmResult(List<long> numbers, long lcm)
            {
                Numbers = numbers;
                LCM = lcm;
            }
        }

        [Tool]
        [Description("Evaluates a mathematical expression using MathJS API.")]
        public static async Task<MathResult> EvaluateMathAsync(
            [Description("Expression like '2+2*5', 'sqrt(16)', 'sin(pi/2)'.")] string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return new MathResult(expression, "Error: empty");

            var encoded = Uri.EscapeDataString(expression.Trim());
            var url = $"https://api.mathjs.org/v4/?expr={encoded}";

            using var response = await _httpClient.GetAsync(url);
            var result = await response.Content.ReadAsStringAsync();

            return new MathResult(expression, response.IsSuccessStatusCode ? result.Trim() : $"Error: {result}");
        }
        [Tool]
        [Description("Generates a cryptographically secure random integer within the specified range.")]
        public static RandomIntResult GenerateRandomInt(
            [Description("Minimum value (inclusive)")] int min,
            [Description("Maximum value (inclusive)")] int max)
        {
            var value = _random.Value.Next(min, max + 1);
            return new RandomIntResult(min, max, value);
        }
        [Tool]
        [Description("Generates multiple random integers within the specified range.")]
        public static RandomIntsResult GenerateRandomInts(
            int min, int max, [Description("How many numbers to generate.")] int count = 1)
        {
            var rng = _random.Value;
            var values = Enumerable.Range(0, count).Select(_ => rng.Next(min, max + 1)).ToList();
            return new RandomIntsResult(min, max, count, values);
        }
        [Tool]
        [Description("Generates a random decimal number within the specified range with configurable precision.")]
        public static RandomDecimalResult GenerateRandomDecimal(
            double min, double max, [Description("Number of decimal places")] int decimalPlaces = 2)
        {
            var rng = _random.Value;
            var value = Math.Round(min + rng.NextDouble() * (max - min), decimalPlaces);
            return new RandomDecimalResult(min, max, value);
        }
        [Tool]
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

            return new StatisticsResult(list.Count, list.Sum(), mean, median, mode, stdDev);
        }
        [Tool]
        [Description("Converts numbers between bases (binary, octal, decimal, hex).")]
        public static ConversionResult ConvertBase(string number, int fromBase, int toBase)
        {
            var dec = Convert.ToInt64(number, fromBase);
            var result = Convert.ToString(dec, toBase);
            return new ConversionResult(number, fromBase, toBase, result);
        }
        [Tool]
        [Description("Calculates the GCD of integers.")]
        public static GcdResult CalculateGCD(string numbers)
        {
            var list = numbers.Split(',').Select(int.Parse).ToList();
            int gcd = list.Aggregate((a, b) => { while (b != 0) { (a, b) = (b, a % b); } return a; });
            return new GcdResult(list, gcd);
        }
        [Tool]
        [Description("Calculates the LCM of integers.")]
        public static LcmResult CalculateLCM(string numbers)
        {
            var list = numbers.Split(',').Select(long.Parse).ToList();
            long lcm = list.Aggregate((a, b) => Math.Abs(a * b) / Gcd((int)a, (int)b));
            return new LcmResult(list, lcm);
        }

        private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);

        // No Dispose method here – handled by host.
    }
}
