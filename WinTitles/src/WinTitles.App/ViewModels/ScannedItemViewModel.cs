using CommunityToolkit.Mvvm.ComponentModel;
using WinTitles.Core.Models;

namespace WinTitles.App.ViewModels;

/// <summary>
/// ViewModel for a scanned media item in the review list.
/// </summary>
public partial class ScannedItemViewModel : ObservableObject
{
    public string Id { get; }
    public ScannedItem Item { get; }
    
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _title;
    [ObservableProperty] private string _statusText;
    [ObservableProperty] private string _statusColor;
    [ObservableProperty] private string _typeIcon;
    [ObservableProperty] private string _filePath;
    
    public ScannedItemViewModel(ScannedItem item)
    {
        Id = item.Id;
        Item = item;
        _isSelected = item.IsSelected;
        _title = item.DisplayName;
        _filePath = item.FilePath;
        _typeIcon = item.Type == MediaType.Movie ? "🎬" : "📺";
        
        UpdateStatus();
    }
    
    partial void OnIsSelectedChanged(bool value)
    {
        Item.IsSelected = value;
    }
    
    private void UpdateStatus()
    {
        (StatusText, StatusColor) = Item.SubtitleStatus switch
        {
            SubtitleStatus.HasAllLanguages => ("✓ Has subtitles", "#50FA7B"),
            SubtitleStatus.MissingSome => ($"Missing: {string.Join(", ", Item.MissingLanguages)}", "#FFB86C"),
            SubtitleStatus.MissingAll => ($"Missing: {string.Join(", ", Item.MissingLanguages)}", "#FF5555"),
            SubtitleStatus.FileNotFound => ("File not found", "#6272A4"),
            _ => ("Unknown", "#6272A4")
        };
    }
}

/// <summary>
/// ViewModel for a queue item during/after download.
/// </summary>
public partial class QueueItemViewModel : ObservableObject
{
    public string Id { get; }
    public QueueItem Item { get; }
    
    [ObservableProperty] private string _title;
    [ObservableProperty] private string _statusText;
    [ObservableProperty] private string _statusIcon;
    [ObservableProperty] private string _statusColor;
    [ObservableProperty] private DownloadStatus _status;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private int _queuePosition;
    
    public QueueItemViewModel(QueueItem item)
    {
        Id = item.Id;
        Item = item;
        _title = item.DisplayName;
        _queuePosition = item.QueuePosition;
        Update(item);
    }
    
    public void Update(QueueItem item)
    {
        Status = item.Status;
        Error = item.Error;
        QueuePosition = item.QueuePosition;
        
        (StatusIcon, StatusText, StatusColor) = item.Status switch
        {
            DownloadStatus.Pending => ("⏳", $"#{item.QueuePosition} in queue", "#6272A4"),
            DownloadStatus.Downloading => ("🔄", "Downloading...", "#FFD700"),
            DownloadStatus.Success => ("✓", "Downloaded", "#50FA7B"),
            DownloadStatus.Failed => ("✗", item.Error ?? "Failed", "#FF5555"),
            DownloadStatus.Skipped => ("⊘", "Skipped", "#6272A4"),
            _ => ("?", "Unknown", "#FFFFFF")
        };
    }
}
