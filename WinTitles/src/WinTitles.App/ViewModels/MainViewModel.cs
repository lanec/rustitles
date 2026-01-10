using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WinTitles.Core.Models;
using WinTitles.Core.Services;
using WinTitles.Core.Services.Subtitles;
using WinTitles.App.Views;

namespace WinTitles.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ILogger<MainViewModel> _logger;
    private readonly FileScanner _fileScanner;
    private readonly DownloadManager _downloadManager;
    private readonly SettingsService _settings;
    private readonly PlexService _plex;
    private readonly PlexWebhookServer _webhookServer;
    private readonly DatabaseService _database;
    private readonly ScanService _scanService;
    private readonly DownloadQueue _downloadQueue;
    private readonly OpenAIService _openAI;
    
    private CancellationTokenSource? _scanCts;

    // Workflow state
    [ObservableProperty] private WorkflowState _workflowState = WorkflowState.Idle;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Ready - Click 'Scan Plex' to find missing subtitles";
    [ObservableProperty] private bool _plexServiceRunning;
    
    // Scan progress
    [ObservableProperty] private string _scanPhase = "";
    [ObservableProperty] private int _scanCurrent;
    [ObservableProperty] private int _scanTotal;
    [ObservableProperty] private double _scanProgress;
    
    // Download progress  
    [ObservableProperty] private int _totalJobs;
    [ObservableProperty] private int _completedJobs;
    [ObservableProperty] private int _failedJobs;
    [ObservableProperty] private int _activeJobs;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _currentItem = "";
    [ObservableProperty] private string _etaText = "";
    
    // Scan results
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private int _missingCount;
    [ObservableProperty] private ResultFilter _currentFilter = ResultFilter.All;
    
    // IsPaused for button binding
    public bool IsPaused => WorkflowState == WorkflowState.Paused;

    // Collections
    public ObservableCollection<ScannedItemViewModel> ScannedItems { get; } = [];
    public ObservableCollection<QueueItemViewModel> QueueItems { get; } = [];
    public ObservableCollection<DownloadJobViewModel> RecentActivity { get; } = [];
    
    public AppSettings Settings => _settings.Settings;

    public MainViewModel(
        ILogger<MainViewModel> logger,
        FileScanner fileScanner,
        DownloadManager downloadManager,
        SettingsService settings,
        PlexService plex,
        PlexWebhookServer webhookServer,
        DatabaseService database,
        ScanService scanService,
        DownloadQueue downloadQueue,
        OpenAIService openAI)
    {
        _logger = logger;
        _fileScanner = fileScanner;
        _downloadManager = downloadManager;
        _settings = settings;
        _plex = plex;
        _webhookServer = webhookServer;
        _database = database;
        _scanService = scanService;
        _openAI = openAI;
        _downloadQueue = downloadQueue;

        // Subscribe to scan service events
        _scanService.OnProgress += OnScanProgress;
        
        // Subscribe to download queue events
        _downloadQueue.OnProgress += OnQueueProgress;
        _downloadQueue.OnItemCompleted += OnQueueItemCompleted;
        _downloadQueue.OnStateChanged += OnQueueStateChanged;
        _downloadQueue.OnUserInputNeeded = OnUserTitleInputNeeded;

        // Subscribe to webhook events
        _webhookServer.OnMediaAdded += OnPlexMediaAdded;

        // Load history from database
        _ = LoadHistoryAsync();
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            var history = await _database.GetRecentDownloadsAsync(50);
            
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var item in history)
                {
                    RecentActivity.Add(new DownloadJobViewModel(new DownloadJob
                    {
                        Id = item.Id.ToString(),
                        VideoPath = item.VideoPath,
                        Title = item.Title,
                        Language = item.Language,
                        Status = Enum.TryParse<DownloadStatus>(item.Status, out var status) ? status : DownloadStatus.Pending,
                        SubtitlePath = item.SubtitlePath,
                        Error = item.Error,
                        Source = item.Source,
                        MediaType = WinTitles.Core.Models.MediaType.Movie
                    }));
                }

                if (history.Count > 0)
                {
                    var stats = _database.GetStatsAsync().GetAwaiter().GetResult();
                    StatusText = $"History: {stats.SuccessfulDownloads} downloaded, {stats.FailedDownloads} failed";
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading history");
        }
    }
    
    private void OnScanProgress(ScanProgress progress)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ScanPhase = progress.Phase;
            ScanCurrent = progress.ItemsCurrent;
            ScanTotal = progress.ItemsTotal;
            ScanProgress = progress.ProgressPercent;
            CurrentItem = progress.CurrentItem;
            StatusText = progress.StatusText;
        });
    }
    
    private void OnQueueProgress(QueueProgressUpdate update)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            TotalJobs = update.Total;
            CompletedJobs = update.Completed;
            FailedJobs = update.Failed;
            ActiveJobs = update.Active;
            Progress = update.ProgressPercent;
            EtaText = update.EtaText;
            
            if (update.CurrentItems.Count > 0)
            {
                CurrentItem = update.CurrentItems[0];
            }
            
            StatusText = $"Downloading: {update.Completed} of {update.Total} complete";
            if (update.Failed > 0)
            {
                StatusText += $" ({update.Failed} failed)";
            }
        });
    }
    
    private void OnQueueItemCompleted(QueueItem item)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            // Update existing item or add new one
            var existing = QueueItems.FirstOrDefault(q => q.Id == item.Id);
            if (existing != null)
            {
                existing.Update(item);
            }
            else
            {
                QueueItems.Insert(0, new QueueItemViewModel(item));
            }
        });
    }
    
    private void OnQueueStateChanged(QueueState state)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            WorkflowState = state switch
            {
                QueueState.Running => WorkflowState.Downloading,
                QueueState.Paused => WorkflowState.Paused,
                QueueState.Completed => WorkflowState.Complete,
                QueueState.Cancelled => WorkflowState.Idle,
                _ => WorkflowState
            };
            
            OnPropertyChanged(nameof(IsPaused));
            
            if (state == QueueState.Completed)
            {
                StatusText = $"Complete: {CompletedJobs} downloaded, {FailedJobs} failed";
            }
        });
    }
    
    private void UpdateSelectedCount()
    {
        SelectedCount = ScannedItems.Count(i => i.IsSelected);
    }

    [RelayCommand]
    public async Task ScanFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select folder to scan for videos"
        };

        if (dialog.ShowDialog() == true)
        {
            await ScanFolderPathAsync(dialog.FolderName);
        }
    }

    public async Task ScanFolderPathAsync(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath)) return;
        if (WorkflowState != WorkflowState.Idle) return;

        _scanCts = new CancellationTokenSource();
        WorkflowState = WorkflowState.Scanning;
        ScannedItems.Clear();
        QueueItems.Clear();

        try
        {
            var result = await _scanService.ScanFolderAsync(folderPath, _scanCts.Token);
            
            // Populate scanned items for review
            foreach (var item in result.Items)
            {
                ScannedItems.Add(new ScannedItemViewModel(item));
            }
            
            MissingCount = result.MissingSubtitles;
            UpdateSelectedCount();
            
            WorkflowState = WorkflowState.Review;
            StatusText = $"Found {result.TotalFound} items, {result.MissingSubtitles} missing subtitles";
            
            if (result.Errors.Count > 0)
            {
                StatusText += $" ({result.Errors.Count} errors)";
            }
        }
        catch (OperationCanceledException)
        {
            WorkflowState = WorkflowState.Idle;
            StatusText = "Scan cancelled";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning folder");
            WorkflowState = WorkflowState.Idle;
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            _scanCts = null;
        }
    }

    [RelayCommand]
    public async Task ScanPlexLibraryAsync()
    {
        if (!Settings.Plex.Enabled)
        {
            StatusText = "Plex integration not configured. Go to Settings to configure.";
            return;
        }
        
        if (WorkflowState != WorkflowState.Idle) return;

        _scanCts = new CancellationTokenSource();
        WorkflowState = WorkflowState.Scanning;
        ScannedItems.Clear();
        QueueItems.Clear();

        try
        {
            _plex.Configure(Settings.Plex.ServerUrl, Settings.Plex.Token);
            
            _logger.LogInformation("Starting Plex scan from ViewModel...");
            var result = await _scanService.ScanPlexAsync(_scanCts.Token);
            _logger.LogInformation("Scan returned: {Count} items, {Errors} errors", 
                result.Items.Count, result.Errors.Count);
            
            // Log any errors
            foreach (var error in result.Errors)
            {
                _logger.LogError("Scan error: {Error}", error);
            }
            
            // Populate scanned items for review
            foreach (var item in result.Items)
            {
                ScannedItems.Add(new ScannedItemViewModel(item));
            }
            
            MissingCount = result.MissingSubtitles;
            UpdateSelectedCount();
            
            WorkflowState = WorkflowState.Review;
            
            if (result.Errors.Count > 0)
            {
                StatusText = $"Scan completed with errors: {string.Join("; ", result.Errors)}";
            }
            else
            {
                StatusText = $"Found {result.TotalFound} items, {result.MissingSubtitles} missing subtitles";
            }
        }
        catch (OperationCanceledException)
        {
            WorkflowState = WorkflowState.Idle;
            StatusText = "Scan cancelled";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning Plex library");
            WorkflowState = WorkflowState.Idle;
            StatusText = $"Error: {ex.Message}";
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
    
    [RelayCommand]
    public async Task StartDownloadsAsync()
    {
        if (WorkflowState != WorkflowState.Review) return;
        
        var selectedItems = ScannedItems
            .Where(i => i.IsSelected)
            .Select(i => i.Item)
            .ToList();
        
        if (selectedItems.Count == 0)
        {
            StatusText = "No items selected for download";
            return;
        }
        
        var language = Settings.Languages.FirstOrDefault() ?? "en";
        
        // Clear old queue items and populate with selected
        QueueItems.Clear();
        _downloadQueue.Clear();
        _downloadQueue.SetConcurrency(Settings.ConcurrentDownloads);
        _downloadQueue.Enqueue(selectedItems, language);
        
        // Add to UI collection
        foreach (var queueItem in _downloadQueue.GetAllItems())
        {
            QueueItems.Add(new QueueItemViewModel(queueItem));
        }
        
        WorkflowState = WorkflowState.Downloading;
        await _downloadQueue.StartAsync();
    }
    
    [RelayCommand]
    public void SelectAll()
    {
        foreach (var item in ScannedItems)
        {
            item.IsSelected = true;
        }
        UpdateSelectedCount();
    }
    
    [RelayCommand]
    public void DeselectAll()
    {
        foreach (var item in ScannedItems)
        {
            item.IsSelected = false;
        }
        UpdateSelectedCount();
    }
    
    [RelayCommand]
    public void SelectMissing()
    {
        foreach (var item in ScannedItems)
        {
            item.IsSelected = item.Item.SubtitleStatus != SubtitleStatus.HasAllLanguages;
        }
        UpdateSelectedCount();
    }

    [RelayCommand]
    public async Task StartPlexServiceAsync()
    {
        if (PlexServiceRunning) return;

        try
        {
            await _webhookServer.StartAsync(Settings.Plex.WebhookPort);
            PlexServiceRunning = true;
            StatusText = $"Plex webhook listening on port {Settings.Plex.WebhookPort}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting Plex webhook server");
            StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task StopPlexServiceAsync()
    {
        if (!PlexServiceRunning) return;

        await _webhookServer.StopAsync();
        PlexServiceRunning = false;
        StatusText = "Plex webhook stopped";
    }

    [RelayCommand]
    public void PauseDownloads()
    {
        if (WorkflowState == WorkflowState.Paused)
        {
            _downloadQueue.Resume();
            StatusText = "Downloads resumed";
        }
        else if (WorkflowState == WorkflowState.Downloading)
        {
            _downloadQueue.Pause();
            StatusText = "Downloads paused";
        }
        OnPropertyChanged(nameof(IsPaused));
    }

    [RelayCommand]
    public async Task RetryFailedAsync()
    {
        if (WorkflowState == WorkflowState.Complete && FailedJobs > 0)
        {
            _downloadQueue.RetryFailed();
            await _downloadQueue.StartAsync();
        }
    }

    [RelayCommand]
    public void ClearActivity()
    {
        _downloadQueue.Clear();
        ScannedItems.Clear();
        QueueItems.Clear();
        RecentActivity.Clear();
        TotalJobs = 0;
        CompletedJobs = 0;
        FailedJobs = 0;
        Progress = 0;
        WorkflowState = WorkflowState.Idle;
        StatusText = "Ready - Click 'Scan Plex' to find missing subtitles";
    }
    
    [RelayCommand]
    public void CancelDownloads()
    {
        _downloadQueue.Cancel();
        WorkflowState = WorkflowState.Idle;
        StatusText = "Downloads cancelled";
    }
    
    [RelayCommand]
    public void BackToReview()
    {
        if (WorkflowState == WorkflowState.Complete)
        {
            WorkflowState = WorkflowState.Review;
        }
    }
    
    [RelayCommand]
    public async Task AnalyzeErrorsAsync()
    {
        if (FailedJobs == 0)
        {
            MessageBox.Show("No failed downloads to analyze.", "No Errors", 
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        if (string.IsNullOrEmpty(Settings.OpenAI?.ApiKey))
        {
            MessageBox.Show("Please configure your OpenAI API key in Settings first.", 
                "OpenAI Not Configured", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        StatusText = "Analyzing errors with AI...";
        
        try
        {
            var failedItems = _downloadQueue.GetCompletedItems()
                .Where(i => i.Status == DownloadStatus.Failed)
                .Select(i => (i.DisplayName, i.Error ?? "Unknown error"))
                .ToList();
            
            var analysis = await _openAI.AnalyzeErrorsAsync(
                failedItems,
                TotalJobs,
                CompletedJobs);
            
            if (!string.IsNullOrEmpty(analysis.Error) && analysis.Categories.Count == 0)
            {
                MessageBox.Show($"Analysis failed: {analysis.Error}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            
            // Build result message
            var message = $"📊 ERROR ANALYSIS\n\n";
            message += $"📝 Summary:\n{analysis.Summary}\n\n";
            
            if (analysis.Categories.Count > 0)
            {
                message += "📋 Error Categories:\n";
                foreach (var cat in analysis.Categories)
                {
                    message += $"\n• {cat.Name}: {cat.Count} items\n";
                    message += $"  {cat.Description}\n";
                    if (!string.IsNullOrEmpty(cat.Fix))
                        message += $"  💡 Fix: {cat.Fix}\n";
                }
            }
            
            if (analysis.Recommendations.Count > 0)
            {
                message += "\n🎯 Recommendations:\n";
                foreach (var rec in analysis.Recommendations)
                {
                    message += $"• {rec}\n";
                }
            }
            
            if (!string.IsNullOrEmpty(analysis.SuggestedAction))
            {
                message += $"\n⚡ SUGGESTED NEXT ACTION:\n{analysis.SuggestedAction}";
            }
            
            MessageBox.Show(message, "AI Error Analysis", 
                MessageBoxButton.OK, MessageBoxImage.Information);
            
            StatusText = $"Analysis complete - {analysis.Categories.Count} error categories identified";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing failures");
            MessageBox.Show($"Analysis failed: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Error analysis failed";
        }
    }

    private async void OnPlexMediaAdded(PlexWebhookEvent evt)
    {
        _logger.LogInformation("Plex webhook received: {Title} (key={Key})", evt.Title, evt.RatingKey);

        // Only auto-process if enabled in settings
        if (!Settings.Plex.AutoStartWebhook)
        {
            _logger.LogInformation("Auto-processing disabled, ignoring webhook");
            return;
        }

        try
        {
            _plex.Configure(Settings.Plex.ServerUrl, Settings.Plex.Token);
            var item = await _plex.GetMetadataAsync(evt.RatingKey);

            if (item?.FilePath == null)
            {
                _logger.LogWarning("No file path found for {Title}", evt.Title);
                return;
            }

            var language = Settings.Languages.FirstOrDefault() ?? "en";
            
            // Check if subtitle already exists
            if (_fileScanner.HasSubtitle(item.FilePath, language))
            {
                _logger.LogInformation("Subtitle already exists for {Title}", evt.Title);
                return;
            }

            // Create a ScannedItem for the queue
            var scannedItem = new ScannedItem
            {
                Id = Guid.NewGuid().ToString(),
                PlexRatingKey = item.RatingKey,
                Title = item.Title,
                FilePath = item.FilePath,
                Type = item.Type,
                Year = item.Year,
                ShowTitle = item.ShowTitle,
                Season = item.SeasonNumber,
                Episode = item.EpisodeNumber,
                SubtitleStatus = SubtitleStatus.MissingAll,
                IsSelected = true
            };

            Application.Current.Dispatcher.Invoke(async () =>
            {
                // Add to queue and start if not already running
                _downloadQueue.Enqueue([scannedItem], language);
                
                // Add to UI
                foreach (var queueItem in _downloadQueue.GetAllItems().Where(q => q.ScannedItem.Id == scannedItem.Id))
                {
                    QueueItems.Add(new QueueItemViewModel(queueItem));
                }
                
                StatusText = $"Webhook: Downloading subtitle for {item.DisplayName}...";
                
                if (_downloadQueue.State != QueueState.Running)
                {
                    WorkflowState = WorkflowState.Downloading;
                    await _downloadQueue.StartAsync();
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Plex webhook for {Title}", evt.Title);
        }
    }

    /// <summary>
    /// Called when the download queue needs user input to confirm a title.
    /// Shows the TitleConfirmationWindow dialog.
    /// </summary>
    private async Task<UserTitleInput?> OnUserTitleInputNeeded(QueueItem item, SmartSearchResult searchResult)
    {
        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var fileName = System.IO.Path.GetFileNameWithoutExtension(item.ScannedItem.FilePath);
            var parsedInfo = new MediaInfo
            {
                OriginalName = fileName,
                CleanTitle = item.ScannedItem.Title,
                Year = item.ScannedItem.Year,
                Season = item.ScannedItem.Season,
                Episode = item.ScannedItem.Episode,
                Type = item.ScannedItem.Season.HasValue 
                    ? WinTitles.Core.Services.Subtitles.MediaType.Episode 
                    : WinTitles.Core.Services.Subtitles.MediaType.Movie
            };

            var dialog = new TitleConfirmationWindow(
                fileName,
                searchResult.AiSuggestions,
                parsedInfo)
            {
                Owner = Application.Current.MainWindow
            };

            var result = dialog.ShowDialog();

            if (result == true && !dialog.WasSkipped && !string.IsNullOrEmpty(dialog.ConfirmedTitle))
            {
                _logger.LogInformation("User confirmed title: {Title}", dialog.ConfirmedTitle);
                return new UserTitleInput
                {
                    Title = dialog.ConfirmedTitle,
                    Year = dialog.ConfirmedYear,
                    Season = dialog.ConfirmedSeason,
                    Episode = dialog.ConfirmedEpisode
                };
            }

            _logger.LogInformation("User skipped title confirmation for {Item}", item.DisplayName);
            return null;
        });
    }
}

public partial class DownloadJobViewModel : ObservableObject
{
    public string Id { get; }
    
    [ObservableProperty] private string _title;
    [ObservableProperty] private string _language;
    [ObservableProperty] private DownloadStatus _status;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string _statusIcon = "📥";
    [ObservableProperty] private string _statusColor = "#FFFFFF";

    public DownloadJobViewModel(DownloadJob job)
    {
        Id = job.Id;
        _title = job.Title;
        _language = job.Language;
        Update(job);
    }

    public void Update(DownloadJob job)
    {
        Title = job.Title;
        Status = job.Status;
        Error = job.Error;

        (StatusIcon, StatusColor) = job.Status switch
        {
            DownloadStatus.Pending => ("📥", "#FFFFFF"),
            DownloadStatus.Downloading => ("⏳", "#FFD700"),
            DownloadStatus.Success => ("✓", "#50FA7B"),
            DownloadStatus.Failed => ("✗", "#FF5555"),
            DownloadStatus.Skipped => ("⊘", "#6272A4"),
            _ => ("?", "#FFFFFF")
        };
    }
}
