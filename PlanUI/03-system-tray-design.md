# System Tray Design

## Tray Icon States

The tray icon dynamically reflects application status using color and animation.

### Icon Variants

| State | Icon | Description |
|-------|------|-------------|
| Idle | ![green](green) | System is idle, no active downloads |
| Working | ![purple-animated](purple) | Downloads in progress (subtle pulse) |
| Success | ![green-check](green) | Recent downloads completed |
| Error | ![red-badge](red) | Failed downloads need attention |
| Disabled | ![gray](gray) | Backend not running |

### Tooltip

Dynamic tooltip showing current status:
```
Idle:      "Rustitles - Ready"
Working:   "Rustitles - Downloading 3 of 47..."
Success:   "Rustitles - 47 subtitles downloaded"
Error:     "Rustitles - 2 failed (click to retry)"
```

## Context Menu Design

```
┌─────────────────────────────┐
│ ● Rustitles                 │  ← Status header (clickable to open)
├─────────────────────────────┤
│ 📊 Activity: 3 downloading  │  ← Live status line
├─────────────────────────────┤
│ 🔍 Scan Plex Library        │
│ 📁 Scan Folder...           │
├─────────────────────────────┤
│ ⏸ Pause Downloads           │  ← Toggle: Pause/Resume
│ 🔄 Retry Failed (2)         │  ← Only shown if failures exist
├─────────────────────────────┤
│ ⚙ Settings                  │
│ 📋 View Logs                │
├─────────────────────────────┤
│ ❌ Exit                     │
└─────────────────────────────┘
```

## Menu Behavior

### Single Click
- Opens/focuses main window

### Double Click  
- Opens/focuses main window (same as single)

### Right Click
- Shows context menu

## Toast Notifications

### When to Notify

| Event | Notification | Priority |
|-------|--------------|----------|
| Batch complete | "Downloaded 47 subtitles" | Normal |
| New media detected | "New: The Matrix (1999)" | Low |
| Download failed | "Failed: Dune (2021)" | High |
| Plex connected | "Connected to Plex: MovieServer" | Low |
| Backend error | "Backend disconnected" | Critical |

### Notification Actions

```
┌─────────────────────────────────────┐
│ 🎬 Rustitles                        │
│                                     │
│ Downloaded subtitle for:            │
│ The Matrix (1999)                   │
│                                     │
│ [Open Folder]  [View in Plex]       │
└─────────────────────────────────────┘
```

### Notification Settings

User can configure:
- [ ] Show notifications for successful downloads
- [x] Show notifications for failures
- [x] Show notifications for new Plex media
- [ ] Play sound on completion

## Jump List Integration

Windows taskbar jump list when right-clicking the taskbar icon:

```
┌─────────────────────────────┐
│ Tasks                       │
│   Scan Plex Library         │
│   Scan Folder...            │
│   Open Settings             │
├─────────────────────────────┤
│ Recent                      │
│   D:\Movies                 │
│   D:\TV Shows               │
└─────────────────────────────┘
```

## Startup Behavior

### With "Start with Windows" enabled:
1. App starts minimized to tray (no window)
2. Backend launches automatically
3. Plex webhook server starts if configured
4. Toast: "Rustitles running in background"

### Manual launch:
1. App starts with main window visible
2. Backend launches
3. Ready state

## Tray Icon Implementation (WPF)

```csharp
// Using Hardcodet.NotifyIcon.Wpf
<tb:TaskbarIcon x:Name="TrayIcon"
                IconSource="{Binding TrayIconSource}"
                ToolTipText="{Binding TrayTooltip}"
                LeftClickCommand="{Binding ShowWindowCommand}"
                DoubleClickCommand="{Binding ShowWindowCommand}">
    <tb:TaskbarIcon.ContextMenu>
        <ContextMenu>
            <MenuItem Header="{Binding StatusHeader}" 
                      Command="{Binding ShowWindowCommand}"
                      FontWeight="Bold"/>
            <Separator/>
            <MenuItem Header="Scan Plex Library" 
                      Command="{Binding ScanPlexCommand}"/>
            <MenuItem Header="Scan Folder..." 
                      Command="{Binding ScanFolderCommand}"/>
            <Separator/>
            <MenuItem Header="{Binding PauseResumeText}" 
                      Command="{Binding TogglePauseCommand}"/>
            <MenuItem Header="{Binding RetryFailedText}" 
                      Command="{Binding RetryFailedCommand}"
                      Visibility="{Binding HasFailures}"/>
            <Separator/>
            <MenuItem Header="Settings" 
                      Command="{Binding ShowSettingsCommand}"/>
            <MenuItem Header="View Logs" 
                      Command="{Binding ViewLogsCommand}"/>
            <Separator/>
            <MenuItem Header="Exit" 
                      Command="{Binding ExitCommand}"/>
        </ContextMenu>
    </tb:TaskbarIcon.ContextMenu>
</tb:TaskbarIcon>
```

## Icon Assets Needed

```
/Resources/Icons/
├── tray-idle.ico           # Green, 16x16 and 32x32
├── tray-working.ico        # Purple, animated frames
├── tray-success.ico        # Green with checkmark
├── tray-error.ico          # Red with badge
├── tray-disabled.ico       # Gray
└── app-icon.ico            # Main app icon, all sizes
```
