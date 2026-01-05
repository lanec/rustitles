# Implementation Phases

## Overview

This document outlines the development roadmap for integrating Rustitles with Plex. The implementation is divided into phases, each building on the previous one.

---

## Phase 1: Foundation (Week 1-2)

### Goals
- Set up Plex API client
- Add configuration for Plex connection
- Test metadata retrieval

### Tasks

#### 1.1 Project Structure
```
src/
├── plex/
│   ├── mod.rs           # Module exports
│   ├── client.rs        # Plex API client
│   ├── models.rs        # Data structures
│   ├── config.rs        # Plex configuration
│   └── error.rs         # Error types
```

- [ ] Create `src/plex/` module directory
- [ ] Add module to `lib.rs` and `main.rs`
- [ ] Define Plex configuration struct
- [ ] Add configuration to settings file

#### 1.2 Plex Client
- [ ] Implement `PlexClient` struct
- [ ] Add authentication (X-Plex-Token)
- [ ] Implement `get_metadata(rating_key)` method
- [ ] Implement `get_recently_added()` method
- [ ] Add path translation for Docker/remote scenarios

#### 1.3 Dependencies
```toml
# Add to Cargo.toml
rusqlite = { version = "0.31", features = ["bundled"] }
uuid = { version = "1.0", features = ["v4"] }
axum = "0.7"
tower-http = "0.5"
multer = "3.0"
```

- [ ] Add new dependencies to Cargo.toml
- [ ] Ensure builds on all platforms

#### 1.4 Configuration UI
- [ ] Add "Plex" section to Settings tab
- [ ] Fields: Server URL, Token, Languages
- [ ] "Test Connection" button
- [ ] Save/load configuration

### Deliverable
Working Plex API client that can fetch metadata for any media item.

### Verification
```bash
# Manual test: fetch metadata for a known item
cargo run -- --test-plex --rating-key 12345
```

---

## Phase 2: Enhanced Subliminal (Week 2-3)

### Goals
- Modify subtitle download logic to use Plex metadata
- Pass IMDB/TMDB IDs and explicit title/year to Subliminal

### Tasks

#### 2.1 Subliminal Command Builder
- [ ] Create `build_subliminal_command(MediaItem, language)` function
- [ ] Add `--movie` / `--series` flags based on media type
- [ ] Add `-y` (year), `-s` (season), `-e` (episode) flags
- [ ] Test with movies and TV episodes

#### 2.2 Integration with Existing Flow
- [ ] Create `plex_download_subtitle()` function
- [ ] Accept `MediaItem` instead of file path
- [ ] Use Plex metadata for better matching
- [ ] Fallback to filename-based if Plex unavailable

#### 2.3 Testing
- [ ] Test with well-known movies (The Matrix, etc.)
- [ ] Test with TV episodes
- [ ] Test with foreign films
- [ ] Compare success rate vs filename-based

### Deliverable
Subtitle downloads using Plex metadata with improved accuracy.

---

## Phase 3: Webhook Server (Week 3-4)

### Goals
- Receive Plex webhook notifications
- Trigger automatic subtitle downloads on new media

### Tasks

#### 3.1 Webhook Server
- [ ] Create `src/plex/webhook.rs`
- [ ] Implement Axum HTTP server
- [ ] Parse multipart/form-data payload
- [ ] Extract `library.new` events
- [ ] Start server on configurable port

#### 3.2 Event Processing
- [ ] Filter for relevant events
- [ ] Extract rating key from webhook
- [ ] Fetch full metadata via Plex API
- [ ] Check if subtitles already exist
- [ ] Queue download if needed

#### 3.3 Service Management
- [ ] Start webhook server on app launch (optional)
- [ ] Add "Enable Plex Service" toggle in settings
- [ ] Show service status in GUI
- [ ] Graceful shutdown

### Deliverable
Automatic subtitle downloads when Plex adds new media.

### Verification
1. Add webhook URL to Plex settings
2. Add new media to Plex library
3. Verify Rustitles downloads subtitles automatically

---

## Phase 4: Failure Tracking (Week 4-5)

### Goals
- Persist failed downloads to SQLite database
- Track retry attempts and errors
- Implement retry scheduling

### Tasks

#### 4.1 Database Setup
- [ ] Create `src/plex/database.rs`
- [ ] Define schema (failures, download_history)
- [ ] Initialize database on first run
- [ ] Handle migrations

#### 4.2 Failure Tracker
- [ ] Create `src/plex/failure_tracker.rs`
- [ ] Implement `record_failure()` method
- [ ] Implement `record_success()` method
- [ ] Implement `get_unresolved_failures()` method
- [ ] Add retry calculation with exponential backoff

#### 4.3 Retry Scheduler
- [ ] Create background task for retry scheduling
- [ ] Query for retry-ready failures
- [ ] Re-queue jobs for retry
- [ ] Update attempt count

