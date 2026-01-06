# Download Queue

## Overview
A properly designed download queue that respects concurrency limits, provides accurate progress, and allows pause/resume/retry operations.

---

## Core Problems to Solve

1. **Concurrency Control** - Process exactly N items at a time
2. **Progress Tracking** - Know exactly how many are pending, active, completed, failed
3. **UI Updates** - Throttle updates to prevent overwhelming the UI thread
4. **State Persistence** - Survive app restart
5. **Cancellation** - Clean cancellation that doesn't leave orphaned tasks
6. **Retry Logic** - Retry failed items without re-processing successful ones

---

## Queue Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                            DownloadQueue                                     │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐                   │
│  │   PENDING    │───▶│    ACTIVE    │───▶│  COMPLETED   │                   │
│  │    Queue     │    │   Workers    │    │    List      │                   │
│  │              │    │   (max N)    │    │              │                   │
│  │  Item 5      │    │  Item 1 ░░░  │    │  Item A ✓    │                   │
│  │  Item 6      │    │  Item 2 ▓▓░  │    │  Item B ✓    │                   │
│  │  Item 7      │    │  Item 3 ▓░░  │    │  Item C ✗    │                   │
│  │  ...         │    │              │    │  ...         │                   │
│  └──────────────┘    └──────────────┘    └──────────────┘                   │
│         │                   │                   │                            │
│         └───────────────────┴───────────────────┘                            │
│                             │                                                │
│                     Progress Aggregator                                      │
│                    (throttled to 100ms)                                      │
│                             │                                                │
│                      OnProgress Event ──────▶ UI                             │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Implementation

