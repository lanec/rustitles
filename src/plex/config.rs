//! Configuration for Plex integration

use serde::{Deserialize, Serialize};

/// Configuration for the Plex integration service
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PlexServiceConfig {
    /// Plex server URL (e.g., "http://192.168.1.100:32400")
    pub plex_url: String,
    
    /// Plex authentication token (X-Plex-Token)
    pub plex_token: String,
    
    /// Whether Plex integration is enabled
    pub enabled: bool,
    
    /// Port for webhook listener
    pub webhook_port: u16,
    
    /// Languages to download subtitles for (ISO 639-1 codes)
    pub languages: Vec<String>,
    
    /// Maximum concurrent subtitle downloads
    pub max_concurrent_downloads: usize,
    
    /// Retry failed downloads after N seconds
    pub retry_delay_seconds: u64,
    
    /// Maximum retry attempts before giving up
    pub max_retries: u32,
    
    /// Path mappings for Docker/remote Plex scenarios
    /// Maps Plex paths to local paths
    pub path_mappings: Vec<PathMapping>,
    
    /// Skip items that already have subtitles in target language
    pub skip_existing: bool,
    
    /// Use polling mode instead of webhooks (for non-Plex Pass users)
    pub use_polling: bool,
    
    /// Polling interval in seconds (only used if use_polling is true)
    pub poll_interval_seconds: u64,
}

/// Maps a path as seen by Plex to a local filesystem path
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PathMapping {
    /// Path prefix as reported by Plex (e.g., "/data/movies")
    pub plex_path: String,
    /// Corresponding local path (e.g., "D:\\Media\\Movies")
    pub local_path: String,
}

impl Default for PlexServiceConfig {
    fn default() -> Self {
        Self {
            plex_url: "http://localhost:32400".to_string(),
            plex_token: String::new(),
            enabled: false,
            webhook_port: 9876,
            languages: vec!["en".to_string()],
            max_concurrent_downloads: 3,
            retry_delay_seconds: 300, // 5 minutes
            max_retries: 3,
            path_mappings: vec![],
            skip_existing: true,
            use_polling: false,
            poll_interval_seconds: 300, // 5 minutes
        }
    }
}

impl PlexServiceConfig {
    /// Validate the configuration
    pub fn validate(&self) -> Result<(), String> {
        if self.plex_url.is_empty() {
            return Err("Plex server URL is required".to_string());
        }
        
        if !self.plex_url.starts_with("http://") && !self.plex_url.starts_with("https://") {
            return Err("Plex server URL must start with http:// or https://".to_string());
        }
        
        if self.plex_token.is_empty() {
            return Err("Plex token is required".to_string());
        }
        
        if self.languages.is_empty() {
            return Err("At least one language must be specified".to_string());
        }
        
        if self.webhook_port == 0 {
            return Err("Webhook port must be non-zero".to_string());
        }
        
        Ok(())
    }
    
    /// Translate a Plex path to a local path using configured mappings
    pub fn translate_path(&self, plex_path: &str) -> String {
        for mapping in &self.path_mappings {
            if plex_path.starts_with(&mapping.plex_path) {
                return plex_path.replacen(&mapping.plex_path, &mapping.local_path, 1);
            }
        }
        plex_path.to_string()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    
    #[test]
    fn test_path_translation() {
        let config = PlexServiceConfig {
            path_mappings: vec![
                PathMapping {
                    plex_path: "/data/movies".to_string(),
                    local_path: "D:\\Media\\Movies".to_string(),
                },
            ],
            ..Default::default()
        };
        
        assert_eq!(
            config.translate_path("/data/movies/The Matrix/movie.mkv"),
            "D:\\Media\\Movies/The Matrix/movie.mkv"
        );
    }
    
    #[test]
    fn test_validation() {
        let mut config = PlexServiceConfig::default();
        assert!(config.validate().is_err()); // Missing token
        
        config.plex_token = "abc123".to_string();
        assert!(config.validate().is_ok());
        
        config.plex_url = "invalid-url".to_string();
        assert!(config.validate().is_err());
    }
}
