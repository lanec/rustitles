# Plex Metadata Extraction

## Overview

The webhook provides basic metadata, but we need to query the Plex API for:
- Full file path on disk
- Complete metadata (seasons, episodes, etc.)
- Existing subtitle streams (to avoid re-downloading)

---

## Plex API Authentication

### Getting Your Plex Token

**Method 1: From Plex Web**
1. Open Plex Web and sign in
2. Open browser Developer Tools (F12) → Network tab
3. Navigate to any library item
4. Look for requests containing `X-Plex-Token=` in URL

**Method 2: From Config File**
```
# Windows
%LOCALAPPDATA%\Plex Media Server\Preferences.xml

# Linux
/var/lib/plexmediaserver/Library/Application Support/Plex Media Server/Preferences.xml

# macOS
~/Library/Application Support/Plex Media Server/Preferences.xml
```
Look for `PlexOnlineToken` attribute.

---

## Key API Endpoints

### Base URL
```
http://<plex-server-ip>:32400
```

### Get Item Metadata
```
GET /library/metadata/{ratingKey}?X-Plex-Token={token}
```

### Get Media Parts (File Paths)
```
GET /library/metadata/{ratingKey}?X-Plex-Token={token}&includeChildren=1
```

### Get Recently Added
```
GET /library/recentlyAdded?X-Plex-Token={token}
```

### Get All Libraries
```
GET /library/sections?X-Plex-Token={token}
```

---

## Response Structure

### Movie Metadata Response
```xml
<MediaContainer>
  <Video ratingKey="12345" title="The Matrix" year="1999" type="movie">
    <Guid id="imdb://tt0133093"/>
    <Guid id="tmdb://603"/>
    <Media>
      <Part file="/movies/The Matrix (1999)/The Matrix.mkv" duration="8160000">
        <Stream streamType="3" language="English" codec="srt" key="/library/streams/54321"/>
      </Part>
    </Media>
  </Video>
</MediaContainer>
```

### TV Episode Metadata Response
```xml
<MediaContainer>
  <Video ratingKey="67890" title="Pilot" type="episode" 
         parentTitle="Breaking Bad" grandparentTitle="Breaking Bad"
         index="1" parentIndex="1">
    <Guid id="imdb://tt0959621"/>
    <Guid id="tmdb://62085"/>
    <Guid id="tvdb://349232"/>
    <Media>
      <Part file="/tv/Breaking Bad/Season 01/Breaking Bad - S01E01 - Pilot.mkv">
        <Stream streamType="3" language="Spanish" codec="srt"/>
      </Part>
    </Media>
  </Video>
</MediaContainer>
```

---

## Rust Implementation

### Plex Client Structure

```rust
// src/plex/client.rs

use reqwest::Client;
use serde::Deserialize;

pub struct PlexClient {
    base_url: String,
    token: String,
    http: Client,
}

impl PlexClient {
    pub fn new(base_url: &str, token: &str) -> Self {
        Self {
            base_url: base_url.trim_end_matches('/').to_string(),
            token: token.to_string(),
            http: Client::new(),
        }
    }
    
    pub async fn get_metadata(&self, rating_key: &str) -> Result<MediaItem, PlexError> {
        let url = format!(
            "{}/library/metadata/{}?X-Plex-Token={}",
            self.base_url, rating_key, self.token
        );
        
        let response = self.http.get(&url)
            .header("Accept", "application/json")
            .send()
            .await?;
        
        let container: MediaContainer = response.json().await?;
        container.metadata.into_iter().next()
            .ok_or(PlexError::NotFound)
    }
}
```

### Data Structures

