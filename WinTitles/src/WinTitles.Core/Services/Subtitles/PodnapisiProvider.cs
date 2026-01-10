using Microsoft.Extensions.Logging;

namespace WinTitles.Core.Services.Subtitles;

/// <summary>
/// Podnapisi.net provider adapter implementing ISubtitleProvider.
/// Free provider, no authentication required.
/// </summary>
public class PodnapisiProvider : ISubtitleProvider
{
    private readonly ILogger<PodnapisiProvider> _logger;
    private readonly PodnapisiService _service;

    public string Name => "Podnapisi";
    public int Priority => 2; // Secondary provider
    public bool RequiresAuth => false;

    public PodnapisiProvider(
        ILogger<PodnapisiProvider> logger,
        PodnapisiService service)
    {
        _logger = logger;
        _service = service;
    }

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        // Podnapisi doesn't require auth, always available
        return Task.FromResult(true);
    }

    public async Task<List<SubtitleSearchResult>> SearchAsync(
        SubtitleSearchRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var results = await _service.SearchAsync(
                request.Query,
                request.Language,
                request.Season,
                request.Episode,
                request.Year,
                ct);
            
            // Podnapisi API doesn't filter by language reliably, so filter client-side
            var filteredResults = results
                .Where(r => string.IsNullOrEmpty(request.Language) || 
                           r.Language.Equals(request.Language, StringComparison.OrdinalIgnoreCase) ||
                           r.Language.StartsWith(request.Language, StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            _logger.LogDebug("Podnapisi: {Total} results, {Filtered} after language filter ({Lang})", 
                results.Count, filteredResults.Count, request.Language);
            
            return filteredResults.Select(r => new SubtitleSearchResult
            {
                Id = r.Id,
                ProviderId = r.Id,
                ProviderName = Name,
                Language = r.Language,
                FileName = r.ReleaseInfo,
                Release = r.Releases.FirstOrDefault(),
                Downloads = 0, // Podnapisi doesn't provide download count
                HearingImpaired = r.HearingImpaired,
                HashMatch = false,
                Year = r.Year,
                Season = r.Season,
                Episode = r.Episode,
                MatchScore = CalculateMatchScore(r, request),
                ProviderData = r
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Podnapisi search failed");
            return [];
        }
    }

    public Task<List<SubtitleSearchResult>> SearchByHashAsync(
        string filePath,
        string language,
        CancellationToken ct = default)
    {
        // Podnapisi doesn't support hash search
        _logger.LogDebug("Podnapisi does not support hash search");
        return Task.FromResult(new List<SubtitleSearchResult>());
    }

    public async Task<SubtitleDownloadResult> DownloadAsync(
        SubtitleSearchResult subtitle,
        string destinationPath,
        CancellationToken ct = default)
    {
        // Use the download URL stored in ProviderData if available
        var downloadUrl = (subtitle.ProviderData as PodnapisiSubtitle)?.DownloadUrl ?? subtitle.ProviderId;
        var result = await _service.DownloadAsync(downloadUrl, destinationPath, ct);
        
        return new SubtitleDownloadResult
        {
            Success = result.Success,
            FilePath = result.FilePath,
            Error = result.Error
        };
    }

    private float CalculateMatchScore(PodnapisiSubtitle r, SubtitleSearchRequest request)
    {
        float score = 0.4f; // Base score slightly lower than OpenSubtitles
        
        // Boost for matching year
        if (request.Year.HasValue && r.Year == request.Year)
            score += 0.2f;
        
        // Boost for matching season/episode
        if (request.Season.HasValue && request.Episode.HasValue)
        {
            if (r.Season == request.Season && r.Episode == request.Episode)
                score += 0.3f;
        }
        
        // Penalty for hearing impaired
        if (r.HearingImpaired) score -= 0.05f;
        
        return Math.Clamp(score, 0f, 1f);
    }
}
