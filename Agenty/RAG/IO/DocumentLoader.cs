using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Agenty.RAG.IO
{
    /// <summary>
    /// Static helpers for common document loading scenarios.
    /// </summary>
    public static class DocumentLoader
    {
        public static async Task<IReadOnlyList<Document>> LoadFilesAsync(
            IEnumerable<string> paths,
            CancellationToken cancellationToken = default)
        {
            var loader = new FileLoader();
            var results = new List<Document>();

            foreach (var p in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.AddRange(await loader.LoadAsync(p));
            }

            return results;
        }

        public static async Task<IReadOnlyList<Document>> LoadDirectoryAsync(
            string directoryPath,
            string searchPattern = "*.*",
            bool recursive = true,
            CancellationToken cancellationToken = default)
        {
            var loader = new DirectoryLoader(searchPattern, recursive);
            cancellationToken.ThrowIfCancellationRequested();
            return await loader.LoadAsync(directoryPath);
        }

        public static async Task<IReadOnlyList<Document>> LoadUrlsAsync(
            IEnumerable<string> urls,
            int maxConcurrency = 6,
            CancellationToken cancellationToken = default)
        {
            var loader = new UrlLoader();
            var sem = new SemaphoreSlim(maxConcurrency);
            var docs = new List<Document>();

            var tasks = urls.Select(async url =>
            {
                await sem.WaitAsync(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var r = await loader.LoadAsync(url);
                    if (r.Count > 0)
                    {
                        lock (docs) docs.AddRange(r);
                    }
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);
            return docs;
        }
    }
}
