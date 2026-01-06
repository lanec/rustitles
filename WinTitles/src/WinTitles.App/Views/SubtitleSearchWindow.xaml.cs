using System.IO;
using System.Windows;
using System.Windows.Input;
using WinTitles.Core.Services;

namespace WinTitles.App.Views;

public partial class SubtitleSearchWindow : Window
{
    private readonly OpenSubtitlesService _subtitles;
    private readonly OpenAIService? _openAI;
    private readonly string _videoPath;
    private readonly string _mediaType;
    private List<SubtitleSearchResultViewModel> _results = [];

    public string? DownloadedSubtitlePath { get; private set; }

    public SubtitleSearchWindow(
        OpenSubtitlesService subtitles,
        OpenAIService? openAI,
        string videoPath,
        string initialQuery,
        string mediaType = "movie")
    {
        InitializeComponent();
        _subtitles = subtitles ?? throw new ArgumentNullException(nameof(subtitles));
        _openAI = openAI;
        _videoPath = videoPath ?? "";
        _mediaType = mediaType ?? "movie";

        try
        {
            FileNameText.Text = $"File: {Path.GetFileName(videoPath ?? "Unknown")}";
            SearchBox.Text = initialQuery ?? "";
            
            // Hide AI button if no OpenAI service
            if (_openAI == null)
            {
                AISearchButton.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error initializing: {ex.Message}";
        }
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Search_Click(sender, e);
        }
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            StatusText.Text = "Please enter a search term";
            return;
        }

        StatusText.Text = "Searching...";
        ResultsList.ItemsSource = null;
        _results.Clear();

        try
        {
            var results = await _subtitles.SearchAsync(query, "en");
            
            if (results.Count == 0)
            {
                StatusText.Text = "No subtitles found. Try a different search term.";
                return;
            }

            _results = results.Select(r => new SubtitleSearchResultViewModel
            {
                FileId = r.FileId,
                FileName = r.FileName ?? "Unknown",
                Language = r.Language ?? "en",
                Downloads = r.Downloads,
                DownloadsFormatted = FormatDownloads(r.Downloads),
                Format = "srt",
                HearingImpaired = r.HearingImpaired,
                Rating = r.Rating
            }).ToList();

            ResultsList.ItemsSource = _results;
            StatusText.Text = $"Found {results.Count} subtitles";
        }
        catch (Exception ex)
        {
            var errorMsg = ex.Message;
            // Clean up HTML error responses
            if (errorMsg.Contains("<!DOCTYPE") || errorMsg.Contains("<html"))
            {
                if (errorMsg.Contains("503"))
                    errorMsg = "OpenSubtitles server is temporarily unavailable (503). Try again in a few minutes.";
                else if (errorMsg.Contains("502"))
                    errorMsg = "OpenSubtitles server error (502). Try again in a few minutes.";
                else
                    errorMsg = "OpenSubtitles server error. Try again later.";
            }
            StatusText.Text = $"Search failed: {errorMsg}";
        }
    }

    private async void AISearch_Click(object sender, RoutedEventArgs e)
    {
        if (_openAI == null) return;

        StatusText.Text = "Getting AI suggestion...";
        AISearchButton.IsEnabled = false;

        try
        {
            var suggestion = await _openAI.GenerateSearchQueryAsync(
                Path.GetFileName(_videoPath),
                _videoPath,
                _mediaType);

            if (!string.IsNullOrEmpty(suggestion.SearchQuery))
            {
                SearchBox.Text = suggestion.SearchQuery;
                StatusText.Text = $"AI suggested: \"{suggestion.SearchQuery}\" - Click Search to find subtitles";
            }
            else if (!string.IsNullOrEmpty(suggestion.Error))
            {
                StatusText.Text = $"AI error: {suggestion.Error}";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"AI suggestion failed: {ex.Message}";
        }
        finally
        {
            AISearchButton.IsEnabled = true;
        }
    }

    private void ResultsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var selected = ResultsList.SelectedItem as SubtitleSearchResultViewModel;
        DownloadButton.IsEnabled = selected != null;
        
        if (selected != null)
        {
            SelectedText.Text = $"Selected: {selected.FileName}";
        }
        else
        {
            SelectedText.Text = "";
        }
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        var selected = ResultsList.SelectedItem as SubtitleSearchResultViewModel;
        if (selected == null) return;

        StatusText.Text = "Downloading subtitle...";
        DownloadButton.IsEnabled = false;

        try
        {
            // Generate destination path in same folder as video
            var dir = Path.GetDirectoryName(_videoPath) ?? ".";
            var baseName = Path.GetFileNameWithoutExtension(_videoPath);
            var destPath = Path.Combine(dir, $"{baseName}.en.srt");

            var result = await _subtitles.DownloadAsync(selected.FileId, destPath);

            if (result.Success)
            {
                DownloadedSubtitlePath = result.FilePath;
                StatusText.Text = $"Downloaded to: {Path.GetFileName(result.FilePath ?? destPath)}";
                
                MessageBox.Show(
                    $"Subtitle downloaded successfully!\n\nSaved to:\n{result.FilePath}",
                    "Download Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                
                DialogResult = true;
                Close();
            }
            else
            {
                StatusText.Text = $"Download failed: {result.Error}";
                DownloadButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            var errorMsg = ex.Message;
            // Clean up HTML error responses
            if (errorMsg.Contains("<!DOCTYPE") || errorMsg.Contains("<html"))
            {
                if (errorMsg.Contains("503"))
                    errorMsg = "OpenSubtitles server unavailable (503). Try again later.";
                else
                    errorMsg = "OpenSubtitles server error. Try again later.";
            }
            StatusText.Text = $"Download failed: {errorMsg}";
            DownloadButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
    
    /// <summary>
    /// Trigger search programmatically (for auto-search after AI suggestions).
    /// </summary>
    public void TriggerSearch()
    {
        Search_Click(this, new RoutedEventArgs());
    }

    private static string FormatDownloads(int downloads)
    {
        return downloads switch
        {
            >= 1000000 => $"{downloads / 1000000.0:F1}M",
            >= 1000 => $"{downloads / 1000.0:F1}K",
            _ => downloads.ToString()
        };
    }
}

public class SubtitleSearchResultViewModel
{
    public int FileId { get; set; }
    public string FileName { get; set; } = "";
    public string Language { get; set; } = "";
    public int Downloads { get; set; }
    public string DownloadsFormatted { get; set; } = "";
    public string Format { get; set; } = "";
    public bool HearingImpaired { get; set; }
    public double Rating { get; set; }
    
    public string DetailsLine
    {
        get
        {
            var parts = new List<string> { Language, Format };
            if (HearingImpaired) parts.Add("👂 HI");
            if (Rating > 0) parts.Add($"★ {Rating:F1}");
            return string.Join(" • ", parts.Where(p => !string.IsNullOrEmpty(p)));
        }
    }
}
