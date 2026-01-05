# Rustitles .NET Frontend Architecture

## Overview

A .NET WPF frontend that provides a modern, activity-centric user interface with full system tray support, communicating with the existing Rust backend via a local HTTP API or named pipes.

## Why .NET for Windows Frontend?

1. **Native System Tray Support** - WPF/WinForms has excellent `NotifyIcon` support
2. **Modern UI Frameworks** - WPF with MVVM, or WinUI 3 for fluent design
3. **Easy Windows Integration** - Toast notifications, jump lists, startup registration
4. **Rich Data Binding** - Perfect for real-time activity feeds
5. **No Dependency Conflicts** - Avoids GTK/tray-icon conflicts in Rust

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    .NET WPF Frontend                        │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐ │
│  │ System Tray │  │ Main Window │  │ Toast Notifications │ │
│  │   (Always)  │  │ (On Demand) │  │    (Events)         │ │
│  └─────────────┘  └─────────────┘  └─────────────────────┘ │
│                          │                                  │
│              ┌───────────┴───────────┐                     │
│              │   RustitlesClient     │                     │
│              │   (HTTP/WebSocket)    │                     │
│              └───────────┬───────────┘                     │
└──────────────────────────┼──────────────────────────────────┘
                           │ localhost:9877
┌──────────────────────────┼──────────────────────────────────┐
│                    Rust Backend                             │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐ │
│  │ HTTP API    │  │ Plex        │  │ Subliminal          │ │
│  │ Server      │  │ Webhook     │  │ Downloads           │ │
│  │ (port 9877) │  │ (port 9876) │  │                     │ │
│  └─────────────┘  └─────────────┘  └─────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

## Communication Protocol

### Option A: HTTP REST + Server-Sent Events (Recommended)
- REST API for commands (scan, download, configure)
- SSE for real-time activity updates
- Simple, well-supported, debuggable

### Option B: WebSocket
- Bidirectional real-time communication
- More complex but lower latency

### Option C: Named Pipes
- Windows-native IPC
- No network stack overhead
- Best for single-machine deployment

## Technology Stack

| Component | Technology | Rationale |
|-----------|------------|-----------|
| UI Framework | WPF + MaterialDesign | Modern look, excellent data binding |
| Architecture | MVVM + CommunityToolkit | Clean separation, testable |
| System Tray | Hardcodet.NotifyIcon.Wpf | Best-in-class WPF tray support |
| HTTP Client | HttpClient + System.Text.Json | Built-in, fast |
| Real-time | ServerSentEvents or SignalR | Live activity updates |
| Notifications | Microsoft.Toolkit.Uwp.Notifications | Windows toast notifications |

## Deployment Model

```
rustitles/
├── Rustitles.exe           # .NET Frontend (what user launches)
├── rustitles-backend.exe   # Rust backend (launched by frontend)
└── resources/
    └── icons/
```

**Startup Flow:**
1. User clicks Rustitles.exe (or auto-starts with Windows)
2. .NET app starts, shows tray icon
3. .NET app launches rust backend as child process
4. Backend starts HTTP API on localhost:9877
5. Frontend connects and subscribes to activity stream
6. User interacts with tray menu or opens main window

## Key Benefits

- **True Background Operation** - Tray icon always visible, main window optional
- **Windows Native Feel** - Proper toast notifications, jump lists
- **Activity-First Design** - See what's happening, not configure things
- **Progressive Disclosure** - Settings hidden until needed
- **Real-time Updates** - Live activity feed, no polling needed
