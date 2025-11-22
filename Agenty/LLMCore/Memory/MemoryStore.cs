using Agenty.LLMCore.ChatHandling;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Memory
{
    /// <summary>
    /// Persistence layer for agent long-term memory.
    /// Implementations can store to file, database, Redis, etc.
    /// </summary>
    public interface IMemoryStore
    {
        Task SaveAsync(string sessionId, Conversation conversation);
        Task<Conversation?> LoadAsync(string sessionId);
        Task DeleteAsync(string sessionId);
        Task<bool> ExistsAsync(string sessionId);
    }

    /// <summary>
    /// File-based memory store. Stores each session as a JSON file.
    /// </summary>
    public sealed class FileMemoryStore : IMemoryStore
    {
        private readonly string _basePath;

        public FileMemoryStore(string? persistDir = null)
        {
            _basePath = persistDir ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Agenty");
            Directory.CreateDirectory(_basePath);
        }

        private string GetFilePath(string sessionId) =>
            Path.Combine(_basePath, $"{sessionId}.json");

        public async Task SaveAsync(string sessionId, Conversation conversation)
        {
            var file = GetFilePath(sessionId);
            var json = conversation.ToJson();
            await Task.Run(() => File.WriteAllText(file, json)).ConfigureAwait(false);
        }

        public async Task<Conversation?> LoadAsync(string sessionId)
        {
            var file = GetFilePath(sessionId);
            if (!File.Exists(file)) return null;

            var json = await Task.Run(() => File.ReadAllText(file)).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<Conversation>(json);
        }

        public Task DeleteAsync(string sessionId)
        {
            var file = GetFilePath(sessionId);
            if (File.Exists(file))
                File.Delete(file);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string sessionId)
        {
            return Task.FromResult(File.Exists(GetFilePath(sessionId)));
        }
    }

    /// <summary>
    /// No-op store for stateless agents or testing.
    /// </summary>
    public sealed class NullMemoryStore : IMemoryStore
    {
        public static NullMemoryStore Instance { get; } = new NullMemoryStore();

        public Task SaveAsync(string sessionId, Conversation conversation) => Task.CompletedTask;
        public Task<Conversation?> LoadAsync(string sessionId) => Task.FromResult<Conversation?>(null);
        public Task DeleteAsync(string sessionId) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string sessionId) => Task.FromResult(false);
    }
}