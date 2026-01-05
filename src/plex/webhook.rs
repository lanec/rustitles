//! Plex webhook server for receiving library events
//! 
//! This module handles incoming webhooks from Plex Media Server,
//! triggering subtitle downloads when new media is added.

use axum::{
    extract::{Multipart, State},
    response::IntoResponse,
    routing::post,
    Router,
};
use std::sync::Arc;
use tokio::sync::mpsc;

use crate::plex::{PlexClient, PlexServiceConfig};
use crate::plex::models::{PlexWebhookPayload, MediaItem};

/// Webhook event that triggers subtitle processing
#[derive(Debug, Clone)]
pub struct WebhookEvent {
    pub event_type: String,
    pub rating_key: String,
    pub title: String,
    pub media_type: String,
    pub year: Option<i32>,
}

/// Shared state for the webhook server
pub struct WebhookState {
    pub plex_client: PlexClient,
    pub config: PlexServiceConfig,
    pub event_sender: mpsc::Sender<WebhookEvent>,
}

/// Handle incoming Plex webhook
/// 
/// Plex sends webhooks as multipart/form-data with a JSON payload
pub async fn handle_webhook(
    State(state): State<Arc<WebhookState>>,
    mut multipart: Multipart,
) -> impl IntoResponse {
    log::info!("📨 Received webhook request");
    
    while let Ok(Some(field)) = multipart.next_field().await {
        let name = field.name().map(|s| s.to_string());
        log::debug!("Webhook field: {:?}", name);
        
        if name.as_deref() == Some("payload") {
            if let Ok(data) = field.text().await {
                log::info!("📋 Webhook payload received ({} bytes)", data.len());
                
                match serde_json::from_str::<PlexWebhookPayload>(&data) {
                    Ok(payload) => {
                        log::info!("🎬 Plex webhook event: {}", payload.event);
                        
                        // Process library.new and library.on.deck events
                        if payload.event == "library.new" || payload.event == "library.on.deck" {
                            if let Some(metadata) = payload.metadata {
                                let event = WebhookEvent {
                                    event_type: payload.event.clone(),
                                    rating_key: metadata.rating_key,
                                    title: metadata.title,
                                    media_type: metadata.media_type,
                                    year: metadata.year,
                                };
                                
                                log::info!(
                                    "✨ New media detected: {} ({}) - triggering subtitle download",
                                    event.title,
                                    event.media_type
                                );
                                
                                // Send event for processing
                                if let Err(e) = state.event_sender.send(event).await {
                                    log::error!("Failed to queue webhook event: {}", e);
                                }
                            } else {
                                log::warn!("Webhook {} event had no metadata", payload.event);
                            }
                        } else {
                            log::debug!("Ignoring event type: {} (only library.new triggers downloads)", payload.event);
                        }
                    }
                    Err(e) => {
                        log::warn!("Failed to parse webhook payload: {}", e);
                        log::debug!("Raw payload: {}", &data[..data.len().min(500)]);
                    }
                }
            }
        }
    }
    
    "OK"
}

/// Health check endpoint
pub async fn health_check() -> impl IntoResponse {
    "Rustitles Plex Webhook Server"
}

/// Create the webhook router
pub fn create_webhook_router(state: Arc<WebhookState>) -> Router {
    Router::new()
        .route("/plex/webhook", post(handle_webhook))
        .route("/health", axum::routing::get(health_check))
        .with_state(state)
}

/// Event processor that handles webhook events
pub struct WebhookEventProcessor {
    plex_client: PlexClient,
    config: PlexServiceConfig,
    event_receiver: mpsc::Receiver<WebhookEvent>,
}

impl WebhookEventProcessor {
    pub fn new(
        plex_client: PlexClient,
        config: PlexServiceConfig,
        event_receiver: mpsc::Receiver<WebhookEvent>,
    ) -> Self {
        Self {
            plex_client,
            config,
            event_receiver,
        }
    }
    
    /// Run the event processor loop
    pub async fn run(mut self) {
        log::info!("Webhook event processor started");
        
        while let Some(event) = self.event_receiver.recv().await {
            self.process_event(event).await;
        }
        
        log::info!("Webhook event processor stopped");
    }
    
