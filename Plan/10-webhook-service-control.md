# Phase 10: Webhook Service Control

## Overview
Add controls to start/stop the Plex webhook server from the GUI, and implement background service management.

## Components

### 1. Service State Management
Add service state to `SubtitleDownloader`:
```rust
// In data_structures.rs
pub plex_webhook_handle: Option<tokio::task::JoinHandle<()>>,
pub plex_service_status: PlexServiceStatus,

#[derive(Default, Clone, PartialEq)]
pub enum PlexServiceStatus {
    #[default]
    Stopped,
    Starting,
    Running,
    Error(String),
}
```

### 2. Service Control Methods
Add to `src/app.rs`:
```rust
impl SubtitleDownloader {
    /// Start the Plex webhook service
    pub fn start_plex_service(&mut self) {
        if self.plex_service_running {
            return;
        }
        
        let config = self.plex_config.clone();
        
        // Spawn the webhook server in a background task
        std::thread::spawn(move || {
            let rt = tokio::runtime::Runtime::new().unwrap();
            rt.block_on(async {
                if let Err(e) = crate::plex::webhook::start_webhook_server(config).await {
                    log::error!("Webhook server error: {}", e);
                }
            });
        });
        
        self.plex_service_running = true;
    }
    
    /// Stop the Plex webhook service
    pub fn stop_plex_service(&mut self) {
        // Send shutdown signal (requires implementing graceful shutdown)
        self.plex_service_running = false;
    }
}
```

### 3. UI Controls
Add to the Plex settings section:
```rust
ui.horizontal(|ui| {
    let status_text = if self.plex_service_running {
        "● Running"
    } else {
        "○ Stopped"
    };
    
    ui.label(status_text);
    
    if self.plex_service_running {
        if ui.button("Stop Service").clicked() {
            self.stop_plex_service();
        }
    } else {
        if ui.button("Start Service").clicked() {
            self.start_plex_service();
        }
    }
});
```

### 4. Auto-Start Option
Add configuration for auto-starting the service:
```rust
// In PlexServiceConfig
pub auto_start: bool,

// In SubtitleDownloader::default()
if settings.plex.enabled && settings.plex.auto_start {
    // Start service automatically
}
```

### 5. Graceful Shutdown
Implement proper shutdown handling:
```rust
// In webhook.rs
use tokio::sync::oneshot;

pub async fn start_webhook_server_with_shutdown(
    config: PlexServiceConfig,
    shutdown_rx: oneshot::Receiver<()>,
) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    let listener = tokio::net::TcpListener::bind(&addr).await?;
    
    axum::serve(listener, app)
        .with_graceful_shutdown(async move {
            let _ = shutdown_rx.await;
        })
        .await?;
    
    Ok(())
}
```

## Polling Mode Alternative
For users without Plex Pass (no webhooks):
```rust
/// Poll Plex for recently added items
pub async fn poll_recently_added(
    client: &PlexClient,
    config: &PlexServiceConfig,
    last_check: &mut DateTime<Utc>,
) {
    match client.get_recently_added(50).await {
        Ok(items) => {
            for item in items {
                if item.added_at > *last_check {
                    // Process new item
                    process_new_media(&item, config).await;
                }
            }
            *last_check = Utc::now();
        }
        Err(e) => log::error!("Polling error: {}", e),
    }
}

/// Start polling loop
pub async fn start_polling_service(config: PlexServiceConfig) {
    let client = PlexClient::from_config(&config);
    let mut last_check = Utc::now();
    let interval = Duration::from_secs(config.poll_interval_seconds);
    
    loop {
        poll_recently_added(&client, &config, &mut last_check).await;
        tokio::time::sleep(interval).await;
    }
}
```

## Expected Outcome
- Start/Stop buttons control the webhook server
- Service status displayed in UI
- Optional auto-start on application launch
- Graceful shutdown when app closes
- Polling mode works as fallback
