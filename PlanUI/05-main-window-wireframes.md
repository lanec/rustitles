# Main Window Wireframes

## Window Chrome

```
┌─────────────────────────────────────────────────────────────────┐
│ 🎬 Rustitles                              [─] [□] [×]           │
├─────────────────────────────────────────────────────────────────┤
```

- **Title Bar**: Custom or standard, draggable
- **[─]**: Minimize to taskbar
- **[□]**: Maximize/restore
- **[×]**: Minimize to tray (NOT exit)

---

## Main View (Activity-Centric)

```
┌─────────────────────────────────────────────────────────────────┐
│ 🎬 Rustitles                                    [⚙]  [─][□][×] │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │                                                           │  │
│  │      ⏳  Downloading subtitles...                         │  │
│  │                                                           │  │
│  │      15 of 47 complete                                    │  │
│  │      ████████████████░░░░░░░░░░░░░░░░░░░░░  32%           │  │
│  │                                                           │  │
│  │      Current: Interstellar (2014)                         │  │
│  │                                              [Pause]      │  │
│  │                                                           │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                 │
│  RECENT ACTIVITY                                    [View All]  │
│  ───────────────────────────────────────────────────────────── │
│                                                                 │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │                                                           │  │
│  │  ✓  The Matrix (1999)                    en    just now   │  │
│  │     Movie • Plex: Movies                                  │  │
│  │                                                           │  │
│  │  ✓  Inception (2010)                     en    2 min ago  │  │
│  │     Movie • Plex: Movies                                  │  │
│  │                                                           │  │
│  │  ⏳ Interstellar (2014)                  en    now        │  │
│  │     Movie • Plex: Movies                 ████░░ 45%       │  │
│  │                                                           │  │
│  │  ✗  Dune (2021)                          en    5 min ago  │  │
│  │     Movie • Plex: Movies                 [Retry]          │  │
│  │     Error: No subtitles found on OpenSubtitles            │  │
│  │                                                           │  │
│  │  ⊘  Avatar (2009)                        en    5 min ago  │  │
│  │     Movie • Plex: Movies                                  │  │
│  │     Skipped: Subtitle already exists                      │  │
│  │                                                           │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                 │
│  SOURCES                                                  [+]   │
│  ───────────────────────────────────────────────────────────── │
│                                                                 │
│  ┌─────────────────────────┐  ┌─────────────────────────────┐  │
│  │ 📺 Plex: Movies         │  │ 📁 D:\TV Shows              │  │
│  │    234 items            │  │    156 items                │  │
│  │    ● Connected          │  │    Last scan: 2 hours ago   │  │
│  │    [Scan Now]           │  │    [Scan Now] [Remove]      │  │
│  └─────────────────────────┘  └─────────────────────────────┘  │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Idle State (No Active Downloads)

```
┌─────────────────────────────────────────────────────────────────┐
│ 🎬 Rustitles                                    [⚙]  [─][□][×] │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │                                                           │  │
│  │      ✓  All caught up!                                    │  │
│  │                                                           │  │
│  │      47 subtitles downloaded today                        │  │
│  │      2 failed • 12 skipped                                │  │
│  │                                                           │  │
│  │      [Scan Plex]  [Scan Folder...]  [Retry Failed]        │  │
│  │                                                           │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                 │
│  ... (rest same as above)                                       │
```

---

## Empty State (First Run)

```
┌─────────────────────────────────────────────────────────────────┐
│ 🎬 Rustitles                                    [⚙]  [─][□][×] │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│                                                                 │
│                                                                 │
│                     📂                                          │
│                                                                 │
│              Welcome to Rustitles!                              │
│                                                                 │
│         Automatically download subtitles for                    │
│              your movies and TV shows.                          │
│                                                                 │
│                                                                 │
│         ┌─────────────────────────────────────┐                 │
│         │      Connect to Plex Server         │                 │
│         └─────────────────────────────────────┘                 │
│                                                                 │
│                        — or —                                   │
│                                                                 │
│         ┌─────────────────────────────────────┐                 │
│         │      Select a Folder to Scan        │                 │
│         └─────────────────────────────────────┘                 │
│                                                                 │
│                                                                 │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Settings Modal

