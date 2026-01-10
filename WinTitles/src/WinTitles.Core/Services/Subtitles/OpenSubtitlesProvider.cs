using Microsoft.Extensions.Logging;

namespace WinTitles.Core.Services.Subtitles;

/// <summary>
/// OpenSubtitles.com provider adapter implementing ISubtitleProvider.
/// </summary>
public class OpenSubtitlesProvider : ISubtitleProvider
{
    private readonly ILogger<OpenSubtitlesProvider> _logger;
    private readonly OpenSubtitlesService _service;
    private readonly SettingsService _settings;

    public string Name => "OpenSubtitles";
    public int Priority => 1; // Primary provider
    public bool RequiresAuth => true;

    public OpenSubtitlesProvider(
        ILogger<OpenSubtitlesProvider> logger,
        OpenSubtitlesService service,
        SettingsService settings)
    {
        _logger = logger;
        _service = service;
        _settings = settings;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        var apiKey = _settings.Settings.OpenSubtitles?.ApiKey;
        var username = _settings.Settings.OpenSubtitles?.Username;
        var password = _settings.Settings.OpenSubtitles?.Password;
        
        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            return false;
        }
        
        // Try to login if not already
        if (!_service.IsLoggedIn)
        {
            return await _service.LoginAsync(ct);
        }
        
        return true;
    }

    public async Task<List<SubtitleSearchResult>> SearchAsync(
        SubtitleSearchRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var results = await _service.SearchAsync(request.Query, request.Language, ct: ct);
            
            return results.Select(r => new SubtitleSearchResult
            {
                Id = r.FileId.ToString(),
                ProviderId = r.FileId.ToString(),
                ProviderName = Name,
                Language = r.Language,
                FileName = r.FileName,
                Release = r.Release,
                Downloads = r.Downloads,
                HearingImpaired = r.HearingImpaired,
                HashMatch = false,
                MatchScore = CalculateMatchScore(r, request),
                ProviderData = r.FileId
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenSubtitles search failed");
            return [];
        }
    }

    public async Task<List<SubtitleSearchResult>> SearchByHashAsync(
        string filePath,
        string language,
        CancellationToken ct = default)
    {
        try
        {
            var results = await _service.SearchByHashAsync(filePath, language, ct);
            
            return results.Select(r => new SubtitleSearchResult
            {
                Id = r.FileId.ToString(),
                ProviderId = r.FileId.ToString(),
                ProviderName = Name,
                Language = r.Language,
                FileName = r.FileName,
                Release = r.Release,
                Downloads = r.Downloads,
                HearingImpaired = r.HearingImpaired,
                HashMatch = true,
                MatchScore = 1.0f, // Hash matches are best
                ProviderData = r.FileId
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenSubtitles hash search failed");
            return [];
        }
    }

    public async Task<SubtitleDownloadResult> DownloadAsync(
        SubtitleSearchResult subtitle,
        string destinationPath,
        CancellationToken ct = default)
    {
        if (subtitle.ProviderData is not int fileId)
        {
            return new SubtitleDownloadResult
            {
                Success = false,
                Error = "Invalid provider data"
            };
        }

        var result = await _service.DownloadAsync(fileId, destinationPath, ct);
        
        return new SubtitleDownloadResult
        {
            Success = result.Success,
            FilePath = result.FilePath,
            Error = result.Error,
            RemainingDownloads = result.RemainingDownloads
        };
    }

    private float CalculateMatchScore(OpenSubtitlesSearchResult r, SubtitleSearchRequest request)
    {
        float score = 0.5f;
        
        // Boost for high download count
        if (r.Downloads > 10000) score += 0.2f;
        else if (r.Downloads > 1000) score += 0.1f;
        
        // Boost for matching language
        if (r.Language.Equals(request.Language, StringComparison.OrdinalIgnoreCase))
            score += 0.1f;
        
        // Penalty for hearing impaired if not requested
        if (r.HearingImpaired) score -= 0.05f;
        
        return Math.Clamp(score, 0f, 1f);
    }
}
