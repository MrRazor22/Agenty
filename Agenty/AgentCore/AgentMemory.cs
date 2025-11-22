using Agenty.ChatHandling;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Agenty.AgentCore
{
    public interface IAgentMemory
    {
        Task<Conversation> RecallAsync(string sessionId, string userRequest);
        Task UpdateAsync(string sessionId, string userRequest, string response);
    }
    public class FileMemoryOptions
    {
        public string? PersistDir { get; set; }
            = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Agenty");
    }
    public sealed class FileMemory : IAgentMemory
    {
        private readonly FileMemoryOptions _memoryOptions;
        private string? _cachedSessionId;
        private Conversation? _cached;

        public FileMemory(FileMemoryOptions memoryOptions = null)
        {
            _memoryOptions = memoryOptions ?? new FileMemoryOptions();
            Directory.CreateDirectory(_memoryOptions.PersistDir);
        }

        private string GetFilePath(string sessionId) =>
            Path.Combine(_memoryOptions.PersistDir, $"{sessionId}.json");

        public async Task<Conversation> RecallAsync(string sessionId, string userRequest)
        {
            if (_cachedSessionId == sessionId && _cached != null)
                return _cached;

            var file = GetFilePath(sessionId);
            if (!File.Exists(file))
            {
                _cached = new Conversation();
                _cachedSessionId = sessionId;
                return _cached;
            }

            var json = await Task.Run(() => File.ReadAllText(file)).ConfigureAwait(false);
            _cached = JsonConvert.DeserializeObject<Conversation>(json) ?? new Conversation();
            _cachedSessionId = sessionId;
            return _cached;
        }

        public async Task UpdateAsync(string sessionId, string userRequest, string response)
        {
            if (_cachedSessionId != sessionId || _cached == null)
                _cached = await RecallAsync(sessionId, userRequest).ConfigureAwait(false);

            _cached.AddUser(userRequest);
            _cached.AddAssistant(response);

            var file = GetFilePath(sessionId);
            var json = _cached.ToJson();
            await Task.Run(() => File.WriteAllText(file, json)).ConfigureAwait(false);
        }
    }
}