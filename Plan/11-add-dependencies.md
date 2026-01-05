# Phase 11: Add Missing Dependencies

## Overview
Add any missing crate dependencies required for the Plex integration to compile and run.

## Required Dependencies

### Add to Cargo.toml

```toml
[dependencies]
# ... existing dependencies ...

# Plex integration dependencies
rusqlite = { version = "0.31", features = ["bundled"] }
uuid = { version = "1.0", features = ["v4"] }
axum = "0.7"
tower-http = { version = "0.5", features = ["cors"] }
multer = "3.0"

# Additional required dependencies
chrono = { version = "0.4", features = ["serde"] }
open = "5"
urlencoding = "2.1"
```

### Dependency Purposes

| Crate | Purpose |
|-------|---------|
| `rusqlite` | SQLite database for failure tracking |
| `uuid` | Generate unique IDs for failure records |
| `axum` | Web framework for webhook server |
| `tower-http` | HTTP middleware (CORS support) |
| `multer` | Multipart form parsing (webhook payloads) |
| `chrono` | Date/time handling for retry scheduling |
| `open` | Open URLs in default browser |
| `urlencoding` | URL encode search queries |

## Steps to Apply

### 1. Edit Cargo.toml
Add the dependencies listed above to the `[dependencies]` section.

### 2. Update Imports
In `src/plex/database.rs`, ensure chrono is imported:
```rust
use chrono::{DateTime, Utc, Duration};
```

### 3. Update URL Encoding
In `src/plex/ui.rs`, replace the manual URL encoding with:
```rust
use urlencoding::encode;

// In render_failure_item:
let url = format!(
    "https://www.opensubtitles.org/en/search/sublanguageid-{}/moviename-{}",
    failure.language,
    encode(&failure.title)
);
```

### 4. Verify Open Crate Usage
```rust
use open;

// Opens URL in default browser
let _ = open::that(&url);
```

## Build Verification

After adding dependencies:
```bash
cargo build
```

Check for any missing or conflicting versions:
```bash
cargo tree | grep -E "(chrono|open|urlencoding)"
```

## Expected Outcome
- All dependencies resolve correctly
- No version conflicts
- Project compiles successfully
