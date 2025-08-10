using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Net.Http;

namespace Agenty.LLMCore.BuiltInTools
{
    public enum SearchEngine
    {
        [Description("DuckDuckGo Instant Answers")]
        DuckDuckGo,
        [Description("Wikipedia Search")]
        Wikipedia,
        [Description("GitHub Repository Search")]
        GitHub,
        [Description("Stack Overflow Questions")]
        StackOverflow
    }

    public enum SearchResultType
    {
        [Description("Quick summary or definition")]
        Summary,
        [Description("Detailed information")]
        Detailed,
        [Description("List of results")]
        List
    }

    public enum WikipediaSection
    {
        [Description("Article summary only")]
        Summary,
        [Description("Full article content")]
        Full,
        [Description("Table of contents")]
        Contents,
        [Description("References and links")]
        References
    }

    public enum GitHubSortBy
    {
        [Description("Best match")]
        BestMatch,
        [Description("Most stars")]
        Stars,
        [Description("Most forks")]
        Forks,
        [Description("Recently updated")]
        Updated
    }

    public enum ContentLanguage
    {
        [Description("English")]
        English,
        [Description("Spanish")]
        Spanish,
        [Description("French")]
        French,
        [Description("German")]
        German,
        [Description("Japanese")]
        Japanese,
        [Description("Chinese")]
        Chinese
    }

    public class SearchResult
    {
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public string Url { get; set; } = "";
        public string Source { get; set; } = "";
        public DateTime LastModified { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();

        public override string ToString() =>
            $"Title: {Title}\n" +
            $"Source: {Source}\n" +
            $"URL: {Url}\n" +
            $"Content: {(Content.Length > 200 ? Content.Substring(0, 200) + "..." : Content)}\n" +
            $"Last Modified: {LastModified:yyyy-MM-dd}\n" +
            new string('-', 50);
    }

    public class SearchRequest
    {
        [Description("Search query text")]
        public string Query { get; set; } = "";

        [Description("Search engine to use")]
        public SearchEngine Engine { get; set; } = SearchEngine.DuckDuckGo;

        [Description("Type of results to return")]
        public SearchResultType ResultType { get; set; } = SearchResultType.Summary;

        [Description("Maximum number of results")]
        public int MaxResults { get; set; } = 5;

        [Description("Content language preference")]
        public ContentLanguage Language { get; set; } = ContentLanguage.English;

        public override string ToString() =>
            $"Search Query: '{Query}' using {Engine} engine, " +
            $"Format: {ResultType}, Max Results: {MaxResults}, Language: {Language}";
    }

    public class WikipediaSearchRequest : SearchRequest
    {
        [Description("Section of Wikipedia article to retrieve")]
        public WikipediaSection Section { get; set; } = WikipediaSection.Summary;

        public override string ToString() =>
            $"Wikipedia Search: '{Query}' (Section: {Section}), " +
            $"Format: {ResultType}, Max Results: {MaxResults}, Language: {Language}";
    }

    public class GitHubSearchRequest : SearchRequest
    {
        [Description("How to sort GitHub results")]
        public GitHubSortBy SortBy { get; set; } = GitHubSortBy.BestMatch;

        [Description("Programming language filter (optional)")]
        public string? LanguageFilter { get; set; }

        public override string ToString() =>
            $"GitHub Search: '{Query}' (Sort: {SortBy}), " +
            $"Language Filter: {LanguageFilter ?? "Any"}, Max Results: {MaxResults}";
    }

    internal class SearchTools
    {
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static SearchTools()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "SearchTool/1.0");
        }

