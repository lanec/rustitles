# Subtitle Service Design

## Overview

The subtitle service runs as a background daemon/service that:
1. Listens for Plex webhooks
2. Processes download queue
3. Manages concurrent downloads
4. Handles retries and failures

---

## Service Modes

### Mode 1: Integrated (GUI + Service)
- Existing Rustitles GUI runs the webhook server in background
- User can see real-time download activity
- Stops when GUI closes

### Mode 2: Standalone Daemon
- Headless service runs independently
- Starts on system boot
- GUI connects to daemon for status/management

### Mode 3: Hybrid
- Daemon handles webhooks and queue
- GUI connects for monitoring and manual operations
- Best of both worlds

**Recommendation**: Start with Mode 1, evolve to Mode 3.

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                      SUBTITLE SERVICE                           │
│                                                                 │
│  ┌───────────────┐     ┌───────────────┐     ┌──────────────┐  │
│  │   Webhook     │────►│  Event Queue  │────►│   Workers    │  │
│  │   Server      │     │  (Channel)    │     │   (Tokio)    │  │
│  └───────────────┘     └───────────────┘     └──────────────┘  │
│         │                                           │           │
│         │              ┌───────────────┐            │           │
│         │              │    Config     │            │           │
│         │              │  - Languages  │            │           │
│         │              │  - Plex Token │            │           │
│         │              │  - Max Workers│            │           │
│         │              └───────────────┘            │           │
│         │                                           │           │
│         ▼                                           ▼           │
│  ┌───────────────┐                         ┌──────────────┐    │
│  │  Plex Client  │                         │  Subliminal  │    │
│  │  (API Calls)  │                         │   Executor   │    │
│  └───────────────┘                         └──────────────┘    │
│                                                    │            │
│                                                    ▼            │
│                                            ┌──────────────┐    │
│                                            │   Result     │    │
│                                            │   Handler    │    │
│                                            └──────────────┘    │
│                                                    │            │
│                              ┌─────────────────────┼───────┐   │
│                              ▼                     ▼       │   │
│                       ┌──────────┐          ┌──────────┐   │   │
│                       │ Success  │          │ Failure  │   │   │
│                       │  Log     │          │ Database │   │   │
│                       └──────────┘          └──────────┘   │   │
└─────────────────────────────────────────────────────────────────┘
```

---

## Core Components

### 1. Service Configuration

```rust
// src/plex/config.rs

