using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Agenty.LLMCore.Messages
{
    public interface IMessageContent { }
    public sealed record TextContent(string Text) : IMessageContent
    {
        public static implicit operator TextContent(string text) => new(text);
    }
    public class ToolCall : IMessageContent
    {
        [JsonConstructor] // 👈 tells serializer to use this
        public ToolCall(string id, string name, JsonObject arguments)
        {
            Id = id;
            Name = name;
            Arguments = arguments;
        }

        public ToolCall(string id, string name, JsonObject arguments, object?[]? parameters = null, string? message = null)
            : this(id, name, arguments)
        {
            Parameters = parameters;
            Message = message;
        }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; private set; }

        [JsonPropertyName("arguments")]
        public JsonObject Arguments { get; private set; }

        [JsonIgnore]
        public object?[]? Parameters { get; private set; }

        [JsonIgnore]
        public string? Message { get; set; }

        // message-only ctor stays fine
        public ToolCall(string message) : this("", "", new JsonObject()) => Message = message;

        public static ToolCall Empty { get; } = new("", "", new JsonObject());

        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(Name) &&
            string.IsNullOrWhiteSpace(Message);

        public override string ToString()
        {
            var argsStr = Arguments is { Count: > 0 }
                ? string.Join(", ", Arguments.Select(kvp => $"{kvp.Key}: {kvp.Value?.ToJsonString()}"))
                : "none";

            return $"Name: '{Name}' (id: {Id}) with Arguments: [{argsStr}]";
        }
    }
    public sealed record ToolCallResult(ToolCall Call, object? Result, Exception? Error) : IMessageContent;
}