```csharp
public class DownloadQueue : IDisposable
{
    private readonly OpenSubtitlesService _subtitles;
    private readonly DatabaseService _database;
    private readonly ILogger<DownloadQueue> _logger;
    
    // Queue storage
    private readonly ConcurrentQueue<QueueItem> _pendingQueue = new();
    private readonly ConcurrentDictionary<string, QueueItem> _activeItems = new();
    private readonly List<QueueItem> _completedItems = new();
    private readonly object _completedLock = new();
    
    // Concurrency control
    private SemaphoreSlim _workerSemaphore = null!;
    private int _maxConcurrent = 5;
    
    // State management
    private CancellationTokenSource? _cts;
    private readonly ManualResetEventSlim _pauseEvent = new(true); // Initially not paused
    
    // Progress throttling
    private readonly Timer _progressTimer;
    private DateTime _lastProgressUpdate = DateTime.MinValue;
    private const int ProgressIntervalMs = 100;
    
    // Public state
    public QueueState State { get; private set; } = QueueState.Empty;
    public int TotalItems { get; private set; }
    public int CompletedCount => _completedItems.Count(i => i.Status == DownloadStatus.Success);
    public int FailedCount => _completedItems.Count(i => i.Status == DownloadStatus.Failed);
    public int PendingCount => _pendingQueue.Count;
    public int ActiveCount => _activeItems.Count;
    
    // Events
    public event Action<QueueProgressUpdate>? OnProgress;
    public event Action<QueueItem>? OnItemCompleted;
    public event Action<QueueState>? OnStateChanged;
    
    public DownloadQueue(
        OpenSubtitlesService subtitles,
        DatabaseService database,
        ILogger<DownloadQueue> logger)
    {
        _subtitles = subtitles;
        _database = database;
        _logger = logger;
        
        _progressTimer = new Timer(BroadcastProgress, null, Timeout.Infinite, Timeout.Infinite);
    }
    
    /// <summary>
    /// Configure max concurrent downloads from settings.
    /// </summary>
    public void SetConcurrency(int maxConcurrent)
    {
        _maxConcurrent = Math.Max(1, Math.Min(maxConcurrent, 50));
        _workerSemaphore = new SemaphoreSlim(_maxConcurrent, _maxConcurrent);
    }
    
    /// <summary>
    /// Add items to the queue. Does not start processing.
    /// </summary>
    public void Enqueue(IEnumerable<ScannedItem> items, string language)
    {
        foreach (var item in items.Where(i => i.IsSelected))
        {
            var queueItem = new QueueItem
            {
                Id = Guid.NewGuid().ToString(),
                ScannedItem = item,
                Language = language,
                Status = DownloadStatus.Pending,
                EnqueuedAt = DateTime.UtcNow
            };
            
            _pendingQueue.Enqueue(queueItem);
        }
        
        TotalItems = _pendingQueue.Count;
        SetState(TotalItems > 0 ? QueueState.Ready : QueueState.Empty);
    }
    
    /// <summary>
    /// Start processing the queue.
    /// </summary>
    public async Task StartAsync()
    {
        if (State == QueueState.Running) return;
        if (_pendingQueue.IsEmpty && _activeItems.IsEmpty)
        {
            SetState(QueueState.Empty);
            return;
        }
        
        _cts = new CancellationTokenSource();
        _pauseEvent.Set(); // Ensure not paused
        SetState(QueueState.Running);
        
        // Start progress timer
        _progressTimer.Change(0, ProgressIntervalMs);
        
        // Process queue
        await ProcessQueueAsync(_cts.Token);
    }
    
    /// <summary>
    /// Pause processing. Active downloads complete, but no new ones start.
    /// </summary>
    public void Pause()
    {
        if (State != QueueState.Running) return;
        
        _pauseEvent.Reset(); // Block workers from taking new items
        SetState(QueueState.Paused);
        
        _logger.LogInformation("Queue paused. {Active} items still processing.", ActiveCount);
    }
    
    /// <summary>
    /// Resume processing after pause.
    /// </summary>
    public async Task ResumeAsync()
    {
        if (State != QueueState.Paused) return;
        
        _pauseEvent.Set(); // Unblock workers
        SetState(QueueState.Running);
        
        _logger.LogInformation("Queue resumed.");
        
        // Continue processing if needed
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            await ProcessQueueAsync(_cts.Token);
        }
    }
    
    /// <summary>
    /// Cancel all processing. Active downloads are cancelled.
    /// </summary>
    public void Cancel()
    {
        _cts?.Cancel();
        _pauseEvent.Set(); // Unblock any waiting workers
        _progressTimer.Change(Timeout.Infinite, Timeout.Infinite);
        
        SetState(QueueState.Cancelled);
        
        _logger.LogInformation("Queue cancelled.");
    }
    
    /// <summary>
    /// Re-queue all failed items for retry.
    /// </summary>
    public void RetryFailed()
    {
        lock (_completedLock)
        {
            var failedItems = _completedItems
                .Where(i => i.Status == DownloadStatus.Failed)
                .ToList();
            
            foreach (var item in failedItems)
            {
                item.Status = DownloadStatus.Pending;
                item.Error = null;
                item.RetryCount++;
                _completedItems.Remove(item);
                _pendingQueue.Enqueue(item);
            }
        }
        
        if (!_pendingQueue.IsEmpty)
        {
            SetState(QueueState.Ready);
        }
        
        _logger.LogInformation("Re-queued {Count} failed items for retry.", _pendingQueue.Count);
    }
    
    /// <summary>
    /// Main processing loop.
    /// </summary>
    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        var workers = new List<Task>();
        
        while (!ct.IsCancellationRequested)
        {
            // Wait if paused
            _pauseEvent.Wait(ct);
            
            // Try to get next item
            if (!_pendingQueue.TryDequeue(out var item))
            {
                // No more pending items
                if (_activeItems.IsEmpty)
                {
                    break; // All done
                }
                
                // Wait for active items to complete
                await Task.Delay(100, ct);
                continue;
            }
            
            // Wait for worker slot
            await _workerSemaphore.WaitAsync(ct);
            
            // Start worker for this item
            var worker = ProcessItemAsync(item, ct);
            workers.Add(worker);
            
            // Clean up completed workers periodically
            workers.RemoveAll(t => t.IsCompleted);
        }
        
        // Wait for all active workers to complete
        await Task.WhenAll(workers);
        
        _progressTimer.Change(Timeout.Infinite, Timeout.Infinite);
        BroadcastProgress(null); // Final update
        
        if (!ct.IsCancellationRequested)
        {
            SetState(QueueState.Completed);
        }
    }
    
    /// <summary>
    /// Process a single item.
    /// </summary>
    private async Task ProcessItemAsync(QueueItem item, CancellationToken ct)
    {
        _activeItems[item.Id] = item;
        item.Status = DownloadStatus.Downloading;
        item.StartedAt = DateTime.UtcNow;
        
        try
        {
            var result = await _subtitles.DownloadWithMetadataAsync(
                item.ScannedItem.FilePath,
                item.ScannedItem.Title,
                item.Language,
                item.ScannedItem.Year,
                item.ScannedItem.ShowTitle,
                item.ScannedItem.Season,
                item.ScannedItem.Episode,
                ct);
            
            item.CompletedAt = DateTime.UtcNow;
            
            if (result.Success)
            {
                item.Status = DownloadStatus.Success;
                item.SubtitlePath = result.FilePath;
            }
            else
            {
                item.Status = DownloadStatus.Failed;
                item.Error = result.Error;
            }
        }
        catch (OperationCanceledException)
        {
            item.Status = DownloadStatus.Pending; // Return to pending
            _pendingQueue.Enqueue(item);
        }
        catch (Exception ex)
        {
            item.Status = DownloadStatus.Failed;
            item.Error = ex.Message;
            _logger.LogError(ex, "Error downloading subtitle for {Title}", item.ScannedItem.Title);
        }
        finally
        {
            _activeItems.TryRemove(item.Id, out _);
            
            if (item.Status != DownloadStatus.Pending)
            {
                lock (_completedLock)
                {
                    _completedItems.Add(item);
                }
                
                // Save to database
                await _database.RecordDownloadAsync(item);
                
                OnItemCompleted?.Invoke(item);
            }
            
            _workerSemaphore.Release();
        }
    }
    
    /// <summary>
    /// Broadcast progress update (throttled).
    /// </summary>
    private void BroadcastProgress(object? state)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastProgressUpdate).TotalMilliseconds < ProgressIntervalMs)
            return;
        
        _lastProgressUpdate = now;
        
        var completed = _completedItems.Count;
        var failed = FailedCount;
        var active = ActiveCount;
        var pending = PendingCount;
        var total = TotalItems;
        
        var update = new QueueProgressUpdate
        {
            Total = total,
            Completed = completed,
            Failed = failed,
            Active = active,
            Pending = pending,
            ProgressPercent = total > 0 ? (double)completed / total * 100 : 0,
            CurrentItems = _activeItems.Values.Select(i => i.ScannedItem.Title).ToList()
        };
        
        // Calculate ETA
        if (completed > 0 && pending > 0)
        {
            var elapsed = DateTime.UtcNow - _completedItems.FirstOrDefault()?.StartedAt;
            if (elapsed.HasValue && elapsed.Value.TotalSeconds > 0)
            {
                var rate = completed / elapsed.Value.TotalSeconds;
                if (rate > 0)
                {
                    update.EstimatedRemaining = TimeSpan.FromSeconds(pending / rate);
                }
            }
        }
        
        OnProgress?.Invoke(update);
    }
    
    private void SetState(QueueState newState)
    {
        if (State != newState)
        {
            State = newState;
            OnStateChanged?.Invoke(newState);
        }
    }
    
    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _pauseEvent.Dispose();
        _progressTimer.Dispose();
        _workerSemaphore?.Dispose();
    }
}

public class QueueItem
{
    public string Id { get; set; } = "";
    public ScannedItem ScannedItem { get; set; } = null!;
    public string Language { get; set; } = "en";
    public DownloadStatus Status { get; set; }
    public string? SubtitlePath { get; set; }
    public string? Error { get; set; }
    public int RetryCount { get; set; }
    public DateTime EnqueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class QueueProgressUpdate
{
    public int Total { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
    public int Active { get; set; }
    public int Pending { get; set; }
    public double ProgressPercent { get; set; }
    public List<string> CurrentItems { get; set; } = new();
    public TimeSpan? EstimatedRemaining { get; set; }
}
```

---

## Key Design Decisions

### 1. SemaphoreSlim for Concurrency
```csharp
await _workerSemaphore.WaitAsync(ct);
try { /* process */ }
finally { _workerSemaphore.Release(); }
```
Guarantees exactly N concurrent operations.

### 2. ManualResetEventSlim for Pause
```csharp
_pauseEvent.Wait(ct); // Blocks when paused
```
Workers check this before taking new items. Active downloads complete.

### 3. Timer-Based Progress Updates
```csharp
_progressTimer = new Timer(BroadcastProgress, null, 0, 100);
```
UI gets updates at most every 100ms, regardless of download speed.

### 4. Completed Items Stay in Memory
All completed items (success and failed) remain accessible for filtering and retry.

### 5. CancellationToken Throughout
Every async operation accepts the cancellation token for clean shutdown.

---

## Next Document
See `05-ui-redesign.md` for the UI changes needed.
