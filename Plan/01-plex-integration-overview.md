# Plex Integration Overview

## Problem Statement

Currently, Rustitles relies on **filename parsing** to identify video content, which often fails for files with non-standard naming conventions. Meanwhile, **Plex** excels at media recognition using:
- Online databases (TMDB, TVDB, etc.)
- Embedded metadata
- Fuzzy matching algorithms

**Goal**: Leverage Plex's superior media identification to improve subtitle download accuracy.

---

## Proposed Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                           PLEX SERVER                               │
│  ┌─────────────┐                                                    │
│  │ Library     │ ──► Scans & identifies media                       │
│  │ Scanner     │                                                    │
│  └─────────────┘                                                    │
│         │                                                           │
│         ▼                                                           │
│  ┌─────────────┐                                                    │
│  │  Webhooks   │ ──► Fires on: library.new, library.on.deck, etc.  │
│  └─────────────┘                                                    │
└─────────┬───────────────────────────────────────────────────────────┘
          │ HTTP POST (JSON payload)
          ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    RUSTITLES PLEX SERVICE                           │
│  ┌─────────────┐     ┌─────────────┐     ┌─────────────┐           │
│  │  Webhook    │ ──► │   Plex API  │ ──► │  Subliminal │           │
│  │  Listener   │     │   Client    │     │   Runner    │           │
│  └─────────────┘     └─────────────┘     └─────────────┘           │
│         │                   │                   │                   │
│         │                   ▼                   ▼                   │
│         │            ┌─────────────┐     ┌─────────────┐           │
│         │            │  Metadata   │     │  Download   │           │
│         │            │  Extractor  │     │   Queue     │           │
│         │            └─────────────┘     └─────────────┘           │
│         │                                       │                   │
│         ▼                                       ▼                   │
│  ┌─────────────────────────────────────────────────────┐           │
│  │              FAILURE TRACKER DATABASE               │           │
│  │  - Failed downloads with Plex metadata              │           │
│  │  - Retry history                                    │           │
│  │  - Manual search queue                              │           │
│  └─────────────────────────────────────────────────────┘           │
│         │                                                           │
│         ▼                                                           │
│  ┌─────────────────────────────────────────────────────┐           │
│  │                 FAILURE UI (Web/GUI)                │           │
│  │  - List of failed items with Plex-recognized names  │           │
│  │  - Manual search interface                          │           │
│  │  - Retry controls                                   │           │
│  └─────────────────────────────────────────────────────┘           │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Key Components

### 1. Webhook Listener
- HTTP server that receives Plex webhook events
- Filters for relevant events (`library.new`, `media.scrobble`)
- Extracts rating key for API lookups

### 2. Plex API Client
- Connects to Plex server using X-Plex-Token
- Fetches rich metadata: title, year, season/episode, IMDB/TMDB IDs
- Resolves file paths from Plex's perspective

### 3. Enhanced Subliminal Runner
- Uses Plex metadata instead of filename parsing
- Passes IMDB ID directly to Subliminal for precise matching
- Falls back to title/year search if ID unavailable

### 4. Failure Tracker
- SQLite database for persistent failure tracking
- Stores: file path, Plex metadata, error reason, retry count
- Timestamps for retry scheduling

### 5. Failure UI
- Web interface (or integrated into existing GUI)
- Browse failed items with Plex-recognized metadata
- Manual subtitle search with OpenSubtitles/Subscene
- Bulk retry functionality

---

## Integration Modes

### Mode A: Webhook-Driven (Recommended)
- Plex fires webhook → Service downloads subtitles automatically
- Real-time, event-driven
- Requires Plex Pass for webhooks

### Mode B: Polling
- Service periodically queries Plex for recently added items
- Works without Plex Pass
- Higher latency, more API calls

### Mode C: Hybrid
- Primary: Webhook-driven
- Fallback: Scheduled full-library scan for missed items

---

## Benefits Over Current Approach

| Current (Filename) | Plex-Integrated |
|--------------------|-----------------|
| Guesses from filename | Uses verified metadata |
| No external IDs | Has IMDB/TMDB IDs |
| Manual folder selection | Automatic on library update |
| No retry tracking | Persistent failure queue |
| Single-run | Continuous service |

---

## Next Steps

1. **[02-plex-webhooks.md](02-plex-webhooks.md)** - Set up webhook receiver
2. **[03-plex-metadata-extraction.md](03-plex-metadata-extraction.md)** - Extract metadata via Plex API
3. **[04-subtitle-service.md](04-subtitle-service.md)** - Design background service
4. **[05-failure-tracking.md](05-failure-tracking.md)** - Implement failure database
5. **[06-failure-ui.md](06-failure-ui.md)** - Build management interface
6. **[07-implementation-phases.md](07-implementation-phases.md)** - Development roadmap
