using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace WinTitles.Core.Services;

/// <summary>
/// ASP.NET Core minimal API server for receiving Plex webhook events.
/// </summary>
public class PlexWebhookServer : IDisposable
{
    private readonly ILogger<PlexWebhookServer> _logger;
    private WebApplication? _app;
    private Task? _serverTask;
    private CancellationTokenSource? _cts;

    public event Action<PlexWebhookEvent>? OnMediaAdded;
    public bool IsRunning => _app != null;
    public int Port { get; private set; }

    public PlexWebhookServer(ILogger<PlexWebhookServer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Start the webhook server.
    /// </summary>
    public async Task StartAsync(int port, CancellationToken ct = default)
    {
        if (_app != null) return;

        Port = port;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        
        _app = builder.Build();

        _app.MapPost("/plex/webhook", async (HttpContext ctx) =>
        {
            try
            {
                var form = await ctx.Request.ReadFormAsync();
                var payload = form["payload"].ToString();

                if (!string.IsNullOrEmpty(payload))
                {
                    var webhookPayload = JsonSerializer.Deserialize<PlexWebhookPayload>(payload);
                    
                    if (webhookPayload?.Event is "library.new" or "library.on.deck")
                    {
                        var metadata = webhookPayload.Metadata;
                        if (metadata != null)
                        {
                            _logger.LogInformation("Plex webhook: {Event} - {Title}", 
                                webhookPayload.Event, metadata.Title);

                            OnMediaAdded?.Invoke(new PlexWebhookEvent
                            {
                                EventType = webhookPayload.Event,
                                RatingKey = metadata.RatingKey,
                                Title = metadata.Title,
                                Year = metadata.Year,
                                MediaType = metadata.Type
                            });
                        }
                    }
                }

                return Results.Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Plex webhook");
                return Results.Ok(); // Still return OK to Plex
            }
        });

        // Health check endpoint
        _app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        _serverTask = _app.RunAsync($"http://0.0.0.0:{port}");
        _logger.LogInformation("Plex webhook server started on port {Port}", port);

        // Give it a moment to start
        await Task.Delay(100, ct);
    }

    /// <summary>
    /// Stop the webhook server.
    /// </summary>
    public async Task StopAsync()
    {
        if (_app == null) return;

        _cts?.Cancel();
        
        try
        {
            await _app.StopAsync();
        }
        catch { }

        _app = null;
        _serverTask = null;
        _logger.LogInformation("Plex webhook server stopped");
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _app?.DisposeAsync().AsTask().Wait();
        GC.SuppressFinalize(this);
    }
}

public class PlexWebhookEvent
{
    public required string EventType { get; init; }
    public required string RatingKey { get; init; }
    public required string Title { get; init; }
    public int? Year { get; init; }
    public required string MediaType { get; init; }
}

#region Plex Webhook Payload Models

internal class PlexWebhookPayload
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = "";

    [JsonPropertyName("Metadata")]
    public PlexWebhookMetadata? Metadata { get; set; }
}

internal class PlexWebhookMetadata
{
    [JsonPropertyName("ratingKey")]
    public string RatingKey { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
}

#endregion
