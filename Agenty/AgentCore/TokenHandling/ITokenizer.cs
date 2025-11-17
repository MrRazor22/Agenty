using SharpToken;
using System;
using System.Collections.Generic;
using System.Text;

namespace Agenty.AgentCore.TokenHandling
{
    public interface ITokenizer
    {
        int Count(string text, string? model = null);
    }

    internal sealed class SharpTokenTokenizer : ITokenizer
    {
        private readonly string _defaultEncoding;

        public SharpTokenTokenizer(string? encoding = null)
        {
            _defaultEncoding = encoding ?? "o200k_base";
        }

        public int Count(string text, string? model = null)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            var encodingName = model ?? _defaultEncoding;

            try
            {
                var encoder = GptEncoding.GetEncodingForModel(encodingName);
                return encoder.Encode(text).Count;
            }
            catch
            {
                try
                {
                    // universal fallback
                    var fallback = GptEncoding.GetEncoding("cl100k_base");
                    return fallback.Encode(text).Count;
                }
                catch
                {
                    // absolute last resort
                    return (int)Math.Ceiling(text.Length / 4.0);
                }
            }
        }
    }
}
