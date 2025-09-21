using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agenty.LLMCore.ChatHandling
{
    public interface IConversationStore
    {
        Task SaveAsync(string sessionId, Conversation conversation);
        Task<Conversation?> LoadAsync(string sessionId);
        Task AppendAsync(string sessionId, Chat chat);
    }

    public sealed class FileConversationStore : IConversationStore
    {
        private readonly string _basePath;
        private readonly JsonSerializerOptions _jsonOptions;

        public FileConversationStore(string? persistDir = null)
        {
            var baseDir = persistDir ??
               Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Agenty");

            _basePath = baseDir;
            Directory.CreateDirectory(_basePath);

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            };
        }

        private string GetFilePath(string sessionId) =>
            Path.Combine(_basePath, $"{sessionId}.json");

        public async Task SaveAsync(string sessionId, Conversation conversation)
        {
            var file = GetFilePath(sessionId);
            var json = JsonSerializer.Serialize(conversation, _jsonOptions);
            await File.WriteAllTextAsync(file, json);
        }

        public async Task<Conversation?> LoadAsync(string sessionId)
        {
            var file = GetFilePath(sessionId);
            if (!File.Exists(file)) return null;

            var json = await File.ReadAllTextAsync(file);
            return JsonSerializer.Deserialize<Conversation>(json, _jsonOptions);
        }

        public async Task AppendAsync(string sessionId, Chat chat)
        {
            var conv = await LoadAsync(sessionId) ?? new Conversation();
            conv.Add(chat.Role, chat.Content);
            await SaveAsync(sessionId, conv);
        }
    }
}