### Deliverable
Persistent failure tracking with automatic retry.

---

## Phase 5: Failure UI (Week 5-6)

### Goals
- Add "Plex Failures" tab to GUI
- Display failed downloads with details
- Enable manual retry and resolution

### Tasks

#### 5.1 UI Layout
- [ ] Add new tab to main GUI
- [ ] Display failure statistics
- [ ] List failed items with details
- [ ] Add filter controls (type, language, search)

#### 5.2 Actions
- [ ] Implement single-item retry
- [ ] Implement bulk retry
- [ ] Implement "Mark Resolved"
- [ ] Implement "Open Manual Search"

#### 5.3 Manual Search
- [ ] Build search URLs for OpenSubtitles, Subscene
- [ ] Open in default browser
- [ ] Provide instructions for manual placement

#### 5.4 Polish
- [ ] Auto-refresh failure list
- [ ] Show next retry time
- [ ] Desktop notifications for failures (optional)

### Deliverable
Fully functional failure management interface.

---

## Phase 6: Polling Mode (Week 6-7)

### Goals
- Support users without Plex Pass (no webhooks)
- Periodic scanning of recently added items

### Tasks

#### 6.1 Polling Implementation
- [ ] Create `poll_recently_added()` function
- [ ] Query Plex API for items added in last N minutes
- [ ] Deduplicate against already-processed items
- [ ] Queue new items for subtitle download

#### 6.2 Scheduling
- [ ] Configurable poll interval (default: 5 minutes)
- [ ] Toggle between webhook/polling modes
- [ ] Handle API rate limits

#### 6.3 Full Library Scan
- [ ] "Scan Full Library" button
- [ ] Progress indicator for large libraries
- [ ] Skip items with existing subtitles

### Deliverable
Alternative mode for users without Plex Pass.

---

## Phase 7: Web UI (Optional, Week 7-8)

### Goals
- Remote management via web browser
- Mobile-friendly interface

### Tasks

#### 7.1 Web Server
- [ ] Extend Axum server with web routes
- [ ] Serve static HTML/CSS/JS
- [ ] Implement API endpoints for failures

#### 7.2 Frontend
- [ ] Simple HTML template
- [ ] Failure list view
- [ ] Retry/resolve buttons
- [ ] Basic authentication

### Deliverable
Web interface for remote failure management.

---

## Testing Checklist

### Unit Tests
- [ ] Plex API client methods
- [ ] Webhook payload parsing
- [ ] Subliminal command building
- [ ] Failure tracker operations

### Integration Tests
- [ ] End-to-end webhook → download flow
- [ ] Failure recording and retry
- [ ] Path translation

### Manual Testing
- [ ] Fresh install experience
- [ ] Plex connection setup
- [ ] Webhook configuration in Plex
- [ ] Various media types (movies, TV, anime)
- [ ] Multiple languages
- [ ] Error scenarios (network issues, no subtitles found)

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| Plex Pass required for webhooks | Implement polling mode as fallback |
| Path differences (Docker) | Configurable path mappings |
| Subliminal API changes | Abstract subliminal calls, version pin |
| Large library scan performance | Batch processing, progress UI |
| Database corruption | Regular backups, integrity checks |

---

## Success Metrics

1. **Accuracy**: 80%+ subtitle match rate (vs ~60% filename-based)
2. **Automation**: Zero-touch subtitle downloads for 90% of new media
3. **Visibility**: All failures visible and actionable in UI
4. **Reliability**: Service runs continuously without crashes

---

## Timeline Summary

| Phase | Duration | Milestone |
|-------|----------|-----------|
| 1. Foundation | Week 1-2 | Plex API working |
| 2. Enhanced Subliminal | Week 2-3 | Metadata-based downloads |
| 3. Webhook Server | Week 3-4 | Automatic downloads |
| 4. Failure Tracking | Week 4-5 | Persistent failures |
| 5. Failure UI | Week 5-6 | Full UI complete |
| 6. Polling Mode | Week 6-7 | Non-webhook support |
| 7. Web UI | Week 7-8 | Remote management |

**Total estimated time: 6-8 weeks**

---

## Getting Started

1. Read through all plan documents in order
2. Set up a test Plex server (or use existing)
3. Get Plex token for development
4. Start with Phase 1 tasks
5. Create feature branch: `feature/plex-integration`
6. Commit incrementally with tests

---

## Resources

- [Plex API Documentation](https://github.com/Arcanemagus/plex-api/wiki)
- [Subliminal CLI Reference](https://subliminal.readthedocs.io/en/latest/user/cli.html)
- [egui Documentation](https://docs.rs/egui/latest/egui/)
- [Axum Documentation](https://docs.rs/axum/latest/axum/)
- [rusqlite Documentation](https://docs.rs/rusqlite/latest/rusqlite/)
