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
using System.Text.RegularExpressions;
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

            // normalize whitespace: collapse tabs/newlines/multiple spaces into a single space
            var normalized = Regex.Replace(text, @"\s+", " ").Trim();

            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(normalized)));
        }

    }
}
