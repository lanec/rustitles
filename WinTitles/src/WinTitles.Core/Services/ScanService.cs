using Microsoft.Extensions.Logging;
using WinTitles.Core.Models;

namespace WinTitles.Core.Services;

/// <summary>
/// Service for scanning media sources (Plex, folders) and checking subtitle status.
/// Separates discovery from downloading for proper workflow control.
/// </summary>
public class ScanService
{
    private readonly PlexService _plex;
    private readonly SettingsService _settings;
    private readonly ILogger<ScanService> _logger;
    
    public event Action<ScanProgress>? OnProgress;
    
    public ScanService(
        PlexService plex, 
        SettingsService settings,
        ILogger<ScanService> logger)
    {
        _plex = plex;
        _settings = settings;
        _logger = logger;
    }
    
    /// <summary>
    /// Scan Plex library and return all media items with subtitle status.
    /// Does NOT start any downloads - just discovery and status check.
    /// </summary>
    public async Task<ScanResult> ScanPlexAsync(CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        var result = new ScanResult { Source = "plex" };
        var items = new List<ScannedItem>();
        var languages = _settings.Settings.Languages ?? ["en"];
        
        var serverUrl = _settings.Settings.Plex.ServerUrl;
        var token = _settings.Settings.Plex.Token;
        
        if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(token))
        {
            result.Errors.Add("Plex server URL or token not configured");
            return result;
        }
        
        try
        {
            // Configure Plex service with credentials
            _plex.Configure(serverUrl, token);
            
            // Step 1: Get libraries
            ReportProgress("Connecting to Plex...");
            _logger.LogInformation("Starting Plex scan at {Url} with token length {TokenLen}", 
                serverUrl, token?.Length ?? 0);
            
            List<PlexLibrary> libraries;
            try
            {
                libraries = await _plex.GetLibrariesAsync(ct);
                _logger.LogInformation("GetLibrariesAsync returned {Count} libraries", libraries.Count);
                foreach (var lib in libraries)
                {
                    _logger.LogInformation("  Library: {Title} (type={Type}, key={Key})", lib.Title, lib.Type, lib.Key);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get libraries from Plex");
                result.Errors.Add($"Failed to connect to Plex: {ex.Message}");
                return result;
            }
            
            var mediaLibraries = libraries
                .Where(l => l.Type is "movie" or "show")
                .ToList();
            
            if (mediaLibraries.Count == 0)
            {
                var allTypes = string.Join(", ", libraries.Select(l => $"{l.Title}({l.Type})"));
                result.Errors.Add($"No movie or TV libraries found. Found: {allTypes}");
                _logger.LogWarning("No movie/show libraries. All libraries: {All}", allTypes);
                return result;
            }
            
            result.LibrariesScanned = mediaLibraries.Select(l => l.Title).ToList();
            _logger.LogInformation("Found {Count} media libraries: {Names}", 
                mediaLibraries.Count, string.Join(", ", result.LibrariesScanned));
            
            // Step 2: Count total items first for accurate progress
            int totalItems = 0;
            foreach (var library in mediaLibraries)
            {
                ct.ThrowIfCancellationRequested();
                ReportProgress($"Counting items in {library.Title}...");
                
                var count = await _plex.GetLibraryItemCountAsync(library.Key, ct);
                totalItems += count;
            }
            
            _logger.LogInformation("Total items to scan: {Count}", totalItems);
            
            // Step 3: Scan each library
            int processedItems = 0;
            int libraryIndex = 0;
            
            foreach (var library in mediaLibraries)
            {
                ct.ThrowIfCancellationRequested();
                libraryIndex++;
                
                ReportProgress(new ScanProgress
                {
                    Phase = "Scanning",
                    CurrentLibrary = library.Title,
                    LibraryCurrent = libraryIndex,
                    LibraryTotal = mediaLibraries.Count,
                    ItemsCurrent = processedItems,
                    ItemsTotal = totalItems
                });
                
                // Get all items from this library
                _logger.LogInformation("Fetching items from library {Name} (key={Key})", library.Title, library.Key);
                var mediaItems = await _plex.GetAllMediaItemsAsync(library.Key, ct);
                _logger.LogInformation("Library {Name} returned {Count} items", library.Title, mediaItems.Count);
                
                if (mediaItems.Count > 0)
                {
                    var first = mediaItems.First();
                    _logger.LogInformation("First item: {Title}, FilePath={Path}", first.Title, first.FilePath ?? "NULL");
                }
                
                foreach (var media in mediaItems)
                {
                    ct.ThrowIfCancellationRequested();
                    
                    var scannedItem = new ScannedItem
                    {
                        Id = Guid.NewGuid().ToString(),
                        PlexRatingKey = media.RatingKey,
                        Title = media.Title,
                        FilePath = media.FilePath ?? "",
                        Type = library.Type == "movie" ? MediaType.Movie : MediaType.Episode,
                        Year = media.Year,
                        ShowTitle = media.GrandparentTitle,
                        Season = media.ParentIndex,
                        Episode = media.Index,
                        LibraryName = library.Title
                    };
                    
                    // Check subtitle status
                    scannedItem.SubtitleStatus = CheckSubtitleStatus(
                        scannedItem.FilePath, 
                        languages,
                        out var existing,
                        out var missing);
                    
                    scannedItem.ExistingSubtitles = existing;
                    scannedItem.MissingLanguages = missing;
                    
                    // Pre-select items that need subtitles
                    scannedItem.IsSelected = scannedItem.SubtitleStatus != SubtitleStatus.HasAllLanguages 
                                           && scannedItem.SubtitleStatus != SubtitleStatus.FileNotFound;
                    
                    items.Add(scannedItem);
                    processedItems++;
                    
                    // Update progress and yield to UI thread frequently
                    if (processedItems % 50 == 0)
                    {
                        ReportProgress(new ScanProgress
                        {
                            Phase = "Checking subtitles",
                            CurrentLibrary = library.Title,
                            LibraryCurrent = libraryIndex,
                            LibraryTotal = mediaLibraries.Count,
                            ItemsCurrent = processedItems,
                            ItemsTotal = totalItems,
                            CurrentItem = scannedItem.DisplayName
                        });
                        
                        // Yield to allow UI to update and prevent freezing
                        await Task.Delay(1, ct);
                    }
                }
            }
            
            result.Items = items;
            result.ScanDuration = DateTime.UtcNow - startTime;
            result.CompletedAt = DateTime.UtcNow;
            
            _logger.LogInformation(
                "Scan complete: {Total} items, {WithSubs} with subtitles, {Missing} missing, took {Duration:F1}s",
                result.TotalFound, result.WithSubtitles, result.MissingSubtitles, 
                result.ScanDuration.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Scan cancelled by user");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Plex scan");
            result.Errors.Add($"Scan error: {ex.Message}");
        }
        
        return result;
    }
    
