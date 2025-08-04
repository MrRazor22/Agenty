using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.Utilities
{
    public class Util
    {
        public static bool IsSimpleType(Type type)
        {
            return type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type.IsEnum;
        }
    }
}