        [Description("Universal search across multiple engines using enums. Returns formatted string results.")]
        public static async Task<string> Search(
            [Description("Search request with engine and options")] SearchRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Query))
                return "Error: Search query cannot be empty";

            try
            {
                var results = await GetSearchResults(request);
                return FormatResults(results, request.ResultType, request.Query, request.Engine.ToString());
            }
            catch (Exception ex)
            {
                return $"Search failed for '{request.Query}' using {request.Engine}: {ex.Message}";
            }
        }

        [Description("Search Wikipedia with specific section options. Returns formatted string.")]
        public static async Task<string> SearchWikipediaAdvanced(
            [Description("Wikipedia search with section control")] WikipediaSearchRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Query))
                return "Error: Search query cannot be empty";

            var langCode = GetLanguageCode(request.Language);

            try
            {
                // First try direct page access
                var results = await GetWikipediaResults(request, langCode);

                if (!results.Any())
                    return $"No Wikipedia results found for '{request.Query}' in {request.Language}";

                return FormatResults(results, request.ResultType, request.Query, "Wikipedia");
            }
            catch (Exception ex)
            {
                return $"Wikipedia search failed for '{request.Query}': {ex.Message}";
            }
        }

        [Description("Search GitHub repositories with sorting options. Returns formatted string.")]
        public static async Task<string> SearchGitHubAdvanced(
            [Description("GitHub search with sorting and language filter")] GitHubSearchRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Query))
                return "Error: Search query cannot be empty";

            try
            {
                var results = await GetGitHubResults(request);

                if (!results.Any())
                    return $"No GitHub repositories found for '{request.Query}'";

                return FormatResults(results, request.ResultType, request.Query, "GitHub");
            }
            catch (Exception ex)
            {
                return $"GitHub search failed for '{request.Query}': {ex.Message}";
            }
        }

        [Description("Quick search with automatic engine selection. Returns formatted string.")]
        public static async Task<string> QuickSearch(
            [Description("Search query")] string query,
            [Description("Result format")] SearchResultType resultType = SearchResultType.Summary)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "Error: Search query cannot be empty";

            var request = new SearchRequest
            {
                Query = query,
                Engine = DetermineOptimalEngine(query),
                ResultType = resultType,
                MaxResults = resultType == SearchResultType.List ? 5 : 1
            };

            return await Search(request);
        }

        [Description("Compare search results across multiple engines. Returns formatted comparison.")]
        public static async Task<string> CompareSearchEngines(
            [Description("Search query")] string query,
            [Description("Engines to compare")] SearchEngine[] engines)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "Error: Search query cannot be empty";

            var sb = new StringBuilder();
            sb.AppendLine($"Search Comparison for: '{query}'");
            sb.AppendLine(new string('=', 60));

            var tasks = engines.Select(async engine =>
            {
                try
                {
                    var request = new SearchRequest
                    {
                        Query = query,
                        Engine = engine,
                        ResultType = SearchResultType.Summary,
                        MaxResults = 1
                    };

                    var result = await Search(request);
                    return $"\n{engine}:\n{result}";
                }
                catch (Exception ex)
                {
                    return $"\n{engine}: Search failed - {ex.Message}";
                }
            });

            var results = await Task.WhenAll(tasks);
            foreach (var result in results)
            {
                sb.AppendLine(result);
                sb.AppendLine(new string('-', 40));
            }

            return sb.ToString();
        }

        private static async Task<List<SearchResult>> GetSearchResults(SearchRequest request)
        {
            return request.Engine switch
            {
                SearchEngine.DuckDuckGo => await SearchDuckDuckGo(request),
                SearchEngine.Wikipedia => await SearchWikipedia(request),
                SearchEngine.GitHub => await SearchGitHub(request),
                SearchEngine.StackOverflow => await SearchStackOverflow(request),
                _ => new List<SearchResult>()
            };
        }

        private static async Task<List<SearchResult>> GetWikipediaResults(WikipediaSearchRequest request, string langCode)
        {
            try
            {
                // Try direct page summary first
                var summaryUrl = $"https://{langCode}.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(request.Query)}";
                var response = await _httpClient.GetStringAsync(summaryUrl);
                var data = JsonNode.Parse(response);

                if (data != null && data["type"]?.ToString() != "disambiguation")
                {
                    var title = data["title"]?.ToString() ?? request.Query;
                    var extract = data["extract"]?.ToString() ?? "No summary available";
                    var url = data["content_urls"]?["desktop"]?["page"]?.ToString() ?? "";

                    return new List<SearchResult>
                    {
                        new SearchResult
                        {
                            Title = title,
                            Content = extract,
                            Url = url,
                            Source = "Wikipedia",
                            LastModified = DateTime.Now,
                            Metadata = new Dictionary<string, string>
                            {
                                ["Language"] = langCode,
                                ["Section"] = request.Section.ToString()
                            }
                        }
                    };
                }
            }
            catch
            {
                // Fall back to search API
            }

            return await FallbackWikipediaSearch(request, langCode);
        }

        private static async Task<List<SearchResult>> GetGitHubResults(GitHubSearchRequest request)
        {
            var query = Uri.EscapeDataString(request.Query);
            var sort = GetGitHubSortParam(request.SortBy);
            var langFilter = !string.IsNullOrEmpty(request.LanguageFilter) ? $"+language:{request.LanguageFilter}" : "";

            var url = $"https://api.github.com/search/repositories?q={query}{langFilter}&sort={sort}&per_page={request.MaxResults}";

            var response = await _httpClient.GetStringAsync(url);
            var data = JsonNode.Parse(response);

            var items = data?["items"]?.AsArray();
            if (items == null) return new List<SearchResult>();

            var results = new List<SearchResult>();
            foreach (var item in items.Take(request.MaxResults))
            {
                var name = item?["name"]?.ToString() ?? "";
                var description = item?["description"]?.ToString() ?? "No description";
                var url_link = item?["html_url"]?.ToString() ?? "";
                var stars = item?["stargazers_count"]?.GetValue<int>() ?? 0;
                var forks = item?["forks_count"]?.GetValue<int>() ?? 0;
                var language = item?["language"]?.ToString() ?? "Unknown";
                var updated = item?["updated_at"]?.ToString() ?? "";

                results.Add(new SearchResult
                {
                    Title = name,
                    Content = $"{description}\nStars: {stars:N0}, Forks: {forks:N0}, Language: {language}",
                    Url = url_link,
                    Source = "GitHub",
                    LastModified = DateTime.TryParse(updated, out var date) ? date : DateTime.MinValue,
                    Metadata = new Dictionary<string, string>
                    {
                        ["Stars"] = stars.ToString(),
                        ["Forks"] = forks.ToString(),
                        ["Language"] = language
                    }
                });
            }

            return results;
        }

        private static async Task<List<SearchResult>> SearchDuckDuckGo(SearchRequest request)
        {
            var url = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(request.Query)}&format=json&no_html=1&skip_disambig=1";

            var response = await _httpClient.GetStringAsync(url);
            var data = JsonNode.Parse(response);

            var results = new List<SearchResult>();

            // Abstract (instant answer)
            var abstract_text = data?["Abstract"]?.ToString();
            var abstract_url = data?["AbstractURL"]?.ToString();

            if (!string.IsNullOrEmpty(abstract_text))
            {
                results.Add(new SearchResult
                {
                    Title = data?["Heading"]?.ToString() ?? request.Query,
                    Content = abstract_text,
                    Url = abstract_url ?? "",
                    Source = "DuckDuckGo Instant Answer",
                    LastModified = DateTime.Now
                });
            }

            // Related topics
            var relatedTopics = data?["RelatedTopics"]?.AsArray();
            if (relatedTopics != null)
            {
                foreach (var topic in relatedTopics.Take(request.MaxResults - results.Count))
                {
                    var text = topic?["Text"]?.ToString();
                    var firstUrl = topic?["FirstURL"]?.ToString();

                    if (!string.IsNullOrEmpty(text))
                    {
                        results.Add(new SearchResult
                        {
                            Title = text.Split('.')[0],
                            Content = text,
                            Url = firstUrl ?? "",
                            Source = "DuckDuckGo",
                            LastModified = DateTime.Now
                        });
                    }
                }
            }

            return results;
        }

        private static async Task<List<SearchResult>> SearchWikipedia(SearchRequest request)
        {
            var wikiRequest = new WikipediaSearchRequest
            {
                Query = request.Query,
                Language = request.Language,
                MaxResults = request.MaxResults,
                ResultType = request.ResultType,
                Section = WikipediaSection.Summary
            };

            return await GetWikipediaResults(wikiRequest, GetLanguageCode(request.Language));
        }

        private static async Task<List<SearchResult>> SearchGitHub(SearchRequest request)
        {
            var gitHubRequest = new GitHubSearchRequest
            {
                Query = request.Query,
                MaxResults = request.MaxResults,
                SortBy = GitHubSortBy.BestMatch,
                ResultType = request.ResultType
            };

            return await GetGitHubResults(gitHubRequest);
        }

        private static async Task<List<SearchResult>> SearchStackOverflow(SearchRequest request)
        {
            var url = $"https://api.stackexchange.com/2.3/search/advanced?order=desc&sort=relevance&q={Uri.EscapeDataString(request.Query)}&site=stackoverflow&pagesize={request.MaxResults}";

            var response = await _httpClient.GetStringAsync(url);
            var data = JsonNode.Parse(response);

            var items = data?["items"]?.AsArray();
            if (items == null) return new List<SearchResult>();

            var results = new List<SearchResult>();
            foreach (var item in items)
            {
                var title = item?["title"]?.ToString() ?? "";
                var tags = item?["tags"]?.AsArray()?.Select(t => t?.ToString()).Where(t => !string.IsNullOrEmpty(t)) ?? Array.Empty<string>();
                var score = item?["score"]?.GetValue<int>() ?? 0;
                var answerCount = item?["answer_count"]?.GetValue<int>() ?? 0;
                var questionUrl = item?["link"]?.ToString() ?? "";
                var creationDate = item?["creation_date"]?.GetValue<long>() ?? 0;

                results.Add(new SearchResult
                {
                    Title = title,
                    Content = $"Score: {score}, Answers: {answerCount}, Tags: {string.Join(", ", tags)}",
                    Url = questionUrl,
                    Source = "Stack Overflow",
                    LastModified = DateTimeOffset.FromUnixTimeSeconds(creationDate).DateTime,
                    Metadata = new Dictionary<string, string>
                    {
                        ["Score"] = score.ToString(),
                        ["AnswerCount"] = answerCount.ToString(),
                        ["Tags"] = string.Join(", ", tags)
                    }
                });
            }

            return results;
        }

        private static SearchEngine DetermineOptimalEngine(string query)
        {
            var lowerQuery = query.ToLowerInvariant();

            if (lowerQuery.Contains("code") || lowerQuery.Contains("programming") || lowerQuery.Contains("error") || lowerQuery.Contains("exception"))
                return SearchEngine.StackOverflow;

            if (lowerQuery.Contains("repository") || lowerQuery.Contains("github") || lowerQuery.Contains("library") || lowerQuery.Contains("framework"))
                return SearchEngine.GitHub;

            if (lowerQuery.StartsWith("what is") || lowerQuery.StartsWith("who is") || lowerQuery.Contains("definition"))
                return SearchEngine.Wikipedia;

            return SearchEngine.DuckDuckGo;
        }

        private static string GetLanguageCode(ContentLanguage language) => language switch
        {
            ContentLanguage.Spanish => "es",
            ContentLanguage.French => "fr",
            ContentLanguage.German => "de",
            ContentLanguage.Japanese => "ja",
            ContentLanguage.Chinese => "zh",
            _ => "en"
        };

        private static string GetGitHubSortParam(GitHubSortBy sortBy) => sortBy switch
        {
            GitHubSortBy.Stars => "stars",
            GitHubSortBy.Forks => "forks",
            GitHubSortBy.Updated => "updated",
            _ => "best-match"
        };

        private static async Task<List<SearchResult>> FallbackWikipediaSearch(WikipediaSearchRequest request, string langCode)
        {
            var searchUrl = $"https://{langCode}.wikipedia.org/w/api.php?action=opensearch&search={Uri.EscapeDataString(request.Query)}&limit={request.MaxResults}&format=json";

            var response = await _httpClient.GetStringAsync(searchUrl);
            var data = JsonNode.Parse(response);
            var array = data?.AsArray();

            if (array == null || array.Count < 4) return new List<SearchResult>();

            var titles = array[1]?.AsArray();
            var descriptions = array[2]?.AsArray();
            var urls = array[3]?.AsArray();

            var results = new List<SearchResult>();
            var count = Math.Min(request.MaxResults, titles?.Count ?? 0);

            for (int i = 0; i < count; i++)
            {
                results.Add(new SearchResult
                {
                    Title = titles?[i]?.ToString() ?? "",
                    Content = descriptions?[i]?.ToString() ?? "No description available",
                    Url = urls?[i]?.ToString() ?? "",
                    Source = "Wikipedia",
                    LastModified = DateTime.Now,
                    Metadata = new Dictionary<string, string>
                    {
                        ["Language"] = langCode,
                        ["SearchType"] = "Fallback"
                    }
                });
            }

            return results;
        }

        private static string FormatResults(List<SearchResult> results, SearchResultType resultType, string query, string source)
        {
            if (!results.Any())
                return $"No results found for '{query}' in {source}";

            var sb = new StringBuilder();

            switch (resultType)
            {
                case SearchResultType.Summary:
                    var first = results.First();
                    sb.AppendLine($"Search Result for '{query}':");
                    sb.AppendLine(new string('=', 40));
                    sb.AppendLine($"Title: {first.Title}");
                    sb.AppendLine($"Source: {first.Source}");
                    sb.AppendLine($"Content: {first.Content}");
                    if (!string.IsNullOrEmpty(first.Url))
                        sb.AppendLine($"URL: {first.Url}");
                    break;

                case SearchResultType.List:
                    sb.AppendLine($"Search Results for '{query}' ({results.Count} found):");
                    sb.AppendLine(new string('=', 50));
                    for (int i = 0; i < results.Count; i++)
                    {
                        var result = results[i];
                        sb.AppendLine($"{i + 1}. {result.Title} ({result.Source})");
                        sb.AppendLine($"   {(result.Content.Length > 100 ? result.Content.Substring(0, 100) + "..." : result.Content)}");
                        if (!string.IsNullOrEmpty(result.Url))
                            sb.AppendLine($"   URL: {result.Url}");
                        sb.AppendLine();
                    }
                    break;

                case SearchResultType.Detailed:
                    sb.AppendLine($"Detailed Search Results for '{query}':");
                    sb.AppendLine(new string('=', 50));
                    foreach (var result in results)
                    {
                        sb.AppendLine($"Title: {result.Title}");
                        sb.AppendLine($"Source: {result.Source}");
                        sb.AppendLine($"Content: {result.Content}");
                        sb.AppendLine($"URL: {result.Url}");
                        sb.AppendLine($"Last Modified: {result.LastModified:yyyy-MM-dd}");

                        if (result.Metadata.Any())
                        {
                            sb.AppendLine("Additional Info:");
                            foreach (var meta in result.Metadata)
                            {
                                sb.AppendLine($"  {meta.Key}: {meta.Value}");
                            }
                        }
                        sb.AppendLine(new string('-', 40));
                    }
                    break;
            }

            return sb.ToString();
        }
    }
}