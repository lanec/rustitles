# OpenSubtitles API Integration

## Overview
WinTitles uses the OpenSubtitles REST API v1 for searching and downloading subtitles.

## Authentication Requirements

OpenSubtitles requires **three credentials** for full functionality:

1. **API Key** - Get from https://opensubtitles.com/consumers
2. **Username** - Your opensubtitles.com account username
3. **Password** - Your opensubtitles.com account password

### Why All Three?
- **Search** only requires the API Key
- **Download** requires login authentication (username/password) to get a JWT token

## API Endpoints

| Endpoint | Method | Auth Required |
|----------|--------|---------------|
| `/subtitles` | GET | API Key only |
| `/login` | POST | API Key + credentials |
| `/download` | POST | API Key + JWT token |

## Critical Implementation Details

### Headers Must Be Set Explicitly

The download endpoint is **very sensitive** to headers. Using HttpClient's default headers or the `AddHeaders` helper method caused 503 errors.

**What Works:**
```csharp
var request = new HttpRequestMessage(HttpMethod.Post, url);
request.Headers.TryAddWithoutValidation("Api-Key", apiKey);
request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
request.Headers.TryAddWithoutValidation("Accept", "*/*");
request.Headers.TryAddWithoutValidation("User-Agent", "WinTitles/1.0");

var jsonContent = $"{{\"file_id\":{fileId}}}";
request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
```

**What Caused 503 Errors:**
- Using `JsonContent.Create()` 
- Using `AddHeaders()` method that cleared and re-added headers
- Relying on HttpClient's `DefaultRequestHeaders`

### Login Flow

Before downloading, the service must:
1. Check if already logged in (`IsLoggedIn` property)
2. If not, call `LoginAsync()` to get a JWT token
3. Store the token in `_authToken` field
4. Include token in `Authorization: Bearer {token}` header

### Error Handling

OpenSubtitles returns HTML error pages for server errors (502, 503). These must be cleaned up before displaying to users:

```csharp
if (errorMsg.Contains("<!DOCTYPE") || errorMsg.Contains("<html"))
{
    if (errorMsg.Contains("503"))
        errorMsg = "OpenSubtitles server is temporarily unavailable (503).";
    else
        errorMsg = "OpenSubtitles server error. Try again later.";
}
```

## Rate Limiting

- Free accounts have limited downloads per day
- Use `RemainingDownloads` from download response to track quota
- Reduce concurrent downloads (default: 3) to avoid 429 errors

## Troubleshooting

| Error | Cause | Solution |
|-------|-------|----------|
| 406 Not Acceptable | Missing/invalid API key or auth | Check API key and login credentials |
| 503 Service Unavailable | Server overload OR bad headers | Ensure headers are set explicitly |
| 401 Unauthorized | Invalid/expired JWT token | Re-login to get new token |
| 429 Too Many Requests | Rate limited | Reduce concurrent downloads |

## File Locations

- `OpenSubtitlesService.cs` - Main API client
- `AppSettings.cs` - Stores API key, username, password
- `SettingsWindow.xaml` - UI for configuring credentials
