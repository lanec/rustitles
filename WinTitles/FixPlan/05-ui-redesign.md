# UI Redesign

## Overview
The UI must support the new workflow states and provide clear, actionable information at every step.

---

## Main Window Layout

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ WinTitles                                                    [⚙ Settings]  │
│ ─────────────────────────────────────────────────────────────────────────── │
│                                                                             │
│ ┌─────────────────────────────────────────────────────────────────────────┐ │
│ │                         STATUS BAR (varies by state)                    │ │
│ └─────────────────────────────────────────────────────────────────────────┘ │
│                                                                             │
│ ┌─────────────────────────────────────────────────────────────────────────┐ │
│ │                         ACTION BAR (varies by state)                    │ │
│ └─────────────────────────────────────────────────────────────────────────┘ │
│                                                                             │
│ ┌─────────────────────────────────────────────────────────────────────────┐ │
│ │                                                                         │ │
│ │                                                                         │ │
│ │                         CONTENT AREA                                    │ │
│ │                     (list of items/results)                             │ │
│ │                                                                         │ │
│ │                                                                         │ │
│ └─────────────────────────────────────────────────────────────────────────┘ │
│                                                                             │
│ ─────────────────────────────────────────────────────────────────────────── │
│ OpenSubtitles API    │    Status: Ready                          v1.0      │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## State-Specific UI

### IDLE State

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Ready to scan                                                               │
├─────────────────────────────────────────────────────────────────────────────┤
│ [📁 Scan Folder]    [📺 Scan Plex]    [🔗 Start Webhook]                    │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│                         No items to display                                 │
│                                                                             │
│              Click "Scan Plex" to discover media and find                  │
│                     missing subtitles in your library                       │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### SCANNING State

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Scanning Plex library...                                                    │
│ ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  45%     │
│ Checking subtitles: 900 of 2,000 items                                      │
│ Current: The Matrix (1999)                                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│                                              [Cancel Scan]                  │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│     📺  Movies library: 1,200 items found                                   │
│     📺  TV Shows library: scanning...                                       │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### REVIEW State

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Scan Complete                                                               │
│ Found 2,000 items  •  1,555 have subtitles  •  445 missing subtitles       │
├─────────────────────────────────────────────────────────────────────────────┤
│ [✓ Select Missing]  [☐ Deselect All]  │  [▶ Download 445 Selected]  [✕ Clear]│
├─────────────────────────────────────────────────────────────────────────────┤
│ Show: [All Items ▾]     Search: [____________________]    445 selected     │
├─────────────────────────────────────────────────────────────────────────────┤
│ ☑│ 🎬 │ The Patriot (2000)              │ Missing [en]  │ D:\Movies\The... │
│ ☑│ 🎬 │ Patch Adams (1998)              │ Missing [en]  │ D:\Movies\Pat... │
│ ☐│ 🎬 │ The Matrix (1999)               │ ✓ Has [en]    │ D:\Movies\The... │
│ ☑│ 📺 │ Breaking Bad S01E01             │ Missing [en]  │ D:\TV\Breaki...  │
│ ☑│ 📺 │ Breaking Bad S01E02             │ Missing [en]  │ D:\TV\Breaki...  │
│  │    │                                 │               │                   │
│  │    │        ... (scrollable list)    │               │                   │
│  │    │                                 │               │                   │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Filter dropdown options:**
- All Items
- Missing Subtitles
- Has Subtitles
- Movies Only
- TV Episodes Only

