//! Plex API client for fetching media metadata

use reqwest::Client;
use crate::plex::{PlexError, PlexServiceConfig, MediaContainer, MediaItem};

/// Client for interacting with Plex Media Server API
pub struct PlexClient {
    base_url: String,
    token: String,
    http: Client,
}

impl PlexClient {
    /// Create a new Plex client
    pub fn new(base_url: &str, token: &str) -> Self {
        Self {
            base_url: base_url.trim_end_matches('/').to_string(),
            token: token.to_string(),
            http: Client::new(),
        }
    }
    
    /// Create a Plex client from configuration
    pub fn from_config(config: &PlexServiceConfig) -> Self {
        Self::new(&config.plex_url, &config.plex_token)
    }
    
    /// Test connection to Plex server
    pub async fn test_connection(&self) -> Result<String, PlexError> {
        let url = format!("{}/?X-Plex-Token={}", self.base_url, self.token);
        
        let response = self.http.get(&url)
            .header("Accept", "application/json")
            .send()
            .await
            .map_err(|e| PlexError::ConnectionFailed(e.to_string()))?;
        
        if response.status() == 401 {
            return Err(PlexError::AuthenticationFailed);
        }
        
        if !response.status().is_success() {
            return Err(PlexError::ConnectionFailed(
                format!("Server returned status: {}", response.status())
            ));
        }
        
        // Parse server name from response
        let json: serde_json::Value = response.json().await
            .map_err(|e| PlexError::ParseError(e.to_string()))?;
        
        let server_name = json["MediaContainer"]["friendlyName"]
            .as_str()
            .unwrap_or("Unknown Server")
            .to_string();
        
        Ok(server_name)
    }
    
    /// Get metadata for a specific item by rating key
    pub async fn get_metadata(&self, rating_key: &str) -> Result<MediaItem, PlexError> {
        let url = format!(
            "{}/library/metadata/{}?X-Plex-Token={}",
            self.base_url, rating_key, self.token
        );
        
        let response = self.http.get(&url)
            .header("Accept", "application/json")
            .send()
            .await?;
        
        if response.status() == 401 {
            return Err(PlexError::AuthenticationFailed);
        }
        
        if response.status() == 404 {
            return Err(PlexError::NotFound(rating_key.to_string()));
        }
        
        let json: serde_json::Value = response.json().await
            .map_err(|e| PlexError::ParseError(e.to_string()))?;
        
        let container: MediaContainer = serde_json::from_value(json["MediaContainer"].clone())
            .map_err(|e| PlexError::ParseError(e.to_string()))?;
        
        container.metadata.into_iter().next()
            .ok_or_else(|| PlexError::NotFound(rating_key.to_string()))
    }
    
    /// Get recently added items from all libraries
    pub async fn get_recently_added(&self, limit: u32) -> Result<Vec<MediaItem>, PlexError> {
        let url = format!(
            "{}/library/recentlyAdded?X-Plex-Token={}&X-Plex-Container-Start=0&X-Plex-Container-Size={}",
            self.base_url, self.token, limit
        );
        
        let response = self.http.get(&url)
            .header("Accept", "application/json")
            .send()
            .await?;
        
        if response.status() == 401 {
            return Err(PlexError::AuthenticationFailed);
        }
        
        let json: serde_json::Value = response.json().await
            .map_err(|e| PlexError::ParseError(e.to_string()))?;
        
        let container: MediaContainer = serde_json::from_value(json["MediaContainer"].clone())
            .map_err(|e| PlexError::ParseError(e.to_string()))?;
        
        Ok(container.metadata)
    }
    
    /// Get all library sections
    pub async fn get_libraries(&self) -> Result<Vec<LibrarySection>, PlexError> {
        let url = format!(
            "{}/library/sections?X-Plex-Token={}",
            self.base_url, self.token
        );
        
        let response = self.http.get(&url)
            .header("Accept", "application/json")
            .send()
            .await?;
        
        if response.status() == 401 {
            return Err(PlexError::AuthenticationFailed);
        }
        
        let json: serde_json::Value = response.json().await
            .map_err(|e| PlexError::ParseError(e.to_string()))?;
        
        let sections: Vec<LibrarySection> = json["MediaContainer"]["Directory"]
            .as_array()
            .map(|arr| {
                arr.iter()
                    .filter_map(|v| serde_json::from_value(v.clone()).ok())
                    .collect()
            })
            .unwrap_or_default();
        
        Ok(sections)
    }
    
    /// Get all items in a library section
    pub async fn get_library_items(&self, section_id: &str) -> Result<Vec<MediaItem>, PlexError> {
        let url = format!(
            "{}/library/sections/{}/all?X-Plex-Token={}",
            self.base_url, section_id, self.token
        );
        
        let response = self.http.get(&url)
            .header("Accept", "application/json")
            .send()
            .await?;
        
        if response.status() == 401 {
            return Err(PlexError::AuthenticationFailed);
        }
        
        let json: serde_json::Value = response.json().await
            .map_err(|e| PlexError::ParseError(e.to_string()))?;
        
        let container: MediaContainer = serde_json::from_value(json["MediaContainer"].clone())
            .map_err(|e| PlexError::ParseError(e.to_string()))?;
        
        Ok(container.metadata)
    }
    
    /// Refresh metadata for an item (triggers Plex to re-scan)
    pub async fn refresh_metadata(&self, rating_key: &str) -> Result<(), PlexError> {
        let url = format!(
            "{}/library/metadata/{}/refresh?X-Plex-Token={}",
            self.base_url, rating_key, self.token
        );
        
        let response = self.http.put(&url)
            .send()
            .await?;
        
        if response.status() == 401 {
            return Err(PlexError::AuthenticationFailed);
        }
        
        if !response.status().is_success() {
            return Err(PlexError::ConnectionFailed(
                format!("Refresh failed with status: {}", response.status())
            ));
        }
        
        Ok(())
    }
}

/// Represents a Plex library section
#[derive(Debug, Clone, serde::Deserialize)]
pub struct LibrarySection {
    /// Section key/ID
    pub key: String,
    
    /// Section title
    pub title: String,
    
    /// Section type (movie, show, music, etc.)
    #[serde(rename = "type")]
    pub section_type: String,
    
    /// Whether scanning is enabled
    pub scanning: Option<bool>,
}

impl LibrarySection {
    /// Check if this is a movie library
    pub fn is_movie_library(&self) -> bool {
        self.section_type == "movie"
    }
    
    /// Check if this is a TV show library
    pub fn is_show_library(&self) -> bool {
        self.section_type == "show"
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    
    #[test]
    fn test_client_creation() {
        let client = PlexClient::new("http://localhost:32400/", "abc123");
        assert_eq!(client.base_url, "http://localhost:32400");
        assert_eq!(client.token, "abc123");
    }
    
    #[test]
    fn test_url_trimming() {
        let client = PlexClient::new("http://localhost:32400///", "token");
        assert_eq!(client.base_url, "http://localhost:32400");
    }
}