use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PlexServiceConfig {
    /// Plex server URL (e.g., "http://192.168.1.100:32400")
    pub plex_url: String,
    
    /// Plex authentication token
    pub plex_token: String,
    
    /// Port for webhook listener
    pub webhook_port: u16,
    
    /// Languages to download (ISO 639-1 codes)
    pub languages: Vec<String>,
    
    /// Maximum concurrent subtitle downloads
    pub max_concurrent_downloads: usize,
    
    /// Retry failed downloads after N seconds
    pub retry_delay_seconds: u64,
    
    /// Maximum retry attempts
    pub max_retries: u32,
    
    /// Path mappings (Plex path -> Local path)
    pub path_mappings: Vec<PathMapping>,
    
    /// Skip items that already have subtitles
    pub skip_existing: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PathMapping {
    pub plex_path: String,
    pub local_path: String,
}

impl Default for PlexServiceConfig {
    fn default() -> Self {
        Self {
            plex_url: "http://localhost:32400".to_string(),
            plex_token: String::new(),
            webhook_port: 9876,
            languages: vec!["en".to_string()],
            max_concurrent_downloads: 3,
            retry_delay_seconds: 300,
            max_retries: 3,
            path_mappings: vec![],
            skip_existing: true,
        }
    }
}
```

### 2. Download Job

```rust
// src/plex/job.rs

use chrono::{DateTime, Utc};

#[derive(Debug, Clone)]
pub struct SubtitleJob {
    pub id: String,
    pub rating_key: String,
    pub file_path: String,
    pub media_type: MediaType,
    pub title: String,
    pub year: Option<i32>,
    pub season: Option<i32>,
    pub episode: Option<i32>,
    pub series_name: Option<String>,
    pub imdb_id: Option<String>,
    pub tmdb_id: Option<String>,
    pub language: String,
    pub status: JobStatus,
    pub created_at: DateTime<Utc>,
    pub attempts: u32,
    pub last_error: Option<String>,
}

#[derive(Debug, Clone, PartialEq)]
pub enum MediaType {
    Movie,
    Episode,
}

#[derive(Debug, Clone, PartialEq)]
pub enum JobStatus {
    Pending,
    InProgress,
    Completed,
    Failed,
    Retrying,
}

impl SubtitleJob {
    pub fn from_plex_item(item: &MediaItem, language: &str) -> Self {
        Self {
            id: uuid::Uuid::new_v4().to_string(),
            rating_key: item.rating_key.clone(),
            file_path: item.file_path().unwrap_or_default().to_string(),
            media_type: if item.media_type == "episode" { 
                MediaType::Episode 
            } else { 
                MediaType::Movie 
            },
            title: item.title.clone(),
            year: item.year,
            season: item.parent_index,
            episode: item.index,
            series_name: item.grandparent_title.clone(),
            imdb_id: item.imdb_id(),
            tmdb_id: item.tmdb_id(),
            language: language.to_string(),
            status: JobStatus::Pending,
            created_at: Utc::now(),
            attempts: 0,
            last_error: None,
        }
    }
}
```

### 3. Job Queue

```rust
// src/plex/queue.rs

use tokio::sync::mpsc;
use std::collections::VecDeque;
use std::sync::Arc;
use tokio::sync::Mutex;

pub struct JobQueue {
    pending: Arc<Mutex<VecDeque<SubtitleJob>>>,
    sender: mpsc::Sender<SubtitleJob>,
    receiver: Arc<Mutex<mpsc::Receiver<SubtitleJob>>>,
}

impl JobQueue {
    pub fn new(buffer_size: usize) -> Self {
        let (sender, receiver) = mpsc::channel(buffer_size);
        Self {
            pending: Arc::new(Mutex::new(VecDeque::new())),
            sender,
            receiver: Arc::new(Mutex::new(receiver)),
        }
    }
    
    pub async fn enqueue(&self, job: SubtitleJob) -> Result<(), QueueError> {
        // Deduplicate: don't add if same file+language already queued
        let mut pending = self.pending.lock().await;
        if pending.iter().any(|j| j.file_path == job.file_path && j.language == job.language) {
            log::debug!("Job already queued: {}", job.title);
            return Ok(());
        }
        
        pending.push_back(job.clone());
        self.sender.send(job).await?;
        Ok(())
    }
    
    pub async fn next(&self) -> Option<SubtitleJob> {
        let mut receiver = self.receiver.lock().await;
        receiver.recv().await
    }
    
    pub async fn pending_count(&self) -> usize {
        self.pending.lock().await.len()
    }
}
```

### 4. Worker Pool

```rust
// src/plex/worker.rs

use tokio::sync::Semaphore;
use std::sync::Arc;

pub struct WorkerPool {
    semaphore: Arc<Semaphore>,
    queue: Arc<JobQueue>,
    config: PlexServiceConfig,
    failure_tracker: Arc<FailureTracker>,
}

impl WorkerPool {
    pub fn new(
        config: PlexServiceConfig,
        queue: Arc<JobQueue>,
        failure_tracker: Arc<FailureTracker>,
    ) -> Self {
        Self {
            semaphore: Arc::new(Semaphore::new(config.max_concurrent_downloads)),
            queue,
            config,
            failure_tracker,
        }
    }
    
    pub async fn run(&self) {
        loop {
            if let Some(mut job) = self.queue.next().await {
                let permit = self.semaphore.clone().acquire_owned().await.unwrap();
                let config = self.config.clone();
                let failure_tracker = self.failure_tracker.clone();
                
                tokio::spawn(async move {
                    let result = execute_subliminal(&job, &config).await;
                    
                    match result {
                        Ok(_) => {
                            log::info!("✓ Downloaded subtitles for: {}", job.title);
                            job.status = JobStatus::Completed;
                        }
                        Err(e) => {
                            log::warn!("✗ Failed to download subtitles for {}: {}", job.title, e);
                            job.status = JobStatus::Failed;
                            job.last_error = Some(e.to_string());
                            job.attempts += 1;
                            
                            failure_tracker.record_failure(&job).await;
                        }
                    }
                    
                    drop(permit); // Release semaphore
                });
            }
        }
    }
}
```

### 5. Subliminal Executor

```rust
// src/plex/executor.rs

use async_process::Command;

pub async fn execute_subliminal(
    job: &SubtitleJob,
    config: &PlexServiceConfig,
) -> Result<(), SubtitleError> {
    let mut cmd = Command::new("subliminal");
    cmd.arg("download")
       .arg("-l").arg(&job.language);
    
    // Add metadata-based arguments for better matching
    match job.media_type {
        MediaType::Movie => {
            cmd.arg("--movie").arg(&job.title);
            if let Some(year) = job.year {
                cmd.arg("-y").arg(year.to_string());
            }
        }
        MediaType::Episode => {
            if let Some(ref series) = job.series_name {
                cmd.arg("--series").arg(series);
            }
            if let Some(season) = job.season {
                cmd.arg("-s").arg(season.to_string());
            }
            if let Some(episode) = job.episode {
                cmd.arg("-e").arg(episode.to_string());
            }
        }
    }
    
    // Translate path if mappings configured
    let local_path = translate_path(&job.file_path, &config.path_mappings);
    cmd.arg(&local_path);
    
    log::debug!("Executing: subliminal {:?}", cmd.get_args().collect::<Vec<_>>());
    
    let output = cmd.output().await?;
    
    if output.status.success() {
        Ok(())
    } else {
        let stderr = String::from_utf8_lossy(&output.stderr);
        Err(SubtitleError::SubliminalFailed(stderr.to_string()))
    }
}

fn translate_path(plex_path: &str, mappings: &[PathMapping]) -> String {
    for mapping in mappings {
        if plex_path.starts_with(&mapping.plex_path) {
            return plex_path.replacen(&mapping.plex_path, &mapping.local_path, 1);
        }
    }
    plex_path.to_string()
}
```

---

## Service Lifecycle

### Startup
```rust
pub async fn start_service(config: PlexServiceConfig) -> Result<(), ServiceError> {
    // 1. Initialize failure tracker (SQLite)
    let failure_tracker = Arc::new(FailureTracker::new(&config).await?);
    
    // 2. Create job queue
    let queue = Arc::new(JobQueue::new(1000));
    
    // 3. Start worker pool
    let worker_pool = WorkerPool::new(config.clone(), queue.clone(), failure_tracker.clone());
    tokio::spawn(async move { worker_pool.run().await });
    
    // 4. Create Plex client
    let plex_client = Arc::new(PlexClient::new(&config.plex_url, &config.plex_token));
    
    // 5. Start webhook server
    let webhook_handler = WebhookHandler::new(plex_client, queue, config.clone());
    start_webhook_server(config.webhook_port, webhook_handler).await?;
    
    Ok(())
}
```

### Graceful Shutdown
```rust
pub async fn shutdown_service() {
    log::info!("Shutting down subtitle service...");
    
    // 1. Stop accepting webhooks
    // 2. Wait for in-progress downloads
    // 3. Persist queue state
    // 4. Close database connections
    
    log::info!("Service shutdown complete");
}
```

---

## Integration with Existing GUI

### Adding to SubtitleDownloader

```rust
// Modify src/app.rs

impl SubtitleDownloader {
    pub fn start_plex_service(&mut self) {
        let config = self.load_plex_config();
        
        self.plex_service_handle = Some(tokio::spawn(async move {
            if let Err(e) = start_service(config).await {
                log::error!("Plex service error: {}", e);
            }
        }));
        
        self.plex_service_running = true;
    }
    
    pub fn stop_plex_service(&mut self) {
        if let Some(handle) = self.plex_service_handle.take() {
            handle.abort();
        }
        self.plex_service_running = false;
    }
}
```

---

## Next Steps

→ **[05-failure-tracking.md](05-failure-tracking.md)** - Persistent failure database
