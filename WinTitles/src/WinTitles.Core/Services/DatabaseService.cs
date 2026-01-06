using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WinTitles.Core.Data;
using WinTitles.Core.Models;

namespace WinTitles.Core.Services;

/// <summary>
/// Service for managing SQLite database operations.
/// </summary>
public class DatabaseService
{
    private readonly ILogger<DatabaseService> _logger;
    private readonly AppDbContext _db;

    public DatabaseService(ILogger<DatabaseService> logger)
    {
        _logger = logger;
        _db = new AppDbContext();
    }

    /// <summary>
    /// Initialize the database (create if not exists, run migrations).
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            await _db.Database.EnsureCreatedAsync();
            _logger.LogInformation("Database initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing database");
        }
    }

    /// <summary>
    /// Record a scanned media item.
    /// </summary>
    public async Task RecordScanAsync(MediaItem item, string source)
    {
        try
        {
            var existing = await _db.ScanHistory
                .FirstOrDefaultAsync(s => s.FilePath == item.FilePath);

            if (existing != null)
            {
                existing.ScannedAt = DateTime.UtcNow;
                existing.HasSubtitle = false; // Will be updated after download
            }
            else
            {
                _db.ScanHistory.Add(new ScanHistoryItem
                {
                    FilePath = item.FilePath ?? "",
                    Title = item.Title,
                    Year = item.Year,
                    MediaType = item.Type.ToString(),
                    ShowTitle = item.ShowTitle,
                    SeasonNumber = item.SeasonNumber,
                    EpisodeNumber = item.EpisodeNumber,
                    Source = source
                });
            }

            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording scan");
        }
    }

    /// <summary>
    /// Record a download attempt from a QueueItem.
    /// </summary>
    public async Task RecordDownloadAsync(QueueItem item)
    {
        try
        {
            _db.DownloadHistory.Add(new DownloadHistoryItem
            {
                VideoPath = item.ScannedItem?.FilePath ?? "",
                Title = item.DisplayName,
                Language = item.Language,
                Status = item.Status.ToString(),
                SubtitlePath = item.SubtitlePath,
                Error = item.Error,
                Source = "queue",
                DownloadedAt = item.CompletedAt ?? DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording download from queue item");
        }
    }

    /// <summary>
    /// Record a download attempt from a DownloadJob.
    /// </summary>
    public async Task RecordDownloadAsync(DownloadJob job)
    {
        try
        {
            _db.DownloadHistory.Add(new DownloadHistoryItem
            {
                VideoPath = job.VideoPath,
                Title = job.Title,
                Language = job.Language,
                Status = job.Status.ToString(),
                SubtitlePath = job.SubtitlePath,
                Error = job.Error,
                DownloadedAt = job.CompletedAt ?? DateTime.UtcNow,
                Source = job.Source
            });

            // Update scan history if download was successful
            if (job.Status == DownloadStatus.Success)
            {
                var scanItem = await _db.ScanHistory
                    .FirstOrDefaultAsync(s => s.FilePath == job.VideoPath);
                if (scanItem != null)
                {
                    scanItem.HasSubtitle = true;
                    scanItem.SubtitlePath = job.SubtitlePath;
                }
            }

            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording download");
        }
    }

    /// <summary>
    /// Get recent download history.
    /// </summary>
    public async Task<List<DownloadHistoryItem>> GetRecentDownloadsAsync(int limit = 100)
    {
        try
        {
            return await _db.DownloadHistory
                .OrderByDescending(d => d.DownloadedAt)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting download history");
            return [];
        }
    }

    /// <summary>
    /// Get recent scan history.
    /// </summary>
    public async Task<List<ScanHistoryItem>> GetRecentScansAsync(int limit = 100)
    {
        try
        {
            return await _db.ScanHistory
                .OrderByDescending(s => s.ScannedAt)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting scan history");
            return [];
        }
    }

    /// <summary>
    /// Get items that still need subtitles.
    /// </summary>
    public async Task<List<ScanHistoryItem>> GetPendingItemsAsync()
    {
        try
        {
            return await _db.ScanHistory
                .Where(s => !s.HasSubtitle)
                .OrderByDescending(s => s.ScannedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending items");
            return [];
        }
    }

    /// <summary>
    /// Get download statistics.
    /// </summary>
    public async Task<DownloadStats> GetStatsAsync()
    {
        try
        {
            var total = await _db.DownloadHistory.CountAsync();
            var success = await _db.DownloadHistory.CountAsync(d => d.Status == "Success");
            var failed = await _db.DownloadHistory.CountAsync(d => d.Status == "Failed");
            var pending = await _db.ScanHistory.CountAsync(s => !s.HasSubtitle);

            return new DownloadStats
            {
                TotalDownloads = total,
                SuccessfulDownloads = success,
                FailedDownloads = failed,
                PendingItems = pending
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting stats");
            return new DownloadStats();
        }
    }

    /// <summary>
    /// Clear all history.
    /// </summary>
    public async Task ClearHistoryAsync()
    {
        try
        {
            _db.DownloadHistory.RemoveRange(_db.DownloadHistory);
            _db.ScanHistory.RemoveRange(_db.ScanHistory);
            await _db.SaveChangesAsync();
            _logger.LogInformation("History cleared");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing history");
        }
    }
}

public class DownloadStats
{
    public int TotalDownloads { get; init; }
    public int SuccessfulDownloads { get; init; }
    public int FailedDownloads { get; init; }
    public int PendingItems { get; init; }
}
