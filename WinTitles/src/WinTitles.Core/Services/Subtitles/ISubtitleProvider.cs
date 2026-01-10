namespace WinTitles.Core.Services.Subtitles;

/// <summary>
/// Common interface for all subtitle providers.
/// </summary>
public interface ISubtitleProvider
{
    /// <summary>
    /// Provider name for display and logging.
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Priority order (lower = higher priority, tried first).
    /// </summary>
    int Priority { get; }
    
    /// <summary>
    /// Whether this provider requires authentication.
    /// </summary>
    bool RequiresAuth { get; }
    
    /// <summary>
    /// Whether this provider is currently available (configured/authenticated).
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Search for subtitles by query text.
    /// </summary>
    Task<List<SubtitleSearchResult>> SearchAsync(
        SubtitleSearchRequest request,
        CancellationToken ct = default);
    
    /// <summary>
    /// Search for subtitles by file hash (if supported).
    /// </summary>
    Task<List<SubtitleSearchResult>> SearchByHashAsync(
        string filePath,
        string language,
        CancellationToken ct = default);
    
    /// <summary>
    /// Download a subtitle to the specified path.
    /// </summary>
    Task<SubtitleDownloadResult> DownloadAsync(
        SubtitleSearchResult subtitle,
        string destinationPath,
        CancellationToken ct = default);
}

/// <summary>
/// Request for subtitle search.
/// </summary>
public class SubtitleSearchRequest
{
    public required string Query { get; init; }
    public string Language { get; init; } = "en";
    public string? FilePath { get; init; }
    public int? Season { get; init; }
    public int? Episode { get; init; }
    public int? Year { get; init; }
    public string? ImdbId { get; init; }
    public MediaType MediaType { get; init; } = MediaType.Unknown;
}

/// <summary>
/// Unified subtitle search result from any provider.
/// </summary>
public class SubtitleSearchResult
{
    public required string Id { get; init; }
    public required string ProviderId { get; init; }
    public required string ProviderName { get; init; }
    public required string Language { get; init; }
    public required string FileName { get; init; }
    public string? Release { get; init; }
    public int Downloads { get; init; }
    public bool HearingImpaired { get; init; }
    public bool HashMatch { get; init; }
    public float MatchScore { get; init; }
    public int? Year { get; init; }
    public int? Season { get; init; }
    public int? Episode { get; init; }
    
    /// <summary>
    /// Provider-specific data needed for download.
    /// </summary>
    public object? ProviderData { get; init; }
}

/// <summary>
/// Result of a subtitle download operation.
/// </summary>
public class SubtitleDownloadResult
{
    public bool Success { get; init; }
    public string? FilePath { get; init; }
    public string? Error { get; init; }
    public int? RemainingDownloads { get; init; }
}

public enum MediaType
{
    Unknown,
    Movie,
    Episode
}
