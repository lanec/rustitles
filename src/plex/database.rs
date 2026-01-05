//! SQLite database for tracking subtitle download failures
//! 
//! This module provides persistent storage for failed subtitle downloads,
//! enabling retry scheduling and manual resolution tracking.

use rusqlite::{Connection, params, Result as SqlResult};
use std::path::PathBuf;
use chrono::{DateTime, Utc, Duration};

/// Database connection wrapper
pub struct FailureDatabase {
    conn: Connection,
}

/// Record of a failed subtitle download
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
    pub language: String,
    pub error_message: Option<String>,
    pub attempt_count: i32,
    pub first_failed_at: DateTime<Utc>,
    pub last_failed_at: DateTime<Utc>,
    pub next_retry_at: Option<DateTime<Utc>>,
    pub resolved: i32,  // 0=unresolved, 1=manual, 2=auto
    pub resolved_at: Option<DateTime<Utc>>,
    pub notes: Option<String>,
}

impl FailureRecord {
    /// Get display name for UI
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
    
    /// Check if eligible for retry
    pub fn can_retry(&self, max_retries: i32) -> bool {
        self.resolved == 0 && self.attempt_count < max_retries
    }
    
    /// Check if retry is due
    pub fn is_retry_due(&self) -> bool {
        match self.next_retry_at {
            Some(next) => Utc::now() >= next,
            None => false,
        }
    }
}

/// Statistics about failures
#[derive(Debug, Default)]
pub struct FailureStats {
    pub total: i64,
    pub unresolved: i64,
    pub pending_retry: i64,
    pub resolved_manual: i64,
    pub resolved_auto: i64,
}

impl FailureDatabase {
    /// Create or open the database
    pub fn new() -> SqlResult<Self> {
        let db_path = Self::get_database_path();
        
        // Ensure parent directory exists
        if let Some(parent) = db_path.parent() {
            std::fs::create_dir_all(parent).ok();
        }
        
        let conn = Connection::open(&db_path)?;
        let db = Self { conn };
        db.initialize_schema()?;
        
        log::info!("Failure database opened at: {}", db_path.display());
        Ok(db)
    }
    
    /// Get the database file path
    fn get_database_path() -> PathBuf {
        #[cfg(windows)]
        {
            dirs::data_local_dir()
                .unwrap_or_else(|| PathBuf::from("."))
                .join("rustitles")
                .join("failures.db")
        }
        
        #[cfg(target_os = "macos")]
        {
            dirs::data_dir()
                .unwrap_or_else(|| PathBuf::from("."))
                .join("rustitles")
                .join("failures.db")
        }
        
        #[cfg(target_os = "linux")]
        {
            dirs::data_dir()
                .unwrap_or_else(|| PathBuf::from("."))
                .join("rustitles")
                .join("failures.db")
        }
    }
    
