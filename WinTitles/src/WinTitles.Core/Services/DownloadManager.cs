using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using WinTitles.Core.Models;

namespace WinTitles.Core.Services;

/// <summary>
/// Manages subtitle download queue and execution.
/// </summary>
public class DownloadManager : IDisposable
{
    private readonly ILogger<DownloadManager> _logger;
    private readonly OpenSubtitlesService _openSubtitles;
    private readonly DatabaseService _database;
    private readonly ConcurrentDictionary<string, DownloadJob> _jobs = new();
    private readonly Channel<DownloadJob> _queue;
    private readonly List<Task> _workers = [];
    private CancellationTokenSource? _cts;
    private int _concurrency = 5; // Lower for API rate limits
    private bool _isPaused;

    public event Action<DownloadJob>? OnJobUpdated;
    public event Action? OnQueueEmpty;

    public int TotalJobs => _jobs.Count;
    public int PendingJobs => _jobs.Values.Count(j => j.Status == DownloadStatus.Pending);
    public int CompletedJobs => _jobs.Values.Count(j => j.Status == DownloadStatus.Success);
    public int FailedJobs => _jobs.Values.Count(j => j.Status == DownloadStatus.Failed);
    public bool IsPaused => _isPaused;
    public bool IsRunning => _workers.Count > 0 && !_isPaused;

    public DownloadManager(ILogger<DownloadManager> logger, OpenSubtitlesService openSubtitles, DatabaseService database)
    {
        _logger = logger;
        _openSubtitles = openSubtitles;
        _database = database;
        _queue = Channel.CreateUnbounded<DownloadJob>();
    }

    /// <summary>
    /// Set the number of concurrent downloads.
    /// </summary>
    public void SetConcurrency(int count)
    {
        _concurrency = Math.Max(1, Math.Min(50, count));
    }

    /// <summary>
    /// Queue items for download.
    /// </summary>
    public void QueueDownloads(IEnumerable<MediaItem> items, string language, string source = "folder")
    {
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.FilePath)) continue;

            var job = new DownloadJob
            {
                Id = item.Id,
                VideoPath = item.FilePath,
                Title = item.DisplayName,
                Year = item.Year,
                MediaType = item.Type,
                Language = language,
                Source = source
            };

            if (_jobs.TryAdd(job.Id, job))
            {
                _queue.Writer.TryWrite(job);
                OnJobUpdated?.Invoke(job);
            }
        }

        _logger.LogInformation("Queued {Count} items for download", items.Count());
    }

    /// <summary>
    /// Start processing the download queue.
    /// </summary>
    public void Start()
    {
        if (_workers.Count > 0) return;

        _cts = new CancellationTokenSource();
        _isPaused = false;

        for (int i = 0; i < _concurrency; i++)
        {
            _workers.Add(Task.Run(() => ProcessQueueAsync(_cts.Token)));
        }

        _logger.LogInformation("Started {Count} download workers", _concurrency);
    }

    /// <summary>
    /// Pause downloads.
    /// </summary>
    public void Pause()
    {
        _isPaused = true;
        _logger.LogInformation("Downloads paused");
    }

    /// <summary>
    /// Resume downloads.
    /// </summary>
    public void Resume()
    {
        _isPaused = false;
        _logger.LogInformation("Downloads resumed");
    }

    /// <summary>
    /// Stop all downloads and clear queue.
    /// </summary>
    public async Task StopAsync()
    {
        _cts?.Cancel();
        
        if (_workers.Count > 0)
        {
            await Task.WhenAll(_workers);
            _workers.Clear();
        }

        _cts?.Dispose();
        _cts = null;
        _logger.LogInformation("Downloads stopped");
    }

    /// <summary>
    /// Retry failed downloads.
    /// </summary>
    public void RetryFailed()
    {
        var failed = _jobs.Values.Where(j => j.Status == DownloadStatus.Failed).ToList();
        foreach (var job in failed)
        {
            job.Status = DownloadStatus.Pending;
            job.Error = null;
            _queue.Writer.TryWrite(job);
            OnJobUpdated?.Invoke(job);
        }

        _logger.LogInformation("Retrying {Count} failed downloads", failed.Count);
    }

    /// <summary>
    /// Clear completed and failed jobs.
    /// </summary>
    public void ClearCompleted()
    {
        var toRemove = _jobs.Values
            .Where(j => j.Status is DownloadStatus.Success or DownloadStatus.Failed or DownloadStatus.Skipped)
            .Select(j => j.Id)
            .ToList();

        foreach (var id in toRemove)
        {
            _jobs.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Get all jobs.
    /// </summary>
    public IEnumerable<DownloadJob> GetJobs() => _jobs.Values.OrderByDescending(j => j.CreatedAt);

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(ct))
        {
            if (ct.IsCancellationRequested) break;

            // Wait while paused
            while (_isPaused && !ct.IsCancellationRequested)
            {
                await Task.Delay(500, ct);
            }

            if (ct.IsCancellationRequested) break;

            try
            {
                job.Status = DownloadStatus.Downloading;
                OnJobUpdated?.Invoke(job);

                OpenSubtitlesDownloadResult result;
                
                // Use metadata-based search if we have metadata (more accurate)
                if (!string.IsNullOrEmpty(job.Title))
                {
                    result = await _openSubtitles.DownloadWithMetadataAsync(
                        job.VideoPath,
                        job.Title,
                        job.Language,
                        job.Year,
                        job.ShowTitle,
                        job.SeasonNumber,
                        job.EpisodeNumber,
                        ct);
                }
                else
                {
                    // Fall back to filename-based search
                    result = await _openSubtitles.DownloadForVideoAsync(
                        job.VideoPath, 
                        job.Language,
                        ct);
                }

                job.CompletedAt = DateTime.UtcNow;

                if (result.Success)
                {
                    job.Status = DownloadStatus.Success;
                    job.SubtitlePath = result.FilePath;
                }
                else
                {
                    job.Status = DownloadStatus.Failed;
                    job.Error = result.Error;
                }

                // Save to database
                await _database.RecordDownloadAsync(job);

                OnJobUpdated?.Invoke(job);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                job.Status = DownloadStatus.Failed;
                job.Error = ex.Message;
                job.CompletedAt = DateTime.UtcNow;
                OnJobUpdated?.Invoke(job);
                _logger.LogError(ex, "Error downloading subtitle for {Path}", job.VideoPath);
            }
        }

        // Check if queue is empty
        if (_queue.Reader.Count == 0 && PendingJobs == 0)
        {
            OnQueueEmpty?.Invoke();
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
