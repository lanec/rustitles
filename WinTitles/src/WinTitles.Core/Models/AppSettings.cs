namespace WinTitles.Core.Models;

/// <summary>
/// Application settings that are persisted to disk.
/// </summary>
public class AppSettings
{
    public List<string> Languages { get; set; } = ["en"];
    public int ConcurrentDownloads { get; set; } = 3; // Reduced to avoid rate limiting
    public bool IgnoreEmbeddedSubtitles { get; set; } = true;
    public bool OverwriteExisting { get; set; } = false;
    public bool IgnoreExtrasFolders { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimized { get; set; } = true;
    public bool NotifyOnSuccess { get; set; } = false;
    public bool NotifyOnFailure { get; set; } = true;
    public bool NotifyOnNewMedia { get; set; } = true;
    
    public PlexSettings Plex { get; set; } = new();
    public OpenSubtitlesSettings OpenSubtitles { get; set; } = new();
    public OpenAISettings OpenAI { get; set; } = new();
    public List<string> WatchedFolders { get; set; } = [];
}

public class OpenSubtitlesSettings
{
    public string ApiKey { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class OpenAISettings
{
    public string ApiKey { get; set; } = "";
    public string SelectedModel { get; set; } = "gpt-4o-mini";
    public bool EnableAISearch { get; set; } = true;
}

public class PlexSettings
{
    public bool Enabled { get; set; } = false;
    public string ServerUrl { get; set; } = "";
    public string Token { get; set; } = "";
    public string AuthToken { get; set; } = ""; // plex.tv auth token for server discovery
    public int WebhookPort { get; set; } = 9876;
    public bool AutoStartWebhook { get; set; } = true;
}
