//! Plex integration module for Rustitles
//! 
//! This module provides integration with Plex Media Server to:
//! - Receive webhooks when new media is added
//! - Extract metadata via Plex API for better subtitle matching
//! - Track failed subtitle downloads
//! - Provide a management interface for failures

pub mod config;
pub mod models;
pub mod client;
pub mod error;
pub mod subliminal;
pub mod webhook;
pub mod database;
pub mod ui;

// Re-export commonly used items
pub use config::PlexServiceConfig;
pub use client::PlexClient;
pub use error::PlexError;
pub use models::*;
pub use subliminal::*;

use std::sync::Mutex;
use once_cell::sync::Lazy;

/// Activity update sent from webhook handler to UI
#[derive(Debug, Clone)]
pub struct PlexActivityUpdate {
    pub title: String,
    pub media_type: String,
    pub status: PlexActivityUpdateStatus,
    pub language: String,
    pub file_path: Option<String>,
}

#[derive(Debug, Clone)]
pub enum PlexActivityUpdateStatus {
    Discovered,
    Processing,
    Success,
    Failed(String),
    Skipped(String),
}

/// Global activity queue for passing updates from webhook thread to UI
static ACTIVITY_QUEUE: Lazy<Mutex<Vec<PlexActivityUpdate>>> = Lazy::new(|| Mutex::new(Vec::new()));

/// Push an activity update to the queue (called from webhook handler)
pub fn push_activity(update: PlexActivityUpdate) {
    if let Ok(mut queue) = ACTIVITY_QUEUE.lock() {
        queue.push(update);
        // Keep queue size bounded
        if queue.len() > 100 {
            queue.remove(0);
        }
    }
}

/// Drain all pending activity updates (called from UI thread)
pub fn drain_activity() -> Vec<PlexActivityUpdate> {
    if let Ok(mut queue) = ACTIVITY_QUEUE.lock() {
        std::mem::take(&mut *queue)
    } else {
        Vec::new()
    }
}
