# UI Design Philosophy: Activity-Centric Experience

## Core Principle

**"Show me what's happening, not how to configure it."**

The current UI is settings-first: language selection, folder paths, checkboxes. The new UI should be activity-first: what files were found, what's downloading, what succeeded/failed.

## Design Shift

### Current (Settings-Centric)
```
┌────────────────────────────────────────┐
│ ☑ Python Installed                     │
│ ☑ Subliminal Installed                 │
│ Languages: [en ▾]                      │
│ Concurrent: [25 ▾]                     │
│ Folder: [Select Folder]               │
│ ☑ Ignore Embedded  ☑ Overwrite        │
│ ─────────────────────────────────────  │
│ Plex Integration                       │
│   Server: [____________]               │
│   Token:  [____________]               │
│ ─────────────────────────────────────  │
│ Status: Scanning...                    │
└────────────────────────────────────────┘
```

### New (Activity-Centric)
```
┌────────────────────────────────────────┐
│ 📂 Rustitles              [⚙] [━]     │
├────────────────────────────────────────┤
│ ┌──────────────────────────────────┐   │
│ │  ⏳ Downloading 3 of 47          │   │
│ │  ████████████░░░░░░░░░  47%      │   │
│ └──────────────────────────────────┘   │
│                                        │
│ RECENT ACTIVITY                        │
│ ┌──────────────────────────────────┐   │
│ │ ✓ The Matrix (1999)         [en] │   │
│ │ ✓ Inception (2010)          [en] │   │
│ │ ⏳ Interstellar (2014)      [en] │   │
│ │ ✗ Dune (2021) - No subs     [en] │   │
│ │ ⊘ Avatar (2009) - Exists    [en] │   │
│ └──────────────────────────────────┘   │
│                                        │
│ SOURCES                          [+]   │
│ ┌──────────────────────────────────┐   │
│ │ 📺 Plex: Movies (234 items)      │   │
│ │ 📁 D:\Movies (47 scanning...)    │   │
│ └──────────────────────────────────┘   │
└────────────────────────────────────────┘
```

## Information Hierarchy

### Level 1: Glance (System Tray)
- Icon color indicates status (green=idle, blue=working, red=errors)
- Tooltip shows: "Rustitles - 3 downloading, 2 failed"
- Right-click menu for quick actions

### Level 2: Summary (Main Window - Default View)
- Current progress bar (if active)
- Recent activity feed (last 20 items)
- Source status cards (Plex, folders)

### Level 3: Details (Expanded Views)
- Full activity history with search/filter
- Per-file details and retry options
- Source configuration panels

### Level 4: Settings (Gear Icon → Modal)
- Language preferences
- Download behavior
- Plex connection
- Startup options

## Visual Language

### Status Colors
| Status | Color | Hex |
|--------|-------|-----|
| Success | Green | #50FA7B |
| In Progress | Purple | #BD93F9 |
| Failed | Red | #FF5555 |
| Skipped | Gray | #6272A4 |
| Discovered | Cyan | #8BE9FD |

### Icons
| Concept | Icon |
|---------|------|
| Movie | 🎬 |
| TV Show | 📺 |
| Folder | 📁 |
| Plex | (Plex logo or 📡) |
| Success | ✓ |
| Failed | ✗ |
| Processing | ⏳ |
| Skipped | ⊘ |

## Interaction Patterns

### 1. Progressive Disclosure
Settings are hidden until needed. First-run wizard handles initial config.

### 2. Contextual Actions
Right-click on any item shows relevant actions:
- Retry download
- Open file location
- View in Plex
- Copy path

### 3. Drag & Drop
Drop folders/files onto window to scan and download.

### 4. Keyboard Shortcuts
| Key | Action |
|-----|--------|
| F5 | Refresh/Rescan |
| Ctrl+, | Open Settings |
| Esc | Minimize to tray |
| Ctrl+F | Filter activity |

## Responsive Behavior

### Window States
1. **Tray Only** - Background operation, icon visible
2. **Compact** - Small widget showing progress only
3. **Normal** - Full activity view
4. **Expanded** - Activity + details panel

### Minimize Behavior
- Close button (X) → Minimize to tray
- Minimize button (━) → Minimize to taskbar
- Explicit "Exit" in tray menu to quit

## First-Run Experience

```
┌────────────────────────────────────────┐
│         Welcome to Rustitles           │
│                                        │
│   Let's get you set up in 3 steps:     │
│                                        │
│   1. Choose Languages     [ ] Done     │
│   2. Connect Plex         [ ] Skip     │
│   3. Add a Folder         [ ] Skip     │
│                                        │
│              [Get Started]             │
└────────────────────────────────────────┘
```

After first run, settings are accessible but not prominent.