    /// Initialize database schema
    fn initialize_schema(&self) -> SqlResult<()> {
        self.conn.execute_batch(
            "
            CREATE TABLE IF NOT EXISTS failures (
                id TEXT PRIMARY KEY,
                rating_key TEXT NOT NULL,
                file_path TEXT NOT NULL,
                media_type TEXT NOT NULL,
                title TEXT NOT NULL,
                year INTEGER,
                season INTEGER,
                episode INTEGER,
                series_name TEXT,
                imdb_id TEXT,
                language TEXT NOT NULL,
                error_message TEXT,
                attempt_count INTEGER DEFAULT 1,
                first_failed_at TEXT NOT NULL,
                last_failed_at TEXT NOT NULL,
                next_retry_at TEXT,
                resolved INTEGER DEFAULT 0,
                resolved_at TEXT,
                notes TEXT
            );
            
            CREATE INDEX IF NOT EXISTS idx_failures_unresolved 
                ON failures(resolved, last_failed_at);
            CREATE INDEX IF NOT EXISTS idx_failures_rating_key 
                ON failures(rating_key);
            CREATE INDEX IF NOT EXISTS idx_failures_retry 
                ON failures(resolved, next_retry_at);
            
            CREATE TABLE IF NOT EXISTS download_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                failure_id TEXT,
                rating_key TEXT NOT NULL,
                file_path TEXT NOT NULL,
                language TEXT NOT NULL,
                success INTEGER NOT NULL,
                provider TEXT,
                error_message TEXT,
                attempted_at TEXT NOT NULL
            );
            "
        )?;
        Ok(())
    }
    
    /// Record a failed download
    pub fn record_failure(
        &self,
        rating_key: &str,
        file_path: &str,
        media_type: &str,
        title: &str,
        year: Option<i32>,
        season: Option<i32>,
        episode: Option<i32>,
        series_name: Option<&str>,
        imdb_id: Option<&str>,
        language: &str,
        error_message: Option<&str>,
        retry_delay_seconds: u64,
        max_retries: i32,
    ) -> SqlResult<String> {
        let now = Utc::now();
        
        // Check if failure already exists for this file+language
        let existing = self.get_failure_by_file_and_language(file_path, language)?;
        
        if let Some(mut failure) = existing {
            // Update existing failure
            failure.attempt_count += 1;
            failure.last_failed_at = now;
            failure.error_message = error_message.map(|s| s.to_string());
            
            // Calculate next retry with exponential backoff
            if failure.attempt_count < max_retries {
                let delay = retry_delay_seconds * 3u64.pow(failure.attempt_count as u32 - 1);
                let max_delay = 6 * 60 * 60; // 6 hours max
                let actual_delay = std::cmp::min(delay, max_delay);
                failure.next_retry_at = Some(now + Duration::seconds(actual_delay as i64));
            } else {
                failure.next_retry_at = None;
            }
            
            self.update_failure(&failure)?;
            Ok(failure.id)
        } else {
            // Create new failure
            let id = uuid::Uuid::new_v4().to_string();
            let next_retry = now + Duration::seconds(retry_delay_seconds as i64);
            
            self.conn.execute(
                "INSERT INTO failures (
                    id, rating_key, file_path, media_type, title, year,
                    season, episode, series_name, imdb_id, language,
                    error_message, attempt_count, first_failed_at,
                    last_failed_at, next_retry_at, resolved
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?, ?, 0)",
                params![
                    id,
                    rating_key,
                    file_path,
                    media_type,
                    title,
                    year,
                    season,
                    episode,
                    series_name,
                    imdb_id,
                    language,
                    error_message,
                    now.to_rfc3339(),
                    now.to_rfc3339(),
                    next_retry.to_rfc3339(),
                ],
            )?;
            
            Ok(id)
        }
    }
    
    /// Update an existing failure record
    fn update_failure(&self, failure: &FailureRecord) -> SqlResult<()> {
        self.conn.execute(
            "UPDATE failures SET
                attempt_count = ?,
                last_failed_at = ?,
                next_retry_at = ?,
                error_message = ?,
                resolved = ?,
                resolved_at = ?,
                notes = ?
            WHERE id = ?",
            params![
                failure.attempt_count,
                failure.last_failed_at.to_rfc3339(),
                failure.next_retry_at.map(|t| t.to_rfc3339()),
                failure.error_message,
                failure.resolved,
                failure.resolved_at.map(|t| t.to_rfc3339()),
                failure.notes,
                failure.id,
            ],
        )?;
        Ok(())
    }
    
    /// Get failure by file path and language
    fn get_failure_by_file_and_language(
        &self,
        file_path: &str,
        language: &str,
    ) -> SqlResult<Option<FailureRecord>> {
        let mut stmt = self.conn.prepare(
            "SELECT * FROM failures WHERE file_path = ? AND language = ? AND resolved = 0"
        )?;
        
        let mut rows = stmt.query(params![file_path, language])?;
        
        if let Some(row) = rows.next()? {
            Ok(Some(self.row_to_failure(row)?))
        } else {
            Ok(None)
        }
    }
    
    /// Get all unresolved failures
    pub fn get_unresolved_failures(&self) -> SqlResult<Vec<FailureRecord>> {
        let mut stmt = self.conn.prepare(
            "SELECT * FROM failures WHERE resolved = 0 ORDER BY last_failed_at DESC"
        )?;
        
        let rows = stmt.query_map([], |row| self.row_to_failure(row))?;
        
        let mut failures = Vec::new();
        for row in rows {
            failures.push(row?);
        }
        
        Ok(failures)
    }
    
    /// Get failures ready for retry
    pub fn get_retry_ready(&self, max_retries: i32) -> SqlResult<Vec<FailureRecord>> {
        let now = Utc::now().to_rfc3339();
        
        let mut stmt = self.conn.prepare(
            "SELECT * FROM failures 
             WHERE resolved = 0 
               AND next_retry_at <= ?
               AND attempt_count < ?
             ORDER BY next_retry_at ASC"
        )?;
        
        let rows = stmt.query_map(params![now, max_retries], |row| self.row_to_failure(row))?;
        
        let mut failures = Vec::new();
        for row in rows {
            failures.push(row?);
        }
        
        Ok(failures)
    }
    
    /// Mark failure as resolved
    pub fn mark_resolved(&self, id: &str, manual: bool, notes: Option<&str>) -> SqlResult<()> {
        let resolved = if manual { 1 } else { 2 };
        let now = Utc::now().to_rfc3339();
        
        self.conn.execute(
            "UPDATE failures SET resolved = ?, resolved_at = ?, notes = ? WHERE id = ?",
            params![resolved, now, notes, id],
        )?;
        
        Ok(())
    }
    
    /// Get failure statistics
    pub fn get_stats(&self) -> SqlResult<FailureStats> {
        let mut stats = FailureStats::default();
        
        stats.total = self.conn.query_row(
            "SELECT COUNT(*) FROM failures",
            [],
            |row| row.get(0),
        )?;
        
        stats.unresolved = self.conn.query_row(
            "SELECT COUNT(*) FROM failures WHERE resolved = 0",
            [],
            |row| row.get(0),
        )?;
        
        let now = Utc::now().to_rfc3339();
        stats.pending_retry = self.conn.query_row(
            "SELECT COUNT(*) FROM failures WHERE resolved = 0 AND next_retry_at <= ?",
            params![now],
            |row| row.get(0),
        )?;
        
        stats.resolved_manual = self.conn.query_row(
            "SELECT COUNT(*) FROM failures WHERE resolved = 1",
            [],
            |row| row.get(0),
        )?;
        
        stats.resolved_auto = self.conn.query_row(
            "SELECT COUNT(*) FROM failures WHERE resolved = 2",
            [],
            |row| row.get(0),
        )?;
        
        Ok(stats)
    }
    
    /// Delete old resolved failures (cleanup)
    pub fn cleanup_old_resolved(&self, days_old: i64) -> SqlResult<usize> {
        let cutoff = (Utc::now() - Duration::days(days_old)).to_rfc3339();
        
        let deleted = self.conn.execute(
            "DELETE FROM failures WHERE resolved > 0 AND resolved_at < ?",
            params![cutoff],
        )?;
        
        Ok(deleted)
    }
    
    /// Convert a database row to FailureRecord
    fn row_to_failure(&self, row: &rusqlite::Row) -> SqlResult<FailureRecord> {
        Ok(FailureRecord {
            id: row.get("id")?,
            rating_key: row.get("rating_key")?,
            file_path: row.get("file_path")?,
            media_type: row.get("media_type")?,
            title: row.get("title")?,
            year: row.get("year")?,
            season: row.get("season")?,
            episode: row.get("episode")?,
            series_name: row.get("series_name")?,
            imdb_id: row.get("imdb_id")?,
            language: row.get("language")?,
            error_message: row.get("error_message")?,
            attempt_count: row.get("attempt_count")?,
            first_failed_at: parse_datetime(row.get::<_, String>("first_failed_at")?),
            last_failed_at: parse_datetime(row.get::<_, String>("last_failed_at")?),
            next_retry_at: row.get::<_, Option<String>>("next_retry_at")?
                .map(parse_datetime),
            resolved: row.get("resolved")?,
            resolved_at: row.get::<_, Option<String>>("resolved_at")?
                .map(parse_datetime),
            notes: row.get("notes")?,
        })
    }
}

