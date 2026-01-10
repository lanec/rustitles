using Microsoft.Extensions.Logging;

namespace WinTitles.Core.Services.Subtitles;

/// <summary>
/// Aggregates multiple subtitle providers and searches them in priority order.
/// Similar to Subliminal's multi-provider approach.
/// </summary>
public class SubtitleAggregator
{
    private readonly ILogger<SubtitleAggregator> _logger;
    private readonly IEnumerable<ISubtitleProvider> _providers;
    private readonly OpenAIService _aiService;

    public SubtitleAggregator(
        ILogger<SubtitleAggregator> logger,
        IEnumerable<ISubtitleProvider> providers,
        OpenAIService aiService)
    {
        _logger = logger;
        _providers = providers.OrderBy(p => p.Priority);
        _aiService = aiService;
    }

    /// <summary>
    /// Search all available providers for subtitles.
    /// </summary>
    public async Task<AggregatedSearchResult> SearchAllAsync(
        SubtitleSearchRequest request,
        CancellationToken ct = default)
    {
        var allResults = new List<SubtitleSearchResult>();
        var providerResults = new Dictionary<string, List<SubtitleSearchResult>>();
        var errors = new List<string>();

        foreach (var provider in _providers)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                if (!await provider.IsAvailableAsync(ct))
                {
                    _logger.LogDebug("Provider {Provider} not available, skipping", provider.Name);
                    continue;
                }

                _logger.LogInformation("Searching {Provider} for: {Query}", provider.Name, request.Query);

                // Try hash search first if file path provided
                List<SubtitleSearchResult> results = [];
                if (!string.IsNullOrEmpty(request.FilePath))
                {
                    results = await provider.SearchByHashAsync(request.FilePath, request.Language, ct);
                    if (results.Count > 0)
                    {
                        _logger.LogInformation("{Provider} hash search found {Count} results", provider.Name, results.Count);
                    }
                }

                // Fall back to text search
                if (results.Count == 0)
                {
                    results = await provider.SearchAsync(request, ct);
                    _logger.LogInformation("{Provider} text search found {Count} results", provider.Name, results.Count);
                }

                if (results.Count > 0)
                {
                    providerResults[provider.Name] = results;
                    allResults.AddRange(results);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching {Provider}", provider.Name);
                errors.Add($"{provider.Name}: {ex.Message}");
            }
        }

        // Sort all results by match score
        allResults = allResults
            .OrderByDescending(r => r.HashMatch)
            .ThenByDescending(r => r.MatchScore)
            .ThenByDescending(r => r.Downloads)
            .ToList();

