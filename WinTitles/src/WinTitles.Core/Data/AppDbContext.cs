using Microsoft.EntityFrameworkCore;
using WinTitles.Core.Models;

namespace WinTitles.Core.Data;

/// <summary>
/// SQLite database context for persisting application state.
/// </summary>
public class AppDbContext : DbContext
{
    public DbSet<ScanHistoryItem> ScanHistory { get; set; }
    public DbSet<DownloadHistoryItem> DownloadHistory { get; set; }

    private readonly string _dbPath;

    public AppDbContext()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appData, "WinTitles");
        Directory.CreateDirectory(appFolder);
        _dbPath = Path.Combine(appFolder, "wintitles.db");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScanHistoryItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.FilePath);
            entity.HasIndex(e => e.ScannedAt);
        });

        modelBuilder.Entity<DownloadHistoryItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.VideoPath);
            entity.HasIndex(e => e.DownloadedAt);
        });
    }
}

/// <summary>
/// Represents a scanned media item in history.
/// </summary>
public class ScanHistoryItem
{
    public int Id { get; set; }
    public required string FilePath { get; set; }
    public required string Title { get; set; }
    public int? Year { get; set; }
    public required string MediaType { get; set; } // "Movie" or "Episode"
    public string? ShowTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public bool HasSubtitle { get; set; }
    public string? SubtitlePath { get; set; }
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;
    public string? Source { get; set; } // "plex", "folder", "drop"
}

/// <summary>
/// Represents a subtitle download in history.
/// </summary>
public class DownloadHistoryItem
{
    public int Id { get; set; }
    public required string VideoPath { get; set; }
    public required string Title { get; set; }
    public required string Language { get; set; }
    public required string Status { get; set; } // "Success", "Failed", "Skipped"
    public string? SubtitlePath { get; set; }
    public string? Error { get; set; }
    public DateTime DownloadedAt { get; set; } = DateTime.UtcNow;
    public string? Source { get; set; }
}
