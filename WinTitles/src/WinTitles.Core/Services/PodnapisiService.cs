using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace WinTitles.Core.Services;

/// <summary>
/// Service for searching and downloading subtitles from Podnapisi.net.
/// This is a free alternative to OpenSubtitles that doesn't require authentication.
/// </summary>
public class PodnapisiService
{
    private readonly ILogger<PodnapisiService> _logger;
    private readonly HttpClient _http;
    private const string BaseUrl = "https://www.podnapisi.net/subtitles";

    public PodnapisiService(ILogger<PodnapisiService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _http = httpClient;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("WinTitles/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    /// <summary>
    /// Search for subtitles by query.
    /// </summary>
    public async Task<List<PodnapisiSubtitle>> SearchAsync(
        string query, 
        string language = "en",
        int? season = null,
        int? episode = null,
        int? year = null,
        CancellationToken ct = default)
    {
        try
        {
            var url = $"{BaseUrl}/search/advanced?keywords={Uri.EscapeDataString(query)}&language={language}";
            
            if (season.HasValue && episode.HasValue)
            {
                url += $"&seasons={season}&episodes={episode}&movie_type=tv-series,mini-series";
            }
            else
            {
                url += "&movie_type=movie";
            }
            
            if (year.HasValue)
            {
                url += $"&year={year}";
            }

            _logger.LogDebug("Podnapisi search: {Url}", url);
            
            var response = await _http.GetAsync(url, ct);
            
            // Handle rate limiting with retry
            if ((int)response.StatusCode == 429)
            {
                _logger.LogWarning("Podnapisi rate limited (429), waiting 5 seconds...");
                await Task.Delay(5000, ct);
                response = await _http.GetAsync(url, ct);
            }
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Podnapisi search failed: {Status}", response.StatusCode);
                return [];
            }

            var result = await response.Content.ReadFromJsonAsync<PodnapisiSearchResponse>(ct);
            
            if (result?.Data == null || result.Data.Count == 0)
            {
                _logger.LogDebug("No results from Podnapisi");
                return [];
            }

            _logger.LogInformation("Podnapisi found {Count} subtitles", result.Data.Count);

            return result.Data.Select(d => new PodnapisiSubtitle
                {
                    Id = d.PublishId,
                    Slug = d.Slug,
                    Language = d.Language,
                    Title = d.Movie?.Title ?? "",
                    Year = d.Movie?.Year,
                    Releases = (d.Releases ?? []).Concat(d.CustomReleases ?? []).ToList(),
                    DownloadUrl = $"{BaseUrl}/{d.Slug}/{d.PublishId}/download",
                    HearingImpaired = d.Flags?.Contains("hearing_impaired") ?? false,
                    Season = d.Movie?.EpisodeInfo?.Season,
                    Episode = d.Movie?.EpisodeInfo?.Episode
                }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Podnapisi search error");
            return [];
        }
    }

    /// <summary>
    /// Extract subtitle ID from Podnapisi URL (e.g., /subtitles/zXYZ/... -> zXYZ)
    /// </summary>
    private static string ExtractIdFromUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        
        // URL format: /subtitles/{id}/... or https://www.podnapisi.net/subtitles/{id}/...
        var parts = url.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals("subtitles", StringComparison.OrdinalIgnoreCase))
            {
                return parts[i + 1];
            }
        }
        return "";
    }

    /// <summary>
    /// Download a subtitle file.
    /// </summary>
    public async Task<PodnapisiDownloadResult> DownloadAsync(
        string subtitleIdOrUrl, 
        string destinationPath,
        CancellationToken ct = default)
    {
        try
        {
            // Support both full URL and just ID (for backwards compatibility)
            var url = subtitleIdOrUrl.StartsWith("http") 
                ? subtitleIdOrUrl 
                : $"{BaseUrl}/{subtitleIdOrUrl}/download";
            _logger.LogDebug("Downloading from Podnapisi: {Url}", url);

            var response = await _http.GetAsync(url, ct);
            
            // Handle rate limiting with retry
            if ((int)response.StatusCode == 429)
            {
                _logger.LogWarning("Podnapisi download rate limited (429), waiting 5 seconds...");
                await Task.Delay(5000, ct);
                response = await _http.GetAsync(url, ct);
            }
            
            if (!response.IsSuccessStatusCode)
            {
                return new PodnapisiDownloadResult
                {
                    Success = false,
                    Error = $"Download failed: {response.StatusCode}"
                };
            }

            var zipBytes = await response.Content.ReadAsByteArrayAsync(ct);
            
            // Extract first file from zip
            using var zipStream = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            
            if (archive.Entries.Count == 0)
            {
                return new PodnapisiDownloadResult
                {
                    Success = false,
                    Error = "Downloaded zip is empty"
                };
            }

            var entry = archive.Entries[0];
            
            // Determine destination with correct extension
            var ext = Path.GetExtension(entry.FullName);
            if (string.IsNullOrEmpty(ext)) ext = ".srt";
            
            var finalPath = Path.ChangeExtension(destinationPath, ext);
            
            // Create directory if needed
            var dir = Path.GetDirectoryName(finalPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Extract to file
            using (var entryStream = entry.Open())
            using (var fileStream = File.Create(finalPath))
            {
                await entryStream.CopyToAsync(fileStream, ct);
            }

            _logger.LogInformation("Downloaded subtitle to: {Path}", finalPath);
            
            return new PodnapisiDownloadResult
            {
                Success = true,
                FilePath = finalPath
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Podnapisi download error");
            return new PodnapisiDownloadResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }
}

public class PodnapisiSubtitle
{
    public string Id { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Language { get; set; } = "";
    public string Title { get; set; } = "";
    public int? Year { get; set; }
    public List<string> Releases { get; set; } = [];
    public string DownloadUrl { get; set; } = "";
    public bool HearingImpaired { get; set; }
    public int? Season { get; set; }
    public int? Episode { get; set; }
    
    public string ReleaseInfo => Releases.Count > 0 ? string.Join(", ", Releases.Take(2)) : Title;
}

public class PodnapisiDownloadResult
{
    public bool Success { get; set; }
    public string? FilePath { get; set; }
    public string? Error { get; set; }
}

// JSON response models
public class PodnapisiSearchResponse
{
    [JsonPropertyName("data")]
    public List<PodnapisiSearchData>? Data { get; set; }
    
    [JsonPropertyName("page")]
    public int Page { get; set; }
    
    [JsonPropertyName("all_pages")]
    public int AllPages { get; set; }
}

public class PodnapisiSearchData
{
    [JsonPropertyName("publish_id")]
    public string PublishId { get; set; } = "";
    
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";
    
    [JsonPropertyName("language")]
    public string Language { get; set; } = "";
    
    [JsonPropertyName("releases")]
    public List<string>? Releases { get; set; }
    
    [JsonPropertyName("custom_releases")]
    public List<string>? CustomReleases { get; set; }
    
    [JsonPropertyName("flags")]
    public List<string>? Flags { get; set; }
    
    [JsonPropertyName("movie")]
    public PodnapisiMovie? Movie { get; set; }
    
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public class PodnapisiMovie
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }
    
    [JsonPropertyName("year")]
    public int? Year { get; set; }
    
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    
    [JsonPropertyName("episode_info")]
    public PodnapisiEpisodeInfo? EpisodeInfo { get; set; }
}

public class PodnapisiEpisodeInfo
{
    [JsonPropertyName("season")]
    public int? Season { get; set; }
    
    [JsonPropertyName("episode")]
    public int? Episode { get; set; }
}
