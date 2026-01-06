namespace WinTitles.Core.Models;

/// <summary>
/// Application workflow states.
/// Defines the distinct phases of the scan/download workflow.
/// </summary>
public enum WorkflowState
{
    /// <summary>
    /// No operation in progress. Ready to start a scan.
    /// </summary>
    Idle,
    
    /// <summary>
    /// Scanning Plex or folder for media items.
    /// </summary>
    Scanning,
    
    /// <summary>
    /// Scan complete. User reviewing results before download.
    /// </summary>
    Review,
    
    /// <summary>
    /// Actively downloading subtitles.
    /// </summary>
    Downloading,
    
    /// <summary>
    /// Downloads paused by user. Can be resumed.
    /// </summary>
    Paused,
    
    /// <summary>
    /// All downloads complete (success or failed).
    /// </summary>
    Complete
}

/// <summary>
/// Filter options for viewing items in the list.
/// </summary>
public enum ResultFilter
{
    All,
    MissingSubtitles,
    HasSubtitles,
    MoviesOnly,
    EpisodesOnly,
    Selected,
    Pending,
    Success,
    Failed
}
