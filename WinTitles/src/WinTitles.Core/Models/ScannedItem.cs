namespace WinTitles.Core.Models;

/// <summary>
/// Represents a media item discovered during a scan.
/// Contains all metadata needed for subtitle searching.
/// </summary>
public class ScannedItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public string FilePath { get; set; } = "";
    public MediaType Type { get; set; }
    public int? Year { get; set; }
    
    // TV Episode metadata
    public string? ShowTitle { get; set; }
    public int? Season { get; set; }
    public int? Episode { get; set; }
    
    // Subtitle status
    public SubtitleStatus SubtitleStatus { get; set; } = SubtitleStatus.Unknown;
    public List<string> ExistingSubtitles { get; set; } = [];
    public List<string> MissingLanguages { get; set; } = [];
    
    // Selection state for UI
    public bool IsSelected { get; set; } = true;
    
    // Source info
    public string? LibraryName { get; set; }
    public string? PlexRatingKey { get; set; }
    
    /// <summary>
    /// Display name for UI.
    /// </summary>
    public string DisplayName => Type == MediaType.Episode
        ? $"{ShowTitle} S{Season:D2}E{Episode:D2} - {Title}"
        : Year.HasValue ? $"{Title} ({Year})" : Title;
    
    /// <summary>
    /// Short display name without show prefix.
    /// </summary>
    public string ShortName => Type == MediaType.Episode
        ? $"S{Season:D2}E{Episode:D2} - {Title}"
        : Year.HasValue ? $"{Title} ({Year})" : Title;
}

/// <summary>
/// Status of subtitles for a scanned item.
/// </summary>
public enum SubtitleStatus
{
    Unknown,
    HasAllLanguages,
    MissingSome,
    MissingAll,
    FileNotFound
}
