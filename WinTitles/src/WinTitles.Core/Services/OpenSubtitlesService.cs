using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace WinTitles.Core.Services;

/// <summary>
/// Native OpenSubtitles.com API client - no Python/Subliminal required.
/// </summary>
public class OpenSubtitlesService
{
    private readonly ILogger<OpenSubtitlesService> _logger;
    private readonly HttpClient _http;
    private readonly SettingsService _settings;
    private const string BaseUrl = "https://api.opensubtitles.com/api/v1";
    private string? _authToken;

    public OpenSubtitlesService(ILogger<OpenSubtitlesService> logger, HttpClient httpClient, SettingsService settings)
    {
        _logger = logger;
        _http = httpClient;
        _settings = settings;
        // OpenSubtitles requires a specific User-Agent format
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("WinTitles/1.0");
    }

    /// <summary>
    /// Get the current API key from settings.
    /// </summary>
    private string ApiKey => _settings.Settings.OpenSubtitles?.ApiKey ?? "";
    
    /// <summary>
    /// Login to OpenSubtitles to get an auth token for downloads.
    /// </summary>
    public async Task<bool> LoginAsync(CancellationToken ct = default)
    {
        var username = _settings.Settings.OpenSubtitles?.Username;
        var password = _settings.Settings.OpenSubtitles?.Password;
        
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            _logger.LogWarning("OpenSubtitles username/password not configured");
            return false;
        }
        
