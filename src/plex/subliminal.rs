//! Enhanced Subliminal command builder using Plex metadata
//! 
//! This module builds Subliminal CLI commands using metadata from Plex
//! instead of relying on filename parsing, resulting in more accurate
//! subtitle matches.

use crate::plex::models::MediaItem;
use crate::plex::config::PlexServiceConfig;
use std::path::Path;
use std::process::Command;

/// Result of a subtitle download attempt
#[derive(Debug)]
pub struct SubtitleDownloadResult {
    pub success: bool,
    pub file_path: String,
    pub language: String,
    pub subtitle_path: Option<String>,
    pub error: Option<String>,
    pub provider: Option<String>,
}

/// Build Subliminal command arguments using Plex metadata
/// 
/// This produces much more accurate results than filename-based matching
/// because it uses the title, year, and series info that Plex has identified.
pub fn build_subliminal_args(item: &MediaItem, language: &str, config: &PlexServiceConfig) -> Vec<String> {
    let mut args = vec![
        "download".to_string(),
        "-l".to_string(),
        language.to_string(),
    ];
    
    match item.media_type.as_str() {
        "episode" => {
            // For TV episodes, use series name, season, and episode number
            if let Some(ref show) = item.grandparent_title {
                args.push("--series".to_string());
                args.push(show.clone());
            }
            if let Some(season) = item.parent_index {
                args.push("-s".to_string());
                args.push(season.to_string());
            }
            if let Some(episode) = item.index {
                args.push("-e".to_string());
                args.push(episode.to_string());
            }
        }
        "movie" | _ => {
            // For movies, use title and year
            args.push("--movie".to_string());
            args.push(item.title.clone());
            if let Some(year) = item.year {
                args.push("-y".to_string());
                args.push(year.to_string());
            }
        }
    }
    
    // Add the file path (translated if needed)
    if let Some(plex_path) = item.file_path() {
        let local_path = config.translate_path(plex_path);
        args.push(local_path);
    }
    
    args
}

/// Build a complete Subliminal command for execution
pub fn build_subliminal_command(item: &MediaItem, language: &str, config: &PlexServiceConfig) -> Command {
    let args = build_subliminal_args(item, language, config);
    
    let mut cmd = Command::new("subliminal");
    cmd.args(&args);
    
    // Set environment for proper encoding
    cmd.env("PYTHONIOENCODING", "utf-8");
    
    cmd
}

/// Download subtitles for a Plex media item
/// 
/// Uses Plex metadata for accurate matching instead of filename parsing.
pub fn download_subtitle_for_item(
    item: &MediaItem,
    language: &str,
    config: &PlexServiceConfig,
) -> SubtitleDownloadResult {
    let file_path = item.file_path().unwrap_or("").to_string();
    let local_path = config.translate_path(&file_path);
    
    // Check if file exists
    if !Path::new(&local_path).exists() {
        return SubtitleDownloadResult {
            success: false,
            file_path: local_path,
            language: language.to_string(),
            subtitle_path: None,
            error: Some("File not found (path mapping may be incorrect)".to_string()),
            provider: None,
        };
    }
    
    let mut cmd = build_subliminal_command(item, language, config);
    
    log::debug!(
        "Executing subliminal for '{}' with args: {:?}",
        item.display_name(),
        build_subliminal_args(item, language, config)
    );
    
    match cmd.output() {
        Ok(output) => {
            let stdout = String::from_utf8_lossy(&output.stdout);
            let stderr = String::from_utf8_lossy(&output.stderr);
            
            if output.status.success() {
                // Check if subtitle was actually downloaded
                let subtitle_downloaded = stdout.contains("Downloaded") || 
                                          stdout.contains("subtitle") ||
                                          check_subtitle_exists(&local_path, language);
                
                if subtitle_downloaded {
                    let subtitle_path = find_subtitle_path(&local_path, language);
                    SubtitleDownloadResult {
                        success: true,
                        file_path: local_path,
                        language: language.to_string(),
                        subtitle_path,
                        error: None,
                        provider: extract_provider(&stdout),
                    }
                } else {
                    SubtitleDownloadResult {
                        success: false,
                        file_path: local_path,
                        language: language.to_string(),
                        subtitle_path: None,
                        error: Some("No subtitles found for this media".to_string()),
                        provider: None,
                    }
                }
            } else {
                SubtitleDownloadResult {
                    success: false,
                    file_path: local_path,
                    language: language.to_string(),
                    subtitle_path: None,
                    error: Some(format!("Subliminal error: {}", stderr.trim())),
                    provider: None,
                }
            }
        }
        Err(e) => SubtitleDownloadResult {
            success: false,
            file_path: local_path,
            language: language.to_string(),
            subtitle_path: None,
            error: Some(format!("Failed to execute subliminal: {}", e)),
            provider: None,
        },
    }
}

