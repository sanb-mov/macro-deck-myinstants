using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Serilog;

namespace SuchByte.MacroDeck.Soundboard;

/// <summary>
/// Reads the trending sounds of myinstants.com. The site has no public API, so the buttons are
/// scraped from the trending page. The result is cached to keep the load on the site low.
/// </summary>
public static partial class MyInstantsService
{
    public const string Host = "www.myinstants.com";

    private const string BaseUrl = "https://www.myinstants.com/en";
    /// <summary>
    /// Sounds per page on myinstants.com
    /// </summary>
    private const int SitePageSize = 36;

    private const int MaxPage = 200;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    private static readonly ILogger Logger = Log.ForContext(typeof(MyInstantsService));
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly SemaphoreSlim FetchLock = new(1, 1);

    // One entry per "query + page", so paging back and forth does not hit the site again
    private static readonly Dictionary<string, (DateTime Time, IReadOnlyList<SoundItem> Sounds)> PageCache = new();

    // Every sound ever listed, so a PLAY_SOUND id can be resolved no matter which page it came from
    private static readonly Dictionary<string, SoundItem> Known = new();

    // <button ... onclick="play('/media/sounds/x.mp3', 'loader-1', 'x-123')" ...> ... <a href="/en/instant/x-123/" class="instant-link ...">Name</a>
    [GeneratedRegex(
        """play\('(?<path>/media/sounds/[^'"]+?\.mp3)'\s*,\s*'[^']*'\s*,\s*'(?<id>[^']+)'\)[\s\S]{0,1500}?class="instant-link[^"]*"[^>]*>(?<name>[^<]+)<""",
        RegexOptions.IgnoreCase)]
    private static partial Regex SoundRegex();

    /// <summary>
    /// Returns one page of <paramref name="pageSize"/> sounds of the trending list, or of the search results
    /// when <paramref name="query"/> is set. The pages of the site have a fixed size, so a page can span
    /// several of them. The sounds are laid out over the pages automatically.
    /// </summary>
    public static async Task<SoundPage> GetSoundPageAsync(string? query, int page, int pageSize,
        bool forceRefresh = false)
    {
        pageSize = Math.Clamp(pageSize, 1, SoundboardSettings.MaxRows * SoundboardSettings.MaxColumns);
        var offset = (Math.Max(page, 1) - 1) * pageSize;

        var sitePage = offset / SitePageSize + 1;
        var skip = offset % SitePageSize;
        var collected = new List<SoundItem>();

        // One sound more than the page needs tells whether there is a next page
        while (collected.Count - skip < pageSize + 1 && sitePage <= MaxPage)
        {
            var sounds = await GetSitePageAsync(query, sitePage++, forceRefresh);
            collected.AddRange(sounds);
            if (sounds.Count < SitePageSize)
            {
                break;
            }
        }

        var shown = collected.Skip(skip).Take(pageSize).ToList();
        return new SoundPage(shown, collected.Count - skip > pageSize);
    }

    private static async Task<IReadOnlyList<SoundItem>> GetSitePageAsync(string? query,
        int page,
        bool forceRefresh)
    {
        query = query?.Trim();
        page = Math.Clamp(page, 1, MaxPage);
        var key = $"{query}|{page}";

        await FetchLock.WaitAsync();
        try
        {
            if (!forceRefresh && PageCache.TryGetValue(key, out var cached)
                              && DateTime.UtcNow - cached.Time < CacheDuration)
            {
                return cached.Sounds;
            }

            var url = string.IsNullOrEmpty(query)
                ? $"{BaseUrl}/trending/us/?page={page}"
                : $"{BaseUrl}/search/?name={Uri.EscapeDataString(query)}&page={page}";

            var sounds = Parse(await Http.GetStringAsync(url));
            if (!string.IsNullOrEmpty(query) && page == 1)
            {
                sounds = RankByQuery(sounds, query);
            }

            PageCache[key] = (DateTime.UtcNow, sounds);
            lock (Known)
            {
                foreach (var sound in sounds)
                {
                    Known[sound.Id] = sound;
                }
            }

            return sounds;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Past the last page the site answers 404: that is just the end of the list
            return [];
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Could not load MyInstants sounds (query {Query}, page {Page})", query, page);
            // Better an outdated page than an empty soundboard
            return PageCache.TryGetValue(key, out var old) ? old.Sounds : [];
        }
        finally
        {
            FetchLock.Release();
        }
    }

    public static SoundItem? FindCached(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        lock (Known)
        {
            return Known.GetValueOrDefault(id);
        }
    }

    /// <summary>
    /// Puts the exact name match first, then names that start with the query. The order is otherwise kept.
    /// </summary>
    private static List<SoundItem> RankByQuery(List<SoundItem> sounds, string query)
    {
        int Rank(SoundItem sound)
        {
            if (sound.Name.Equals(query, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            return sound.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
        }

        // OrderBy is stable, so the site's own relevance order decides between equal ranks
        return sounds.OrderBy(Rank).ToList();
    }

    public static List<SoundItem> Parse(string html)
    {
        var sounds = new List<SoundItem>();
        var seen = new HashSet<string>();

        foreach (Match match in SoundRegex().Matches(html))
        {
            var id = match.Groups["id"].Value;
            if (!seen.Add(id))
            {
                continue;
            }

            var name = WebUtility.HtmlDecode(match.Groups["name"].Value).Trim();
            sounds.Add(new SoundItem(id, name, $"https://{Host}{match.Groups["path"].Value}"));
        }

        return sounds;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MacroDeck-Soundboard/1.0");
        return client;
    }
}
