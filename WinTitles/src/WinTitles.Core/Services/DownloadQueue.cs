using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WinTitles.Core.Models;
using WinTitles.Core.Services.Subtitles;

namespace WinTitles.Core.Services;

/// <summary>
/// A proper download queue with concurrency control, pause/resume, and progress tracking.
/// Replaces the fire-and-forget approach with controlled, observable processing.
/// </summary>
public class DownloadQueue : IDisposable
{
    private readonly OpenSubtitlesService _subtitles;
    private readonly SubtitleAggregator? _aggregator;
    private readonly DatabaseService _database;
    private readonly SettingsService _settings;
    private readonly ILogger<DownloadQueue> _logger;
    
    // Queue storage
    private readonly ConcurrentQueue<QueueItem> _pendingQueue = new();
    private readonly ConcurrentDictionary<string, QueueItem> _activeItems = new();
    private readonly List<QueueItem> _completedItems = [];
    private readonly object _completedLock = new();
    
    // Concurrency control
    private SemaphoreSlim _workerSemaphore = null!;
    private int _maxConcurrent = 5;
    
    // State management
    private CancellationTokenSource? _cts;
    private readonly ManualResetEventSlim _pauseEvent = new(true); // Initially not paused
    private Task? _processingTask;
    
    // Progress throttling
    private readonly System.Timers.Timer _progressTimer;
    private DateTime _lastProgressUpdate = DateTime.MinValue;
    private const int ProgressIntervalMs = 100;
    
    // Tracking
    private int _totalEnqueued;
    private DateTime _startTime;
    
    // Public state
    public QueueState State { get; private set; } = QueueState.Empty;
    public int TotalItems => _totalEnqueued;
    public int CompletedCount { get { lock (_completedLock) { return _completedItems.Count(i => i.Status == DownloadStatus.Success); } } }
    public int FailedCount { get { lock (_completedLock) { return _completedItems.Count(i => i.Status == DownloadStatus.Failed); } } }
    public int PendingCount => _pendingQueue.Count;
    public int ActiveCount => _activeItems.Count;
    
    // Events
    public event Action<QueueProgressUpdate>? OnProgress;
    public event Action<QueueItem>? OnItemCompleted;
    public event Action<QueueState>? OnStateChanged;
    
    /// <summary>
    /// Callback for when user input is needed (e.g., title confirmation).
    /// Returns the corrected title info, or null to skip the item.
    /// </summary>
    public Func<QueueItem, SmartSearchResult, Task<UserTitleInput?>>? OnUserInputNeeded;
    
    public DownloadQueue(
        OpenSubtitlesService subtitles,
        SubtitleAggregator? aggregator,
        DatabaseService database,
        SettingsService settings,
        ILogger<DownloadQueue> logger)
    {
        _subtitles = subtitles;
        _aggregator = aggregator;
        _database = database;
        _settings = settings;
        _logger = logger;
        
        // Initialize with settings
        _maxConcurrent = Math.Max(1, Math.Min(_settings.Settings.ConcurrentDownloads, 50));
        _workerSemaphore = new SemaphoreSlim(_maxConcurrent, _maxConcurrent);
        
        _progressTimer = new System.Timers.Timer(ProgressIntervalMs);
        _progressTimer.Elapsed += (s, e) => BroadcastProgress();
        _progressTimer.AutoReset = true;
    }
    
    /// <summary>
    /// Update concurrency limit (takes effect on next Start).
    /// </summary>
    public void SetConcurrency(int maxConcurrent)
    {
        _maxConcurrent = Math.Max(1, Math.Min(maxConcurrent, 50));
        _logger.LogInformation("Concurrency set to {Max}", _maxConcurrent);
    }
    
    /// <summary>
    /// Clear the queue and reset state.
    /// </summary>
    public void Clear()
    {
        Cancel();
        
        while (_pendingQueue.TryDequeue(out _)) { }
        _activeItems.Clear();
        lock (_completedLock) { _completedItems.Clear(); }
        
        _totalEnqueued = 0;
        SetState(QueueState.Empty);
        
        _logger.LogInformation("Queue cleared");
    }
    
