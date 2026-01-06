namespace WinTitles.Core.Models;

/// <summary>
/// Result of a media scan operation.
/// </summary>
public class ScanResult
{
    public List<ScannedItem> Items { get; set; } = [];
    public int TotalFound => Items.Count;
    public int WithSubtitles => Items.Count(i => i.SubtitleStatus == SubtitleStatus.HasAllLanguages);
    public int MissingSubtitles => Items.Count(i => i.SubtitleStatus != SubtitleStatus.HasAllLanguages);
    public int MoviesCount => Items.Count(i => i.Type == MediaType.Movie);
    public int EpisodesCount => Items.Count(i => i.Type == MediaType.Episode);
    public TimeSpan ScanDuration { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    public string? Source { get; set; } // "plex" or folder path
    public List<string> LibrariesScanned { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}

/// <summary>
/// Progress update during a scan operation.
/// </summary>
public class ScanProgress
{
    public string Phase { get; set; } = "";
    public int LibraryCurrent { get; set; }
    public int LibraryTotal { get; set; }
    public int ItemsCurrent { get; set; }
    public int ItemsTotal { get; set; }
    public string CurrentItem { get; set; } = "";
    public string CurrentLibrary { get; set; } = "";
    
    public double ProgressPercent => ItemsTotal > 0 
        ? (double)ItemsCurrent / ItemsTotal * 100 
        : 0;
    
    public string StatusText => ItemsTotal > 0
        ? $"{Phase} {ItemsCurrent:N0} of {ItemsTotal:N0}"
        : Phase;
}