        var url = $"{BaseUrl}/login";
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { username, password })
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        AddHeaders(request);
        
        try
        {
            var response = await _http.SendAsync(request, ct);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("OpenSubtitles login failed: {Status} - {Error}", response.StatusCode, error);
                return false;
            }
            
            var result = await response.Content.ReadFromJsonAsync<OpenSubtitlesLoginResponse>(ct);
            _authToken = result?.Token;
            
            if (!string.IsNullOrEmpty(_authToken))
            {
                _logger.LogInformation("OpenSubtitles login successful");
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenSubtitles login error");
            return false;
        }
    }
    
    /// <summary>
    /// Check if we're logged in.
    /// </summary>
    public bool IsLoggedIn => !string.IsNullOrEmpty(_authToken);

    /// <summary>
    /// Search for subtitles by movie/show name.
    /// </summary>
    public async Task<List<SubtitleSearchResult>> SearchAsync(
        string query,
        string language = "en",
        int? year = null,
        int? season = null,
        int? episode = null,
        CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/subtitles?query={Uri.EscapeDataString(query)}&languages={language}";
        
        if (year.HasValue) url += $"&year={year}";
        if (season.HasValue) url += $"&season_number={season}";
        if (episode.HasValue) url += $"&episode_number={episode}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddHeaders(request);

        try
        {
            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OpenSubtitlesSearchResponse>(cancellationToken: ct);
            
            return result?.Data?.Select(d => new SubtitleSearchResult
            {
                FileId = d.Attributes?.Files?.FirstOrDefault()?.FileId ?? 0,
                FileName = d.Attributes?.Files?.FirstOrDefault()?.FileName ?? "",
                Language = d.Attributes?.Language ?? language,
                Downloads = d.Attributes?.DownloadCount ?? 0,
                Rating = d.Attributes?.Ratings ?? 0,
                HearingImpaired = d.Attributes?.HearingImpaired ?? false
            }).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching OpenSubtitles");
            return [];
        }
    }

    /// <summary>
    /// Search for subtitles by file hash (more accurate).
    /// </summary>
    public async Task<List<SubtitleSearchResult>> SearchByHashAsync(
        string filePath,
        string language = "en",
        CancellationToken ct = default)
    {
        var hash = await ComputeOpenSubtitlesHashAsync(filePath, ct);
        if (string.IsNullOrEmpty(hash)) return [];

        var fileInfo = new FileInfo(filePath);
        var url = $"{BaseUrl}/subtitles?moviehash={hash}&languages={language}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddHeaders(request);

        try
        {
            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OpenSubtitlesSearchResponse>(cancellationToken: ct);
            
            return result?.Data?.Select(d => new SubtitleSearchResult
            {
                FileId = d.Attributes?.Files?.FirstOrDefault()?.FileId ?? 0,
                FileName = d.Attributes?.Files?.FirstOrDefault()?.FileName ?? "",
                Language = d.Attributes?.Language ?? language,
                Downloads = d.Attributes?.DownloadCount ?? 0,
                Rating = d.Attributes?.Ratings ?? 0,
                HearingImpaired = d.Attributes?.HearingImpaired ?? false
            }).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching OpenSubtitles by hash");
            return [];
        }
    }

    /// <summary>
    /// Download a subtitle file.
    /// </summary>
    public async Task<SubtitleDownloadResult> DownloadAsync(
        int fileId,
        string destinationPath,
        CancellationToken ct = default)
    {
        // Check for API key - required for downloads
        if (string.IsNullOrEmpty(ApiKey))
        {
            return new SubtitleDownloadResult 
            { 
                Success = false, 
                Error = "OpenSubtitles API key required. Configure in Settings." 
            };
        }
        
        // Auto-login if not already logged in
        if (!IsLoggedIn)
        {
            var loggedIn = await LoginAsync(ct);
            if (!loggedIn)
            {
                return new SubtitleDownloadResult 
                { 
                    Success = false, 
                    Error = "OpenSubtitles login required. Configure username/password in Settings." 
                };
            }
        }
        
        var url = $"{BaseUrl}/download";
        
        // Create request with all headers explicitly set
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Api-Key", ApiKey);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_authToken}");
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("User-Agent", "WinTitles/1.0");
        
        var jsonContent = $"{{\"file_id\":{fileId}}}";
        request.Content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");
        
        _logger.LogInformation("Download request - FileId: {FileId}, HasToken: {HasToken}, ApiKey: {ApiKey}", 
            fileId, !string.IsNullOrEmpty(_authToken), ApiKey?.Substring(0, Math.Min(8, ApiKey?.Length ?? 0)) + "...");

        try
        {
            _logger.LogInformation("Requesting download for file ID {FileId} with API key present: {HasKey}", 
                fileId, !string.IsNullOrEmpty(ApiKey));
            
            var response = await _http.SendAsync(request, ct);
            
            // Log all response headers for debugging
            _logger.LogDebug("Response status: {Status}", response.StatusCode);
            foreach (var header in response.Headers)
            {
                _logger.LogDebug("Response header: {Key}={Value}", header.Key, string.Join(", ", header.Value));
            }
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Download API failed: {Status} - {Content}", response.StatusCode, errorContent);
                
                // Check for specific error codes and clean up HTML responses
                var cleanError = errorContent.Contains("<!DOCTYPE") || errorContent.Contains("<html")
                    ? "" : errorContent;
                    
                if ((int)response.StatusCode == 406)
                {
                    return new SubtitleDownloadResult 
                    { 
                        Success = false, 
                        Error = "API rejected request (406). Please verify your API key is valid at opensubtitles.com" 
                    };
                }
                
                if ((int)response.StatusCode == 503)
                {
                    return new SubtitleDownloadResult 
                    { 
                        Success = false, 
                        Error = "OpenSubtitles server is temporarily unavailable (503). Please try again in a few minutes." 
                    };
                }
                
                return new SubtitleDownloadResult 
                { 
                    Success = false, 
                    Error = $"API error {(int)response.StatusCode}: {response.ReasonPhrase}" 
                };
            }

            var result = await response.Content.ReadFromJsonAsync<OpenSubtitlesDownloadResponse>(cancellationToken: ct);
            
            if (string.IsNullOrEmpty(result?.Link))
            {
                return new SubtitleDownloadResult { Success = false, Error = "No download link returned" };
            }

            // Download the actual subtitle file
            var subtitleBytes = await _http.GetByteArrayAsync(result.Link, ct);
            await File.WriteAllBytesAsync(destinationPath, subtitleBytes, ct);

            _logger.LogInformation("Downloaded subtitle to {Path}", destinationPath);

            return new SubtitleDownloadResult
            {
                Success = true,
                FilePath = destinationPath,
                RemainingDownloads = result.Remaining
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading subtitle");
            return new SubtitleDownloadResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Search and download subtitles for a video file.
    /// </summary>
    public async Task<SubtitleDownloadResult> DownloadForVideoAsync(
        string videoPath,
        string language = "en",
        CancellationToken ct = default)
    {
        // First try hash-based search (most accurate)
        var results = await SearchByHashAsync(videoPath, language, ct);

        // Fall back to name-based search
        if (results.Count == 0)
        {
            var fileName = Path.GetFileNameWithoutExtension(videoPath);
            results = await SearchAsync(fileName, language, ct: ct);
        }

        if (results.Count == 0)
        {
            return new SubtitleDownloadResult { Success = false, Error = "No subtitles found" };
        }

        // Pick the best result (highest downloads/rating)
        var best = results.OrderByDescending(r => r.Downloads).First();

        // Generate destination path
        var dir = Path.GetDirectoryName(videoPath) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(videoPath);
        var destPath = Path.Combine(dir, $"{baseName}.{language}.srt");

        return await DownloadAsync(best.FileId, destPath, ct);
    }

    /// <summary>
    /// Search and download subtitles using metadata (like rustitles does with Subliminal).
    /// This is more accurate than filename-based search.
    /// </summary>
    public async Task<SubtitleDownloadResult> DownloadWithMetadataAsync(
        string videoPath,
        string title,
        string language = "en",
        int? year = null,
        string? showTitle = null,
        int? season = null,
        int? episode = null,
        CancellationToken ct = default)
    {
        List<SubtitleSearchResult> results;

        // First try hash-based search (most accurate for exact file matches)
        results = await SearchByHashAsync(videoPath, language, ct);

        // If no hash match, use metadata-based search
        if (results.Count == 0)
        {
            if (!string.IsNullOrEmpty(showTitle) && season.HasValue && episode.HasValue)
            {
                // TV Episode - search by show, season, episode
                _logger.LogInformation("Searching for TV: {Show} S{Season}E{Episode}", showTitle, season, episode);
                results = await SearchAsync(showTitle, language, year, season, episode, ct);
                
                // If no results, try with just the episode title
                if (results.Count == 0 && !string.IsNullOrEmpty(title))
                {
                    results = await SearchAsync($"{showTitle} {title}", language, year, null, null, ct);
                }
            }
            else
            {
                // Movie - search by title and year
                _logger.LogInformation("Searching for Movie: {Title} ({Year})", title, year);
                results = await SearchAsync(title, language, year, null, null, ct);
                
                // Try without year if no results
                if (results.Count == 0 && year.HasValue)
                {
                    results = await SearchAsync(title, language, null, null, null, ct);
                }
            }
        }

        if (results.Count == 0)
        {
            return new SubtitleDownloadResult { Success = false, Error = "No subtitles found" };
        }

        // Pick the best result (highest downloads/rating)
        var best = results.OrderByDescending(r => r.Downloads).First();

        // Generate destination path
        var dir = Path.GetDirectoryName(videoPath) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(videoPath);
        var destPath = Path.Combine(dir, $"{baseName}.{language}.srt");

        return await DownloadAsync(best.FileId, destPath, ct);
    }

    private void AddHeaders(HttpRequestMessage request)
    {
        // OpenSubtitles API requires Accept and Api-Key headers
        request.Headers.Accept.Clear();
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("*/*");
        
        var apiKey = ApiKey;
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.TryAddWithoutValidation("Api-Key", apiKey);
            _logger.LogDebug("Using API key: {Key}", apiKey.Substring(0, Math.Min(8, apiKey.Length)) + "...");
        }
        else
        {
            _logger.LogWarning("No API key configured for OpenSubtitles");
        }
        
        if (!string.IsNullOrEmpty(_authToken))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_authToken}");
        }
    }

    /// <summary>
    /// Compute OpenSubtitles hash for a video file.
    /// </summary>
    private static async Task<string?> ComputeOpenSubtitlesHashAsync(string filePath, CancellationToken ct)
    {
        try
        {
            const int ChunkSize = 64 * 1024; // 64KB
            var fileInfo = new FileInfo(filePath);
            var fileSize = fileInfo.Length;

            if (fileSize < ChunkSize * 2) return null;

            ulong hash = (ulong)fileSize;

            await using var stream = File.OpenRead(filePath);
            var buffer = new byte[ChunkSize];

            // Read first 64KB
            await stream.ReadExactlyAsync(buffer, ct);
            for (int i = 0; i < ChunkSize; i += 8)
            {
                hash += BitConverter.ToUInt64(buffer, i);
            }

            // Read last 64KB
            stream.Seek(-ChunkSize, SeekOrigin.End);
            await stream.ReadExactlyAsync(buffer, ct);
            for (int i = 0; i < ChunkSize; i += 8)
            {
                hash += BitConverter.ToUInt64(buffer, i);
            }

            return hash.ToString("x16");
        }
        catch
        {
            return null;
        }
    }
}

