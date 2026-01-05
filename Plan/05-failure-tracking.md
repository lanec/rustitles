# Failure Tracking System

## Overview

Track subtitle download failures persistently so users can:
- See what failed and why
- Retry failed downloads
- Manually search for alternatives
- Understand patterns (e.g., specific providers failing)

---

## Database Schema

Using SQLite for simplicity and portability.

### Tables

```sql
-- Failed subtitle download attempts
CREATE TABLE IF NOT EXISTS failures (
    id TEXT PRIMARY KEY,
    rating_key TEXT NOT NULL,
    file_path TEXT NOT NULL,
    media_type TEXT NOT NULL,  -- 'movie' or 'episode'
    title TEXT NOT NULL,
    year INTEGER,
    season INTEGER,
    episode INTEGER,
    series_name TEXT,
    imdb_id TEXT,
    tmdb_id TEXT,
    language TEXT NOT NULL,
    error_message TEXT,
    attempt_count INTEGER DEFAULT 1,
    first_failed_at TEXT NOT NULL,
    last_failed_at TEXT NOT NULL,
    next_retry_at TEXT,
    resolved INTEGER DEFAULT 0,  -- 0 = unresolved, 1 = manually resolved, 2 = auto-resolved
    resolved_at TEXT,
    notes TEXT
);

-- Index for common queries
CREATE INDEX idx_failures_unresolved ON failures(resolved, last_failed_at);
CREATE INDEX idx_failures_rating_key ON failures(rating_key);
CREATE INDEX idx_failures_file_path ON failures(file_path);

-- Download history (successful and failed)
CREATE TABLE IF NOT EXISTS download_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    failure_id TEXT,
    rating_key TEXT NOT NULL,
    file_path TEXT NOT NULL,
    language TEXT NOT NULL,
    success INTEGER NOT NULL,
    subtitle_source TEXT,  -- e.g., 'opensubtitles', 'subscene'
    error_message TEXT,
    attempted_at TEXT NOT NULL,
    FOREIGN KEY (failure_id) REFERENCES failures(id)
);

-- Configuration table
CREATE TABLE IF NOT EXISTS config (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
```

---

## Rust Implementation

### Database Setup

```rust
// src/plex/database.rs

use rusqlite::{Connection, params};
use std::path::PathBuf;

pub struct FailureDatabase {
    conn: Connection,
}

impl FailureDatabase {
    pub fn new(data_dir: &PathBuf) -> Result<Self, DatabaseError> {
        let db_path = data_dir.join("rustitles_plex.db");
        let conn = Connection::open(&db_path)?;
        
        // Run migrations
        conn.execute_batch(include_str!("schema.sql"))?;
        
        Ok(Self { conn })
    }
}
```

### Failure Tracker

