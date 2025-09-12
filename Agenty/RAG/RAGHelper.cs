// File: RAGHelper.cs
using Agenty.AgentCore;
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