public class SubtitleSearchResult
{
    public int FileId { get; init; }
    public string FileName { get; init; } = "";
    public string Language { get; init; } = "";
    public int Downloads { get; init; }
    public double Rating { get; init; }
    public bool HearingImpaired { get; init; }
}

public class SubtitleDownloadResult
{
    public bool Success { get; init; }
    public string? FilePath { get; init; }
    public string? Error { get; init; }
    public int RemainingDownloads { get; init; }
}

#region OpenSubtitles API Response Models

internal class OpenSubtitlesSearchResponse
{
    [JsonPropertyName("data")]
    public List<OpenSubtitlesDataItem>? Data { get; set; }
}

internal class OpenSubtitlesDataItem
{
    [JsonPropertyName("attributes")]
    public OpenSubtitlesAttributes? Attributes { get; set; }
}

internal class OpenSubtitlesAttributes
{
    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("download_count")]
    public int DownloadCount { get; set; }

    [JsonPropertyName("ratings")]
    public double Ratings { get; set; }

    [JsonPropertyName("hearing_impaired")]
    public bool HearingImpaired { get; set; }

    [JsonPropertyName("files")]
    public List<OpenSubtitlesFile>? Files { get; set; }
}

internal class OpenSubtitlesFile
{
    [JsonPropertyName("file_id")]
    public int FileId { get; set; }

    [JsonPropertyName("file_name")]
    public string? FileName { get; set; }
}

internal class OpenSubtitlesDownloadResponse
{
    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("remaining")]
    public int Remaining { get; set; }
}

internal class OpenSubtitlesLoginResponse
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }
    
    [JsonPropertyName("status")]
    public int Status { get; set; }
}

#endregion
