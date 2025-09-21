using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Agenty.LLMCore.ToolHandling
{
    public class Tool
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public JsonObject ParametersSchema { get; set; }
        [JsonIgnore] public Delegate? Function { get; set; }
        [JsonIgnore] public List<string> Tags { get; set; } = new();
        public override string ToString()
        {
            var props = ParametersSchema?["properties"]?.AsObject();
            var args = props != null
                ? string.Join(", ", props.Select(p => p.Key))
                : "";

            var argPart = args.Length > 0 ? $"({args})" : "()";

            return !string.IsNullOrWhiteSpace(Description)
                ? $"{Name}{argPart} => {Description}"
                : $"{Name}{argPart}";
            //return Name;
        }
    }
}
