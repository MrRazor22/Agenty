using HtmlAgilityPack;
using System.Text;

namespace Agenty.RAG.IO
{
    /// <summary>
    /// Static helpers + utilities for common loads.
    /// </summary>
    public static class DocumentLoader
    {
        public static async Task<IReadOnlyList<(string Doc, string Source)>> LoadFilesAsync(IEnumerable<string> paths)
        {
            var loader = new FileLoader();
            var results = new List<(string, string)>();
            foreach (var p in paths)
                results.AddRange(await loader.LoadAsync(p));
            return results;
        }

        public static async Task<IReadOnlyList<(string Doc, string Source)>> LoadDirectoryAsync(
            string directoryPath, string searchPattern = "*.*", bool recursive = true)
        {
            var loader = new DirectoryLoader(searchPattern, recursive);
            return await loader.LoadAsync(directoryPath);
        }

        public static async Task<IReadOnlyList<(string Doc, string Source)>> LoadUrlsAsync(IEnumerable<string> urls, int maxConcurrency = 6)
        {
            var loader = new UrlLoader();
            var sem = new System.Threading.SemaphoreSlim(maxConcurrency);
            var docs = new List<(string, string)>();

            var tasks = urls.Select(async url =>
            {
                await sem.WaitAsync();
                try
                {
                    var r = await loader.LoadAsync(url);
                    if (r.Count > 0) lock (docs) docs.AddRange(r);
                }
                finally { sem.Release(); }
            });

            await Task.WhenAll(tasks);
            return docs;
        }
    }
}