    /// <summary>
    /// Add items to the queue. Does not start processing.
    /// </summary>
    public void Enqueue(IEnumerable<ScannedItem> items, string language)
    {
        int added = 0;
        int position = _pendingQueue.Count;
        
        foreach (var item in items.Where(i => i.IsSelected))
        {
            var queueItem = new QueueItem
            {
                Id = Guid.NewGuid().ToString(),
                ScannedItem = item,
                Language = language,
                Status = DownloadStatus.Pending,
                EnqueuedAt = DateTime.UtcNow,
                QueuePosition = position++
            };
            
            _pendingQueue.Enqueue(queueItem);
            added++;
        }
        
        _totalEnqueued = _pendingQueue.Count;
        
        if (_totalEnqueued > 0)
        {
            SetState(QueueState.Ready);
        }
        
        _logger.LogInformation("Enqueued {Count} items, total: {Total}", added, _totalEnqueued);
        BroadcastProgress();
    }
    
    /// <summary>
    /// Get all items (pending, active, completed) for UI display.
    /// </summary>
    public List<QueueItem> GetAllItems()
    {
        var items = new List<QueueItem>();
        items.AddRange(_pendingQueue);
        items.AddRange(_activeItems.Values);
        lock (_completedLock) { items.AddRange(_completedItems); }
        return items.OrderBy(i => i.EnqueuedAt).ToList();
    }
    
    /// <summary>
    /// Get completed items for filtering.
    /// </summary>
    public List<QueueItem> GetCompletedItems()
    {
        lock (_completedLock) { return _completedItems.ToList(); }
    }
    
    /// <summary>
    /// Start processing the queue.
    /// </summary>
    public async Task StartAsync()
    {
        if (State == QueueState.Running)
        {
            _logger.LogWarning("Queue already running");
            return;
        }
        
        if (_pendingQueue.IsEmpty && _activeItems.IsEmpty)
        {
            SetState(QueueState.Empty);
            return;
        }
        
        // Reinitialize semaphore with current setting
        _workerSemaphore?.Dispose();
        _workerSemaphore = new SemaphoreSlim(_maxConcurrent, _maxConcurrent);
        
        _cts = new CancellationTokenSource();
        _pauseEvent.Set(); // Ensure not paused
        _startTime = DateTime.UtcNow;
        
        SetState(QueueState.Running);
        
        // Start progress timer
        _progressTimer.Start();
        
        _logger.LogInformation("Starting queue processing with {Concurrent} concurrent downloads", _maxConcurrent);
        
        // Process queue in background
        _processingTask = ProcessQueueAsync(_cts.Token);
        
        try
        {
            await _processingTask;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Queue processing cancelled");
        }
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
        BroadcastProgress();
    }
    
    /// <summary>
    /// Resume processing after pause.
    /// </summary>
    public void Resume()
    {
        if (State != QueueState.Paused) return;
        
        _pauseEvent.Set(); // Unblock workers
        SetState(QueueState.Running);
        
        _logger.LogInformation("Queue resumed");
        BroadcastProgress();
    }
    
    /// <summary>
    /// Cancel all processing.
    /// </summary>
    public void Cancel()
    {
        if (_cts == null || _cts.IsCancellationRequested) return;
        
        _cts.Cancel();
        _pauseEvent.Set(); // Unblock any waiting workers
        _progressTimer.Stop();
        
        SetState(QueueState.Cancelled);
        
        _logger.LogInformation("Queue cancelled");
        BroadcastProgress();
    }
    