```rust
// src/plex/models.rs

#[derive(Debug, Deserialize)]
pub struct MediaContainer {
    #[serde(rename = "Metadata")]
    pub metadata: Vec<MediaItem>,
}

#[derive(Debug, Deserialize)]
pub struct MediaItem {
    #[serde(rename = "ratingKey")]
    pub rating_key: String,
    
    pub title: String,
    
    #[serde(rename = "type")]
    pub media_type: String,  // "movie" or "episode"
    
    pub year: Option<i32>,
    
    // For episodes
    pub index: Option<i32>,           // Episode number
    #[serde(rename = "parentIndex")]
    pub parent_index: Option<i32>,    // Season number
    #[serde(rename = "parentTitle")]
    pub parent_title: Option<String>, // Season title
    #[serde(rename = "grandparentTitle")]
    pub grandparent_title: Option<String>, // Show title
    
    #[serde(rename = "Guid")]
    pub guids: Option<Vec<GuidEntry>>,
    
    #[serde(rename = "Media")]
    pub media: Vec<Media>,
}

#[derive(Debug, Deserialize)]
pub struct GuidEntry {
    pub id: String,
}

#[derive(Debug, Deserialize)]
pub struct Media {
    #[serde(rename = "Part")]
    pub parts: Vec<Part>,
}

#[derive(Debug, Deserialize)]
pub struct Part {
    pub file: String,
    
    #[serde(rename = "Stream")]
    pub streams: Option<Vec<Stream>>,
}

#[derive(Debug, Deserialize)]
pub struct Stream {
    #[serde(rename = "streamType")]
    pub stream_type: i32,  // 3 = subtitle
    
    pub language: Option<String>,
    pub codec: Option<String>,
}

impl MediaItem {
    /// Extract IMDB ID from GUIDs
    pub fn imdb_id(&self) -> Option<String> {
        self.guids.as_ref()?.iter()
            .find(|g| g.id.starts_with("imdb://"))
            .map(|g| g.id.strip_prefix("imdb://").unwrap().to_string())
    }
    
    /// Extract TMDB ID from GUIDs
    pub fn tmdb_id(&self) -> Option<String> {
        self.guids.as_ref()?.iter()
            .find(|g| g.id.starts_with("tmdb://"))
            .map(|g| g.id.strip_prefix("tmdb://").unwrap().to_string())
    }
    
    /// Extract TVDB ID from GUIDs (for TV shows)
    pub fn tvdb_id(&self) -> Option<String> {
        self.guids.as_ref()?.iter()
            .find(|g| g.id.starts_with("tvdb://"))
            .map(|g| g.id.strip_prefix("tvdb://").unwrap().to_string())
    }
    
    /// Get file path for the media
    pub fn file_path(&self) -> Option<&str> {
        self.media.first()?.parts.first().map(|p| p.file.as_str())
    }
    
    /// Check if subtitles already exist for a language
    pub fn has_subtitle(&self, language: &str) -> bool {
        self.media.iter()
            .flat_map(|m| m.parts.iter())
            .flat_map(|p| p.streams.iter().flatten())
            .any(|s| s.stream_type == 3 && 
                     s.language.as_ref().map_or(false, |l| l.eq_ignore_ascii_case(language)))
    }
    
    /// Get display name for logging/UI
    pub fn display_name(&self) -> String {
        match self.media_type.as_str() {
            "episode" => {
                let show = self.grandparent_title.as_deref().unwrap_or("Unknown");
                let season = self.parent_index.unwrap_or(0);
                let episode = self.index.unwrap_or(0);
                format!("{} S{:02}E{:02} - {}", show, season, episode, self.title)
            }
            _ => {
                let year = self.year.map_or(String::new(), |y| format!(" ({})", y));
                format!("{}{}", self.title, year)
            }
        }
    }
}
```

---

## Mapping to Subliminal

### Current Subliminal Command (Filename-based)
```bash
subliminal download -l en "/path/to/movie.mkv"
```

### Enhanced Command (With Metadata)
```bash
# For movies with IMDB ID
subliminal download -l en --movie "The Matrix" -y 1999 "/path/to/movie.mkv"

# For TV episodes
subliminal download -l en --series "Breaking Bad" -s 1 -e 1 "/path/to/episode.mkv"
```

### Rust Helper

```rust
// src/plex/subliminal.rs

pub fn build_subliminal_command(item: &MediaItem, language: &str) -> Vec<String> {
    let mut args = vec![
        "subliminal".to_string(),
        "download".to_string(),
        "-l".to_string(),
        language.to_string(),
    ];
    
    match item.media_type.as_str() {
        "episode" => {
            if let Some(show) = &item.grandparent_title {
                args.extend(["--series".to_string(), show.clone()]);
            }
            if let Some(season) = item.parent_index {
                args.extend(["-s".to_string(), season.to_string()]);
            }
            if let Some(episode) = item.index {
                args.extend(["-e".to_string(), episode.to_string()]);
            }
        }
        "movie" => {
            args.extend(["--movie".to_string(), item.title.clone()]);
            if let Some(year) = item.year {
                args.extend(["-y".to_string(), year.to_string()]);
            }
        }
        _ => {}
    }
    
    if let Some(path) = item.file_path() {
        args.push(path.to_string());
    }
    
    args
}
```

---

## Subtitle Detection Flow

```rust
pub async fn should_download_subtitle(
    plex: &PlexClient,
    rating_key: &str,
    target_languages: &[String],
) -> Result<Vec<String>, PlexError> {
    let item = plex.get_metadata(rating_key).await?;
    
    let missing: Vec<String> = target_languages.iter()
        .filter(|lang| !item.has_subtitle(lang))
        .cloned()
        .collect();
    
    Ok(missing)
}
```

---

## Path Translation (Docker/Remote Scenarios)

If Plex runs in Docker or on a different machine, file paths may differ:

```rust
pub struct PathMapper {
    mappings: Vec<(String, String)>, // (plex_path, local_path)
}

impl PathMapper {
    pub fn translate(&self, plex_path: &str) -> String {
        for (from, to) in &self.mappings {
            if plex_path.starts_with(from) {
                return plex_path.replacen(from, to, 1);
            }
        }
        plex_path.to_string()
    }
}

// Config example:
// plex_path: "/data/movies"
// local_path: "/mnt/media/movies"
```

---

## Next Steps

→ **[04-subtitle-service.md](04-subtitle-service.md)** - Background service architecture
