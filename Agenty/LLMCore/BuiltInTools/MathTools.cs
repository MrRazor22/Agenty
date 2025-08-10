using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.LLMCore.BuiltInTools
{
    internal class MathTools
    {
        [Description("Evaluates a math expression using MathJS API.")]
        public static async Task<string> EvaluateMath(
[Description("Mathematical expression (e.g., 2+2*5)")] string expression)
        {
            try
            {
                using var client = new HttpClient();
                var url = $"https://api.mathjs.org/v4/?expr={Uri.EscapeDataString(expression)}";
                var result = await client.GetStringAsync(url);
                return $"{expression} = {result}";
            }
            catch
            {
                return "Failed to evaluate expression.";
            }
        }

        [Description("Generates a random integer in the given range.")]
        public static string RandomInt(
[Description("Minimum value (inclusive)")] int min,
[Description("Maximum value (inclusive)")] int max)
        {
            if (min > max)
                return "Min cannot be greater than max.";

            var rng = new Random();
            var value = rng.Next(min, max + 1);
            return $"Random number between {min} and {max}: {value}";
        }
    }
}
