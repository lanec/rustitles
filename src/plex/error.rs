//! Error types for Plex integration

use thiserror::Error;

#[derive(Debug, Error)]
pub enum PlexError {
    #[error("HTTP request failed: {0}")]
    HttpError(#[from] reqwest::Error),
    
    #[error("Failed to parse Plex response: {0}")]
    ParseError(String),
    
    #[error("Plex authentication failed: invalid or missing token")]
    AuthenticationFailed,
    
    #[error("Media item not found: {0}")]
    NotFound(String),
    
    #[error("Plex server unreachable: {0}")]
    ConnectionFailed(String),
    
    #[error("Invalid Plex server URL: {0}")]
    InvalidUrl(String),
    
    #[error("Missing required field: {0}")]
    MissingField(String),
}