```
┌─────────────────────────────────────────────────────────────────┐
│ Settings                                                   [×]  │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────┐                                                │
│  │ General     │  LANGUAGES                                     │
│  │ Plex        │  ─────────────────────────────────────────     │
│  │ Downloads   │                                                │
│  │ Startup     │  Select subtitle languages to download:        │
│  │ About       │                                                │
│  └─────────────┘  [x] English (en)                              │
│                   [ ] Spanish (es)                              │
│                   [ ] French (fr)                               │
│                   [ ] German (de)                               │
│                   [ ] Portuguese (pt)                           │
│                   [+ Add Language...]                           │
│                                                                 │
│                   BEHAVIOR                                      │
│                   ─────────────────────────────────────────     │
│                                                                 │
│                   [ ] Ignore embedded subtitles                 │
│                   [ ] Overwrite existing subtitle files         │
│                   [ ] Ignore Plex "Extras" folders              │
│                                                                 │
│                   Concurrent downloads: [25 ▾]                  │
│                                                                 │
├─────────────────────────────────────────────────────────────────┤
│                                        [Cancel]  [Save]         │
└─────────────────────────────────────────────────────────────────┘
```

---

## Settings - Plex Tab

```
│                   PLEX CONNECTION                               │
│                   ─────────────────────────────────────────     │
│                                                                 │
│                   [x] Enable Plex Integration                   │
│                                                                 │
│                   Server URL:                                   │
│                   ┌─────────────────────────────────────────┐   │
│                   │ http://192.168.1.100:32400              │   │
│                   └─────────────────────────────────────────┘   │
│                                                                 │
│                   Token:                                        │
│                   ┌─────────────────────────────────────────┐   │
│                   │ ••••••••••••••••••••                    │   │
│                   └─────────────────────────────────────────┘   │
│                   [Paste from Plex URL] [Auto-detect]           │
│                                                                 │
│                   Status: ● Connected to "MovieServer"          │
│                                                  [Test]         │
│                                                                 │
│                   WEBHOOK SERVICE                               │
│                   ─────────────────────────────────────────     │
│                                                                 │
│                   [x] Enable webhook listener (port 9876)       │
│                   [x] Auto-start webhook service                │
│                                                                 │
│                   Add this URL to Plex webhooks:                │
│                   http://YOUR_IP:9876/plex/webhook    [Copy]    │
```

---

## Settings - Startup Tab

```
│                   STARTUP                                       │
│                   ─────────────────────────────────────────     │
│                                                                 │
│                   [x] Start Rustitles with Windows              │
│                   [x] Start minimized to system tray            │
│                   [x] Auto-start Plex webhook service           │
│                                                                 │
│                   NOTIFICATIONS                                 │
│                   ─────────────────────────────────────────     │
│                                                                 │
│                   [ ] Show notification for each download       │
│                   [x] Show notification for failures            │
│                   [x] Show notification when new Plex media     │
│                       is detected                               │
│                   [ ] Play sound on batch completion            │
│                                                                 │
│                   WINDOW BEHAVIOR                               │
│                   ─────────────────────────────────────────     │
│                                                                 │
│                   When closing window:                          │
│                   (•) Minimize to system tray                   │
│                   ( ) Exit application                          │
```

---

## Activity History (Full View)

```
┌─────────────────────────────────────────────────────────────────┐
│ Activity History                                           [×]  │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Filter: [All ▾]  [All Sources ▾]  [All Languages ▾]  🔍 [    ]│
│                                                                 │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ Today                                                     │  │
│  ├───────────────────────────────────────────────────────────┤  │
│  │ ✓  The Matrix (1999)           en   Movies    4:30 PM     │  │
│  │ ✓  Inception (2010)            en   Movies    4:28 PM     │  │
│  │ ✗  Dune (2021)                 en   Movies    4:25 PM     │  │
│  │ ⊘  Avatar (2009)               en   Movies    4:25 PM     │  │
│  ├───────────────────────────────────────────────────────────┤  │
│  │ Yesterday                                                 │  │
│  ├───────────────────────────────────────────────────────────┤  │
│  │ ✓  Breaking Bad S01E01         en   TV Shows  Yesterday   │  │
│  │ ✓  Breaking Bad S01E02         en   TV Shows  Yesterday   │  │
│  │ ...                                                       │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                 │
│  Showing 50 of 247 items                    [Load More]         │
│                                                                 │
├─────────────────────────────────────────────────────────────────┤
│  [Export CSV]  [Clear History]                     [Close]      │
└─────────────────────────────────────────────────────────────────┘
```

---

## Responsive Sizes

| Size | Width | Behavior |
|------|-------|----------|
| Compact | 400px | Progress + mini activity list |
| Normal | 600px | Full activity + sources |
| Wide | 800px+ | Activity + details side panel |
