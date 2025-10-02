using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Agenty.LLMCore.ToolHandling
{
    public class Tool
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public JObject ParametersSchema { get; set; }

        [JsonIgnore]
        public Delegate? Function { get; set; }

        [JsonIgnore]
        public List<string> Tags { get; set; } = new List<string>();

        public override string ToString()
        {
            var props = ParametersSchema?["properties"] as JObject;

            var args = props != null
                ? string.Join(", ", props.Properties().Select(p => p.Name))
                : "";

            var argPart = args.Length > 0 ? $"({args})" : "()";

            return !string.IsNullOrWhiteSpace(Description)
                ? $"{Name}{argPart} => {Description}"
                : $"{Name}{argPart}";
        }
    }
}
