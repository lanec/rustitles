namespace WinTitles.Core.Models;

/// <summary>
/// Represents a media item (movie or TV episode) from Plex or local folder.
/// </summary>
public record MediaItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public int? Year { get; init; }
    public required MediaType Type { get; init; }
    public string? FilePath { get; init; }
    public string? RatingKey { get; init; }
    
    // TV Show specific
    public string? ShowTitle { get; init; }
    public int? SeasonNumber { get; init; }
    public int? EpisodeNumber { get; init; }
    
    public string DisplayName => Type == MediaType.Episode 
        ? $"{ShowTitle} S{SeasonNumber:D2}E{EpisodeNumber:D2} - {Title}"
        : Year.HasValue ? $"{Title} ({Year})" : Title;
}

public enum MediaType
{
    Movie,
    Episode
}