    /// Process a single webhook event
    async fn process_event(&self, event: WebhookEvent) {
        log::info!("Processing event for: {} (key={})", event.title, event.rating_key);
        
        // Push discovered activity
        crate::plex::push_activity(crate::plex::PlexActivityUpdate {
            title: event.title.clone(),
            media_type: event.media_type.clone(),
            status: crate::plex::PlexActivityUpdateStatus::Discovered,
            language: self.config.languages.first().cloned().unwrap_or_else(|| "eng".to_string()),
            file_path: None,
        });
        
        // Fetch full metadata from Plex
        match self.plex_client.get_metadata(&event.rating_key).await {
            Ok(item) => {
                self.download_subtitles_for_item(&item).await;
            }
            Err(e) => {
                log::error!("Failed to fetch metadata for {}: {}", event.rating_key, e);
                // Push failure activity
                crate::plex::push_activity(crate::plex::PlexActivityUpdate {
                    title: event.title.clone(),
                    media_type: event.media_type.clone(),
                    status: crate::plex::PlexActivityUpdateStatus::Failed(format!("Metadata fetch failed: {}", e)),
                    language: self.config.languages.first().cloned().unwrap_or_else(|| "eng".to_string()),
                    file_path: None,
                });
            }
        }
    }
    
    /// Download subtitles for a media item
    async fn download_subtitles_for_item(&self, item: &MediaItem) {
        let file_path = item.file_path().map(|s| s.to_string());
        let display_name = item.display_name();
        
        // Check which languages are missing
        let missing = crate::plex::subliminal::get_missing_languages(
            item,
            &self.config.languages,
        );
        
        if missing.is_empty() {
            log::info!(
                "All requested subtitles already exist for: {}",
                display_name
            );
            // Push skipped activity
            crate::plex::push_activity(crate::plex::PlexActivityUpdate {
                title: display_name.clone(),
                media_type: item.media_type.clone(),
                status: crate::plex::PlexActivityUpdateStatus::Skipped("Subtitles already exist".to_string()),
                language: self.config.languages.first().cloned().unwrap_or_else(|| "eng".to_string()),
                file_path: file_path.clone(),
            });
            return;
        }
        
        log::info!(
            "Downloading subtitles for '{}' in languages: {:?}",
            display_name,
            missing
        );
        
        // Download subtitles for each missing language
        for language in &missing {
            // Push processing activity
            crate::plex::push_activity(crate::plex::PlexActivityUpdate {
                title: display_name.clone(),
                media_type: item.media_type.clone(),
                status: crate::plex::PlexActivityUpdateStatus::Processing,
                language: language.clone(),
                file_path: file_path.clone(),
            });
            
            let result = crate::plex::subliminal::download_subtitle_for_item(
                item,
                language,
                &self.config,
            );
            
            if result.success {
                log::info!(
                    "✓ Downloaded {} subtitles for: {}",
                    language,
                    display_name
                );
                // Push success activity
                crate::plex::push_activity(crate::plex::PlexActivityUpdate {
                    title: display_name.clone(),
                    media_type: item.media_type.clone(),
                    status: crate::plex::PlexActivityUpdateStatus::Success,
                    language: language.clone(),
                    file_path: file_path.clone(),
                });
            } else {
                let error_msg = result.error.unwrap_or_else(|| "Unknown error".to_string());
                log::warn!(
                    "✗ Failed to download {} subtitles for {}: {}",
                    language,
                    display_name,
                    error_msg
                );
                // Push failure activity
                crate::plex::push_activity(crate::plex::PlexActivityUpdate {
                    title: display_name.clone(),
                    media_type: item.media_type.clone(),
                    status: crate::plex::PlexActivityUpdateStatus::Failed(error_msg),
                    language: language.clone(),
                    file_path: file_path.clone(),
                });
            }
        }
    }
}

/// Start the webhook server
pub async fn start_webhook_server(
    config: PlexServiceConfig,
) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    let port = config.webhook_port;
    
    // Create event channel
    let (event_sender, event_receiver) = mpsc::channel::<WebhookEvent>(100);
    
    // Create Plex client
    let plex_client = PlexClient::from_config(&config);
    
    // Create shared state
    let state = Arc::new(WebhookState {
        plex_client: PlexClient::from_config(&config),
        config: config.clone(),
        event_sender,
    });
    
    // Start event processor
    let processor = WebhookEventProcessor::new(
        plex_client,
        config,
        event_receiver,
    );
    tokio::spawn(async move {
        processor.run().await;
    });
    
    // Create router
    let app = create_webhook_router(state);
    
    // Start server
    let addr = format!("0.0.0.0:{}", port);
    log::info!("Starting Plex webhook server on {}", addr);
    
    let listener = tokio::net::TcpListener::bind(&addr).await?;
    axum::serve(listener, app).await?;
    
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    
    #[test]
    fn test_webhook_event_creation() {
        let event = WebhookEvent {
            event_type: "library.new".to_string(),
            rating_key: "12345".to_string(),
            title: "Test Movie".to_string(),
            media_type: "movie".to_string(),
            year: Some(2024),
        };
        
        assert_eq!(event.event_type, "library.new");
        assert_eq!(event.rating_key, "12345");
    }
}
