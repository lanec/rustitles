namespace WinTitles.Core.Models;

/// <summary>
/// Represents a subtitle download job.
/// </summary>
public class DownloadJob
{
    public required string Id { get; init; }
    public required string VideoPath { get; init; }
    public required string Title { get; init; }
    public int? Year { get; init; }
    public required MediaType MediaType { get; init; }
    public required string Language { get; init; }
    public DownloadStatus Status { get; set; } = DownloadStatus.Pending;
    public string? SubtitlePath { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? Source { get; init; } // "plex", "folder", "drop"
    
    // TV Episode metadata (for accurate subtitle search)
    public string? ShowTitle { get; init; }
    public int? SeasonNumber { get; init; }
    public int? EpisodeNumber { get; init; }
    
    public string DisplayName => MediaType == MediaType.Episode 
        ? $"{ShowTitle ?? Title} S{SeasonNumber:D2}E{EpisodeNumber:D2}" 
        : Year.HasValue ? $"{Title} ({Year})" : Title;
}

public enum DownloadStatus
{
    Pending,
    Downloading,
    Success,
    Failed,
    Skipped
}
