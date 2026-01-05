# Implementation Roadmap

## Overview

This roadmap outlines the phased implementation of the .NET WPF frontend for Rustitles.

---

## Phase 1: Foundation (Week 1-2)

### 1.1 Project Setup
- [ ] Create .NET 8 WPF project: `Rustitles.UI`
- [ ] Configure project structure (MVVM)
- [ ] Add NuGet packages:
  - `CommunityToolkit.Mvvm` - MVVM helpers
  - `Hardcodet.NotifyIcon.Wpf` - System tray
  - `MaterialDesignThemes` - UI components
  - `Microsoft.Toolkit.Uwp.Notifications` - Toast notifications
- [ ] Set up dependency injection

### 1.2 Rust Backend API
- [ ] Add Axum HTTP server to Rust backend (port 9877)
- [ ] Implement `/health` endpoint
- [ ] Implement `/status` endpoint
- [ ] Implement `/activity/stream` SSE endpoint
- [ ] Test API with curl/Postman

### 1.3 Basic Communication
- [ ] Create `RustitlesClient` class in .NET
- [ ] Implement health check
- [ ] Implement SSE subscription
- [ ] Backend process management (start/stop)

**Deliverable:** .NET app can launch Rust backend and receive activity events.

---

## Phase 2: System Tray (Week 2-3)

### 2.1 Tray Icon
- [ ] Implement `TaskbarIcon` with basic menu
- [ ] Create icon assets (idle, working, error states)
- [ ] Dynamic tooltip based on status
- [ ] Left-click to show/hide window

### 2.2 Context Menu
- [ ] Status header (clickable)
- [ ] Scan Plex Library command
- [ ] Scan Folder command
- [ ] Pause/Resume toggle
- [ ] Settings command
- [ ] Exit command

### 2.3 Startup Integration
- [ ] Implement "Start with Windows" via Registry
- [ ] Start minimized to tray option
- [ ] Auto-launch backend on startup

**Deliverable:** Fully functional system tray with menu.

---

## Phase 3: Main Window - Activity View (Week 3-4)

### 3.1 Window Shell
- [ ] Custom title bar (or standard)
- [ ] Window state management (min/max/close behaviors)
- [ ] Remember window position/size

### 3.2 Progress Panel
- [ ] Current operation display
- [ ] Progress bar with percentage
- [ ] Pause/Cancel buttons
- [ ] Current item indicator

### 3.3 Activity Feed
- [ ] Virtual scrolling list (performance)
- [ ] Activity item template (icon, title, status, time)
- [ ] Real-time updates from SSE
- [ ] Status-based coloring
- [ ] Retry button for failed items
- [ ] Context menu per item

### 3.4 Sources Panel
- [ ] Source cards (Plex, folders)
- [ ] Status indicators
- [ ] Quick actions (Scan Now, Remove)
- [ ] Add source button

**Deliverable:** Main window showing live activity and sources.

---

## Phase 4: Backend API Completion (Week 4-5)

### 4.1 Download Endpoints
- [ ] `POST /downloads/scan/folder`
- [ ] `POST /downloads/scan/plex`
- [ ] `POST /downloads/start`
- [ ] `POST /downloads/pause`
- [ ] `POST /downloads/resume`
- [ ] `POST /downloads/cancel`
- [ ] `POST /downloads/retry`

### 4.2 Configuration Endpoints
- [ ] `GET /config`
- [ ] `PUT /config`
- [ ] `POST /config/plex/test`

### 4.3 Plex Endpoints
- [ ] `POST /plex/service/start`
- [ ] `POST /plex/service/stop`
- [ ] `GET /plex/libraries`

### 4.4 Activity Endpoints
- [ ] `GET /activity/history` with pagination
- [ ] Activity persistence (SQLite)

**Deliverable:** Full API coverage for all features.

---

## Phase 5: Settings UI (Week 5-6)

### 5.1 Settings Window
- [ ] Tab navigation (General, Plex, Downloads, Startup, About)
- [ ] Save/Cancel behavior

### 5.2 General Tab
- [ ] Language selection (multi-select)
- [ ] Behavior checkboxes

