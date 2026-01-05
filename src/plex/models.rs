//! Data models for Plex API responses

use serde::Deserialize;

/// Container wrapper for Plex API responses
#[derive(Debug, Deserialize)]
pub struct MediaContainer {
    #[serde(rename = "Metadata", default)]
    pub metadata: Vec<MediaItem>,
    
    #[serde(rename = "size", default)]
    pub size: i32,
}

/// Represents a media item (movie or episode) from Plex
#[derive(Debug, Clone, Deserialize)]
pub struct MediaItem {
    /// Unique identifier for this item in Plex
    #[serde(rename = "ratingKey")]
    pub rating_key: String,
    
    /// Item title (movie title or episode title)
    pub title: String,
    
    /// Type of media: "movie" or "episode"
    #[serde(rename = "type")]
    pub media_type: String,
    
    /// Release year
    pub year: Option<i32>,
    
    /// Episode number (for TV episodes)
    pub index: Option<i32>,
    
    /// Season number (for TV episodes)
    #[serde(rename = "parentIndex")]
    pub parent_index: Option<i32>,
    
    /// Season title (for TV episodes)
    #[serde(rename = "parentTitle")]
    pub parent_title: Option<String>,
    
    /// Show title (for TV episodes)
    #[serde(rename = "grandparentTitle")]
    pub grandparent_title: Option<String>,
    
    /// External GUIDs (IMDB, TMDB, TVDB)
    #[serde(rename = "Guid", default)]
    pub guids: Vec<GuidEntry>,
    
    /// Media files and streams
    #[serde(rename = "Media", default)]
    pub media: Vec<Media>,
    
    /// Library section ID
    #[serde(rename = "librarySectionID")]
    pub library_section_id: Option<i32>,
    
    /// Library section title
    #[serde(rename = "librarySectionTitle")]
    pub library_section_title: Option<String>,
}

/// External GUID reference (IMDB, TMDB, TVDB)
#[derive(Debug, Clone, Deserialize)]
pub struct GuidEntry {
    /// GUID in format "imdb://tt1234567" or "tmdb://12345"
    pub id: String,
}

/// Media container with file parts
#[derive(Debug, Clone, Deserialize)]
pub struct Media {
    /// Video resolution width
    pub width: Option<i32>,
    
    /// Video resolution height
    pub height: Option<i32>,
    
    /// Container format
    pub container: Option<String>,
    
    /// File parts (usually one per media item)
    #[serde(rename = "Part", default)]
    pub parts: Vec<Part>,
}

/// A file part containing the actual media file path and streams
#[derive(Debug, Clone, Deserialize)]
pub struct Part {
    /// Full file path as seen by Plex
    pub file: String,
    
    /// Duration in milliseconds
    pub duration: Option<i64>,
    
    /// File size in bytes
    pub size: Option<i64>,
    
    /// Streams (video, audio, subtitle)
    #[serde(rename = "Stream", default)]
    pub streams: Vec<Stream>,
}

/// A stream within a media file (video, audio, or subtitle)
#[derive(Debug, Clone, Deserialize)]
pub struct Stream {
    /// Stream type: 1 = video, 2 = audio, 3 = subtitle
    #[serde(rename = "streamType")]
    pub stream_type: i32,
    
    /// Language code
    pub language: Option<String>,
    
    /// Language code (3-letter)
    #[serde(rename = "languageCode")]
    pub language_code: Option<String>,
    
    /// Codec (e.g., "srt", "ass", "pgs")
    pub codec: Option<String>,
    
    /// Display title
    #[serde(rename = "displayTitle")]
    pub display_title: Option<String>,
    
    /// Whether this is an external subtitle file
    pub external: Option<bool>,
}

impl MediaItem {
    /// Extract IMDB ID from GUIDs (e.g., "tt1234567")
    pub fn imdb_id(&self) -> Option<String> {
        self.guids.iter()
            .find(|g| g.id.starts_with("imdb://"))
            .map(|g| g.id.strip_prefix("imdb://").unwrap().to_string())
    }
    
    /// Extract TMDB ID from GUIDs
    pub fn tmdb_id(&self) -> Option<String> {
        self.guids.iter()
            .find(|g| g.id.starts_with("tmdb://"))
            .map(|g| g.id.strip_prefix("tmdb://").unwrap().to_string())
    }
    
    /// Extract TVDB ID from GUIDs (for TV shows)
    pub fn tvdb_id(&self) -> Option<String> {
        self.guids.iter()
            .find(|g| g.id.starts_with("tvdb://"))
            .map(|g| g.id.strip_prefix("tvdb://").unwrap().to_string())
    }
    
    /// Get the primary file path for this media item
    pub fn file_path(&self) -> Option<&str> {
        self.media.first()
            .and_then(|m| m.parts.first())
            .map(|p| p.file.as_str())
    }
    