```rust
// src/plex/failure_tracker.rs

use chrono::{DateTime, Utc, Duration};
use rusqlite::params;

pub struct FailureTracker {
    db: FailureDatabase,
    config: PlexServiceConfig,
}

impl FailureTracker {
    pub async fn new(config: &PlexServiceConfig) -> Result<Self, TrackerError> {
        let data_dir = get_data_directory();
        let db = FailureDatabase::new(&data_dir)?;
        
        Ok(Self {
            db,
            config: config.clone(),
        })
    }
    
    /// Record a failed subtitle download
    pub async fn record_failure(&self, job: &SubtitleJob) -> Result<(), TrackerError> {
        let now = Utc::now();
        let next_retry = self.calculate_next_retry(job.attempts);
        
        // Check if failure already exists for this file+language
        let existing = self.get_failure_by_file(&job.file_path, &job.language)?;
        
        if let Some(mut failure) = existing {
            // Update existing failure
            failure.attempt_count += 1;
            failure.last_failed_at = now;
            failure.next_retry_at = next_retry;
            failure.error_message = job.last_error.clone();
            self.update_failure(&failure)?;
        } else {
            // Create new failure record
            let failure = FailureRecord {
                id: job.id.clone(),
                rating_key: job.rating_key.clone(),
                file_path: job.file_path.clone(),
                media_type: job.media_type.to_string(),
                title: job.title.clone(),
                year: job.year,
                season: job.season,
                episode: job.episode,
                series_name: job.series_name.clone(),
                imdb_id: job.imdb_id.clone(),
                tmdb_id: job.tmdb_id.clone(),
                language: job.language.clone(),
                error_message: job.last_error.clone(),
                attempt_count: 1,
                first_failed_at: now,
                last_failed_at: now,
                next_retry_at,
                resolved: ResolvedStatus::Unresolved,
                resolved_at: None,
                notes: None,
            };
            self.insert_failure(&failure)?;
        }
        
        // Record in history
        self.record_history(job, false)?;
        
        Ok(())
    }
    
    /// Record a successful download (clears failure if exists)
    pub async fn record_success(&self, job: &SubtitleJob, source: &str) -> Result<(), TrackerError> {
        // Mark any existing failure as resolved
        if let Some(mut failure) = self.get_failure_by_file(&job.file_path, &job.language)? {
            failure.resolved = ResolvedStatus::AutoResolved;
            failure.resolved_at = Some(Utc::now());
            self.update_failure(&failure)?;
        }
        
        // Record in history
        self.record_history_success(job, source)?;
        
        Ok(())
    }
    
    /// Get all unresolved failures
    pub fn get_unresolved_failures(&self) -> Result<Vec<FailureRecord>, TrackerError> {
        let mut stmt = self.db.conn.prepare(
            "SELECT * FROM failures WHERE resolved = 0 ORDER BY last_failed_at DESC"
        )?;
        
        let rows = stmt.query_map([], |row| FailureRecord::from_row(row))?;
        Ok(rows.filter_map(|r| r.ok()).collect())
    }
    
    /// Get failures ready for retry
    pub fn get_retry_ready(&self) -> Result<Vec<FailureRecord>, TrackerError> {
        let now = Utc::now().to_rfc3339();
        
        let mut stmt = self.db.conn.prepare(
            "SELECT * FROM failures 
             WHERE resolved = 0 
             AND next_retry_at <= ?
             AND attempt_count < ?
             ORDER BY next_retry_at ASC"
        )?;
        
        let rows = stmt.query_map(
            params![now, self.config.max_retries],
            |row| FailureRecord::from_row(row)
        )?;
        
        Ok(rows.filter_map(|r| r.ok()).collect())
    }
    
    /// Mark failure as manually resolved
    pub fn mark_resolved(&self, failure_id: &str, notes: Option<&str>) -> Result<(), TrackerError> {
        self.db.conn.execute(
            "UPDATE failures SET resolved = 1, resolved_at = ?, notes = ? WHERE id = ?",
            params![Utc::now().to_rfc3339(), notes, failure_id]
        )?;
        Ok(())
    }
    
    /// Calculate exponential backoff for retry
    fn calculate_next_retry(&self, attempts: u32) -> Option<DateTime<Utc>> {
        if attempts >= self.config.max_retries {
            return None; // No more retries
        }
        
        // Exponential backoff: 5min, 15min, 45min, 2h, 6h...
        let delay_seconds = self.config.retry_delay_seconds * 3u64.pow(attempts);
        let max_delay = 6 * 60 * 60; // Cap at 6 hours
        let delay = std::cmp::min(delay_seconds, max_delay);
        
        Some(Utc::now() + Duration::seconds(delay as i64))
    }
    
    /// Get failure statistics
    pub fn get_stats(&self) -> Result<FailureStats, TrackerError> {
        let total: i64 = self.db.conn.query_row(
            "SELECT COUNT(*) FROM failures", [], |r| r.get(0)
        )?;
        
        let unresolved: i64 = self.db.conn.query_row(
            "SELECT COUNT(*) FROM failures WHERE resolved = 0", [], |r| r.get(0)
        )?;
        
        let pending_retry: i64 = self.db.conn.query_row(
            "SELECT COUNT(*) FROM failures WHERE resolved = 0 AND next_retry_at IS NOT NULL", 
            [], |r| r.get(0)
        )?;
        
        Ok(FailureStats {
            total_failures: total as u64,
            unresolved: unresolved as u64,
            pending_retry: pending_retry as u64,
        })
    }
}
```

### Data Structures

