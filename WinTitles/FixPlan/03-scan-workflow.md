# Scan Workflow

## Overview
The scan phase discovers media from Plex or local folders, checks for existing subtitles, and presents results for user review before any downloads begin.

---

## Scan Flow Diagram

```
┌──────────────────┐
│   User clicks    │
│   "Scan Plex"    │
└────────┬─────────┘
         │
         ▼
┌──────────────────────────────────────────────────┐
│ 1. VALIDATE CONNECTION                            │
│    - Test Plex server reachability               │
│    - Verify token validity                       │
│    - Show error if connection fails              │
└────────┬─────────────────────────────────────────┘
         │
         ▼
┌──────────────────────────────────────────────────┐
│ 2. FETCH LIBRARY SECTIONS                         │
│    - Get all movie and TV show libraries         │
│    - User could optionally select which to scan  │
└────────┬─────────────────────────────────────────┘
         │
         ▼
┌──────────────────────────────────────────────────┐
│ 3. ENUMERATE ALL MEDIA ITEMS                      │
│    - Iterate through each library section        │
│    - For TV: get all episodes, not just shows    │
│    - Collect: title, year, file path, metadata   │
│    - Progress: "Scanning library 1 of 3..."      │
└────────┬─────────────────────────────────────────┘
         │
         ▼
┌──────────────────────────────────────────────────┐
│ 4. CHECK SUBTITLE STATUS (Batched)                │
│    - For each item, check if subtitle files exist│
│    - Check both embedded and external subtitles  │
│    - Compare against requested languages         │
│    - Progress: "Checking subtitles... 500/2000"  │
└────────┬─────────────────────────────────────────┘
         │
         ▼
┌──────────────────────────────────────────────────┐
│ 5. COMPILE RESULTS                                │
│    - Group by status: has subs, missing, unknown │
│    - Calculate statistics                        │
│    - Prepare for display in Review state         │
└────────┬─────────────────────────────────────────┘
         │
         ▼
┌──────────────────────────────────────────────────┐
│ 6. TRANSITION TO REVIEW STATE                     │
│    - Populate ScannedItems collection            │
│    - Show summary: "Found 2000 items"            │
│    - Pre-select items missing subtitles          │
│    - Wait for user action                        │
└──────────────────────────────────────────────────┘
```

---

## ScanService Implementation

```csharp
public class ScanService
{
    private readonly PlexService _plex;
    private readonly ILogger<ScanService> _logger;
    
    public event Action<ScanProgress>? OnProgress;
    
    public async Task<ScanResult> ScanPlexAsync(
        string serverUrl, 
        string token,
        List<string> languages,
        CancellationToken ct = default)
    {
        var result = new ScanResult();
        var items = new List<ScannedItem>();
        
        // Step 1: Get libraries
        OnProgress?.Invoke(new ScanProgress { Phase = "Connecting to Plex..." });
        
        var libraries = await _plex.GetLibrariesAsync(serverUrl, token, ct);
        var mediaLibraries = libraries.Where(l => l.Type is "movie" or "show").ToList();
        
        // Step 2: Get all media items
        int totalItems = 0;
        int processedItems = 0;
        
        foreach (var library in mediaLibraries)
        {
            OnProgress?.Invoke(new ScanProgress 
            { 
                Phase = $"Scanning {library.Title}...",
                LibraryCurrent = mediaLibraries.IndexOf(library) + 1,
                LibraryTotal = mediaLibraries.Count
            });
            
            var mediaItems = await _plex.GetAllMediaAsync(serverUrl, token, library.Key, ct);
            totalItems += mediaItems.Count;
            
            foreach (var media in mediaItems)
            {
                ct.ThrowIfCancellationRequested();
                
                var scannedItem = new ScannedItem
                {
                    Id = media.RatingKey,
                    Title = media.Title,
                    FilePath = media.FilePath,
                    Type = media.Type == "movie" ? MediaType.Movie : MediaType.Episode,
                    Year = media.Year,
                    ShowTitle = media.GrandparentTitle,
                    Season = media.ParentIndex,
                    Episode = media.Index,
                    IsSelected = false // Will set based on subtitle status
                };
                
                // Check subtitle status
                scannedItem.SubtitleStatus = await CheckSubtitleStatusAsync(
                    scannedItem.FilePath, 
                    languages,
                    ct);
                
                scannedItem.IsSelected = scannedItem.SubtitleStatus != SubtitleStatus.HasAllLanguages;
                
                items.Add(scannedItem);
                processedItems++;
                
                // Throttle progress updates
                if (processedItems % 10 == 0)
                {
                    OnProgress?.Invoke(new ScanProgress
                    {
                        Phase = $"Checking subtitles...",
                        ItemsCurrent = processedItems,
                        ItemsTotal = totalItems,
                        CurrentItem = scannedItem.Title
                    });
                }
            }
        }
        
        // Compile results
        result.Items = items;
        result.TotalFound = items.Count;
        result.WithSubtitles = items.Count(i => i.SubtitleStatus == SubtitleStatus.HasAllLanguages);
        result.MissingSubtitles = items.Count(i => i.SubtitleStatus != SubtitleStatus.HasAllLanguages);
        
        return result;
    }
    
    private async Task<SubtitleStatus> CheckSubtitleStatusAsync(
        string filePath,
        List<string> languages,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return SubtitleStatus.Unknown;
        
        var dir = Path.GetDirectoryName(filePath);
        var baseName = Path.GetFileNameWithoutExtension(filePath);
        
        var foundLanguages = new List<string>();
        
        foreach (var lang in languages)
        {
            // Check common subtitle patterns
            var patterns = new[]
            {
                $"{baseName}.{lang}.srt",
                $"{baseName}.{lang}.ass",
                $"{baseName}.{lang}.sub",
                $"{baseName}.srt" // No language code
            };
            
            foreach (var pattern in patterns)
            {
                if (File.Exists(Path.Combine(dir!, pattern)))
                {
                    foundLanguages.Add(lang);
                    break;
                }
            }
        }
        
        if (foundLanguages.Count == languages.Count)
            return SubtitleStatus.HasAllLanguages;
        else if (foundLanguages.Count > 0)
            return SubtitleStatus.MissingSome;
        else
            return SubtitleStatus.MissingAll;
    }
}

public class ScanProgress
{
    public string Phase { get; set; } = "";
    public int LibraryCurrent { get; set; }
    public int LibraryTotal { get; set; }
    public int ItemsCurrent { get; set; }
    public int ItemsTotal { get; set; }
    public string CurrentItem { get; set; } = "";
    
    public double ProgressPercent => ItemsTotal > 0 
        ? (double)ItemsCurrent / ItemsTotal * 100 
        : 0;
}
```