        return new AggregatedSearchResult
        {
            Results = allResults,
            ResultsByProvider = providerResults,
            Errors = errors,
            TotalCount = allResults.Count
        };
    }

    /// <summary>
    /// Smart search: tries file name, then AI-suggested names if no results.
    /// </summary>
    public async Task<SmartSearchResult> SmartSearchAsync(
        string filePath,
        string language,
        CancellationToken ct = default)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var originalQuery = fileName;
        
        // Parse media info from filename
        var mediaInfo = ParseMediaInfo(fileName);
        
        var request = new SubtitleSearchRequest
        {
            Query = mediaInfo.CleanTitle,
            Language = language,
            FilePath = filePath,
            Season = mediaInfo.Season,
            Episode = mediaInfo.Episode,
            Year = mediaInfo.Year,
            MediaType = mediaInfo.Type
        };

        _logger.LogInformation("Smart search for: {FileName} -> {Query}", fileName, request.Query);

        // First attempt with parsed filename
        var result = await SearchAllAsync(request, ct);

        if (result.TotalCount > 0)
        {
            return new SmartSearchResult
            {
                Success = true,
                Results = result,
                UsedQuery = request.Query,
                AiSuggested = false
            };
        }

        // No results - try AI suggestions if available
        if (_aiService != null && !string.IsNullOrEmpty(fileName))
        {
            _logger.LogInformation("No results found, requesting AI suggestions for: {FileName}", fileName);
            
            var suggestions = await GetAiSuggestionsAsync(fileName, mediaInfo, ct);
            
            foreach (var suggestion in suggestions)
            {
                if (ct.IsCancellationRequested) break;
                
                request = new SubtitleSearchRequest
                {
                    Query = suggestion.Title,
                    Language = language,
                    FilePath = filePath,
                    Season = suggestion.Season ?? mediaInfo.Season,
                    Episode = suggestion.Episode ?? mediaInfo.Episode,
                    Year = suggestion.Year ?? mediaInfo.Year,
                    MediaType = suggestion.Type
                };

                _logger.LogInformation("Trying AI suggestion: {Title}", suggestion.Title);
                result = await SearchAllAsync(request, ct);

                if (result.TotalCount > 0)
                {
                    return new SmartSearchResult
                    {
                        Success = true,
                        Results = result,
                        UsedQuery = suggestion.Title,
                        AiSuggested = true,
                        AiSuggestions = suggestions
                    };
                }
            }

            // Return with suggestions even if no results
            return new SmartSearchResult
            {
                Success = false,
                Results = result,
                UsedQuery = originalQuery,
                AiSuggested = false,
                AiSuggestions = suggestions,
                NeedsUserInput = true
            };
        }

        return new SmartSearchResult
        {
            Success = false,
            Results = result,
            UsedQuery = originalQuery,
            AiSuggested = false,
            NeedsUserInput = true
        };
    }

    /// <summary>
    /// Download a subtitle using the appropriate provider.
    /// If download fails, tries other results from different providers.
    /// </summary>
    public async Task<SubtitleDownloadResult> DownloadAsync(
        SubtitleSearchResult subtitle,
        string destinationPath,
        CancellationToken ct = default)
    {
        var provider = _providers.FirstOrDefault(p => p.Name == subtitle.ProviderName);
        
        if (provider == null)
        {
            return new SubtitleDownloadResult
            {
                Success = false,
                Error = $"Provider '{subtitle.ProviderName}' not found"
            };
        }

        var result = await provider.DownloadAsync(subtitle, destinationPath, ct);
        
        if (!result.Success)
        {
            _logger.LogWarning("Download from {Provider} failed: {Error}", subtitle.ProviderName, result.Error);
        }
        
        return result;
    }

    /// <summary>
    /// Download with fallback - tries multiple results from different providers until one succeeds.
    /// </summary>
    public async Task<SubtitleDownloadResult> DownloadWithFallbackAsync(
        AggregatedSearchResult searchResults,
        string destinationPath,
        CancellationToken ct = default)
    {
        if (searchResults.TotalCount == 0)
        {
            return new SubtitleDownloadResult
            {
                Success = false,
                Error = "No subtitles found from any provider"
            };
        }

        // Group results by provider and try each provider's best result
        var resultsByProvider = searchResults.Results
            .GroupBy(r => r.ProviderName)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.MatchScore).ToList());

        var errors = new List<string>();

        foreach (var provider in _providers)
        {
            if (ct.IsCancellationRequested) break;
            
            if (!resultsByProvider.TryGetValue(provider.Name, out var providerResults) || providerResults.Count == 0)
                continue;

            var best = providerResults.First();
            _logger.LogInformation("Trying download from {Provider}: {FileName}", provider.Name, best.FileName);

            try
            {
                var result = await provider.DownloadAsync(best, destinationPath, ct);
                
                if (result.Success)
                {
                    _logger.LogInformation("Successfully downloaded from {Provider}", provider.Name);
                    return result;
                }
                
                errors.Add($"{provider.Name}: {result.Error}");
                _logger.LogWarning("Download from {Provider} failed: {Error}, trying next provider...", 
                    provider.Name, result.Error);
            }
            catch (Exception ex)
            {
                errors.Add($"{provider.Name}: {ex.Message}");
                _logger.LogError(ex, "Error downloading from {Provider}, trying next provider...", provider.Name);
            }
        }

        return new SubtitleDownloadResult
        {
            Success = false,
            Error = $"All providers failed: {string.Join("; ", errors)}"
        };
    }

    /// <summary>
    /// Get AI suggestions for a filename that couldn't be matched.
    /// </summary>
    private async Task<List<AiMediaSuggestion>> GetAiSuggestionsAsync(
        string fileName,
        MediaInfo parsed,
        CancellationToken ct)
    {
        try
        {
            var prompt = $@"Analyze this video filename and suggest the correct movie or TV show title for searching subtitles.

Filename: {fileName}

What I already parsed:
- Title guess: {parsed.CleanTitle}
- Year: {parsed.Year?.ToString() ?? "unknown"}
- Season: {parsed.Season?.ToString() ?? "N/A"}
- Episode: {parsed.Episode?.ToString() ?? "N/A"}
- Type: {parsed.Type}

Please provide up to 3 possible matches in order of likelihood. For each, provide:
1. The correct title (clean, no year or episode info)
2. Year (if known)
3. Season/Episode (if TV show)
4. Type (movie or episode)

Respond in JSON format:
[
  {{""title"": ""Correct Title"", ""year"": 2020, ""season"": null, ""episode"": null, ""type"": ""movie""}},
  ...
]";

            var response = await _aiService.GetCompletionAsync(prompt, ct);
            
            if (string.IsNullOrEmpty(response))
                return [];

            // Parse JSON response
            var suggestions = System.Text.Json.JsonSerializer.Deserialize<List<AiMediaSuggestion>>(
                response,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return suggestions ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI suggestion failed for: {FileName}", fileName);
            return [];
        }
    }

    /// <summary>
    /// Parse media info from a filename.
    /// </summary>
    private MediaInfo ParseMediaInfo(string fileName)
    {
        var info = new MediaInfo { OriginalName = fileName };
        
        // Remove common file extensions and quality tags
        var clean = fileName;
        var patterns = new[] { 
            @"\.mkv$", @"\.mp4$", @"\.avi$", @"\.m4v$",
            @"\b(1080p|720p|480p|2160p|4k)\b",
            @"\b(bluray|brrip|bdrip|webrip|web-dl|hdtv|dvdrip)\b",
            @"\b(x264|x265|h264|h265|hevc|avc)\b",
            @"\b(aac|ac3|dts|flac|mp3)\b",
            @"\[.*?\]", @"\(.*?\)",
            @"-[a-z0-9]+$"
        };
        
        foreach (var pattern in patterns)
        {
            clean = System.Text.RegularExpressions.Regex.Replace(
                clean, pattern, "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        // Try to extract year
        var yearMatch = System.Text.RegularExpressions.Regex.Match(
            fileName, @"\b(19|20)\d{2}\b");
        if (yearMatch.Success)
        {
            info.Year = int.Parse(yearMatch.Value);
            clean = clean.Replace(yearMatch.Value, "");
        }

        // Try to extract season/episode (S01E05 format)
        var episodeMatch = System.Text.RegularExpressions.Regex.Match(
            fileName, @"[Ss](\d{1,2})[Ee](\d{1,2})", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (episodeMatch.Success)
        {
            info.Season = int.Parse(episodeMatch.Groups[1].Value);
            info.Episode = int.Parse(episodeMatch.Groups[2].Value);
            info.Type = MediaType.Episode;
            clean = clean.Substring(0, episodeMatch.Index);
        }
        else
        {
            // Try season x episode format (1x05)
            var altMatch = System.Text.RegularExpressions.Regex.Match(
                fileName, @"(\d{1,2})x(\d{1,2})");
            if (altMatch.Success)
            {
                info.Season = int.Parse(altMatch.Groups[1].Value);
                info.Episode = int.Parse(altMatch.Groups[2].Value);
                info.Type = MediaType.Episode;
                clean = clean.Substring(0, altMatch.Index);
            }
            else
            {
                info.Type = MediaType.Movie;
            }
        }

        // Clean up and set title
        clean = clean.Replace(".", " ").Replace("_", " ").Trim();
        clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+", " ");
        info.CleanTitle = clean;

        return info;
    }
}

public class AggregatedSearchResult
{
    public List<SubtitleSearchResult> Results { get; init; } = [];
    public Dictionary<string, List<SubtitleSearchResult>> ResultsByProvider { get; init; } = [];
    public List<string> Errors { get; init; } = [];
    public int TotalCount { get; init; }
}

public class SmartSearchResult
{
    public bool Success { get; init; }
    public AggregatedSearchResult Results { get; init; } = new();
    public string UsedQuery { get; init; } = "";
    public bool AiSuggested { get; init; }
    public List<AiMediaSuggestion>? AiSuggestions { get; init; }
    public bool NeedsUserInput { get; init; }
}

public class AiMediaSuggestion
{
    public string Title { get; set; } = "";
    public int? Year { get; set; }
    public int? Season { get; set; }
    public int? Episode { get; set; }
    public string TypeString { get; set; } = "movie";
    
    public MediaType Type => TypeString?.ToLower() == "episode" ? MediaType.Episode : MediaType.Movie;
}

public class MediaInfo
{
    public string OriginalName { get; set; } = "";
    public string CleanTitle { get; set; } = "";
    public int? Year { get; set; }
    public int? Season { get; set; }
    public int? Episode { get; set; }
    public MediaType Type { get; set; } = MediaType.Unknown;
}