/// Parse RFC3339 datetime string
fn parse_datetime(s: String) -> DateTime<Utc> {
    DateTime::parse_from_rfc3339(&s)
        .map(|dt| dt.with_timezone(&Utc))
        .unwrap_or_else(|_| Utc::now())
}

#[cfg(test)]
mod tests {
    use super::*;
    
    #[test]
    fn test_failure_display_name() {
        let movie = FailureRecord {
            id: "1".to_string(),
            rating_key: "123".to_string(),
            file_path: "/test/movie.mkv".to_string(),
            media_type: "movie".to_string(),
            title: "Test Movie".to_string(),
            year: Some(2024),
            season: None,
            episode: None,
            series_name: None,
            imdb_id: None,
            language: "en".to_string(),
            error_message: None,
            attempt_count: 1,
            first_failed_at: Utc::now(),
            last_failed_at: Utc::now(),
            next_retry_at: None,
            resolved: 0,
            resolved_at: None,
            notes: None,
        };
        
        assert_eq!(movie.display_name(), "Test Movie (2024)");
        
        let episode = FailureRecord {
            media_type: "episode".to_string(),
            title: "Pilot".to_string(),
            season: Some(1),
            episode: Some(1),
            series_name: Some("Test Show".to_string()),
            ..movie
        };
        
        assert_eq!(episode.display_name(), "Test Show S01E01 - Pilot");
    }
}
