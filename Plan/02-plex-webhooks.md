# Plex Webhooks Integration

## Overview

Plex webhooks allow real-time notifications when events occur on your Plex server. When a new file is added and scanned, Plex can notify Rustitles to trigger subtitle downloads.

---

## Prerequisites

- **Plex Pass** subscription (webhooks require Plex Pass)
- Rustitles service must be network-accessible from Plex server
- Port forwarding or same-network deployment

---

## Webhook Configuration in Plex

### Step 1: Get Rustitles Service URL
```
http://<rustitles-host>:<port>/plex/webhook
Example: http://192.168.1.100:9876/plex/webhook
```

### Step 2: Add Webhook in Plex
1. Open Plex Web → Settings → Webhooks
2. Click "Add Webhook"
3. Enter the Rustitles webhook URL
4. Save

---

## Relevant Plex Events

| Event | Description | Use Case |
|-------|-------------|----------|
| `library.new` | New item added to library | **Primary trigger** - Download subtitles for new media |
| `library.on.deck` | Item added to On Deck | Alternative trigger |
| `media.play` | Playback started | Could trigger if subtitles missing |
| `media.scrobble` | Playback completed | Not needed |

**We primarily care about `library.new`** - this fires after Plex has scanned and identified the media.

---

## Webhook Payload Structure

Plex sends a `multipart/form-data` POST with a JSON payload:

```json
{
  "event": "library.new",
  "user": true,
  "owner": true,
  "Account": {
    "id": 12345,
    "title": "username"
  },
  "Server": {
    "title": "My Plex Server",
    "uuid": "abc123..."
  },
  "Metadata": {
    "librarySectionType": "movie",
    "ratingKey": "12345",
    "key": "/library/metadata/12345",
    "guid": "plex://movie/5d776b59ad5437001f79c6f8",
    "studio": "Warner Bros.",
    "type": "movie",
    "title": "The Matrix",
    "year": 1999,
    "Guid": [
      { "id": "imdb://tt0133093" },
      { "id": "tmdb://603" },
      { "id": "tvdb://264" }
    ]
  }
}
```

### Key Fields to Extract

| Field | Path | Purpose |
|-------|------|---------|
| Event Type | `event` | Filter for `library.new` |
| Rating Key | `Metadata.ratingKey` | Unique ID for API lookups |
| Type | `Metadata.type` | `movie` or `episode` |
| Title | `Metadata.title` | Display name |
| Year | `Metadata.year` | Release year |
| IMDB ID | `Metadata.Guid[].id` | Extract `imdb://ttXXXXXXX` |
| TMDB ID | `Metadata.Guid[].id` | Extract `tmdb://XXXXX` |

---

## Rust Implementation

### Dependencies to Add

```toml
# Cargo.toml additions
axum = "0.7"           # HTTP server for webhooks
tower-http = "0.5"     # Middleware
multer = "3.0"         # Multipart form parsing
```

### Webhook Handler Structure

```rust
// src/plex/webhook.rs

use axum::{
    extract::Multipart,
    response::IntoResponse,
    routing::post,
    Router,
};
use serde::Deserialize;

#[derive(Debug, Deserialize)]
pub struct PlexWebhookPayload {
    pub event: String,
    #[serde(rename = "Metadata")]
    pub metadata: Option<PlexMetadata>,
}

#[derive(Debug, Deserialize)]
pub struct PlexMetadata {
    #[serde(rename = "ratingKey")]
    pub rating_key: String,
    #[serde(rename = "type")]
    pub media_type: String,
    pub title: String,
    pub year: Option<i32>,
    #[serde(rename = "Guid")]
    pub guids: Option<Vec<PlexGuid>>,
}

#[derive(Debug, Deserialize)]
pub struct PlexGuid {
    pub id: String,
}

impl PlexMetadata {
    pub fn imdb_id(&self) -> Option<String> {
        self.guids.as_ref()?.iter()
            .find(|g| g.id.starts_with("imdb://"))
            .map(|g| g.id.replace("imdb://", ""))
    }
    
    pub fn tmdb_id(&self) -> Option<String> {
        self.guids.as_ref()?.iter()
            .find(|g| g.id.starts_with("tmdb://"))
            .map(|g| g.id.replace("tmdb://", ""))
    }
}

pub async fn handle_webhook(mut multipart: Multipart) -> impl IntoResponse {
    while let Some(field) = multipart.next_field().await.unwrap() {
        if field.name() == Some("payload") {
            let data = field.text().await.unwrap();
            let payload: PlexWebhookPayload = serde_json::from_str(&data).unwrap();
            
            if payload.event == "library.new" {
                if let Some(metadata) = payload.metadata {
                    // Queue subtitle download
                    tokio::spawn(async move {
                        process_new_media(metadata).await;
                    });
                }
            }
        }
    }
    "OK"
}

pub fn webhook_router() -> Router {
    Router::new()
        .route("/plex/webhook", post(handle_webhook))
}
```

### Starting the Webhook Server

```rust
// src/plex/server.rs

pub async fn start_webhook_server(port: u16) -> Result<(), Box<dyn std::error::Error>> {
    let app = webhook_router();
    
    let listener = tokio::net::TcpListener::bind(format!("0.0.0.0:{}", port)).await?;
    log::info!("Plex webhook server listening on port {}", port);
    
    axum::serve(listener, app).await?;
    Ok(())
}
```

---

## Security Considerations

### 1. Verify Request Origin
- Optionally validate requests come from Plex server IP
- Consider adding a shared secret query parameter

### 2. Rate Limiting
- Plex may send bursts of webhooks during library scans
- Implement debouncing or queue throttling

### 3. HTTPS (Production)
- Use TLS in production environments
- Consider reverse proxy (nginx, Caddy)

---

## Testing Webhooks

### Local Testing with ngrok
```bash
# Expose local server to internet
ngrok http 9876

# Use the ngrok URL in Plex webhook settings
# https://abc123.ngrok.io/plex/webhook
```

### Manual Trigger
```bash
# Simulate Plex webhook
curl -X POST http://localhost:9876/plex/webhook \
  -F 'payload={"event":"library.new","Metadata":{"ratingKey":"123","type":"movie","title":"Test Movie","year":2024}}'
```

---

## Alternative: Polling Mode (No Plex Pass)

If webhooks aren't available, poll the Plex API:

```rust
// Poll every 5 minutes for recently added items
pub async fn poll_recently_added(plex_client: &PlexClient) {
    let items = plex_client.get_recently_added(Duration::from_secs(300)).await;
    for item in items {
        if !has_subtitles(&item) {
            queue_subtitle_download(item).await;
        }
    }
}
```

---

## Next Steps

→ **[03-plex-metadata-extraction.md](03-plex-metadata-extraction.md)** - Extract full metadata via Plex API
