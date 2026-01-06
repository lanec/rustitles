# WinTitles Design Plan

## Summary

**WinTitles** is a Windows-native subtitle downloader that serves as a Plex companion app while also working standalone for users with local media files.

This plan outlines the complete design and implementation of WinTitles using .NET 8 and WPF. The app prioritizes:

1. **Plex Companion + Standalone** - Works with Plex or independently
2. **System Tray First** - True background operation with always-visible tray icon
3. **Activity-Centric** - Focus on what's happening, not configuration
4. **Windows Native** - Toast notifications, jump lists, proper startup integration

## Project Details

| Field | Value |
|-------|-------|
| **Name** | WinTitles |
| **Tagline** | Your Windows companion for automatic subtitle downloads |
| **Repository** | `WinTitles/` folder (future: github.com/lanec/wintitles) |
| **Technology** | .NET 8, WPF, ASP.NET Core |
| **Platform** | Windows 10/11 only |

## Use Cases

### 🎬 Plex Companion Mode
- Connect to Plex Media Server
- Auto-download subtitles when new media is added
- Scan existing libraries for missing subtitles
- Background operation via webhooks

### 📁 Standalone Mode
- Scan any folder for movies/TV shows
- Drag & drop files or folders
- One-click subtitle downloads
- No Plex required

## Feasibility Assessment

### ✅ Highly Feasible

| Aspect | Assessment |
|--------|------------|
| **Technical** | .NET WPF is mature, well-documented, excellent tray support |
| **Effort** | ~6 weeks for full implementation |
| **User Benefit** | Significantly improved UX, true background operation |

### Trade-offs

| Pro | Con |
|-----|-----|
| Native Windows feel | Windows-only |
| Full tray support | Larger download size (+~50MB for .NET runtime) |
| Single codebase | Requires .NET 8 runtime |
| Better notifications | No Linux/macOS support |

## Documents

| Document | Description |
|----------|-------------|
| [01-architecture-overview.md](01-architecture-overview.md) | System architecture, tech stack, deployment model |
| [02-ui-design-philosophy.md](02-ui-design-philosophy.md) | Activity-centric design principles, visual language |
| [03-system-tray-design.md](03-system-tray-design.md) | Tray icon states, context menu, notifications |
| [04-backend-api-spec.md](04-backend-api-spec.md) | Full HTTP API specification for Rust backend |
| [05-main-window-wireframes.md](05-main-window-wireframes.md) | ASCII wireframes for all UI states |
| [06-implementation-roadmap.md](06-implementation-roadmap.md) | Phased implementation plan with milestones |

## Quick Start

### Phase 1: Core Foundation (Week 1)
1. Set up .NET 8 WPF project with system tray
2. Implement SubliminalService (Python CLI wrapper)
3. Basic file scanning
4. Demonstrate: Tray icon + scan folder + download subtitles

### Phase 2: Plex Integration (Week 2)
1. PlexService (API client)
2. PlexWebhookServer (ASP.NET Core)
3. Connect to Plex, scan libraries

### Phase 3: Full UI (Weeks 3-4)
1. Activity-centric main window
2. Settings modal
3. Toast notifications

## Why Pure .NET?

See [07-pure-dotnet-analysis.md](07-pure-dotnet-analysis.md) for the full analysis.

**Summary:** .NET can do everything the Rust version does, with better Windows integration. No need for a hybrid Rust+.NET architecture.

## Alternatives Considered

| Option | Rejected Because |
|--------|------------------|
| Rust + egui | System tray dependency conflicts |
| Rust backend + .NET frontend | Unnecessary complexity |
| Electron | Heavy runtime, not native feeling |
| MAUI | Less mature tray support |

## Recommendation

**Build WinTitles as a pure .NET application.** Benefits:

- Single codebase, single language
- Native Windows experience (tray, notifications, startup)
- Plex companion + standalone flexibility
- ~6 weeks to complete implementation