### DOWNLOADING State

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Downloading subtitles...                                                    │
│ ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  52%      │
│ 234 of 445 complete  •  12 failed  •  5 active  •  ETA: ~8 minutes         │
├─────────────────────────────────────────────────────────────────────────────┤
│                                    [⏸ Pause]  [✕ Cancel]                   │
├─────────────────────────────────────────────────────────────────────────────┤
│ Currently downloading:                                                      │
│   • The Patriot (2000)                                                      │
│   • Patch Adams (1998)                                                      │
│   • Breaking Bad S01E05                                                     │
│   • The Shawshank Redemption (1994)                                         │
│   • Forrest Gump (1994)                                                     │
├─────────────────────────────────────────────────────────────────────────────┤
│ Show: [All ▾]                                                               │
├─────────────────────────────────────────────────────────────────────────────┤
│ ✓│ The Matrix Reloaded (2003)         │ Downloaded    │ 2 seconds ago      │
│ ✓│ The Matrix Revolutions (2003)      │ Downloaded    │ 5 seconds ago      │
│ ✗│ Some Obscure Film (2020)           │ Not found     │ [🔍 Search]        │
│ ⏳│ Waiting...                         │ Pending       │ #199 in queue      │
└─────────────────────────────────────────────────────────────────────────────┘
```

### PAUSED State

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Downloads Paused                                                            │
│ ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  52%      │
│ 234 of 445 complete  •  12 failed  •  199 remaining                        │
├─────────────────────────────────────────────────────────────────────────────┤
│                                    [▶ Resume]  [✕ Cancel]                  │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│              Downloads are paused. Click Resume to continue.               │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### COMPLETE State

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Downloads Complete                                                          │
│ ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓  100%     │
│ 433 downloaded  •  12 failed  •  Completed in 15 minutes                   │
├─────────────────────────────────────────────────────────────────────────────┤
│ [🔄 Retry Failed (12)]  [📁 New Scan]  [✕ Clear All]                        │
├─────────────────────────────────────────────────────────────────────────────┤
│ Show: [Failed Only ▾]     Search: [____________________]                    │
├─────────────────────────────────────────────────────────────────────────────┤
│ ✗│ Some Obscure Film (2020)           │ Not found     │ [🔍 Manual Search] │
│ ✗│ Foreign Film (2019)                │ API error     │ [🔍 Manual Search] │
│ ✗│ Indie Movie (2021)                 │ No match      │ [🔍 Manual Search] │
│ ✗│ Documentary (2018)                 │ Rate limited  │ [🔄 Retry]         │
│  │                                    │               │                    │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Component Specifications

### 1. Status Bar
- **Always visible** at top of content area
- Shows current state and key metrics
- Progress bar when applicable
- ETA during downloads

### 2. Action Bar
- **Context-sensitive** based on current state
- Primary action always on the right (most prominent)
- Destructive actions (Cancel, Clear) use different styling

### 3. Filter Bar
- Dropdown for quick filtering
- Search box for text search
- Selection count when in Review state

### 4. Item List
- **Virtualized** for performance with large lists
- Fixed columns: checkbox, type icon, title, status, path
- Hover shows full path in tooltip
- Click row to see details or take action
- Right-click context menu for per-item actions

### 5. Item Status Icons
| Icon | Meaning |
|------|---------|
| ☑ | Selected (checkbox) |
| ☐ | Not selected |
| 🎬 | Movie |
| 📺 | TV Episode |
| ✓ | Success (green) |
| ✗ | Failed (red) |
| ⏳ | Pending |
| 🔄 | Downloading/Processing |
| ⏸ | Paused |

---

## Manual Search Dialog

When user clicks "Manual Search" on a failed item:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Manual Subtitle Search                                              [X]    │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│ File: Some Obscure Film (2020)                                             │
│ Path: D:\Movies\Some.Obscure.Film.2020.1080p.BluRay.mkv                    │
│                                                                             │
│ ─────────────────────────────────────────────────────────────────────────── │
│                                                                             │
│ Search:  [Some Obscure Film_______________]  [🔍 Search]                   │
│                                                                             │
│ Results:                                                                    │
│ ┌─────────────────────────────────────────────────────────────────────────┐ │
│ │ ○ Some.Obscure.Film.2020.1080p.BluRay.srt          │ 1,234 downloads   │ │
│ │ ○ Some.Obscure.Film.2020.WEB-DL.srt                │ 567 downloads     │ │
│ │ ○ Some_Obscure_Film_2020.srt                       │ 123 downloads     │ │
│ └─────────────────────────────────────────────────────────────────────────┘ │
│                                                                             │
│                                              [Cancel]  [Download Selected] │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Responsive Behavior

### Window Resize
- Minimum size: 800x600
- Item list takes remaining space
- Columns resize proportionally
- Title column truncates with ellipsis

### Large Lists (1000+ items)
- Virtualized ItemsControl (VirtualizingStackPanel)
- Lazy loading of additional metadata
- "Loading..." placeholder for off-screen items

---

## Color Scheme (Dracula Theme)

| Element | Color | Hex |
|---------|-------|-----|
| Background | Dark | #282A36 |
| Surface | Slightly lighter | #44475A |
| Primary | Purple | #BD93F9 |
| Success | Green | #50FA7B |
| Warning | Orange | #FFB86C |
| Error | Red | #FF5555 |
| Text Primary | White | #F8F8F2 |
| Text Secondary | Gray | #6272A4 |

---

## Accessibility

1. **Keyboard navigation** - Tab through all controls, Enter to activate
2. **Screen reader support** - Proper ARIA labels
3. **High contrast** - Sufficient color contrast ratios
4. **Focus indicators** - Visible focus ring on all interactive elements

---

## Next Document
See `06-implementation-order.md` for the step-by-step implementation plan.
