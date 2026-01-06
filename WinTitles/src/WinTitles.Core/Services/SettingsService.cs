using System.Text.Json;
using Microsoft.Extensions.Logging;
using WinTitles.Core.Models;

namespace WinTitles.Core.Services;

/// <summary>
/// Manages application settings persistence.
/// </summary>
public class SettingsService
{
    private readonly ILogger<SettingsService> _logger;
    private readonly string _settingsPath;
    private AppSettings _settings = new();

    public AppSettings Settings => _settings;

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
        
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appData, "WinTitles");
        Directory.CreateDirectory(appFolder);
        _settingsPath = Path.Combine(appFolder, "settings.json");
    }

    /// <summary>
    /// Load settings from disk.
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = await File.ReadAllTextAsync(_settingsPath);
                _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                
                // Ensure nested objects are never null
                _settings.Plex ??= new PlexSettings();
                _settings.OpenSubtitles ??= new OpenSubtitlesSettings();
                _settings.Languages ??= ["en"];
                _settings.WatchedFolders ??= [];
                
                _logger.LogInformation("Settings loaded from {Path}", _settingsPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading settings, using defaults");
            _settings = new AppSettings();
        }
    }

    /// <summary>
    /// Save settings to disk.
    /// </summary>
    public async Task SaveAsync()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_settings, options);
            await File.WriteAllTextAsync(_settingsPath, json);
            _logger.LogInformation("Settings saved to {Path}", _settingsPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving settings");
        }
    }

    /// <summary>
    /// Update settings and save.
    /// </summary>
    public async Task UpdateAsync(Action<AppSettings> update)
    {
        update(_settings);
        await SaveAsync();
    }
}
