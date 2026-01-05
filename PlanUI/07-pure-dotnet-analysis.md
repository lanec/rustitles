# Pure .NET Solution Analysis

## Question: Can we do this all with .NET?

**Answer: Yes, absolutely.** .NET can handle every piece of functionality currently in Rust, and in some cases better.

---

## Feature Comparison

| Feature | Rust Implementation | .NET Equivalent | Verdict |
|---------|---------------------|-----------------|---------|
| **Call Subliminal (Python)** | `std::process::Command` | `System.Diagnostics.Process` | ✅ Equal |
| **HTTP Server (Webhooks)** | Axum | ASP.NET Core Minimal APIs | ✅ .NET better (more mature) |
| **HTTP Client (Plex API)** | reqwest | HttpClient | ✅ Equal |
| **File System Scanning** | std::fs, walkdir | System.IO | ✅ Equal |
| **SQLite Database** | rusqlite | EF Core / Dapper | ✅ .NET better (migrations, LINQ) |
| **JSON Serialization** | serde_json | System.Text.Json | ✅ Equal |
| **Async Operations** | tokio | async/await (built-in) | ✅ .NET simpler |
| **System Tray** | ❌ Conflicts | NotifyIcon | ✅ .NET only option |
| **Windows Integration** | windows crate | Native | ✅ .NET better |
| **Toast Notifications** | ❌ Not implemented | WinRT APIs | ✅ .NET only |

---

## What We Lose

| Loss | Impact | Mitigation |
|------|--------|------------|
| Cross-platform (Linux/macOS) | Medium | Could maintain Rust version for non-Windows, or use Avalonia UI |
| Rust's memory safety | Low | .NET is memory-safe with GC |
| Single binary deployment | Low | .NET 8 has single-file publish |
| Smaller binary size | Low | .NET ~60MB vs Rust ~15MB, acceptable |

---

## What We Gain

| Gain | Impact |
|------|--------|
| **Single codebase** | High - One language, one project |
| **Native Windows integration** | High - Tray, notifications, startup |
| **Faster development** | High - Rich ecosystem, familiar tooling |
| **Better debugging** | Medium - Visual Studio debugger |
| **Hot reload** | Medium - Faster iteration |
| **Entity Framework** | Medium - Easier database work |

---

## Recommended Architecture (Pure .NET)

```
Rustitles.sln
├── Rustitles.App/              # WPF Application (UI + Tray)
│   ├── Views/
│   ├── ViewModels/
│   └── App.xaml
│
├── Rustitles.Core/             # Core Logic (reusable)
│   ├── Services/
│   │   ├── SubliminalService.cs    # Calls Python/Subliminal
│   │   ├── PlexService.cs          # Plex API client
│   │   ├── PlexWebhookServer.cs    # ASP.NET Core webhook listener
│   │   ├── FileScanner.cs          # Video file discovery
│   │   └── SubtitleChecker.cs      # Check existing subtitles
│   ├── Models/
│   │   ├── MediaItem.cs
│   │   ├── DownloadJob.cs
│   │   └── PlexLibrary.cs
│   └── Data/
│       ├── AppDbContext.cs         # EF Core SQLite
│       └── Migrations/
│
└── Rustitles.Tests/            # Unit Tests
```

---

## Key Implementation Details

### 1. Calling Subliminal (Python)

```csharp
public class SubliminalService
{
    public async Task<SubtitleResult> DownloadSubtitleAsync(
        string videoPath, 
        string language,
        CancellationToken ct = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "subliminal",
            Arguments = $"download -l {language} \"{videoPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return new SubtitleResult
        {
            Success = process.ExitCode == 0,
            Output = output,
            Error = error
        };
    }
}
```

### 2. Plex Webhook Server (ASP.NET Core)

```csharp
public class PlexWebhookServer
{
    private WebApplication? _app;

    public async Task StartAsync(int port, Action<PlexWebhookEvent> onEvent)
    {
        var builder = WebApplication.CreateSlimBuilder();
        _app = builder.Build();

        _app.MapPost("/plex/webhook", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var payload = form["payload"].ToString();
            var webhookEvent = JsonSerializer.Deserialize<PlexWebhookEvent>(payload);
            
            if (webhookEvent?.Event == "library.new")
            {
                onEvent(webhookEvent);
            }
            
            return Results.Ok();
        });

        await _app.RunAsync($"http://localhost:{port}");
    }

    public async Task StopAsync() => await _app?.StopAsync();
}
```

### 3. System Tray

```csharp
// Built-in to WPF with Hardcodet.NotifyIcon.Wpf
<tb:TaskbarIcon IconSource="/icon.ico"
                ToolTipText="Rustitles"
                LeftClickCommand="{Binding ShowCommand}">
    <tb:TaskbarIcon.ContextMenu>
        <ContextMenu>
            <MenuItem Header="Scan Plex" Command="{Binding ScanPlexCommand}"/>
            <MenuItem Header="Exit" Command="{Binding ExitCommand}"/>
        </ContextMenu>
    </tb:TaskbarIcon.ContextMenu>
</tb:TaskbarIcon>
```

---

## Migration Path

### Option A: Fresh Start (Recommended)
Start new .NET project, reimplement features incrementally:
1. Week 1: Core services (Subliminal, Plex API)
2. Week 2: Webhook server, file scanning
3. Week 3: WPF UI with tray
4. Week 4: Activity tracking, database
5. Week 5: Polish, settings, notifications

### Option B: Port Rust Logic
Translate Rust code to C# file-by-file. More tedious, same outcome.

---

## Recommendation

**Go pure .NET.** 

The benefits are significant:
- Single language/codebase
- Native Windows experience
- Faster development
- Better tooling

The only real loss is cross-platform support, but:
1. The Rust/egui version can remain for Linux/macOS users
2. Most users are on Windows anyway
3. Could later add Avalonia for cross-platform .NET if needed

---

## Revised Timeline (Pure .NET)

| Phase | Duration | Deliverable |
|-------|----------|-------------|
| 1. Project setup + Core services | 1 week | Subliminal + Plex working |
| 2. Webhook server + File scanning | 1 week | Full backend functionality |
| 3. WPF UI + System tray | 1 week | Basic UI with tray |
| 4. Activity feed + Database | 1 week | Real-time activity view |
| 5. Settings + Notifications | 1 week | Feature complete |
| 6. Polish + Testing | 1 week | Release ready |

**Total: ~6 weeks** (same as hybrid approach, but simpler)