    /// Check if subtitles exist for a given language
    pub fn has_subtitle(&self, language: &str) -> bool {
        self.media.iter()
            .flat_map(|m| m.parts.iter())
            .flat_map(|p| p.streams.iter())
            .any(|s| {
                s.stream_type == 3 && // 3 = subtitle
                (s.language.as_ref().map_or(false, |l| l.eq_ignore_ascii_case(language)) ||
                 s.language_code.as_ref().map_or(false, |l| l.eq_ignore_ascii_case(language)))
            })
    }
    
    /// Get list of existing subtitle languages
    pub fn existing_subtitle_languages(&self) -> Vec<String> {
        self.media.iter()
            .flat_map(|m| m.parts.iter())
            .flat_map(|p| p.streams.iter())
            .filter(|s| s.stream_type == 3)
            .filter_map(|s| s.language.clone().or_else(|| s.language_code.clone()))
            .collect()
    }
    
    /// Get a human-readable display name
    pub fn display_name(&self) -> String {
        match self.media_type.as_str() {
            "episode" => {
                let show = self.grandparent_title.as_deref().unwrap_or("Unknown Show");
                let season = self.parent_index.unwrap_or(0);
                let episode = self.index.unwrap_or(0);
                format!("{} S{:02}E{:02} - {}", show, season, episode, self.title)
            }
            _ => {
                match self.year {
                    Some(y) => format!("{} ({})", self.title, y),
                    None => self.title.clone(),
                }
            }
        }
    }
    
    /// Check if this is a movie
    pub fn is_movie(&self) -> bool {
        self.media_type == "movie"
    }
    
    /// Check if this is a TV episode
    pub fn is_episode(&self) -> bool {
        self.media_type == "episode"
    }
}

/// Webhook payload from Plex
#[derive(Debug, Deserialize)]
pub struct PlexWebhookPayload {
    /// Event type (e.g., "library.new", "media.play")
    pub event: String,
    
    /// Whether this is from the owner account
    pub owner: Option<bool>,
    
    /// Account information
    #[serde(rename = "Account")]
    pub account: Option<WebhookAccount>,
    
    /// Server information
    #[serde(rename = "Server")]
    pub server: Option<WebhookServer>,
    
    /// Metadata for the affected item
    #[serde(rename = "Metadata")]
    pub metadata: Option<WebhookMetadata>,
}

#[derive(Debug, Deserialize)]
pub struct WebhookAccount {
    pub id: Option<i64>,
    pub title: Option<String>,
}

#[derive(Debug, Deserialize)]
pub struct WebhookServer {
    pub title: Option<String>,
    pub uuid: Option<String>,
}

#[derive(Debug, Deserialize)]
pub struct WebhookMetadata {
    #[serde(rename = "ratingKey")]
    pub rating_key: String,
    
    pub title: String,
    
    #[serde(rename = "type")]
    pub media_type: String,
    
    pub year: Option<i32>,
    
    #[serde(rename = "Guid", default)]
    pub guids: Vec<GuidEntry>,
}

#[cfg(test)]
mod tests {
    use super::*;
    
    #[test]
    fn test_imdb_extraction() {
        let item = MediaItem {
            rating_key: "123".to_string(),
            title: "The Matrix".to_string(),
            media_type: "movie".to_string(),
            year: Some(1999),
            index: None,
            parent_index: None,
            parent_title: None,
            grandparent_title: None,
            guids: vec![
                GuidEntry { id: "imdb://tt0133093".to_string() },
                GuidEntry { id: "tmdb://603".to_string() },
            ],
            media: vec![],
            library_section_id: None,
            library_section_title: None,
        };
        
        assert_eq!(item.imdb_id(), Some("tt0133093".to_string()));
        assert_eq!(item.tmdb_id(), Some("603".to_string()));
    }
    
    #[test]
    fn test_display_name() {
        let movie = MediaItem {
            rating_key: "123".to_string(),
            title: "The Matrix".to_string(),
            media_type: "movie".to_string(),
            year: Some(1999),
            index: None,
            parent_index: None,
            parent_title: None,
            grandparent_title: None,
            guids: vec![],
            media: vec![],
            library_section_id: None,
            library_section_title: None,
        };
        
        assert_eq!(movie.display_name(), "The Matrix (1999)");
        
        let episode = MediaItem {
            rating_key: "456".to_string(),
            title: "Pilot".to_string(),
            media_type: "episode".to_string(),
            year: Some(2008),
            index: Some(1),
            parent_index: Some(1),
            parent_title: Some("Season 1".to_string()),
            grandparent_title: Some("Breaking Bad".to_string()),
            guids: vec![],
            media: vec![],
            library_section_id: None,
            library_section_title: None,
        };
        
        assert_eq!(episode.display_name(), "Breaking Bad S01E01 - Pilot");
    }
}