### 5.3 Plex Tab
- [ ] Server URL input
- [ ] Token input (with paste helper)
- [ ] Connection test
- [ ] Webhook configuration

### 5.4 Downloads Tab
- [ ] Concurrent downloads slider
- [ ] File handling options

### 5.5 Startup Tab
- [ ] Start with Windows
- [ ] Start minimized
- [ ] Notification preferences

**Deliverable:** Complete settings UI.

---

## Phase 6: Polish & Advanced Features (Week 6-7)

### 6.1 Notifications
- [ ] Toast notifications for events
- [ ] Configurable notification types
- [ ] Action buttons in toasts

### 6.2 Activity History
- [ ] Full history view with filtering
- [ ] Search functionality
- [ ] Export to CSV

### 6.3 Drag & Drop
- [ ] Drop folders onto window
- [ ] Drop files onto window

### 6.4 Keyboard Shortcuts
- [ ] F5 - Refresh
- [ ] Ctrl+, - Settings
- [ ] Esc - Minimize to tray

### 6.5 First-Run Experience
- [ ] Setup wizard
- [ ] Welcome screen
- [ ] Quick start guide

**Deliverable:** Polished, feature-complete application.

---

## Phase 7: Testing & Release (Week 7-8)

### 7.1 Testing
- [ ] Unit tests for ViewModels
- [ ] Integration tests for API client
- [ ] Manual testing checklist

### 7.2 Packaging
- [ ] Single-file deployment
- [ ] Installer (MSIX or Inno Setup)
- [ ] Auto-update mechanism

### 7.3 Documentation
- [ ] User guide
- [ ] Release notes
- [ ] Migration guide from old UI

**Deliverable:** Release-ready package.

---

## Project Structure

```
Rustitles.UI/
├── Rustitles.UI.sln
├── src/
│   └── Rustitles.UI/
│       ├── App.xaml
│       ├── App.xaml.cs
│       ├── ViewModels/
│       │   ├── MainViewModel.cs
│       │   ├── SettingsViewModel.cs
│       │   ├── ActivityViewModel.cs
│       │   └── TrayViewModel.cs
│       ├── Views/
│       │   ├── MainWindow.xaml
│       │   ├── SettingsWindow.xaml
│       │   └── ActivityHistoryWindow.xaml
│       ├── Models/
│       │   ├── ActivityItem.cs
│       │   ├── Source.cs
│       │   └── AppSettings.cs
│       ├── Services/
│       │   ├── RustitlesClient.cs
│       │   ├── BackendManager.cs
│       │   ├── NotificationService.cs
│       │   └── StartupService.cs
│       ├── Controls/
│       │   ├── ActivityFeed.xaml
│       │   ├── ProgressPanel.xaml
│       │   └── SourceCard.xaml
│       └── Resources/
│           ├── Icons/
│           ├── Styles/
│           └── Themes/
└── tests/
    └── Rustitles.UI.Tests/
```

---

## Dependencies

### NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| CommunityToolkit.Mvvm | 8.x | MVVM infrastructure |
| Hardcodet.NotifyIcon.Wpf | 1.x | System tray icon |
| MaterialDesignThemes | 4.x | Modern UI components |
| Microsoft.Toolkit.Uwp.Notifications | 7.x | Toast notifications |
| Microsoft.Extensions.DependencyInjection | 8.x | DI container |
| System.Text.Json | 8.x | JSON serialization |

### Rust Backend Changes

| Crate | Purpose |
|-------|---------|
| axum | HTTP API server |
| tokio | Async runtime (already present) |
| tower-http | CORS, logging middleware |
| serde_json | JSON serialization (already present) |

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| API design changes | Medium | Medium | Version API, maintain compatibility |
| Performance with large libraries | Low | High | Virtual scrolling, pagination |
| Backend process crashes | Medium | High | Auto-restart, error handling |
| Windows version compatibility | Low | Medium | Target .NET 8, test on Win10/11 |

---

## Success Metrics

- [ ] System tray icon visible and responsive
- [ ] Real-time activity updates < 100ms latency
- [ ] Startup time < 2 seconds
- [ ] Memory usage < 100MB idle
- [ ] All existing features accessible
- [ ] User can operate entirely from tray menu