---

## Review State UI

After scanning completes, the UI should show:

```
┌─────────────────────────────────────────────────────────────────────┐
│ Scan Complete                                                        │
│ Found 2,000 items • 1,555 have subtitles • 445 need subtitles       │
├─────────────────────────────────────────────────────────────────────┤
│ [Select All Missing] [Deselect All] [Download Selected (445)]       │
├─────────────────────────────────────────────────────────────────────┤
│ Filter: [All ▾]  Search: [________________]                         │
├─────────────────────────────────────────────────────────────────────┤
│ ☑ The Patriot (2000)           Movie    Missing [en]     📁 D:\...  │
│ ☑ Patch Adams (1998)           Movie    Missing [en]     📁 D:\...  │
│ ☐ The Matrix (1999)            Movie    ✓ Has [en]       📁 D:\...  │
│ ☑ Breaking Bad S01E01          Episode  Missing [en]     📁 D:\...  │
│ ...                                                                  │
└─────────────────────────────────────────────────────────────────────┘
```

### Key Features:
1. **Summary bar** - Shows totals at a glance
2. **Bulk selection** - Select/deselect all, or just missing
3. **Filter dropdown** - All, Has Subtitles, Missing Subtitles
4. **Search box** - Filter by title
5. **Per-item selection** - Checkbox for each item
6. **Status indicator** - Clear visual for subtitle status
7. **File path** - Shows where file is located (hover for full path)

---

## Cancellation Handling

User can cancel at any point during scanning:

```csharp
private CancellationTokenSource? _scanCts;

[RelayCommand]
public async Task ScanPlexAsync()
{
    _scanCts = new CancellationTokenSource();
    State = WorkflowState.Scanning;
    
    try
    {
        var result = await _scanService.ScanPlexAsync(
            _settings.Settings.Plex.ServerUrl,
            _settings.Settings.Plex.Token,
            _settings.Settings.Languages,
            _scanCts.Token);
        
        // Populate items
        ScannedItems.Clear();
        foreach (var item in result.Items)
        {
            ScannedItems.Add(new ScannedItemViewModel(item));
        }
        
        State = WorkflowState.Review;
        StatusText = $"Found {result.TotalFound} items, {result.MissingSubtitles} missing subtitles";
    }
    catch (OperationCanceledException)
    {
        State = WorkflowState.Idle;
        StatusText = "Scan cancelled";
    }
    finally
    {
        _scanCts = null;
    }
}

[RelayCommand]
public void CancelScan()
{
    _scanCts?.Cancel();
}
```

---

## Next Document
See `04-download-queue.md` for the download queue implementation.
