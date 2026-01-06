# New Architecture

## Overview
This document proposes a clean separation of concerns with distinct phases for the subtitle workflow.

---

## State Machine

```
┌─────────┐     ┌──────────┐     ┌────────┐     ┌─────────────┐     ┌──────────┐
│  IDLE   │────▶│ SCANNING │────▶│ REVIEW │────▶│ DOWNLOADING │────▶│ COMPLETE │
└─────────┘     └──────────┘     └────────┘     └─────────────┘     └──────────┘
                                      │                │                  │
                                      │                │                  │
                                      ▼                ▼                  ▼
                                 [User can         [User can         [User can
                                  exclude           pause/            retry
                                  items]            cancel]           failed]
```

### State Definitions

| State | Description | User Actions Available |
|-------|-------------|------------------------|
| **IDLE** | No operation in progress | Start Scan, Open Settings |
| **SCANNING** | Discovering media from Plex | Cancel Scan |
| **REVIEW** | Showing discovered media | Select/Deselect items, Start Downloads, Cancel |
| **DOWNLOADING** | Processing download queue | Pause, Resume, Cancel, View Progress |
| **COMPLETE** | All downloads finished | Filter results, Retry Failed, Manual Search, Clear |

---

## Core Components

### 1. ScanService (New)
Responsible only for discovering media and checking subtitle status.

```csharp
public class ScanService
{
    // Scan Plex library and return all media items with subtitle status
    Task<ScanResult> ScanPlexAsync(string serverUrl, string token, CancellationToken ct);
    
    // Scan local folder
    Task<ScanResult> ScanFolderAsync(string folderPath, CancellationToken ct);
    
    // Check if a single item has subtitles
    Task<SubtitleStatus> CheckSubtitlesAsync(MediaItem item, List<string> languages);
}

public class ScanResult
{
    public List<ScannedItem> Items { get; set; }
    public int TotalFound { get; set; }
    public int WithSubtitles { get; set; }
    public int MissingSubtitles { get; set; }
    public TimeSpan ScanDuration { get; set; }
}

public class ScannedItem
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string FilePath { get; set; }
    public MediaType Type { get; set; }
    public int? Year { get; set; }
    public string? ShowTitle { get; set; }
    public int? Season { get; set; }
    public int? Episode { get; set; }
    public SubtitleStatus SubtitleStatus { get; set; }
    public List<string> ExistingSubtitles { get; set; }
    public bool IsSelected { get; set; } = true; // For user selection
}

public enum SubtitleStatus
{
    Unknown,
    HasAllLanguages,
    MissingSome,
    MissingAll
}
```

### 2. DownloadQueue (Redesigned)
A proper queue with concurrency control and status tracking.

```csharp
public class DownloadQueue
{
    // Queue state
    public QueueState State { get; }
    public int TotalItems { get; }
    public int CompletedItems { get; }
    public int FailedItems { get; }
    public int PendingItems { get; }
    public int ActiveItems { get; }
    
    // Operations
    Task EnqueueAsync(IEnumerable<ScannedItem> items, string language);
    Task StartAsync();
    Task PauseAsync();
    Task ResumeAsync();
    Task CancelAsync();
    Task RetryFailedAsync();
    Task RetryItemAsync(string itemId);
    
    // Events (throttled for UI)
    event Action<QueueProgressUpdate> OnProgress;
    event Action<DownloadResult> OnItemCompleted;
    event Action<QueueState> OnStateChanged;
}

public enum QueueState
{
    Empty,
    Ready,      // Has items, not started
    Running,    // Actively processing
    Paused,     // Temporarily stopped
    Cancelled,  // User cancelled
    Completed   // All items processed
}

public class QueueProgressUpdate
{
    public int Total { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
    public int Active { get; set; }
    public double ProgressPercent { get; set; }
    public string CurrentItemTitle { get; set; }
    public TimeSpan? EstimatedRemaining { get; set; }
}
```

### 3. MainViewModel (Simplified)
Orchestrates the workflow without mixing concerns.

```csharp
public partial class MainViewModel : ObservableObject
{
    // Current workflow state
    [ObservableProperty] WorkflowState _state = WorkflowState.Idle;
    
    // Scan results (shown in Review state)
    public ObservableCollection<ScannedItemViewModel> ScannedItems { get; }
    
    // Download queue items (shown in Downloading/Complete states)  
    public ObservableCollection<DownloadItemViewModel> DownloadItems { get; }
    
    // Filter for viewing results
    [ObservableProperty] ResultFilter _currentFilter = ResultFilter.All;
    
    // Progress information
    [ObservableProperty] QueueProgressUpdate _progress;
    
    // Commands
    ICommand ScanPlexCommand { get; }
    ICommand ScanFolderCommand { get; }
    ICommand StartDownloadsCommand { get; }
    ICommand PauseCommand { get; }
    ICommand ResumeCommand { get; }
    ICommand CancelCommand { get; }
    ICommand RetryFailedCommand { get; }
    ICommand RetryItemCommand { get; }
    ICommand ManualSearchCommand { get; }
    ICommand SelectAllCommand { get; }
    ICommand DeselectAllCommand { get; }
    ICommand ClearCommand { get; }
}

public enum WorkflowState
{
    Idle,
    Scanning,
    Review,
    Downloading,
    Paused,
    Complete
}

public enum ResultFilter
{
    All,
    Pending,
    Success,
    Failed,
    Skipped
}
```

---

## Data Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│                              USER CLICKS                                 │
│                             "Scan Plex"                                  │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ 1. ScanService.ScanPlexAsync()                                          │
│    - Fetches all media from Plex                                        │
│    - Checks each item for existing subtitles                            │
│    - Returns ScanResult with all items                                  │
│    - UI shows progress: "Scanning... 150 of 2000 items"                 │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ 2. REVIEW STATE                                                          │
│    - ScannedItems collection populated                                   │
│    - UI shows: "Found 2000 items, 445 missing subtitles"                │
│    - User can scroll through all items                                  │
│    - User can select/deselect items                                     │
│    - User clicks "Download Selected" when ready                         │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ 3. DownloadQueue.EnqueueAsync(selectedItems)                            │
│    - Only selected items with missing subtitles added to queue          │
│    - Queue state: Ready                                                 │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ 4. DownloadQueue.StartAsync()                                           │
│    - Processes up to N concurrent downloads (from settings)             │
│    - Each completion triggers OnItemCompleted                           │
│    - Progress updates throttled to 100ms intervals                      │
│    - UI shows real progress with ETA                                    │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ 5. COMPLETE STATE                                                        │
│    - All items processed (success, failed, or skipped)                  │
│    - User can filter to see only failed items                           │
│    - User can retry failed items individually or in bulk                │
│    - User can manually search for specific items                        │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Key Design Principles

### 1. Separation of Phases
Never mix scanning and downloading. Each phase completes before the next begins.

### 2. User Control
User must explicitly trigger transitions between phases. No automatic progression.

### 3. Visibility
Every item's status is visible at all times. Nothing disappears or gets "lost".

### 4. Throttled UI Updates
Batch UI updates to prevent overwhelming the render thread. Max 10 updates/second.

### 5. Proper Concurrency
Use `Channel<T>` or `BlockingCollection<T>` with dedicated worker tasks, not fire-and-forget.

### 6. Resilient State
State persisted to database so app can resume after restart.

---

## Next Document
See `03-scan-workflow.md` for detailed scan phase implementation.
