using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using WinTitles.App.ViewModels;
using WinTitles.Core.Services;
using WinTitles.Core.Services.Subtitles;

namespace WinTitles.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IServiceProvider _serviceProvider;

    public MainWindow(MainViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _serviceProvider = serviceProvider;
        DataContext = viewModel;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsService = _serviceProvider.GetRequiredService<SettingsService>();
        var plexService = _serviceProvider.GetRequiredService<PlexService>();
        var plexAuthService = _serviceProvider.GetRequiredService<PlexAuthService>();
        var openAIService = _serviceProvider.GetRequiredService<OpenAIService>();
        var settingsWindow = new SettingsWindow(settingsService, plexService, plexAuthService, openAIService)
        {
            Owner = this
        };
        settingsWindow.ShowDialog();
    }

    private async void PlexServiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.PlexServiceRunning)
        {
            await _viewModel.StopPlexServiceAsync();
        }
        else
        {
            await _viewModel.StartPlexServiceAsync();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Minimize to tray instead of closing
        e.Cancel = true;
        Hide();
    }
    
    private async void AISearchSuggestions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag is ViewModels.ScannedItemViewModel item)
        {
            var openAIService = _serviceProvider.GetRequiredService<OpenAIService>();
            var settingsService = _serviceProvider.GetRequiredService<SettingsService>();
            
            if (string.IsNullOrEmpty(settingsService.Settings.OpenAI?.ApiKey))
            {
                MessageBox.Show("Please configure your OpenAI API key in Settings first.", 
                    "OpenAI Not Configured", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            // Show loading dialog
            var loadingWindow = new Window
            {
                Title = "Getting AI Suggestions...",
                Width = 400,
                Height = 100,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#282A36")),
                ResizeMode = ResizeMode.NoResize,
                Content = new TextBlock
                {
                    Text = "Analyzing file name with AI...",
                    Foreground = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            loadingWindow.Show();
            
            try
            {
                var suggestion = await openAIService.GenerateSearchQueryAsync(
                    Path.GetFileName(item.FilePath),
                    item.FilePath,
                    item.Item?.Type.ToString() ?? "movie");
                
                loadingWindow.Close();
                
                if (!string.IsNullOrEmpty(suggestion.Error))
                {
                    MessageBox.Show($"AI Error: {suggestion.Error}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                // Build list of all query options
                var queryOptions = new List<string>();
                if (!string.IsNullOrEmpty(suggestion.SearchQuery))
                    queryOptions.Add(suggestion.SearchQuery);
                if (suggestion.AlternativeQueries?.Count > 0)
                    queryOptions.AddRange(suggestion.AlternativeQueries.Where(q => !string.IsNullOrEmpty(q)));
                
                if (queryOptions.Count == 0)
                {
                    MessageBox.Show("AI couldn't generate any search queries.", "No Suggestions", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // Show selection dialog
                var currentFileName = Path.GetFileNameWithoutExtension(item.FilePath);
                var (selectedQuery, shouldRename, newFileName) = ShowQuerySelectionDialog(suggestion, queryOptions, currentFileName);
                
                // Handle rename if requested
                if (shouldRename && !string.IsNullOrEmpty(newFileName) && newFileName != currentFileName)
                {
                    var newPath = RenameVideoFile(item.FilePath, newFileName);
                    if (newPath != item.FilePath)
                    {
                        // Update the item's file path reference
                        item.FilePath = newPath;
                    }
                }
                
                // Open search window if a query was selected
                if (!string.IsNullOrEmpty(selectedQuery))
                {
                    OpenSearchWindowWithQuery(item, selectedQuery);
                }
            }
            catch (Exception ex)
            {
                loadingWindow.Close();
                MessageBox.Show($"Error: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    
    private void ManualSearch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not MenuItem menuItem || menuItem.Tag is not ViewModels.ScannedItemViewModel item)
            {
                MessageBox.Show("Could not get item information.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            if (string.IsNullOrEmpty(item.FilePath))
            {
                MessageBox.Show("File path is not available for this item.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            var aggregator = _serviceProvider.GetRequiredService<SubtitleAggregator>();
            var openAIService = _serviceProvider.GetService<OpenAIService>(); // Use GetService to allow null
            
            var initialQuery = Path.GetFileNameWithoutExtension(item.FilePath) ?? "";
            // Clean up common release tags for better search
            initialQuery = CleanSearchQuery(initialQuery);
            
            var mediaType = item.Item?.Type.ToString().ToLower() ?? "movie";
            
            var searchWindow = new SubtitleSearchWindow(
                aggregator, 
                openAIService, 
                item.FilePath, 
                initialQuery,
                mediaType)
            {
                Owner = this
            };
            
            searchWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening search window: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private static string CleanSearchQuery(string filename)
    {
        // Remove common release tags and quality indicators
        var cleanName = filename;
        
        // Remove year in parentheses/brackets at end
        cleanName = System.Text.RegularExpressions.Regex.Replace(cleanName, @"[\(\[\{]\d{4}[\)\]\}]", " ");
        
        // Remove quality/release tags
        var tagsToRemove = new[] { 
            "1080p", "720p", "2160p", "4K", "BluRay", "WEB-DL", "WEBRip", "HDRip", 
            "BRRip", "DVDRip", "x264", "x265", "HEVC", "H264", "H265", "AAC", "DTS",
            "YIFY", "RARBG", "YTS", "FGT", "EVO", "SPARKS", "GECKOS", "LOL",
            "HDTV", "WEB", "Bluray", "BDRip", "XviD", "AC3", "DD5", "DDP5"
        };
        
        foreach (var tag in tagsToRemove)
        {
            cleanName = System.Text.RegularExpressions.Regex.Replace(
                cleanName, 
                $@"[\.\-\s_]{tag}[\.\-\s_]?", 
                " ", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        
        // Replace dots and underscores with spaces
        cleanName = cleanName.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
        
        // Remove multiple spaces
        cleanName = System.Text.RegularExpressions.Regex.Replace(cleanName, @"\s+", " ");
        
        return cleanName.Trim();
    }
    
    private void CopyFilePath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag is ViewModels.ScannedItemViewModel item)
        {
            Clipboard.SetText(item.FilePath);
        }
    }
    
    private (string? selectedQuery, bool shouldRename, string? newFileName) ShowQuerySelectionDialog(
        SubtitleSearchSuggestion suggestion, List<string> queryOptions, string currentFileName)
    {
        string? selectedQuery = null;
        bool shouldRename = false;
        string? newFileName = null;
        
        // Generate suggested filename
        var suggestedName = GenerateSuggestedFileName(suggestion);
        
        var dialog = new Window
        {
            Title = "AI Search Suggestions",
            Width = 500,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#282A36")),
            ResizeMode = ResizeMode.NoResize
        };
        
        var mainPanel = new StackPanel { Margin = new Thickness(20) };
        
        // Title detected
        var titleText = $"🎬 Detected: {suggestion.Title}";
        if (suggestion.Year.HasValue) titleText += $" ({suggestion.Year})";
        mainPanel.Children.Add(new TextBlock 
        { 
            Text = titleText, 
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        });
        
        // Rename section
        mainPanel.Children.Add(new TextBlock 
        { 
            Text = "📝 Suggested Filename:", 
            Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6272A4")),
            FontSize = 12,
            Margin = new Thickness(0, 5, 0, 5)
        });
        
        var renameTextBox = new TextBox
        {
            Text = suggestedName,
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#44475A")),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 5)
        };
        mainPanel.Children.Add(renameTextBox);
        
        // Rename Only button
        var renameOnlyBtn = new Button
        {
            Content = "📁 Rename File Only",
            Padding = new Thickness(15, 8, 15, 8),
            Margin = new Thickness(0, 5, 0, 15),
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#50FA7B")),
            Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#282A36")),
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        renameOnlyBtn.Click += (s, e) =>
        {
            selectedQuery = null; // No search
            shouldRename = true;
            newFileName = renameTextBox.Text?.Trim();
            dialog.DialogResult = true;
            dialog.Close();
        };
        mainPanel.Children.Add(renameOnlyBtn);
        
        // Instructions
        mainPanel.Children.Add(new TextBlock 
        { 
            Text = "🔍 Or click a query to search OpenSubtitles:", 
            Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6272A4")),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 10)
        });
        
        // Query buttons
        for (int i = 0; i < queryOptions.Count; i++)
        {
            var query = queryOptions[i];
            var isMain = i == 0;
            
            var btn = new Button
            {
                Content = (isMain ? "⭐ " : "   ") + query,
                Padding = new Thickness(15, 10, 15, 10),
                Margin = new Thickness(0, 3, 0, 3),
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isMain ? "#BD93F9" : "#44475A")),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            
            btn.Click += (s, e) =>
            {
                selectedQuery = query;
                shouldRename = false; // Only rename with the Rename button
                newFileName = renameTextBox.Text?.Trim();
                dialog.DialogResult = true;
                dialog.Close();
            };
            
            mainPanel.Children.Add(btn);
        }
        
        // Cancel button
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(15, 8, 15, 8),
            Margin = new Thickness(0, 15, 0, 0),
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#44475A")),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        cancelBtn.Click += (s, e) => dialog.Close();
        mainPanel.Children.Add(cancelBtn);
        
        dialog.Content = mainPanel;
        dialog.ShowDialog();
        
        return (selectedQuery, shouldRename, newFileName);
    }
    
    private static string GenerateSuggestedFileName(SubtitleSearchSuggestion suggestion)
    {
        if (!string.IsNullOrEmpty(suggestion.ShowName))
        {
            // TV Show format: Show Name - S01E01 - Episode Title
            var name = suggestion.ShowName;
            if (suggestion.Season.HasValue && suggestion.Episode.HasValue)
            {
                name += $" - S{suggestion.Season:D2}E{suggestion.Episode:D2}";
            }
            if (!string.IsNullOrEmpty(suggestion.Title) && suggestion.Title != suggestion.ShowName)
            {
                name += $" - {suggestion.Title}";
            }
            return SanitizeFileName(name);
        }
        else
        {
            // Movie format: Title (Year)
            var name = suggestion.Title ?? "Unknown";
            if (suggestion.Year.HasValue)
            {
                name += $" ({suggestion.Year})";
            }
            return SanitizeFileName(name);
        }
    }
    
    private static string SanitizeFileName(string name)
    {
        // Remove invalid filename characters
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
        {
            name = name.Replace(c, '_');
        }
        return name.Trim();
    }
    
    private string RenameVideoFile(string originalPath, string newBaseName)
    {
        try
        {
            var directory = Path.GetDirectoryName(originalPath) ?? ".";
            var extension = Path.GetExtension(originalPath);
            var originalBaseName = Path.GetFileNameWithoutExtension(originalPath);
            var newPath = Path.Combine(directory, newBaseName + extension);
            
            // Check if target already exists
            if (File.Exists(newPath) && newPath != originalPath)
            {
                var result = MessageBox.Show(
                    $"File '{newBaseName}{extension}' already exists. Overwrite?",
                    "File Exists", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                    return originalPath;
            }
            
            // Rename the video file
            if (originalPath != newPath)
            {
                File.Move(originalPath, newPath, overwrite: true);
            }
            
            // Also rename any existing subtitle files with matching base name
            var subtitleExtensions = new[] { ".srt", ".sub", ".ass", ".ssa", ".vtt" };
            var languages = new[] { "", ".en", ".eng", ".es", ".spa", ".fr", ".fra", ".de", ".deu" };
            
            foreach (var subExt in subtitleExtensions)
            {
                foreach (var lang in languages)
                {
                    var oldSubPath = Path.Combine(directory, originalBaseName + lang + subExt);
                    var newSubPath = Path.Combine(directory, newBaseName + lang + subExt);
                    
                    if (File.Exists(oldSubPath) && oldSubPath != newSubPath)
                    {
                        File.Move(oldSubPath, newSubPath, overwrite: true);
                    }
                }
            }
            
            MessageBox.Show($"Renamed to: {newBaseName}{extension}", "File Renamed", 
                MessageBoxButton.OK, MessageBoxImage.Information);
            
            return newPath;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error renaming file: {ex.Message}", "Rename Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
            return originalPath;
        }
    }
    
    private void OpenSearchWindowWithQuery(ViewModels.ScannedItemViewModel item, string query)
    {
        try
        {
            var aggregator = _serviceProvider.GetRequiredService<SubtitleAggregator>();
            var openAIService = _serviceProvider.GetService<OpenAIService>();
            var mediaType = item.Item?.Type.ToString().ToLower() ?? "movie";
            
            var searchWindow = new SubtitleSearchWindow(
                aggregator, 
                openAIService, 
                item.FilePath, 
                query,
                mediaType)
            {
                Owner = this
            };
            
            // Auto-trigger search after window loads
            searchWindow.Loaded += async (s, e) =>
            {
                await Task.Delay(100); // Small delay for UI to render
                searchWindow.TriggerSearch();
            };
            
            searchWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening search: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