    /// <summary>
    /// Re-queue all failed items for retry.
    /// </summary>
    public void RetryFailed()
    {
        int retried = 0;
        
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
                item.StartedAt = null;
                item.CompletedAt = null;
                _completedItems.Remove(item);
                _pendingQueue.Enqueue(item);
                retried++;
            }
        }
        
        _totalEnqueued = _pendingQueue.Count + _activeItems.Count + CompletedCount + FailedCount;
        
        if (!_pendingQueue.IsEmpty)
        {
            SetState(QueueState.Ready);
        }
        
        _logger.LogInformation("Re-queued {Count} failed items for retry", retried);
        BroadcastProgress();
    }
    
    /// <summary>
    /// Retry a single item.
    /// </summary>
    public void RetryItem(string itemId)
    {
        lock (_completedLock)
        {
            var item = _completedItems.FirstOrDefault(i => i.Id == itemId);
            if (item != null && item.Status == DownloadStatus.Failed)
            {
                item.Status = DownloadStatus.Pending;
                item.Error = null;
                item.RetryCount++;
                item.StartedAt = null;
                item.CompletedAt = null;
                _completedItems.Remove(item);
                _pendingQueue.Enqueue(item);
                
                if (State == QueueState.Completed || State == QueueState.Empty)
                {
                    SetState(QueueState.Ready);
                }
                
                _logger.LogInformation("Re-queued item {Id} for retry", itemId);
            }
        }
        
        BroadcastProgress();
    }
    
    /// <summary>
    /// Main processing loop.
    /// </summary>
    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        var workers = new List<Task>();
        
        while (!ct.IsCancellationRequested)
        {
            // Wait if paused (with cancellation check) - use async-friendly approach
            while (!_pauseEvent.IsSet && !ct.IsCancellationRequested)
            {
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
            
            if (ct.IsCancellationRequested)
            {
                break;
            }
            
            // Try to get next item
            if (!_pendingQueue.TryDequeue(out var item))
            {
                // No more pending items
                if (_activeItems.IsEmpty)
                {
                    break; // All done
                }
                
                // Wait for active items to complete
                await Task.Delay(50, ct);
                continue;
            }
            
            // Wait for worker slot
            try
            {
                await _workerSemaphore.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // Put item back and exit
                _pendingQueue.Enqueue(item);
                break;
            }
            
            // Start worker for this item (don't await - run in parallel)
            var worker = ProcessItemAsync(item, ct);
            workers.Add(worker);
            
            // Clean up completed workers periodically
            workers.RemoveAll(t => t.IsCompleted);
        }
        
        // Wait for all active workers to complete
        if (workers.Count > 0)
        {
            await Task.WhenAll(workers);
        }
        
        _progressTimer.Stop();
        BroadcastProgress(); // Final update
        
        if (!ct.IsCancellationRequested)
        {
            SetState(QueueState.Completed);
            _logger.LogInformation("Queue completed: {Success} success, {Failed} failed", 
                CompletedCount, FailedCount);
        }
    }
    
    /// <summary>
    /// Process a single item using multi-provider aggregator with AI fallback.
    /// </summary>
    private async Task ProcessItemAsync(QueueItem item, CancellationToken ct)
    {
        _activeItems[item.Id] = item;
        item.Status = DownloadStatus.Downloading;
        item.StartedAt = DateTime.UtcNow;
        
        _logger.LogDebug("Starting download for {Title}", item.DisplayName);
        _logger.LogInformation("Aggregator available: {Available}", _aggregator != null);
        
        try
        {
            var scanned = item.ScannedItem;
            SubtitleDownloadResult downloadResult;
            
            // Use aggregator for multi-provider search (includes OpenSubtitles + Podnapisi)
            if (_aggregator != null)
            {
                _logger.LogInformation("Using multi-provider aggregator for {Title}", item.DisplayName);
                downloadResult = await ProcessWithAggregatorAsync(item, scanned, ct) 
                    ?? new SubtitleDownloadResult { Success = false, Error = "No subtitles found from any provider" };
            }
            else
            {
                // Fallback to direct OpenSubtitles only if aggregator not available
                var osResult = await _subtitles.DownloadWithMetadataAsync(
                    scanned.FilePath,
                    scanned.Title,
                    item.Language,
                    scanned.Year,
                    scanned.ShowTitle,
                    scanned.Season,
                    scanned.Episode,
                    ct);
                
                downloadResult = new SubtitleDownloadResult
                {
                    Success = osResult.Success,
                    FilePath = osResult.FilePath,
                    Error = osResult.Error,
                    RemainingDownloads = osResult.RemainingDownloads
                };
            }
            
            item.CompletedAt = DateTime.UtcNow;
            
            if (downloadResult.Success)
            {
                item.Status = DownloadStatus.Success;
                item.SubtitlePath = downloadResult.FilePath;
                _logger.LogInformation("Downloaded subtitle for {Title}", item.DisplayName);
            }
            else
            {
                item.Status = DownloadStatus.Failed;
                item.Error = downloadResult.Error ?? "Unknown error";
                _logger.LogWarning("Failed to download subtitle for {Title}: {Error}", 
                    item.DisplayName, item.Error);
            }
        }
        catch (OperationCanceledException)
        {
            // Put back in pending queue
            item.Status = DownloadStatus.Pending;
            item.StartedAt = null;
            _pendingQueue.Enqueue(item);
            _logger.LogDebug("Download cancelled for {Title}, returned to queue", item.DisplayName);
        }
        catch (Exception ex)
        {
            item.Status = DownloadStatus.Failed;
            item.Error = ex.Message;
            item.CompletedAt = DateTime.UtcNow;
            _logger.LogError(ex, "Error downloading subtitle for {Title}", item.DisplayName);
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
                try
                {
                    await _database.RecordDownloadAsync(item);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving download result to database");
                }
                
                // Notify listeners
                OnItemCompleted?.Invoke(item);
            }
            
            _workerSemaphore.Release();
        }
    }
    
    /// <summary>
    /// Process an item using the multi-provider aggregator.
    /// Uses fallback download - tries each provider until one succeeds.
    /// Does NOT block on user input - marks for later review instead.
    /// </summary>
    private async Task<SubtitleDownloadResult?> ProcessWithAggregatorAsync(
        QueueItem item, 
        ScannedItem scanned, 
        CancellationToken ct)
    {
        if (_aggregator == null) return null;
        
        // Build search request with all available metadata
        var request = new SubtitleSearchRequest
        {
            Query = scanned.Title,
            Language = item.Language,
            FilePath = scanned.FilePath,
            Year = scanned.Year,
            Season = scanned.Season,
            Episode = scanned.Episode,
            MediaType = scanned.Season.HasValue ? Subtitles.MediaType.Episode : Subtitles.MediaType.Movie
        };
        
        _logger.LogInformation("Searching all providers for: {Title}", scanned.Title);
        
        // Search all providers - don't use SmartSearch to avoid AI delays
        var searchResult = await _aggregator.SearchAllAsync(request, ct);
        
        // If no results, try hash-based search as fallback
        if (searchResult.TotalCount == 0 && !string.IsNullOrEmpty(scanned.FilePath))
        {
            _logger.LogDebug("No text results, trying filename-based search for: {File}", 
                Path.GetFileName(scanned.FilePath));
            
            var fileName = Path.GetFileNameWithoutExtension(scanned.FilePath);
            request = new SubtitleSearchRequest
            {
                Query = fileName,
                Language = item.Language,
                FilePath = scanned.FilePath
            };
            
            searchResult = await _aggregator.SearchAllAsync(request, ct);
        }
        
        // If still no results, mark for later review (don't block queue)
        if (searchResult.TotalCount == 0)
        {
            _logger.LogWarning("No subtitles found for {Title} from any provider", item.DisplayName);
            return new SubtitleDownloadResult
            {
                Success = false,
                Error = "No subtitles found from any provider"
            };
        }
        
        // Generate destination path
        var dir = Path.GetDirectoryName(scanned.FilePath) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(scanned.FilePath);
        var destPath = Path.Combine(dir, $"{baseName}.{item.Language}.srt");
        
        // Download with fallback - tries each provider until one succeeds
        return await _aggregator.DownloadWithFallbackAsync(searchResult, destPath, ct);
    }
    
    /// <summary>
    /// Broadcast progress update.
    /// </summary>
    private void BroadcastProgress()
    {
        var completed = CompletedCount;
        var failed = FailedCount;
        var active = ActiveCount;
        var pending = PendingCount;
        var total = _totalEnqueued;
        
        var update = new QueueProgressUpdate
        {
            Total = total,
            Completed = completed,
            Failed = failed,
            Active = active,
            Pending = pending,
            ProgressPercent = total > 0 ? (double)(completed + failed) / total * 100 : 0,
            CurrentItems = _activeItems.Values.Select(i => i.DisplayName).ToList(),
            Timestamp = DateTime.UtcNow
        };
        
        // Calculate ETA
        var totalProcessed = completed + failed;
        if (totalProcessed > 0 && pending > 0)
        {
            var elapsed = DateTime.UtcNow - _startTime;
            if (elapsed.TotalSeconds > 0)
            {
                var rate = totalProcessed / elapsed.TotalSeconds;
                if (rate > 0)
                {
                    update.EstimatedRemaining = TimeSpan.FromSeconds((pending + active) / rate);
                }
            }
        }
        
        OnProgress?.Invoke(update);
    }
    
    private void SetState(QueueState newState)
    {
        if (State != newState)
        {
            var oldState = State;
            State = newState;
            _logger.LogDebug("Queue state changed: {Old} -> {New}", oldState, newState);
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

/// <summary>
/// User-provided title information for retry search.
/// </summary>
public class UserTitleInput
{
    public required string Title { get; init; }
    public int? Year { get; init; }
    public int? Season { get; init; }
    public int? Episode { get; init; }
}
