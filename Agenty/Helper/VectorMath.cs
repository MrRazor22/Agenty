using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.Helper
{
    public static class VectorMath
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
    }

}
