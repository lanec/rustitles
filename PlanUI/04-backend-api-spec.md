# Rust Backend API Specification

## Overview

The Rust backend exposes a local HTTP API on `localhost:9877` for the .NET frontend to communicate with. This API handles all subtitle operations while the frontend handles UI and system tray.

## Base URL

```
http://localhost:9877/api/v1
```

## Authentication

No authentication required (localhost only). Backend binds to 127.0.0.1 to prevent external access.

---

## Endpoints

### Health & Status

#### GET /health
Check if backend is running.

**Response:**
```json
{
  "status": "ok",
  "version": "2.1.3",
  "uptime_seconds": 3600,
  "python_installed": true,
  "subliminal_installed": true
}
```

#### GET /status
Get current operation status.

**Response:**
```json
{
  "state": "downloading",  // "idle" | "scanning" | "downloading"
  "progress": {
    "current": 15,
    "total": 47,
    "percent": 31.9
  },
  "plex_service_running": true,
  "active_sources": [
    { "type": "plex", "name": "Movies", "item_count": 234 },
    { "type": "folder", "path": "D:\\Movies", "item_count": 47 }
  ]
}
```

---

### Activity Stream

#### GET /activity/stream
Server-Sent Events stream for real-time activity updates.

**Event Types:**
```
event: discovered
data: {"id": "abc123", "title": "The Matrix", "year": 1999, "type": "movie", "source": "plex", "language": "en"}

event: processing
data: {"id": "abc123", "title": "The Matrix", "progress": 50}

event: success
data: {"id": "abc123", "title": "The Matrix", "subtitle_path": "D:\\Movies\\The Matrix.en.srt"}

event: failed
data: {"id": "abc123", "title": "The Matrix", "error": "No subtitles found"}

event: skipped
data: {"id": "abc123", "title": "The Matrix", "reason": "Subtitle already exists"}

event: status_changed
data: {"state": "idle", "progress": null}
```

#### GET /activity/history
Get recent activity history.

**Query Parameters:**
- `limit` (int, default 50): Number of items
- `offset` (int, default 0): Pagination offset
- `status` (string, optional): Filter by status (success, failed, skipped)

**Response:**
```json
{
  "items": [
    {
      "id": "abc123",
      "title": "The Matrix",
      "year": 1999,
      "type": "movie",
      "source": "plex",
      "language": "en",
      "status": "success",
      "subtitle_path": "D:\\Movies\\The Matrix.en.srt",
      "timestamp": "2024-01-05T16:30:00Z"
    }
  ],
  "total": 247,
  "has_more": true
}
```

---

### Downloads

#### POST /downloads/scan/folder
Scan a folder for videos missing subtitles.

**Request:**
```json
{
  "path": "D:\\Movies",
  "recursive": true
}
```

**Response:**
```json
{
  "scan_id": "scan_xyz",
  "status": "started",
  "path": "D:\\Movies"
}
```

#### POST /downloads/scan/plex
Scan Plex library for missing subtitles.

**Request:**
```json
{
  "library_key": "1",  // Optional, null for all libraries
  "include_shows": true
}
```

**Response:**
```json
{
  "scan_id": "scan_abc",
  "status": "started"
}
```

#### POST /downloads/start
Start downloading subtitles for scanned items.

**Request:**
```json
{
  "scan_id": "scan_xyz",  // Optional, download all pending if null
  "item_ids": ["abc123", "def456"]  // Optional, specific items only
}
```

#### POST /downloads/pause
Pause active downloads.

#### POST /downloads/resume
Resume paused downloads.

#### POST /downloads/cancel
Cancel active downloads.

#### POST /downloads/retry
Retry failed downloads.

**Request:**
```json
{
  "item_ids": ["abc123", "def456"]  // Optional, retry all if null
}
```

---

### Configuration

#### GET /config
Get current configuration.

**Response:**
```json
{
  "languages": ["en", "es"],
  "concurrent_downloads": 25,
  "force_download": false,
  "overwrite_existing": false,
  "ignore_embedded": true,
  "ignore_extras_folders": true,
  "plex": {
    "enabled": true,
    "server_url": "http://192.168.1.100:32400",
    "has_token": true,
    "webhook_port": 9876
  },
  "startup": {
    "start_with_windows": true,
    "start_minimized": true,
    "auto_start_plex_service": true
  }
}
```

#### PUT /config
Update configuration.

**Request:**
```json
{
  "languages": ["en"],
  "concurrent_downloads": 10
}
```

#### POST /config/plex/test
Test Plex connection.

**Request:**
```json
{
  "server_url": "http://192.168.1.100:32400",
  "token": "xxxxx"
}
```

**Response:**
```json
{
  "success": true,
  "server_name": "MovieServer",
  "libraries": [
    { "key": "1", "title": "Movies", "type": "movie", "item_count": 234 },
    { "key": "2", "title": "TV Shows", "type": "show", "item_count": 56 }
  ]
}
```

---

### Plex Service

#### POST /plex/service/start
Start the Plex webhook listener.

#### POST /plex/service/stop
Stop the Plex webhook listener.

#### GET /plex/libraries
Get Plex library information.

**Response:**
```json
{
  "libraries": [
    {
      "key": "1",
      "title": "Movies",
      "type": "movie",
      "item_count": 234,
      "last_scanned": "2024-01-05T12:00:00Z"
    }
  ]
}
```

---

## Error Responses

All errors follow this format:

```json
{
  "error": {
    "code": "PLEX_CONNECTION_FAILED",
    "message": "Could not connect to Plex server",
    "details": "Connection refused"
  }
}
```

### Error Codes

| Code | HTTP Status | Description |
|------|-------------|-------------|
| INVALID_REQUEST | 400 | Malformed request |
| NOT_FOUND | 404 | Resource not found |
| PLEX_CONNECTION_FAILED | 502 | Cannot connect to Plex |
| PLEX_AUTH_FAILED | 401 | Invalid Plex token |
| SUBLIMINAL_NOT_INSTALLED | 500 | Subliminal not available |
| PYTHON_NOT_INSTALLED | 500 | Python not available |
| SCAN_IN_PROGRESS | 409 | Already scanning |
| DOWNLOAD_IN_PROGRESS | 409 | Already downloading |

---

## WebSocket Alternative

If real-time performance is critical, a WebSocket endpoint can be used instead of SSE:

```
ws://localhost:9877/ws
```

Messages follow the same format as SSE events but bidirectional.
