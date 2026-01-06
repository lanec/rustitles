using Microsoft.Extensions.Logging;
using WinTitles.Core.Models;

namespace WinTitles.Core.Services;

/// <summary>
/// Scans folders for video files that may need subtitles.
/// </summary>
public class FileScanner
{
    private readonly ILogger<FileScanner> _logger;
    
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v", ".webm", ".flv", ".ts", ".m2ts"
    };

    private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".sub", ".ssa", ".ass", ".vtt"
    };

    private static readonly HashSet<string> ExtrasFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "extras", "featurettes", "behind the scenes", "deleted scenes", 
        "interviews", "scenes", "shorts", "trailers", "other"
    };

    public FileScanner(ILogger<FileScanner> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Scan a folder for video files missing subtitles.
    /// </summary>
    public async Task<List<MediaItem>> ScanFolderAsync(
        string folderPath, 
        string language,
        bool ignoreExtras = true,
        CancellationToken ct = default)
    {
        var results = new List<MediaItem>();
        
        if (!Directory.Exists(folderPath))
        {
            _logger.LogWarning("Folder does not exist: {Path}", folderPath);
            return results;
        }

        _logger.LogInformation("Scanning folder: {Path}", folderPath);

        await Task.Run(() =>
        {
            var videoFiles = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(f => VideoExtensions.Contains(Path.GetExtension(f)))
                .Where(f => !ignoreExtras || !IsInExtrasFolder(f));

            foreach (var videoPath in videoFiles)
            {
                ct.ThrowIfCancellationRequested();

                if (HasSubtitle(videoPath, language))
                {
                    _logger.LogDebug("Skipping (has subtitle): {Path}", Path.GetFileName(videoPath));
                    continue;
                }

                var info = ParseVideoFileName(videoPath);
                results.Add(new MediaItem
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = info.Title,
                    Year = info.Year,
                    Type = info.Type,
                    FilePath = videoPath,
                    ShowTitle = info.ShowTitle,
                    SeasonNumber = info.Season,
                    EpisodeNumber = info.Episode
                });
            }
        }, ct);

        _logger.LogInformation("Found {Count} videos missing subtitles", results.Count);
        return results;
    }

    /// <summary>
    /// Check if a video file has a subtitle in the specified language.
    /// </summary>
    public bool HasSubtitle(string videoPath, string language)
    {
        var dir = Path.GetDirectoryName(videoPath) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(videoPath);

        foreach (var ext in SubtitleExtensions)
        {
            // Check for language-specific subtitle (e.g., movie.en.srt)
            var langSubtitle = Path.Combine(dir, $"{baseName}.{language}{ext}");
            if (File.Exists(langSubtitle)) return true;

            // Check for generic subtitle (e.g., movie.srt)
            var genericSubtitle = Path.Combine(dir, $"{baseName}{ext}");
            if (File.Exists(genericSubtitle)) return true;
        }

        return false;
    }

    private static bool IsInExtrasFolder(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => ExtrasFolders.Contains(p));
    }

    private static VideoInfo ParseVideoFileName(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var info = new VideoInfo { Title = fileName, Type = MediaType.Movie };

        // Try to detect TV show pattern: S01E01, 1x01, etc.
        var tvPatterns = new[]
        {
            @"[Ss](\d{1,2})[Ee](\d{1,2})",  // S01E01
            @"(\d{1,2})x(\d{1,2})",          // 1x01
            @"[Ss]eason\s*(\d{1,2}).*[Ee]pisode\s*(\d{1,2})"  // Season 1 Episode 1
        };

        foreach (var pattern in tvPatterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(fileName, pattern);
            if (match.Success)
            {
                info.Type = MediaType.Episode;
                info.Season = int.Parse(match.Groups[1].Value);
                info.Episode = int.Parse(match.Groups[2].Value);
                
                // Extract show title (everything before the pattern)
                var idx = match.Index;
                if (idx > 0)
                {
                    info.ShowTitle = CleanTitle(fileName[..idx]);
                    info.Title = info.ShowTitle;
                }
                break;
            }
        }

        // Try to extract year for movies
        if (info.Type == MediaType.Movie)
        {
            var yearMatch = System.Text.RegularExpressions.Regex.Match(fileName, @"\(?(19|20)\d{2}\)?");
            if (yearMatch.Success)
            {
                info.Year = int.Parse(yearMatch.Value.Trim('(', ')'));
                var idx = yearMatch.Index;
                if (idx > 0)
                {
                    info.Title = CleanTitle(fileName[..idx]);
                }
            }
            else
            {
                info.Title = CleanTitle(fileName);
            }
        }

        return info;
    }

    private static string CleanTitle(string title)
    {
        // Remove common separators and clean up
        return title
            .Replace('.', ' ')
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Trim();
    }

    private class VideoInfo
    {
        public string Title { get; set; } = "";
        public string? ShowTitle { get; set; }
        public int? Year { get; set; }
        public int? Season { get; set; }
        public int? Episode { get; set; }
        public MediaType Type { get; set; }
    }
}
