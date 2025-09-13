// File: RAGHelper.cs
using Agenty.AgentCore.TokenHandling;
using HtmlAgilityPack;
using SharpToken;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.RAG
{
    public static class RAGHelper
    {
        public static double CosineSimilarity(float[] v1, float[] v2)
        {
            if (v1.Length != v2.Length) return 0.0;

            double dot = 0, norm1 = 0, norm2 = 0;
            for (int i = 0; i < v1.Length; i++)
            {
                dot += v1[i] * v2[i];
                norm1 += v1[i] * v1[i];
                norm2 += v2[i] * v2[i];
            }
            return (norm1 == 0 || norm2 == 0) ? 0 : dot / (Math.Sqrt(norm1) * Math.Sqrt(norm2));
        }

        public static float[] Normalize(float[] v)
        {
            var norm = Math.Sqrt(v.Sum(x => x * x));
            return norm == 0 ? v : v.Select(x => (float)(x / norm)).ToArray();
        }
        public static string ComputeId(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }

        public static IEnumerable<string> SplitByParagraphs(string text) =>
            text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p));

        public static IEnumerable<string> Chunk(string text, int maxTokens, int overlap, ITokenizer tokenizer)
        {
            var tokens = tokenizer.Encode(text);

            for (int start = 0; start < tokens.Count; start += maxTokens - overlap)
            {
                var end = Math.Min(start + maxTokens, tokens.Count);
                var chunkTokens = tokens.Skip(start).Take(end - start);
                yield return tokenizer.Decode(chunkTokens);
            }
        }
    }
}
