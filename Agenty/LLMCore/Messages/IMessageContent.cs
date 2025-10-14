using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace Agenty.LLMCore.Messages
{
    public interface IMessageContent { }

    public sealed class TextContent : IMessageContent
    {
        public string Text { get; }

        public TextContent(string text) => Text = text;

        public static implicit operator TextContent(string text) => new TextContent(text);
    }

    public class ToolCall : IMessageContent
    {
        [JsonConstructor]
        public ToolCall(string id, string name, JObject arguments)
        {
            Id = id;
            Name = name;
            Arguments = arguments ?? new JObject();
        }

        public ToolCall(string id, string name, JObject arguments, object[] parameters, string message = null)
            : this(id, name, arguments)
        {
            Parameters = parameters;
            Message = message;
        }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; private set; }

        [JsonProperty("arguments")]
        public JObject Arguments { get; private set; }

        [JsonIgnore]
        public object[] Parameters { get; private set; } = Array.Empty<object>();

        [JsonIgnore]
        public string Message { get; set; }

        // message-only ctor
        public ToolCall(string message) : this(Guid.NewGuid().ToString(), "", new JObject())
        {
            Message = message;
        }

        public static ToolCall Empty { get; } = new ToolCall(Guid.NewGuid().ToString(), "", new JObject());

        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(Name) &&
            string.IsNullOrWhiteSpace(Message);

        public override string ToString()
        {
            var argsStr = (Arguments != null && Arguments.Count > 0)
                ? string.Join(", ", Arguments.Properties().Select(p => $"{p.Name}: {p.Value}"))
                : "none";

            return $"Name: '{Name}' (id: {Id}) with Arguments: [{argsStr}]";
        }
    }

    public sealed class ToolCallResult : IMessageContent
    {
        public ToolCallResult(ToolCall call, object result, Exception error)
        {
            Call = call;
            Result = result;
            Error = error;
        }

        public ToolCall Call { get; }
        public object? Result { get; }
        public Exception? Error { get; }
    }
}
