using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WinTitles.Core.Models;

namespace WinTitles.Core.Services;

/// <summary>
/// Service for interacting with Plex Media Server API.
/// </summary>
public class PlexService
{
    private readonly ILogger<PlexService> _logger;
    private readonly HttpClient _http;
    private string _serverUrl = "";
    private string _token = "";

    public PlexService(ILogger<PlexService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _http = httpClient;
    }

    /// <summary>
    /// Configure the Plex connection.
    /// </summary>
    public void Configure(string serverUrl, string token)
    {
        _serverUrl = serverUrl.TrimEnd('/');
        _token = token;
    }

    /// <summary>
    /// Test the Plex connection.
    /// </summary>
    public async Task<PlexConnectionResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await GetAsync<PlexIdentityResponse>("/", ct);
            return new PlexConnectionResult
            {
                Success = true,
                ServerName = response?.MediaContainer?.FriendlyName ?? "Unknown"
            };
        }
        catch (HttpRequestException ex)
        {
            return new PlexConnectionResult
            {
                Success = false,
                Error = ex.Message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing Plex connection");
            return new PlexConnectionResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Get all library sections.
    /// </summary>
    public async Task<List<PlexLibrary>> GetLibrariesAsync(CancellationToken ct = default)
    {
        var response = await GetAsync<PlexLibrariesResponse>("/library/sections", ct);
        return response?.MediaContainer?.Directory?
            .Select(d => new PlexLibrary
            {
                Key = d.Key,
                Title = d.Title,
                Type = d.Type,
                ItemCount = d.ChildCount
            })
            .ToList() ?? [];
    }

    /// <summary>
    /// Get all items in a library section.
    /// </summary>
    public async Task<List<MediaItem>> GetLibraryItemsAsync(string libraryKey, CancellationToken ct = default)
    {
        var response = await GetAsync<PlexLibraryItemsResponse>($"/library/sections/{libraryKey}/all", ct);
        return response?.MediaContainer?.Metadata?
            .Select(m => new MediaItem
            {
                Id = m.RatingKey,
                Title = m.Title,
                Year = m.Year,
                Type = m.Type == "movie" ? MediaType.Movie : MediaType.Episode,
                RatingKey = m.RatingKey,
                FilePath = m.Media?.FirstOrDefault()?.Part?.FirstOrDefault()?.File
            })
            .ToList() ?? [];
    }

    /// <summary>
    /// Get count of items in a library section.
    /// For TV shows, counts episodes (type=4), not shows.
    /// </summary>
    public async Task<int> GetLibraryItemCountAsync(string libraryKey, CancellationToken ct = default)
    {
        try
        {
            // First check if this is a TV show library
            var libraries = await GetLibrariesAsync(ct);
            var library = libraries.FirstOrDefault(l => l.Key == libraryKey);
            
            // For TV shows, count episodes (type=4), not shows
            var typeParam = library?.Type == "show" ? "&type=4" : "";
            var response = await GetAsync<PlexLibraryItemsResponse>(
                $"/library/sections/{libraryKey}/all?X-Plex-Container-Start=0&X-Plex-Container-Size=0{typeParam}", ct);
            return response?.MediaContainer?.TotalSize ?? 0;
        }
        catch
        {
            // Fallback: get all items and count (less efficient)
            var items = await GetLibraryItemsAsync(libraryKey, ct);
            return items.Count;
        }
    }

    /// <summary>
    /// Get all media items from a library, including TV episodes.
    /// For TV shows, this returns individual episodes, not just the shows.
    /// </summary>
    public async Task<List<PlexMediaItemInfo>> GetAllMediaItemsAsync(string libraryKey, CancellationToken ct = default)
    {
        var results = new List<PlexMediaItemInfo>();
        
        // First, get the library type
        var libraries = await GetLibrariesAsync(ct);
        var library = libraries.FirstOrDefault(l => l.Key == libraryKey);
        
        if (library == null) return results;
        
        if (library.Type == "movie")
        {
            // For movies, get all items directly
            var response = await GetAsync<PlexLibraryItemsResponse>($"/library/sections/{libraryKey}/all", ct);
            if (response?.MediaContainer?.Metadata != null)
            {
                foreach (var m in response.MediaContainer.Metadata)
                {
                    results.Add(new PlexMediaItemInfo
                    {
                        RatingKey = m.RatingKey,
                        Title = m.Title,
                        Year = m.Year,
                        Type = "movie",
                        FilePath = m.Media?.FirstOrDefault()?.Part?.FirstOrDefault()?.File
                    });
                }
            }
        }
        else if (library.Type == "show")
        {
            // For TV shows, we need to get all episodes
            var response = await GetAsync<PlexLibraryItemsResponse>($"/library/sections/{libraryKey}/all?type=4", ct);
            if (response?.MediaContainer?.Metadata != null)
            {
                foreach (var m in response.MediaContainer.Metadata)
                {
                    results.Add(new PlexMediaItemInfo
                    {
                        RatingKey = m.RatingKey,
                        Title = m.Title,
                        Year = m.Year,
                        Type = "episode",
                        GrandparentTitle = m.GrandparentTitle,
                        ParentIndex = m.ParentIndex,
                        Index = m.Index,
                        FilePath = m.Media?.FirstOrDefault()?.Part?.FirstOrDefault()?.File
                    });
                }
            }
        }
        
        return results;
    }

    /// <summary>
    /// Get metadata for a specific item.
    /// </summary>
    public async Task<MediaItem?> GetMetadataAsync(string ratingKey, CancellationToken ct = default)
    {
        var response = await GetAsync<PlexLibraryItemsResponse>($"/library/metadata/{ratingKey}", ct);
        var m = response?.MediaContainer?.Metadata?.FirstOrDefault();
        if (m == null) return null;

        return new MediaItem
        {
            Id = m.RatingKey,
            Title = m.Title,
            Year = m.Year,
            Type = m.Type == "movie" ? MediaType.Movie : MediaType.Episode,
            RatingKey = m.RatingKey,
            FilePath = m.Media?.FirstOrDefault()?.Part?.FirstOrDefault()?.File,
            ShowTitle = m.GrandparentTitle,
            SeasonNumber = m.ParentIndex,
            EpisodeNumber = m.Index
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    
    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        // Handle paths that already have query params
        var separator = path.Contains('?') ? "&" : "?";
        var url = $"{_serverUrl}{path}{separator}X-Plex-Token={_token}";
        
        _logger.LogDebug("Plex API request: {Path}", path);
        
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", "application/json");
        
        var response = await _http.SendAsync(request, ct);
        
        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Plex API error {Status}: {Content}", response.StatusCode, content.Substring(0, Math.Min(500, content.Length)));
        }
        
        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        return result;
    }
}

public class PlexConnectionResult
{
    public bool Success { get; init; }
    public string? ServerName { get; init; }
    public string? Error { get; init; }
}

public class PlexLibrary
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Type { get; init; }
    public int ItemCount { get; init; }
}

