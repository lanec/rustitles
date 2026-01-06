# WinTitles

> Your Windows companion for automatic subtitle downloads. Works with Plex or standalone.

<p align="center">
  <img src="src/WinTitles.App/Resources/app.ico" alt="WinTitles Logo" width="128"/>
</p>

## What is WinTitles?

**WinTitles** is a Windows-native subtitle downloader that runs quietly in your system tray, automatically fetching subtitles for your media from OpenSubtitles. It features AI-powered filename detection and smart file renaming.

### Two Ways to Use It

#### 🎬 As a Plex Companion
Connect WinTitles to your Plex Media Server and it will:
- Automatically download subtitles when new media is added via webhooks
- Scan your entire Plex library (movies + TV episodes) for missing subtitles
- Show real-time progress for large libraries (9000+ items)
- Work silently in the background

#### 📁 Standalone Mode
Don't have Plex? No problem. WinTitles works great on its own:
- Point it at any folder containing movies or TV shows
- Scan and download subtitles with one click
- Manual search with OpenSubtitles integration

## Key Features

### Core Features
- 🖥️ **Native Windows Experience** - System tray, toast notifications, Start with Windows
- 📊 **Activity-Centric UI** - See what's happening with real-time progress updates
- 🔔 **Real-time Notifications** - Know when subtitles are downloaded or fail
- 🌍 **Multi-language Support** - Download subtitles in any language
- 🔄 **Background Operation** - Runs silently, always ready
- 📺 **Plex Integration** - Optional but powerful when connected

### AI-Powered Features (OpenAI)
- 🤖 **AI Search Suggestions** - Analyzes filenames to extract title, year, and episode info
- 📝 **Smart File Renaming** - Suggests clean filenames like `Movie Title (2024)` or `Show - S01E01 - Episode Name`
- 🔍 **Multiple Query Options** - Provides main and alternative search queries for better results

### OpenSubtitles Integration
- 🔐 **Full API Support** - Search and download with your OpenSubtitles account
- 📥 **Manual Search Window** - Search, browse results, and download specific subtitles
- ⬇️ **Batch Downloads** - Download subtitles for multiple files at once
- 📊 **Download Quota Tracking** - See remaining downloads for your account

## Relationship to Rustitles

WinTitles is a Windows-focused fork of [Rustitles](https://github.com/lanec/rustitles), rebuilt from the ground up in .NET for the best Windows experience.

| Aspect | Rustitles (Rust) | WinTitles (.NET) |
|--------|------------------|------------------|
| Platform | Cross-platform | Windows 10/11 |
| Focus | Universal compatibility | Windows-native experience |
| System Tray | Limited | Full support |
| Notifications | None | Windows toast notifications |
| AI Features | None | OpenAI integration |
| Best For | Linux/macOS users | Windows users |

## Screenshots

### Main Window
The main window shows all scanned media with subtitle status at a glance. Select items missing subtitles and download them in batch.

### AI Suggestions Dialog
Right-click any file and use "AI Search Suggestions" to:
- See the detected title/year/episode
- Choose from multiple search queries
- Rename the file to a clean format

### Manual Search
Search OpenSubtitles directly, browse results sorted by downloads, and pick the exact subtitle you want.

## Configuration

### Required Settings

1. **OpenSubtitles Account** (for downloads)
   - API Key from [opensubtitles.com/consumers](https://www.opensubtitles.com/consumers)
   - Username and Password for your account

2. **OpenAI API Key** (optional, for AI features)
   - Get from [platform.openai.com](https://platform.openai.com)
   - Recommended model: `gpt-4o-mini`

3. **Plex** (optional)
   - Server URL (e.g., `http://localhost:32400`)
   - Authentication token

## Architecture

```
┌─────────────────────────────────────────┐
│              WinTitles                  │
│                                         │
│  ┌─────────────┐  ┌─────────────────┐  │
│  │ System Tray │  │   Main Window   │  │
│  │  (Always)   │  │   (On Demand)   │  │
│  └─────────────┘  └─────────────────┘  │
│                                         │
│  ┌─────────────────────────────────────┐│
│  │         Core Services               ││
│  │  • OpenSubtitlesService (REST API)  ││
│  │  • OpenAIService (AI suggestions)   ││
│  │  • PlexService (API + Webhooks)     ││
│  │  • ScanService (Library scanner)    ││
│  │  • DownloadQueue (Batch downloads)  ││
│  └─────────────────────────────────────┘│
└─────────────────────────────────────────┘
```

## Tech Stack

- **.NET 8** - Latest LTS runtime
- **WPF** - Windows UI framework with Dracula theme
- **CommunityToolkit.Mvvm** - MVVM infrastructure
- **Hardcodet.NotifyIcon.Wpf** - System tray integration
- **HttpClient** - REST API clients for OpenSubtitles & OpenAI
- **SQLite** - Activity and settings storage

## Development

### Prerequisites

- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022 or VS Code with C# extension

### Building

```powershell
cd WinTitles
dotnet build
dotnet run --project src/WinTitles.App
```

### Project Structure

```
WinTitles/
├── src/
│   ├── WinTitles.App/          # WPF Application
│   │   ├── Views/
│   │   ├── ViewModels/
│   │   ├── Resources/
│   │   └── App.xaml
│   │
│   └── WinTitles.Core/         # Core Logic
│       ├── Services/
│       ├── Models/
│       └── Data/
│
├── tests/
│   └── WinTitles.Tests/
│
└── docs/
    └── design/
```

## Documentation

See the `docs/` folder for technical documentation:

- [OpenSubtitles API Integration](docs/opensubtitles-api-integration.md) - API usage, authentication, and troubleshooting

## Version History

### v1.0.0 (Current)
- Full Plex library scanning with accurate episode counting
- OpenSubtitles REST API integration (search + download)
- AI-powered filename analysis and renaming (OpenAI)
- Manual search window with result browsing
- System tray with left-click to show window
- Dracula-themed dark UI
- Responsive scanning for large libraries (9000+ items)

## License

MIT License - Same as main Rustitles repository.

## Contributing

Contributions welcome! Please open an issue first to discuss proposed changes.
