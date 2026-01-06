using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace WinTitles.Core.Services;

/// <summary>
/// Handles Plex PIN-based authentication flow.
/// </summary>
public class PlexAuthService
{
    private readonly ILogger<PlexAuthService> _logger;
    private readonly HttpClient _http;
    private const string PlexTvUrl = "https://plex.tv";
    private const string ClientId = "WinTitles";
    private const string Product = "WinTitles";

    public PlexAuthService(ILogger<PlexAuthService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _http = httpClient;
    }

    /// <summary>
    /// Start the PIN authentication flow.
    /// Returns the PIN code and ID for polling.
    /// </summary>
    public async Task<PlexPinResult?> RequestPinAsync(CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{PlexTvUrl}/api/v2/pins")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["strong"] = "true",
                ["X-Plex-Product"] = Product,
                ["X-Plex-Client-Identifier"] = ClientId
            })
        };
        request.Headers.Add("Accept", "application/json");

        try
        {
            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var pin = await response.Content.ReadFromJsonAsync<PlexPinResponse>(cancellationToken: ct);
            if (pin == null) return null;

            _logger.LogInformation("Plex PIN requested: {Code}", pin.Code);

            return new PlexPinResult
            {
                Id = pin.Id,
                Code = pin.Code,
                AuthUrl = $"https://app.plex.tv/auth#?clientID={ClientId}&code={pin.Code}&context%5Bdevice%5D%5Bproduct%5D={Product}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting Plex PIN");
            return null;
        }
    }

    /// <summary>
    /// Poll for PIN claim and get the auth token.
    /// </summary>
    public async Task<string?> PollForTokenAsync(int pinId, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{PlexTvUrl}/api/v2/pins/{pinId}")
        {
            Headers =
            {
                { "Accept", "application/json" },
                { "X-Plex-Client-Identifier", ClientId }
            }
        };

        try
        {
            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var pin = await response.Content.ReadFromJsonAsync<PlexPinResponse>(cancellationToken: ct);
            
            if (!string.IsNullOrEmpty(pin?.AuthToken))
            {
                _logger.LogInformation("Plex auth token obtained");
                return pin.AuthToken;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error polling Plex PIN");
            return null;
        }
    }

    /// <summary>
    /// Wait for the user to complete authentication, polling periodically.
    /// </summary>
    public async Task<string?> WaitForAuthAsync(int pinId, TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var token = await PollForTokenAsync(pinId, ct);
            if (!string.IsNullOrEmpty(token))
            {
                return token;
            }

            await Task.Delay(2000, ct); // Poll every 2 seconds
        }

        return null;
    }

    /// <summary>
    /// Discover local Plex servers using plex.tv resources API.
    /// </summary>
    public async Task<List<PlexServer>> DiscoverServersAsync(string authToken, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{PlexTvUrl}/api/v2/resources?includeHttps=1&includeRelay=0")
        {
            Headers =
            {
                { "Accept", "application/json" },
                { "X-Plex-Token", authToken },
                { "X-Plex-Client-Identifier", ClientId }
            }
        };

        try
        {
            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var resources = await response.Content.ReadFromJsonAsync<List<PlexResourceResponse>>(cancellationToken: ct);
            
            return resources?
                .Where(r => r.Provides?.Contains("server") == true)
                .Select(r => new PlexServer
                {
                    Name = r.Name ?? "Unknown",
                    AccessToken = r.AccessToken ?? "",
                    Connections = GetOrderedConnections(r.Connections)
                })
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering Plex servers");
            return [];
        }
    }

    /// <summary>
    /// Order connections: local IPs first, then other local, then remote.
    /// </summary>
    private static List<PlexConnection> GetOrderedConnections(List<PlexConnectionResponse>? connections)
    {
        if (connections == null) return [];

        var result = new List<PlexConnection>();

        foreach (var conn in connections)
        {
            if (string.IsNullOrEmpty(conn.Uri)) continue;

            var uri = conn.Uri;
            var isLocal = conn.Local == true;
            var isLocalIp = IsLocalIpAddress(uri);
            var isPlexDirect = uri.Contains("plex.direct");
            
            // Try to extract local IP from plex.direct URLs
            string? localIpUrl = null;
            if (isPlexDirect && isLocal)
            {
                localIpUrl = ConvertToLocalUrl(uri);
                if (localIpUrl == uri) localIpUrl = null; // No change means no local IP found
            }

            result.Add(new PlexConnection
            {
                Uri = uri,
                IsLocal = isLocal,
                IsLocalIp = isLocalIp,
                IsPlexDirect = isPlexDirect,
                LocalIpUrl = localIpUrl
            });
        }

        // Sort: Connections with local IP URLs first, then direct local IPs, then remote
        return result
            .OrderByDescending(c => !string.IsNullOrEmpty(c.LocalIpUrl))
            .ThenByDescending(c => c.IsLocalIp)
            .ThenByDescending(c => c.IsLocal)
            .ThenBy(c => c.IsPlexDirect)
            .ToList();
    }

    /// <summary>
    /// Check if a URL points to a local IP address.
    /// </summary>
    private static bool IsLocalIpAddress(string url)
    {
        try
        {
            var uri = new Uri(url);
            var host = uri.Host;

            // Check for common local IP patterns
            if (host.StartsWith("192.168.")) return true;
            if (host.StartsWith("10.")) return true;
            if (host.StartsWith("172.16.") || host.StartsWith("172.17.") || 
                host.StartsWith("172.18.") || host.StartsWith("172.19.") ||
                host.StartsWith("172.20.") || host.StartsWith("172.21.") ||
                host.StartsWith("172.22.") || host.StartsWith("172.23.") ||
                host.StartsWith("172.24.") || host.StartsWith("172.25.") ||
                host.StartsWith("172.26.") || host.StartsWith("172.27.") ||
                host.StartsWith("172.28.") || host.StartsWith("172.29.") ||
                host.StartsWith("172.30.") || host.StartsWith("172.31.")) return true;
            if (host == "localhost" || host == "127.0.0.1") return true;

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Extract local IP from plex.direct URL (e.g., "10-13-37-151.xxx.plex.direct" → "10.13.37.151")
    /// </summary>
    public static string? ExtractLocalIpFromPlexDirect(string url)
    {
        try
        {
            var uri = new Uri(url);
            var host = uri.Host;

            // plex.direct format: IP-with-dashes.hash.plex.direct
            if (!host.Contains("plex.direct")) return null;

            var parts = host.Split('.');
            if (parts.Length < 4) return null;

            var ipPart = parts[0]; // e.g., "10-13-37-151"
            var ipOctets = ipPart.Split('-');
            
            if (ipOctets.Length == 4 && ipOctets.All(o => byte.TryParse(o, out _)))
            {
                return string.Join(".", ipOctets); // "10.13.37.151"
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Convert plex.direct URL to local IP URL if possible.
    /// </summary>
    public static string ConvertToLocalUrl(string url)
    {
        var localIp = ExtractLocalIpFromPlexDirect(url);
        if (localIp == null) return url;

        try
        {
            var uri = new Uri(url);
            // Use HTTP for local connections (HTTPS cert won't match local IP)
            return $"http://{localIp}:{uri.Port}";
        }
        catch
        {
            return url;
        }
    }
}

public class PlexPinResult
{
    public int Id { get; init; }
    public string Code { get; init; } = "";
    public string AuthUrl { get; init; } = "";
}

public class PlexServer
{
    public string Name { get; init; } = "";
    public string AccessToken { get; init; } = "";
    public List<PlexConnection> Connections { get; init; } = [];
    
    /// <summary>
    /// Get the best local connection URL.
    /// </summary>
    public string? GetLocalUrl() => Connections.FirstOrDefault(c => c.IsLocalIp)?.Uri 
        ?? Connections.FirstOrDefault(c => c.IsLocal)?.Uri;
    
    /// <summary>
    /// Get the best remote connection URL.
    /// </summary>
    public string? GetRemoteUrl() => Connections.FirstOrDefault(c => !c.IsLocal)?.Uri
        ?? Connections.FirstOrDefault(c => c.IsPlexDirect)?.Uri;
}

public class PlexConnection
{
    public string Uri { get; init; } = "";
    public bool IsLocal { get; init; }
    public bool IsLocalIp { get; init; }
    public bool IsPlexDirect { get; init; }
    public string? LocalIpUrl { get; init; }
    
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrEmpty(LocalIpUrl))
                return $"🏠 {LocalIpUrl}";
            if (IsLocalIp)
                return $"🏠 {Uri}";
            return $"🌐 {Uri}";
        }
    }
    
    /// <summary>
    /// Get the best URL to use (local IP preferred).
    /// </summary>
    public string BestUrl => LocalIpUrl ?? Uri;
}

#region Plex API Response Models

internal class PlexPinResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("authToken")]
    public string? AuthToken { get; set; }
}

internal class PlexResourceResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("provides")]
    public string? Provides { get; set; }

    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("connections")]
    public List<PlexConnectionResponse>? Connections { get; set; }
}

internal class PlexConnectionResponse
{
    [JsonPropertyName("uri")]
    public string? Uri { get; set; }

    [JsonPropertyName("local")]
    public bool? Local { get; set; }
}

#endregion
