using System.Windows;
using System.Windows.Controls;
using WinTitles.Core.Services.Subtitles;

namespace WinTitles.App.Views;

public partial class TitleConfirmationWindow : Window
{
    public string? ConfirmedTitle { get; private set; }
    public int? ConfirmedYear { get; private set; }
    public int? ConfirmedSeason { get; private set; }
    public int? ConfirmedEpisode { get; private set; }
    public bool WasSkipped { get; private set; }

    public TitleConfirmationWindow(
        string originalFileName,
        List<AiMediaSuggestion>? suggestions,
        MediaInfo? parsedInfo)
    {
        InitializeComponent();
        
        OriginalFileNameText.Text = originalFileName;
        
        // Pre-fill with parsed info
        if (parsedInfo != null)
        {
            TitleTextBox.Text = parsedInfo.CleanTitle;
            if (parsedInfo.Year.HasValue)
                YearTextBox.Text = parsedInfo.Year.Value.ToString();
            if (parsedInfo.Season.HasValue)
                SeasonTextBox.Text = parsedInfo.Season.Value.ToString();
            if (parsedInfo.Episode.HasValue)
                EpisodeTextBox.Text = parsedInfo.Episode.Value.ToString();
        }
        
        // Populate AI suggestions
        if (suggestions?.Count > 0)
        {
            var displayItems = suggestions.Select(s => new SuggestionDisplayItem
            {
                Title = s.Title,
                Year = s.Year,
                Season = s.Season,
                Episode = s.Episode,
                Type = s.Type
            }).ToList();
            
            SuggestionsListBox.ItemsSource = displayItems;
        }
        else
        {
            SuggestionsListBox.Visibility = Visibility.Collapsed;
        }
    }

    private void SuggestionsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SuggestionsListBox.SelectedItem is SuggestionDisplayItem item)
        {
            TitleTextBox.Text = item.Title;
            YearTextBox.Text = item.Year?.ToString() ?? "";
            SeasonTextBox.Text = item.Season?.ToString() ?? "";
            EpisodeTextBox.Text = item.Episode?.ToString() ?? "";
        }
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        ConfirmedTitle = TitleTextBox.Text.Trim();
        
        if (string.IsNullOrEmpty(ConfirmedTitle))
        {
            MessageBox.Show("Please enter a title.", "Title Required", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        if (int.TryParse(YearTextBox.Text, out var year))
            ConfirmedYear = year;
        if (int.TryParse(SeasonTextBox.Text, out var season))
            ConfirmedSeason = season;
        if (int.TryParse(EpisodeTextBox.Text, out var episode))
            ConfirmedEpisode = episode;
        
        WasSkipped = false;
        DialogResult = true;
        Close();
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        WasSkipped = true;
        DialogResult = false;
        Close();
    }
}

public class SuggestionDisplayItem
{
    public string Title { get; set; } = "";
    public int? Year { get; set; }
    public int? Season { get; set; }
    public int? Episode { get; set; }
    public MediaType Type { get; set; }
    
    public string YearDisplay => Year.HasValue ? $"({Year})" : "";
    public string EpisodeDisplay => Season.HasValue && Episode.HasValue 
        ? $"S{Season:D2}E{Episode:D2}" 
        : "";
    public string TypeDisplay => $"[{Type}]";
}
