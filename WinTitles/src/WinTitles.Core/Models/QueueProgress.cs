namespace WinTitles.Core.Models;

/// <summary>
/// Progress update from the download queue.
/// Throttled to prevent overwhelming the UI.
/// </summary>
public class QueueProgressUpdate
{
    public int Total { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
    public int Active { get; set; }
    public int Pending { get; set; }
    public double ProgressPercent { get; set; }
    public List<string> CurrentItems { get; set; } = [];
    public TimeSpan? EstimatedRemaining { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    public string StatusText => Total > 0
        ? $"{Completed:N0} of {Total:N0} complete"
        : "Ready";
    
    public string DetailText
    {
        get
        {
            var parts = new List<string>();
            if (Failed > 0) parts.Add($"{Failed} failed");
            if (Active > 0) parts.Add($"{Active} active");
            if (Pending > 0) parts.Add($"{Pending} pending");
            return string.Join(" • ", parts);
        }
    }
    
    public string EtaText => EstimatedRemaining.HasValue
        ? $"~{EstimatedRemaining.Value.TotalMinutes:F0} minutes remaining"
        : "";
}

/// <summary>
/// State of the download queue.
/// </summary>
public enum QueueState
{
    Empty,
    Ready,      // Has items, not started
    Running,    // Actively processing
    Paused,     // Temporarily stopped
    Cancelled,  // User cancelled
    Completed   // All items processed
}
