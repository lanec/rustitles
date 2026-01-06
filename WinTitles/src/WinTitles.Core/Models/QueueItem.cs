namespace WinTitles.Core.Models;

/// <summary>
/// Represents an item in the download queue.
/// Tracks download progress and status for a single subtitle download.
/// </summary>
public class QueueItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public ScannedItem ScannedItem { get; set; } = null!;
    public string Language { get; set; } = "en";
    
    // Status tracking
    public DownloadStatus Status { get; set; } = DownloadStatus.Pending;
    public string? SubtitlePath { get; set; }
    public string? Error { get; set; }
    public int RetryCount { get; set; }
    
    // Timing
    public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    
    // Queue position (for display)
    public int QueuePosition { get; set; }
    
    /// <summary>
    /// Display name for UI.
    /// </summary>
    public string DisplayName => ScannedItem?.DisplayName ?? "Unknown";
    
    /// <summary>
    /// Duration of download attempt if completed.
    /// </summary>
    public TimeSpan? Duration => CompletedAt.HasValue && StartedAt.HasValue
        ? CompletedAt.Value - StartedAt.Value
        : null;
}