/// Check if a subtitle file exists for the given video and language
fn check_subtitle_exists(video_path: &str, language: &str) -> bool {
    let path = Path::new(video_path);
    let stem = path.file_stem().and_then(|s| s.to_str()).unwrap_or("");
    let parent = path.parent().unwrap_or(Path::new("."));
    
    // Check for common subtitle naming patterns
    let patterns = [
        format!("{}.{}.srt", stem, language),
        format!("{}.srt", stem),
        format!("{}.{}.ass", stem, language),
        format!("{}.{}.sub", stem, language),
    ];
    
    patterns.iter().any(|pattern| parent.join(pattern).exists())
}

/// Find the path to a downloaded subtitle file
fn find_subtitle_path(video_path: &str, language: &str) -> Option<String> {
    let path = Path::new(video_path);
    let stem = path.file_stem().and_then(|s| s.to_str()).unwrap_or("");
    let parent = path.parent().unwrap_or(Path::new("."));
    
    let patterns = [
        format!("{}.{}.srt", stem, language),
        format!("{}.srt", stem),
        format!("{}.{}.ass", stem, language),
        format!("{}.{}.sub", stem, language),
    ];
    
    for pattern in &patterns {
        let sub_path = parent.join(pattern);
        if sub_path.exists() {
            return Some(sub_path.to_string_lossy().to_string());
        }
    }
    
    None
}

/// Extract provider name from Subliminal output
fn extract_provider(output: &str) -> Option<String> {
    // Subliminal output includes provider info like "Downloaded 1 subtitle from opensubtitles"
    let providers = ["opensubtitles", "subscene", "podnapisi", "addic7ed", "tvsubtitles"];
    
    for provider in &providers {
        if output.to_lowercase().contains(provider) {
            return Some(provider.to_string());
        }
    }
    
    None
}

/// Download subtitles for multiple languages
pub fn download_subtitles_for_item(
    item: &MediaItem,
    languages: &[String],
    config: &PlexServiceConfig,
) -> Vec<SubtitleDownloadResult> {
    languages
        .iter()
        .map(|lang| download_subtitle_for_item(item, lang, config))
        .collect()
}

/// Check which languages are missing subtitles for an item
pub fn get_missing_languages(item: &MediaItem, target_languages: &[String]) -> Vec<String> {
    target_languages
        .iter()
        .filter(|lang| !item.has_subtitle(lang))
        .cloned()
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::plex::models::MediaItem;
    
    fn create_test_movie() -> MediaItem {
        MediaItem {
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
        }
    }
    
    fn create_test_episode() -> MediaItem {
        MediaItem {
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
        }
    }
    
    #[test]
    fn test_movie_args() {
        let movie = create_test_movie();
        let config = PlexServiceConfig::default();
        let args = build_subliminal_args(&movie, "en", &config);
        
        assert!(args.contains(&"download".to_string()));
        assert!(args.contains(&"-l".to_string()));
        assert!(args.contains(&"en".to_string()));
        assert!(args.contains(&"--movie".to_string()));
        assert!(args.contains(&"The Matrix".to_string()));
        assert!(args.contains(&"-y".to_string()));
        assert!(args.contains(&"1999".to_string()));
    }
    
    #[test]
    fn test_episode_args() {
        let episode = create_test_episode();
        let config = PlexServiceConfig::default();
        let args = build_subliminal_args(&episode, "en", &config);
        
        assert!(args.contains(&"--series".to_string()));
        assert!(args.contains(&"Breaking Bad".to_string()));
        assert!(args.contains(&"-s".to_string()));
        assert!(args.contains(&"1".to_string()));
        assert!(args.contains(&"-e".to_string()));
    }
}
