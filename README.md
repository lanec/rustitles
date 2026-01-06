# WinTitles

> A modern Windows subtitle downloader with Plex integration and AI-powered features.

## Features

- 🎬 **Plex Integration** - Scan your Plex library, auto-download subtitles via webhooks
- 🤖 **AI Search Suggestions** - OpenAI-powered filename analysis and smart renaming
- 📥 **OpenSubtitles API** - Direct REST API integration, no Python required
- 🖥️ **System Tray** - Runs quietly in the background
- 🔔 **Toast Notifications** - Know when subtitles are downloaded or fail
- 🎨 **Dracula Theme** - Modern dark UI
- 🌍 **Multi-language** - Download subtitles in any language

## Installation

### Prerequisites
- Windows 10/11
- .NET 8 SDK

### Build & Run
```powershell
cd WinTitles
dotnet build
dotnet run --project src/WinTitles.App
```

## Configuration

### Required: OpenSubtitles Account
1. Get an API key from [opensubtitles.com/consumers](https://www.opensubtitles.com/consumers)
2. Enter your API key, username, and password in Settings

### Optional: OpenAI (for AI features)
1. Get an API key from [platform.openai.com](https://platform.openai.com)
2. Enter your API key in Settings
3. Recommended model: `gpt-4o-mini`

### Optional: Plex Integration
1. Enter your Plex server URL (e.g., `http://localhost:32400`)
2. Enter your Plex authentication token
3. Enable webhooks for automatic subtitle downloads on new media

## Usage

### Scan Plex Library
1. Click "Scan Plex" to scan all movies and TV episodes
2. Items missing subtitles are auto-selected
3. Click "Download Selected" to batch download

### Manual Search
1. Right-click any item → "Search Subtitles"
2. Browse results sorted by downloads
3. Click to download specific subtitle

### AI Suggestions
1. Right-click any item → "AI Search Suggestions"
2. View detected title/year/episode info
3. Choose a search query or rename the file

## Documentation

See [WinTitles/README.md](WinTitles/README.md) for detailed documentation.

## License

MIT License