    /// <summary>
    /// Scan a local folder for video files and check subtitle status.
    /// </summary>
    public async Task<ScanResult> ScanFolderAsync(string folderPath, CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        var result = new ScanResult { Source = folderPath };
        var items = new List<ScannedItem>();
        var languages = _settings.Settings.Languages ?? ["en"];
        var ignoreExtras = _settings.Settings.IgnoreExtrasFolders;
        
        if (!Directory.Exists(folderPath))
        {
            result.Errors.Add($"Folder not found: {folderPath}");
            return result;
        }
        
        try
        {
            ReportProgress("Scanning folder...");
            _logger.LogInformation("Starting folder scan at {Path}", folderPath);
            
            // Find all video files
            var videoFiles = new List<string>();
            await Task.Run(() => FindVideoFiles(folderPath, videoFiles, ignoreExtras), ct);
            
            _logger.LogInformation("Found {Count} video files", videoFiles.Count);
            
            int processedItems = 0;
            foreach (var filePath in videoFiles)
            {
                ct.ThrowIfCancellationRequested();
                
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var parsed = ParseFileName(fileName);
                
                var scannedItem = new ScannedItem
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = parsed.Title,
                    FilePath = filePath,
                    Type = parsed.IsEpisode ? MediaType.Episode : MediaType.Movie,
                    Year = parsed.Year,
                    ShowTitle = parsed.ShowTitle,
                    Season = parsed.Season,
                    Episode = parsed.Episode
                };
                
                // Check subtitle status
                scannedItem.SubtitleStatus = CheckSubtitleStatus(
                    filePath, 
                    languages,
                    out var existing,
                    out var missing);
                
                scannedItem.ExistingSubtitles = existing;
                scannedItem.MissingLanguages = missing;
                scannedItem.IsSelected = scannedItem.SubtitleStatus != SubtitleStatus.HasAllLanguages;
                
                items.Add(scannedItem);
                processedItems++;
                
                if (processedItems % 50 == 0)
                {
                    ReportProgress(new ScanProgress
                    {
                        Phase = "Checking subtitles",
                        ItemsCurrent = processedItems,
                        ItemsTotal = videoFiles.Count,
                        CurrentItem = scannedItem.DisplayName
                    });
                    
                    // Yield to allow UI to update and prevent freezing
                    await Task.Delay(1, ct);
                }
            }
            
            result.Items = items;
            result.ScanDuration = DateTime.UtcNow - startTime;
            result.CompletedAt = DateTime.UtcNow;
            
            _logger.LogInformation(
                "Folder scan complete: {Total} items, {Missing} missing subtitles",
                result.TotalFound, result.MissingSubtitles);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Folder scan cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during folder scan");
            result.Errors.Add($"Scan error: {ex.Message}");
        }
        