/// <summary>
/// Lightweight info about a Plex media item for scanning.
/// </summary>
public class PlexMediaItemInfo
{
    public string RatingKey { get; set; } = "";
    public string Title { get; set; } = "";
    public int? Year { get; set; }
    public string Type { get; set; } = "";
    public string? GrandparentTitle { get; set; }
    public int? ParentIndex { get; set; }
    public int? Index { get; set; }
    public string? FilePath { get; set; }
}

#region Plex API Response Models

internal class PlexIdentityResponse
{
    public PlexIdentityContainer? MediaContainer { get; set; }
}

internal class PlexIdentityContainer
{
    [JsonPropertyName("friendlyName")]
    public string? FriendlyName { get; set; }
}

internal class PlexLibrariesResponse
{
    public PlexLibrariesContainer? MediaContainer { get; set; }
}

internal class PlexLibrariesContainer
{
    public List<PlexDirectoryItem>? Directory { get; set; }
}

internal class PlexDirectoryItem
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";
    
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";
    
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
    
    [JsonPropertyName("childCount")]
    public int ChildCount { get; set; }
}

internal class PlexLibraryItemsResponse
{
    public PlexLibraryItemsContainer? MediaContainer { get; set; }
}

internal class PlexLibraryItemsContainer
{
    public List<PlexMetadataItem>? Metadata { get; set; }
    
    [JsonPropertyName("totalSize")]
    public int TotalSize { get; set; }
}

internal class PlexMetadataItem
{
    [JsonPropertyName("ratingKey")]
    public string RatingKey { get; set; } = "";
    
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";
    
    [JsonPropertyName("year")]
    public int? Year { get; set; }
    
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
    
    [JsonPropertyName("grandparentTitle")]
    public string? GrandparentTitle { get; set; }
    
    [JsonPropertyName("parentIndex")]
    public int? ParentIndex { get; set; }
    
    [JsonPropertyName("index")]
    public int? Index { get; set; }
    
    [JsonPropertyName("Media")]
    public List<PlexMediaItem>? Media { get; set; }
}

internal class PlexMediaItem
{
    [JsonPropertyName("Part")]
    public List<PlexPartItem>? Part { get; set; }
}

internal class PlexPartItem
{
    [JsonPropertyName("file")]
    public string? File { get; set; }
}

#endregion