```rust
// src/plex/models.rs

#[derive(Debug, Clone)]
pub struct FailureRecord {
    pub id: String,
    pub rating_key: String,
    pub file_path: String,
    pub media_type: String,
    pub title: String,
    pub year: Option<i32>,
    pub season: Option<i32>,
    pub episode: Option<i32>,
    pub series_name: Option<String>,
    pub imdb_id: Option<String>,
    pub tmdb_id: Option<String>,
    pub language: String,
    pub error_message: Option<String>,
    pub attempt_count: u32,
    pub first_failed_at: DateTime<Utc>,
    pub last_failed_at: DateTime<Utc>,
    pub next_retry_at: Option<DateTime<Utc>>,
    pub resolved: ResolvedStatus,
    pub resolved_at: Option<DateTime<Utc>>,
    pub notes: Option<String>,
}

#[derive(Debug, Clone, PartialEq)]
pub enum ResolvedStatus {
    Unresolved = 0,
    ManuallyResolved = 1,
    AutoResolved = 2,
}

#[derive(Debug)]
pub struct FailureStats {
    pub total_failures: u64,
    pub unresolved: u64,
    pub pending_retry: u64,
}

impl FailureRecord {
    /// Display name for UI
    pub fn display_name(&self) -> String {
        match self.media_type.as_str() {
            "episode" => {
                let show = self.series_name.as_deref().unwrap_or("Unknown");
                let s = self.season.unwrap_or(0);
                let e = self.episode.unwrap_or(0);
                format!("{} S{:02}E{:02} - {}", show, s, e, self.title)
            }
            _ => {
                match self.year {
                    Some(y) => format!("{} ({})", self.title, y),
                    None => self.title.clone(),
                }
            }
        }
    }
    
    /// Time since last failure attempt
    pub fn time_since_last_attempt(&self) -> Duration {
        Utc::now() - self.last_failed_at
    }
    
    /// Whether this failure is eligible for retry
    pub fn can_retry(&self, max_retries: u32) -> bool {
        self.resolved == ResolvedStatus::Unresolved && 
        self.attempt_count < max_retries
    }
}
```

---

## Retry Scheduler

```rust
// src/plex/retry_scheduler.rs

pub struct RetryScheduler {
    failure_tracker: Arc<FailureTracker>,
    job_queue: Arc<JobQueue>,
    plex_client: Arc<PlexClient>,
}

impl RetryScheduler {
    pub async fn run(&self) {
        let mut interval = tokio::time::interval(tokio::time::Duration::from_secs(60));
        
        loop {
            interval.tick().await;
            
            if let Ok(ready) = self.failure_tracker.get_retry_ready() {
                for failure in ready {
                    log::info!("Retrying: {} (attempt {})", 
                        failure.display_name(), 
                        failure.attempt_count + 1
                    );
                    
                    // Fetch fresh metadata from Plex
                    if let Ok(item) = self.plex_client.get_metadata(&failure.rating_key).await {
                        let job = SubtitleJob::from_plex_item(&item, &failure.language);
                        self.job_queue.enqueue(job).await.ok();
                    }
                }
            }
        }
    }
}
```

---

## Database Location

```rust
fn get_database_path() -> PathBuf {
    #[cfg(windows)]
    {
        dirs::data_local_dir()
            .unwrap_or_else(|| PathBuf::from("."))
            .join("rustitles")
            .join("rustitles_plex.db")
    }
    
    #[cfg(target_os = "macos")]
    {
        dirs::data_dir()
            .unwrap_or_else(|| PathBuf::from("."))
            .join("rustitles")
            .join("rustitles_plex.db")
    }
    
    #[cfg(target_os = "linux")]
    {
        dirs::data_dir()
            .unwrap_or_else(|| PathBuf::from("."))
            .join("rustitles")
            .join("rustitles_plex.db")
    }
}
```

---

## Dependencies to Add

```toml
# Cargo.toml additions
rusqlite = { version = "0.31", features = ["bundled"] }
uuid = { version = "1.0", features = ["v4"] }
```

---

## Next Steps

→ **[06-failure-ui.md](06-failure-ui.md)** - User interface for managing failures
