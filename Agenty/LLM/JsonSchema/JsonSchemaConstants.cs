using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.LLMCore.JsonSchema
{
    public static class JsonSchemaConstants
    { // Key constants
        public const string TypeKey = "type";
        public const string PropertiesKey = "properties";
        public const string RequiredKey = "required";
        public const string DescriptionKey = "description";
        public const string EnumKey = "enum";
        public const string FormatKey = "format";
        public const string MinLengthKey = "minLength";
        public const string MaxLengthKey = "maxLength";
        public const string PatternKey = "pattern";
        public const string AdditionalPropertiesKey = "additionalProperties";
        public const string ItemsKey = "items";
    }
}