        return result;
    }
    
    /// <summary>
    /// Check subtitle status for a single file.
    /// </summary>
    private SubtitleStatus CheckSubtitleStatus(
        string filePath,
        List<string> languages,
        out List<string> existingLanguages,
        out List<string> missingLanguages)
    {
        existingLanguages = [];
        missingLanguages = [];
        
        if (string.IsNullOrEmpty(filePath))
            return SubtitleStatus.FileNotFound;
            
        if (!File.Exists(filePath))
            return SubtitleStatus.FileNotFound;
        
        var dir = Path.GetDirectoryName(filePath);
        var baseName = Path.GetFileNameWithoutExtension(filePath);
        
        if (string.IsNullOrEmpty(dir))
            return SubtitleStatus.Unknown;
        
        foreach (var lang in languages)
        {
            bool found = false;
            
            // Check common subtitle patterns
            var patterns = new[]
            {
                $"{baseName}.{lang}.srt",
                $"{baseName}.{lang}.ass",
                $"{baseName}.{lang}.sub",
                $"{baseName}.{lang}.ssa",
                $"{baseName}.{lang}.vtt"
            };
            
            foreach (var pattern in patterns)
            {
                if (File.Exists(Path.Combine(dir, pattern)))
                {
                    found = true;
                    break;
                }
            }
            
            // Also check for subtitle without language code (common for single-language)
            if (!found && languages.Count == 1)
            {
                var noLangPatterns = new[]
                {
                    $"{baseName}.srt",
                    $"{baseName}.ass",
                    $"{baseName}.sub"
                };
                
                foreach (var pattern in noLangPatterns)
                {
                    if (File.Exists(Path.Combine(dir, pattern)))
                    {
                        found = true;
                        break;
                    }
                }
            }
            
            if (found)
                existingLanguages.Add(lang);
            else
                missingLanguages.Add(lang);
        }
        
        if (existingLanguages.Count == languages.Count)
            return SubtitleStatus.HasAllLanguages;
        else if (existingLanguages.Count > 0)
            return SubtitleStatus.MissingSome;
        else
            return SubtitleStatus.MissingAll;
    }
    
    /// <summary>
    /// Recursively find video files in a folder.
    /// </summary>
    private void FindVideoFiles(string folder, List<string> results, bool ignoreExtras)
    {
        var videoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v", ".webm", ".flv", ".ts"
        };
        
        var extrasFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Behind The Scenes", "Deleted Scenes", "Featurettes",
            "Interviews", "Scenes", "Shorts", "Trailers", "Other", "Extras"
        };
        
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var ext = Path.GetExtension(file);
                if (videoExtensions.Contains(ext))
                {
                    results.Add(file);
                }
            }
            
            foreach (var subDir in Directory.EnumerateDirectories(folder))
            {
                var dirName = Path.GetFileName(subDir);
                
                // Skip extras folders if configured
                if (ignoreExtras && extrasFolders.Contains(dirName))
                    continue;
                
                FindVideoFiles(subDir, results, ignoreExtras);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip folders we can't access
        }
    }
    
    /// <summary>
    /// Parse a video filename to extract metadata.
    /// </summary>
    private (string Title, int? Year, bool IsEpisode, string? ShowTitle, int? Season, int? Episode) ParseFileName(string fileName)
    {
        // Try to detect TV episode pattern: ShowName S01E02 or ShowName 1x02
        var episodeMatch = System.Text.RegularExpressions.Regex.Match(
            fileName, 
            @"^(.+?)[.\s_-]+[Ss](\d{1,2})[Ee](\d{1,2})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (episodeMatch.Success)
        {
            var showTitle = episodeMatch.Groups[1].Value.Replace(".", " ").Trim();
            var season = int.Parse(episodeMatch.Groups[2].Value);
            var episode = int.Parse(episodeMatch.Groups[3].Value);
            
            return (fileName, null, true, showTitle, season, episode);
        }
        
        // Try movie pattern: Title (Year) or Title.Year
        var movieMatch = System.Text.RegularExpressions.Regex.Match(
            fileName,
            @"^(.+?)[.\s_-]*[\(\[]?(19|20\d{2})[\)\]]?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (movieMatch.Success)
        {
            var title = movieMatch.Groups[1].Value.Replace(".", " ").Trim();
            var year = int.Parse(movieMatch.Groups[2].Value);
            
            return (title, year, false, null, null, null);
        }
        
        // Fallback: just use filename as title
        var cleanTitle = fileName.Replace(".", " ").Replace("_", " ").Trim();
        return (cleanTitle, null, false, null, null, null);
    }
    
    private void ReportProgress(string phase)
    {
        ReportProgress(new ScanProgress { Phase = phase });
    }
    
    private void ReportProgress(ScanProgress progress)
    {
        OnProgress?.Invoke(progress);
    }
}
