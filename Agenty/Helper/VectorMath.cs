using SharpToken;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.Helper
{
    public static class RAGHelper
    {
        public static double CosineSimilarity(IReadOnlyList<float> v1, IReadOnlyList<float> v2)
        {
            if (v1.Count != v2.Count)
                throw new ArgumentException("Vectors must have the same length");

            double dot = 0, norm1 = 0, norm2 = 0;
            for (int i = 0; i < v1.Count; i++)
            {
                dot += v1[i] * v2[i];
                norm1 += v1[i] * v1[i];
                norm2 += v2[i] * v2[i];
            }

            if (norm1 == 0 || norm2 == 0)
                return 0; // avoid NaN if one vector is zero

            return dot / (Math.Sqrt(norm1) * Math.Sqrt(norm2));
        }

        public static IEnumerable<string> Chunk(string text, int maxTokens, int overlap, string model)
        {
            var encoder = GptEncoding.GetEncodingForModel(model);
            var tokens = encoder.Encode(text);

            int start = 0;
            while (start < tokens.Count)
            {
                var end = Math.Min(start + maxTokens, tokens.Count);
                var chunkTokens = tokens.GetRange(start, end - start);
                yield return encoder.Decode(chunkTokens);
                start += maxTokens - overlap;
            }
        }

        public static int CountTokens(string text, string model = "gpt-3.5-turbo")
        {
            var encoder = GptEncoding.GetEncodingForModel(model);
            return encoder.Encode(text).Count;
        }
    }

}
