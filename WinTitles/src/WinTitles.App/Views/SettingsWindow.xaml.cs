using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using WinTitles.Core.Services;

namespace WinTitles.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly PlexService _plexService;
    private readonly PlexAuthService _plexAuthService;
    private readonly OpenAIService? _openAIService;
    private List<PlexServer> _discoveredServers = [];
    private string? _currentAuthToken;
    private List<OpenAIModel> _availableModels = [];

    public SettingsWindow(SettingsService settingsService, PlexService plexService, PlexAuthService plexAuthService, OpenAIService? openAIService = null)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _plexService = plexService;
        _plexAuthService = plexAuthService;
        _openAIService = openAIService;
        LoadSettings();
    }

    private async void LoadSettings()
    {
        var s = _settingsService.Settings;
        
        // General
        LanguageTextBox.Text = string.Join(", ", s.Languages ?? ["en"]);
        IgnoreExtrasCheckBox.IsChecked = s.IgnoreExtrasFolders;
        OverwriteCheckBox.IsChecked = s.OverwriteExisting;
        
        // Startup
        StartWithWindowsCheckBox.IsChecked = s.StartWithWindows;
        StartMinimizedCheckBox.IsChecked = s.StartMinimized;
        
        // Notifications
        NotifySuccessCheckBox.IsChecked = s.NotifyOnSuccess;
        NotifyFailureCheckBox.IsChecked = s.NotifyOnFailure;
        
        // Plex - ensure Plex is not null
        var plex = s.Plex ?? new WinTitles.Core.Models.PlexSettings();
        PlexEnabledCheckBox.IsChecked = plex.Enabled;
        PlexUrlTextBox.Text = !string.IsNullOrEmpty(plex.ServerUrl) ? plex.ServerUrl : "http://localhost:32400";
        PlexTokenTextBox.Text = plex.Token ?? "";
        WebhookPortTextBox.Text = plex.WebhookPort.ToString();
        AutoStartWebhookCheckBox.IsChecked = plex.AutoStartWebhook;
        
        // Auto-discover servers if we have a saved auth token
        if (!string.IsNullOrEmpty(plex.AuthToken))
        {
            _currentAuthToken = plex.AuthToken;
            PlexAuthStatus.Text = "✓ Signed in - loading servers...";
            PlexAuthStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#50FA7B"));
            
            await DiscoverServersAsync(plex.AuthToken);
            
            // Try to select the previously saved server URL
            SelectSavedServer(plex.ServerUrl);
        }
        else if (!string.IsNullOrEmpty(plex.Token))
        {
            PlexAuthStatus.Text = "✓ Plex configured (sign in to see servers)";
            PlexAuthStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#50FA7B"));
        }
        
        // OpenSubtitles
        var openSubs = s.OpenSubtitles ?? new WinTitles.Core.Models.OpenSubtitlesSettings();
        OpenSubtitlesApiKeyTextBox.Text = openSubs.ApiKey ?? "";
        OpenSubtitlesUsernameTextBox.Text = openSubs.Username ?? "";
        OpenSubtitlesPasswordBox.Password = openSubs.Password ?? "";
        
        // OpenAI
        var openAI = s.OpenAI ?? new WinTitles.Core.Models.OpenAISettings();
        OpenAIApiKeyTextBox.Text = openAI.ApiKey ?? "";
        EnableAISearchCheckBox.IsChecked = openAI.EnableAISearch;
        
        // Load models if API key exists
        if (!string.IsNullOrEmpty(openAI.ApiKey))
        {
            await LoadOpenAIModelsAsync(openAI.ApiKey, openAI.SelectedModel);
        }
        else
        {
            // Add default models
            OpenAIModelComboBox.Items.Clear();
            OpenAIModelComboBox.Items.Add("gpt-4o-mini");
            OpenAIModelComboBox.Items.Add("gpt-4o");
            OpenAIModelComboBox.Items.Add("gpt-4-turbo");
            OpenAIModelComboBox.Items.Add("gpt-3.5-turbo");
            OpenAIModelComboBox.SelectedItem = openAI.SelectedModel;
        }
    }
    
    private async Task LoadOpenAIModelsAsync(string apiKey, string? selectedModel = null)
    {
        if (_openAIService == null) return;
        
        OpenAIModelComboBox.Items.Clear();
        OpenAIModelComboBox.Items.Add("Loading...");
        OpenAIModelComboBox.SelectedIndex = 0;
        
        _availableModels = await _openAIService.GetAvailableModelsAsync(apiKey);
        
        OpenAIModelComboBox.Items.Clear();
        
        if (_availableModels.Count == 0)
        {
            OpenAIModelComboBox.Items.Add("gpt-4o-mini");
            OpenAIModelComboBox.Items.Add("gpt-4o");
            OpenAIModelComboBox.SelectedIndex = 0;
            return;
        }
        
        foreach (var model in _availableModels)
        {
            OpenAIModelComboBox.Items.Add(model.Id);
        }
        
        // Select the saved model or first available
        if (!string.IsNullOrEmpty(selectedModel) && OpenAIModelComboBox.Items.Contains(selectedModel))
        {
            OpenAIModelComboBox.SelectedItem = selectedModel;
        }
        else
        {
            OpenAIModelComboBox.SelectedIndex = 0;
        }
    }
    
    private void SelectSavedServer(string savedUrl)
    {
        if (string.IsNullOrEmpty(savedUrl) || _discoveredServers.Count == 0) return;
        
        // Find which server and connection matches the saved URL
        for (int i = 0; i < _discoveredServers.Count; i++)
        {
            var server = _discoveredServers[i];
            for (int j = 0; j < server.Connections.Count; j++)
            {
                var conn = server.Connections[j];
                if (conn.BestUrl == savedUrl || conn.Uri == savedUrl)
                {
                    PlexServerComboBox.SelectedIndex = i;
                    PlexConnectionComboBox.SelectedIndex = j;
                    return;
                }
            }
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await _settingsService.UpdateAsync(s =>
        {
            // General
            s.Languages = LanguageTextBox.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            s.IgnoreExtrasFolders = IgnoreExtrasCheckBox.IsChecked ?? true;
            s.OverwriteExisting = OverwriteCheckBox.IsChecked ?? false;
            
            // Startup
            s.StartWithWindows = StartWithWindowsCheckBox.IsChecked ?? false;
            s.StartMinimized = StartMinimizedCheckBox.IsChecked ?? true;
            
            // Notifications
            s.NotifyOnSuccess = NotifySuccessCheckBox.IsChecked ?? false;
            s.NotifyOnFailure = NotifyFailureCheckBox.IsChecked ?? true;
            
            // Plex - ensure Plex is not null before saving
            s.Plex ??= new WinTitles.Core.Models.PlexSettings();
            s.Plex.Enabled = PlexEnabledCheckBox.IsChecked ?? false;
            s.Plex.ServerUrl = PlexUrlTextBox.Text ?? "";
            s.Plex.Token = PlexTokenTextBox.Text ?? "";
            s.Plex.AuthToken = _currentAuthToken ?? ""; // Save auth token for server discovery
            if (int.TryParse(WebhookPortTextBox.Text, out var port))
                s.Plex.WebhookPort = port;
            s.Plex.AutoStartWebhook = AutoStartWebhookCheckBox.IsChecked ?? true;
            
            // OpenSubtitles
            s.OpenSubtitles ??= new WinTitles.Core.Models.OpenSubtitlesSettings();
            s.OpenSubtitles.ApiKey = OpenSubtitlesApiKeyTextBox.Text ?? "";
            s.OpenSubtitles.Username = OpenSubtitlesUsernameTextBox.Text ?? "";
            s.OpenSubtitles.Password = OpenSubtitlesPasswordBox.Password ?? "";
            
            // OpenAI
            s.OpenAI ??= new WinTitles.Core.Models.OpenAISettings();
            s.OpenAI.ApiKey = OpenAIApiKeyTextBox.Text ?? "";
            s.OpenAI.SelectedModel = OpenAIModelComboBox.SelectedItem?.ToString() ?? "gpt-4o-mini";
            s.OpenAI.EnableAISearch = EnableAISearchCheckBox.IsChecked ?? true;
        });

        // Handle Windows startup registration
        if (StartWithWindowsCheckBox.IsChecked == true)
        {
            RegisterStartup();
        }
        else
        {
            UnregisterStartup();
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        _plexService.Configure(PlexUrlTextBox.Text, PlexTokenTextBox.Text);
        var result = await _plexService.TestConnectionAsync();

        if (result.Success)
        {
            MessageBox.Show($"Connected to: {result.ServerName}", "Success", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show($"Connection failed: {result.Error}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SignInPlex_Click(object sender, RoutedEventArgs e)
    {
        SignInPlexButton.IsEnabled = false;
        SignInPlexButton.Content = "⏳ Waiting for sign in...";
        PlexAuthStatus.Text = "";

        try
        {
            // Request a PIN
            var pin = await _plexAuthService.RequestPinAsync();
            if (pin == null)
            {
                PlexAuthStatus.Text = "Failed to request PIN";
                PlexAuthStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF5555"));
                return;
            }

            // Open browser for user to authenticate
            PlexAuthStatus.Text = $"Opening browser... Code: {pin.Code}";
            Process.Start(new ProcessStartInfo(pin.AuthUrl) { UseShellExecute = true });

            // Poll for auth token (timeout after 2 minutes)
            var token = await _plexAuthService.WaitForAuthAsync(pin.Id, TimeSpan.FromMinutes(2));

            if (!string.IsNullOrEmpty(token))
            {
                _currentAuthToken = token;
                PlexTokenTextBox.Text = token;
                PlexAuthStatus.Text = "✓ Signed in successfully!";
                PlexAuthStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#50FA7B"));

                // Discover servers
                await DiscoverServersAsync(token);
            }
            else
            {
                PlexAuthStatus.Text = "Authentication timed out";
                PlexAuthStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF5555"));
            }
        }
        catch (Exception ex)
        {
            PlexAuthStatus.Text = $"Error: {ex.Message}";
            PlexAuthStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF5555"));
        }
        finally
        {
            SignInPlexButton.IsEnabled = true;
            SignInPlexButton.Content = "🔑 Sign in with Plex";
        }
    }

    private async void RefreshServers_Click(object sender, RoutedEventArgs e)
    {
        var token = _currentAuthToken ?? PlexTokenTextBox.Text;
        if (string.IsNullOrEmpty(token))
        {
            MessageBox.Show("Please sign in with Plex first", "No Token", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await DiscoverServersAsync(token);
    }
    
    private async void RefreshOpenAIModels_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = OpenAIApiKeyTextBox.Text;
        if (string.IsNullOrEmpty(apiKey))
        {
            MessageBox.Show("Please enter an OpenAI API key first", "No API Key", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        await LoadOpenAIModelsAsync(apiKey);
    }

    private async Task DiscoverServersAsync(string token)
    {
        PlexAuthStatus.Text = "Discovering servers...";
        
        _discoveredServers = await _plexAuthService.DiscoverServersAsync(token);
        
        PlexServerComboBox.Items.Clear();
        
        if (_discoveredServers.Count == 0)
        {
            PlexServerComboBox.Items.Add("No servers found");
            PlexAuthStatus.Text = "No Plex servers found on your account";
            return;
        }

        foreach (var server in _discoveredServers)
        {
            PlexServerComboBox.Items.Add(server.Name);
        }

        PlexServerComboBox.SelectedIndex = 0;
        PlexAuthStatus.Text = $"✓ Found {_discoveredServers.Count} server(s)";
        PlexAuthStatus.Foreground = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#50FA7B"));
    }

    private void PlexServerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlexServerComboBox.SelectedIndex < 0 || PlexServerComboBox.SelectedIndex >= _discoveredServers.Count)
            return;

        var server = _discoveredServers[PlexServerComboBox.SelectedIndex];
        
        // Use the server's access token
        PlexTokenTextBox.Text = server.AccessToken;
        
        // Populate connection dropdown
        PlexConnectionComboBox.Items.Clear();
        foreach (var conn in server.Connections)
        {
            PlexConnectionComboBox.Items.Add(conn.DisplayName);
        }
        
        if (server.Connections.Count > 0)
        {
            PlexConnectionComboBox.SelectedIndex = 0; // First item is best (local IP)
        }
    }

    private void PlexConnectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlexServerComboBox.SelectedIndex < 0 || PlexServerComboBox.SelectedIndex >= _discoveredServers.Count)
            return;
        if (PlexConnectionComboBox.SelectedIndex < 0)
            return;

        var server = _discoveredServers[PlexServerComboBox.SelectedIndex];
        if (PlexConnectionComboBox.SelectedIndex < server.Connections.Count)
        {
            // Use BestUrl which prefers local IP when available
            PlexUrlTextBox.Text = server.Connections[PlexConnectionComboBox.SelectedIndex].BestUrl;
        }
    }

    private static void RegisterStartup()
    {
        try
        {
            var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (key != null && exePath != null)
            {
                key.SetValue("WinTitles", $"\"{exePath}\"");
            }
        }
        catch { }
    }

    private static void UnregisterStartup()
    {
        try
        {
            var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            key?.DeleteValue("WinTitles", false);
        }
        catch { }
    }
}
