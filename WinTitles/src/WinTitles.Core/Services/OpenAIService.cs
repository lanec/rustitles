using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WinTitles.Core.Models;

namespace WinTitles.Core.Services;

/// <summary>
/// Service for interacting with OpenAI API to generate intelligent search queries.
/// </summary>
public class OpenAIService
{
    private readonly ILogger<OpenAIService> _logger;
    private readonly HttpClient _http;
    private readonly SettingsService _settings;
    
    private const string BaseUrl = "https://api.openai.com/v1";
    
    public OpenAIService(ILogger<OpenAIService> logger, HttpClient httpClient, SettingsService settings)
    {
        _logger = logger;
        _http = httpClient;
        _settings = settings;
    }
    
    /// <summary>
    /// Get available models from OpenAI.
    /// </summary>
    public async Task<List<OpenAIModel>> GetAvailableModelsAsync(string? apiKey = null, CancellationToken ct = default)
    {
        var key = apiKey ?? _settings.Settings.OpenAI.ApiKey;
        if (string.IsNullOrEmpty(key))
        {
            return [];
        }
        
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models");
            request.Headers.Add("Authorization", $"Bearer {key}");
            
            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<OpenAIModelsResponse>(ct);
            
            // Filter to chat models only and sort by name
            var chatModels = result?.Data?
                .Where(m => m.Id.StartsWith("gpt-") || m.Id.StartsWith("o1") || m.Id.StartsWith("o3"))
                .OrderByDescending(m => m.Id.Contains("4o"))
                .ThenBy(m => m.Id)
                .Select(m => new OpenAIModel { Id = m.Id, Name = m.Id })
                .ToList() ?? [];
            
            _logger.LogInformation("Found {Count} chat models from OpenAI", chatModels.Count);
            return chatModels;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch OpenAI models");
            return [];
        }
    }
    
    /// <summary>
    /// Generate a search query for OpenSubtitles based on file information.
    /// </summary>
    public async Task<SubtitleSearchSuggestion> GenerateSearchQueryAsync(
        string fileName,
        string? folderPath,
        string mediaType,
        CancellationToken ct = default)
    {
        var apiKey = _settings.Settings.OpenAI.ApiKey;
        var model = _settings.Settings.OpenAI.SelectedModel;
        
        if (string.IsNullOrEmpty(apiKey))
        {
            return new SubtitleSearchSuggestion { Error = "OpenAI API key not configured" };
        }
        
        // Extract folder name from path
        var folderName = string.IsNullOrEmpty(folderPath) ? "" : Path.GetFileName(Path.GetDirectoryName(folderPath)) ?? "";
        
        var prompt = BuildSearchPrompt(fileName, folderName, mediaType);
        
        try
        {
            var requestBody = new
            {
                model = model,
                messages = new[]
                {
                    new { role = "system", content = "You are a helpful assistant that analyzes video file names to extract movie/TV show information for subtitle searches. Always respond with valid JSON only, no markdown." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.3,
                max_tokens = 500
            };
            
            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions");
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");
            
            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<OpenAIChatResponse>(ct);
            var content = result?.Choices?.FirstOrDefault()?.Message?.Content ?? "";
            
            _logger.LogDebug("OpenAI response: {Content}", content);
            
            // Parse the JSON response
            return ParseSearchSuggestion(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate search query with OpenAI");
            return new SubtitleSearchSuggestion { Error = ex.Message };
        }
    }
    
    /// <summary>
    /// Analyze download errors and provide AI diagnosis with recommendations.
    /// </summary>
    public async Task<ErrorAnalysisResult> AnalyzeErrorsAsync(
        List<(string Title, string Error)> failedItems,
        int totalItems,
        int successCount,
        CancellationToken ct = default)
    {
        var apiKey = _settings.Settings.OpenAI.ApiKey;
        var model = _settings.Settings.OpenAI.SelectedModel;
        
        if (string.IsNullOrEmpty(apiKey))
        {
            return new ErrorAnalysisResult { Error = "OpenAI API key not configured" };
        }
        
        // Categorize errors first
        var errorGroups = failedItems
            .GroupBy(e => CategorizeError(e.Error))
            .Select(g => new { Category = g.Key, Count = g.Count(), Examples = g.Take(3).Select(x => x.Title).ToList() })
            .OrderByDescending(g => g.Count)
            .ToList();
        
        var errorSummary = string.Join("\n", errorGroups.Select(g => 
            $"- {g.Category}: {g.Count} items (examples: {string.Join(", ", g.Examples)})"));
        
        var prompt = $@"Analyze these subtitle download errors and provide actionable recommendations.

DOWNLOAD STATISTICS:
- Total items: {totalItems}
- Successful: {successCount}
- Failed: {failedItems.Count}
- Success rate: {(totalItems > 0 ? (double)successCount / totalItems * 100 : 0):F1}%

ERROR CATEGORIES:
{errorSummary}

Based on these errors, provide a JSON response with:
{{
  ""summary"": ""Brief 1-2 sentence summary of the main issues"",
  ""categories"": [
    {{
      ""name"": ""Error category name"",
      ""count"": number,
      ""description"": ""What this error means"",
      ""fix"": ""How to fix or work around this""
    }}
  ],
  ""recommendations"": [
    ""Specific actionable recommendation 1"",
    ""Specific actionable recommendation 2""
  ],
  ""suggestedAction"": ""The single most important thing to do next""
}}

Common subtitle download issues:
- 429 Too Many Requests: Rate limiting - reduce concurrent downloads
- No subtitles found: File naming issues or subtitles don't exist
- Network errors: Connection issues
- Authentication errors: API key problems";

        try
        {
            var requestBody = new
            {
                model = model,
                messages = new[]
                {
                    new { role = "system", content = "You are a helpful assistant that analyzes software errors and provides clear, actionable recommendations. Respond with valid JSON only." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.3,
                max_tokens = 1000
            };
            
            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions");
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");
            
            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<OpenAIChatResponse>(ct);
            var content = result?.Choices?.FirstOrDefault()?.Message?.Content ?? "";
            
            return ParseErrorAnalysis(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze errors with OpenAI");
            return new ErrorAnalysisResult { Error = ex.Message };
        }
    }
    
    private string CategorizeError(string error)
    {
        if (string.IsNullOrEmpty(error)) return "Unknown Error";
        
        error = error.ToLowerInvariant();
        
        if (error.Contains("429") || error.Contains("too many requests") || error.Contains("rate limit"))
            return "Rate Limiting (429)";
        if (error.Contains("no subtitles found") || error.Contains("not found"))
            return "No Subtitles Found";
        if (error.Contains("401") || error.Contains("unauthorized") || error.Contains("authentication"))
            return "Authentication Error";
        if (error.Contains("timeout") || error.Contains("timed out"))
            return "Timeout";
        if (error.Contains("network") || error.Contains("connection"))
            return "Network Error";
        if (error.Contains("403") || error.Contains("forbidden"))
            return "Access Forbidden";
        if (error.Contains("500") || error.Contains("server error"))
            return "Server Error";
            
        return "Other Error";
    }
    
    private ErrorAnalysisResult ParseErrorAnalysis(string content)
    {
        try
        {
            content = content.Trim();
            if (content.StartsWith("```"))
            {
                var lines = content.Split('\n');
                content = string.Join('\n', lines.Skip(1).TakeWhile(l => !l.StartsWith("```")));
            }
            
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var parsed = JsonSerializer.Deserialize<ErrorAnalysisResult>(content, options);
            return parsed ?? new ErrorAnalysisResult { Error = "Failed to parse response" };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse error analysis response");
            return new ErrorAnalysisResult 
            { 
                Summary = content,
                Error = "Could not parse structured response"
            };
        }
    }
    
    private string BuildSearchPrompt(string fileName, string folderName, string mediaType)
    {
        return $@"Analyze this video file to help find English subtitles on OpenSubtitles.

File name: {fileName}
Folder name: {folderName}
Media type: {mediaType}

Extract the following information and respond with ONLY a JSON object (no markdown, no explanation):
{{
  ""title"": ""cleaned movie/show title"",
  ""year"": 2024 or null if unknown,
  ""isMovie"": true/false,
  ""showName"": ""TV show name if episode, null otherwise"",
  ""season"": season number or null,
  ""episode"": episode number or null,
  ""searchQuery"": ""recommended search string for OpenSubtitles"",
  ""alternativeQueries"": [""alt query 1"", ""alt query 2""]
}}

Tips:
- Remove quality indicators (1080p, 720p, BluRay, WEB-DL, x264, YIFY, etc.)
- Extract year from title or folder if present
- For TV shows, identify show name, season (S01), and episode (E01)
- searchQuery should be clean and likely to find results
- Provide 2-3 alternative search queries in case the main one fails";
    }
    
    private SubtitleSearchSuggestion ParseSearchSuggestion(string content)
    {
        try
        {
            // Clean up the response - remove markdown code blocks if present
            content = content.Trim();
            if (content.StartsWith("```"))
            {
                var lines = content.Split('\n');
                content = string.Join('\n', lines.Skip(1).TakeWhile(l => !l.StartsWith("```")));
            }
            
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var parsed = JsonSerializer.Deserialize<SubtitleSearchSuggestion>(content, options);
            return parsed ?? new SubtitleSearchSuggestion { Error = "Failed to parse response" };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse OpenAI response: {Content}", content);
            return new SubtitleSearchSuggestion 
            { 
                Error = "Failed to parse AI response",
                SearchQuery = content // Use raw content as fallback
            };
        }
    }
}

public class OpenAIModel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public class SubtitleSearchSuggestion
{
    public string? Title { get; set; }
    public int? Year { get; set; }
    public bool IsMovie { get; set; } = true;
    public string? ShowName { get; set; }
    public int? Season { get; set; }
    public int? Episode { get; set; }
    public string? SearchQuery { get; set; }
    public List<string> AlternativeQueries { get; set; } = [];
    public string? Error { get; set; }
}

public class ErrorAnalysisResult
{
    public string Summary { get; set; } = "";
    public List<ErrorCategory> Categories { get; set; } = [];
    public List<string> Recommendations { get; set; } = [];
    public string? SuggestedAction { get; set; }
    public string? Error { get; set; }
}

public class ErrorCategory
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public string Description { get; set; } = "";
    public string? Fix { get; set; }
}

#region OpenAI API Response Models

internal class OpenAIModelsResponse
{
    [JsonPropertyName("data")]
    public List<OpenAIModelData>? Data { get; set; }
}

internal class OpenAIModelData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
}

internal class OpenAIChatResponse
{
    [JsonPropertyName("choices")]
    public List<OpenAIChatChoice>? Choices { get; set; }
}

internal class OpenAIChatChoice
{
    [JsonPropertyName("message")]
    public OpenAIChatMessage? Message { get; set; }
}

internal class OpenAIChatMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

#endregion
