//! Plex integration UI components for egui
//! 
//! This module provides UI rendering functions for:
//! - Plex configuration settings
//! - Failed subtitle download management
//! - Service status display

use eframe::egui;
use crate::plex::database::{FailureRecord, FailureStats, FailureDatabase};
use crate::plex::config::PlexServiceConfig;

/// State for the Plex failures tab
#[derive(Default)]
pub struct PlexFailuresState {
    pub failures: Vec<FailureRecord>,
    pub stats: FailureStats,
    pub selected_ids: std::collections::HashSet<String>,
    pub filter_media_type: Option<String>,
    pub filter_language: Option<String>,
    pub search_query: String,
    pub last_refresh: Option<std::time::Instant>,
    pub show_resolved: bool,
}

impl PlexFailuresState {
    /// Refresh failure data from database
    pub fn refresh(&mut self) {
        if let Ok(db) = FailureDatabase::new() {
            if let Ok(failures) = db.get_unresolved_failures() {
                self.failures = failures;
            }
            if let Ok(stats) = db.get_stats() {
                self.stats = stats;
            }
        }
        self.last_refresh = Some(std::time::Instant::now());
    }
    
    /// Get filtered failures based on current filter settings
    pub fn get_filtered_failures(&self) -> Vec<&FailureRecord> {
        self.failures.iter()
            .filter(|f| {
                // Media type filter
                if let Some(ref mt) = self.filter_media_type {
                    if &f.media_type != mt {
                        return false;
                    }
                }
                
                // Language filter
                if let Some(ref lang) = self.filter_language {
                    if &f.language != lang {
                        return false;
                    }
                }
                
                // Search query
                if !self.search_query.is_empty() {
                    let query = self.search_query.to_lowercase();
                    let matches = f.title.to_lowercase().contains(&query) ||
                        f.series_name.as_ref().map_or(false, |s| s.to_lowercase().contains(&query)) ||
                        f.file_path.to_lowercase().contains(&query);
                    if !matches {
                        return false;
                    }
                }
                
                true
            })
            .collect()
    }
}

/// Render the Plex settings section
pub fn render_plex_settings(ui: &mut egui::Ui, config: &mut PlexServiceConfig, testing: bool, status: &Option<Result<String, String>>) {
    ui.heading("Plex Integration");
    ui.add_space(10.0);
    
    // Enable toggle
    ui.horizontal(|ui| {
        ui.checkbox(&mut config.enabled, "Enable Plex Integration");
    });
    
    if !config.enabled {
        ui.label("Enable Plex integration to automatically download subtitles when new media is added.");
        return;
    }
    
    ui.add_space(10.0);
    ui.separator();
    ui.add_space(10.0);
    
    // Quick Setup from URL
    ui.collapsing("📋 Quick Setup - Paste Plex URL", |ui| {
        ui.add_space(5.0);
        ui.label("Paste any URL from your Plex Web interface:");
        
        // Use a static/persistent string for the paste field
        static PASTE_URL: std::sync::OnceLock<std::sync::Mutex<String>> = std::sync::OnceLock::new();
        let paste_url = PASTE_URL.get_or_init(|| std::sync::Mutex::new(String::new()));
        let mut url_text = paste_url.lock().unwrap();
        
        ui.horizontal(|ui| {
            ui.text_edit_singleline(&mut *url_text);
            if ui.button("Extract").clicked() && !url_text.is_empty() {
                if let Some(server_url) = extract_server_url(&url_text) {
                    config.plex_url = server_url;
                }
                // Check for X-Plex-Token in URL
                if let Some(token) = extract_token_from_url(&url_text) {
                    config.plex_token = token;
                }
            }
        });
        
        ui.label(
            egui::RichText::new("Example: http://localhost:32400/web/index.html#!/media/...")
                .small()
                .color(egui::Color32::GRAY)
        );
        ui.add_space(5.0);
    });
    
    ui.add_space(10.0);
    
    // Server URL
    ui.horizontal(|ui| {
        ui.label("Plex Server URL:");
        ui.text_edit_singleline(&mut config.plex_url);
    });
    ui.label(
        egui::RichText::new("Example: http://192.168.1.100:32400")
            .small()
            .color(egui::Color32::GRAY)
    );
    
    ui.add_space(5.0);
    
    // Token (password field)
    ui.horizontal(|ui| {
        ui.label("Plex Token:");
        ui.add(egui::TextEdit::singleline(&mut config.plex_token).password(true));
    });
    
    // Auto-detect token button
    static DETECT_STATUS: std::sync::OnceLock<std::sync::Mutex<Option<String>>> = std::sync::OnceLock::new();
    let detect_status = DETECT_STATUS.get_or_init(|| std::sync::Mutex::new(None));
    
    ui.horizontal(|ui| {
        if ui.button("🔍 Auto-detect Token").clicked() {
            match find_plex_token() {
                Some(token) => {
                    config.plex_token = token;
                    *detect_status.lock().unwrap() = Some("✓ Token found!".to_string());
                }
                None => {
                    *detect_status.lock().unwrap() = Some("✗ Token not found - try manual method".to_string());
                }
            }
        }
        
        if let Some(ref msg) = *detect_status.lock().unwrap() {
            let color = if msg.starts_with("✓") {
                egui::Color32::from_rgb(80, 250, 123)
            } else {
                egui::Color32::from_rgb(255, 85, 85)
            };
            ui.label(egui::RichText::new(msg).color(color));
        }
    });
    
    // Token help section
    ui.collapsing("❓ How to find your Plex Token manually", |ui| {
        ui.add_space(5.0);
        ui.label(egui::RichText::new("Option 1: From Plex Web URL").strong());
        ui.label("1. Open Plex Web and sign in");
        ui.label("2. Click on any media item");
        ui.label("3. Click '...' menu → 'Get Info' → 'View XML'");
        ui.label("4. Copy the URL and paste it above - it contains X-Plex-Token=");
        
        ui.add_space(10.0);
        ui.label(egui::RichText::new("Option 2: From Preferences File").strong());
        ui.label("The auto-detect button searches these locations:");
        
        #[cfg(windows)]
        {
            ui.label(egui::RichText::new("• %LOCALAPPDATA%\\Plex Media Server\\").small().monospace());
            ui.label(egui::RichText::new("• %APPDATA%\\Plex Media Server\\").small().monospace());
            ui.label(egui::RichText::new("• %PROGRAMDATA%\\Plex Media Server\\").small().monospace());
            ui.label(egui::RichText::new("• Registry: HKCU\\Software\\Plex, Inc.\\Plex Media Server").small().monospace());
        }
        
        #[cfg(target_os = "macos")]
        {
            ui.label(egui::RichText::new("• ~/Library/Application Support/Plex Media Server/").small().monospace());
        }
        
        #[cfg(target_os = "linux")]
        {
            ui.label(egui::RichText::new("• /var/lib/plexmediaserver/Library/Application Support/Plex Media Server/").small().monospace());
            ui.label(egui::RichText::new("• ~/.plex/").small().monospace());
        }
        
        ui.add_space(5.0);
        ui.label("Look for: PlexOnlineToken=\"YOUR_TOKEN_HERE\"");
        
        ui.add_space(10.0);
        if ui.button("📖 Official Plex Guide").clicked() {
            let _ = open::that("https://support.plex.tv/articles/204059436-finding-an-authentication-token-x-plex-token/");
        }
    });
    
    ui.add_space(10.0);
    
    // Test connection button and status
    ui.horizontal(|ui| {
        let button_text = if testing { "Testing..." } else { "Test Connection" };
        if ui.button(button_text).clicked() && !testing {
            // Trigger test - handled by caller
        }
        
        if let Some(result) = status {
            match result {
                Ok(server_name) => {
                    ui.label(
                        egui::RichText::new(format!("✓ Connected to: {}", server_name))
                            .color(egui::Color32::from_rgb(80, 250, 123))
                    );
                }
                Err(error) => {
                    ui.label(
                        egui::RichText::new(format!("✗ {}", error))
                            .color(egui::Color32::from_rgb(255, 85, 85))
                    );
                }
            }
        }
    });
    
    ui.add_space(10.0);
    ui.separator();
    ui.add_space(10.0);
    
    // Webhook settings
    ui.heading("Webhook Settings");
    ui.add_space(5.0);
    
    ui.horizontal(|ui| {
        ui.label("Webhook Port:");
        let mut port_str = config.webhook_port.to_string();
        if ui.text_edit_singleline(&mut port_str).changed() {
            if let Ok(port) = port_str.parse() {
                config.webhook_port = port;
            }
        }
    });
    
    let webhook_url = format!("http://<your-ip>:{}/plex/webhook", config.webhook_port);
    ui.label(
        egui::RichText::new(format!("Add this URL to Plex → Settings → Webhooks: {}", webhook_url))
            .small()
            .color(egui::Color32::GRAY)
    );
    
    ui.add_space(5.0);
    ui.checkbox(&mut config.use_polling, "Use polling instead of webhooks (no Plex Pass required)");
    
    if config.use_polling {
        ui.horizontal(|ui| {
            ui.label("Poll interval (seconds):");
            let mut interval_str = config.poll_interval_seconds.to_string();
            if ui.text_edit_singleline(&mut interval_str).changed() {
                if let Ok(interval) = interval_str.parse() {
                    config.poll_interval_seconds = interval;
                }
            }
        });
    }
    
    ui.add_space(10.0);
    ui.separator();
    ui.add_space(10.0);
    
    // Download settings
    ui.heading("Download Settings");
    ui.add_space(5.0);
    
    ui.horizontal(|ui| {
        ui.label("Max concurrent downloads:");
        let mut concurrent_str = config.max_concurrent_downloads.to_string();
        if ui.text_edit_singleline(&mut concurrent_str).changed() {
            if let Ok(concurrent) = concurrent_str.parse() {
                config.max_concurrent_downloads = concurrent;
            }
        }
    });
    
    ui.horizontal(|ui| {
        ui.label("Max retry attempts:");
        let mut retries_str = config.max_retries.to_string();
        if ui.text_edit_singleline(&mut retries_str).changed() {
            if let Ok(retries) = retries_str.parse() {
                config.max_retries = retries;
            }
        }
    });
    
    ui.checkbox(&mut config.skip_existing, "Skip items that already have subtitles");
}

/// Render the failures management tab
pub fn render_failures_tab(ui: &mut egui::Ui, state: &mut PlexFailuresState) {
    // Auto-refresh every 30 seconds
    let should_refresh = state.last_refresh
        .map_or(true, |t| t.elapsed() > std::time::Duration::from_secs(30));
    
    if should_refresh {
        state.refresh();
    }
    
    // Stats header
    ui.horizontal(|ui| {
        ui.label(
            egui::RichText::new(format!("Unresolved: {}", state.stats.unresolved))
                .color(egui::Color32::from_rgb(255, 184, 108))
        );
        ui.separator();
        ui.label(format!("Pending Retry: {}", state.stats.pending_retry));
        ui.separator();
        ui.label(format!("Total: {}", state.stats.total));
        
        ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
            if ui.button("🔄 Refresh").clicked() {
                state.refresh();
            }
        });
    });
    
    ui.add_space(10.0);
    
    // Filters
    ui.horizontal(|ui| {
        // Media type filter
        egui::ComboBox::from_label("Type")
            .selected_text(state.filter_media_type.as_deref().unwrap_or("All"))
            .show_ui(ui, |ui| {
                ui.selectable_value(&mut state.filter_media_type, None, "All");
                ui.selectable_value(&mut state.filter_media_type, Some("movie".to_string()), "Movies");
                ui.selectable_value(&mut state.filter_media_type, Some("episode".to_string()), "TV Episodes");
            });
        
        // Language filter
        egui::ComboBox::from_label("Language")
            .selected_text(state.filter_language.as_deref().unwrap_or("All"))
            .show_ui(ui, |ui| {
                ui.selectable_value(&mut state.filter_language, None, "All");
                ui.selectable_value(&mut state.filter_language, Some("en".to_string()), "English");
                ui.selectable_value(&mut state.filter_language, Some("es".to_string()), "Spanish");
                ui.selectable_value(&mut state.filter_language, Some("fr".to_string()), "French");
            });
        
        // Search
        ui.label("Search:");
        ui.text_edit_singleline(&mut state.search_query);
    });
    
    ui.add_space(10.0);
    
    // Failures list - collect indices to avoid borrow issues
    let filtered_indices: Vec<usize> = state.failures.iter().enumerate()
        .filter(|(_, f)| {
            // Media type filter
            if let Some(ref mt) = state.filter_media_type {
                if &f.media_type != mt {
                    return false;
                }
            }
            // Language filter
            if let Some(ref lang) = state.filter_language {
                if &f.language != lang {
                    return false;
                }
            }
            // Search query
            if !state.search_query.is_empty() {
                let query = state.search_query.to_lowercase();
                let matches = f.title.to_lowercase().contains(&query) ||
                    f.series_name.as_ref().map_or(false, |s| s.to_lowercase().contains(&query)) ||
                    f.file_path.to_lowercase().contains(&query);
                if !matches {
                    return false;
                }
            }
            true
        })
        .map(|(i, _)| i)
        .collect();
    
    if filtered_indices.is_empty() {
        ui.centered_and_justified(|ui| {
            if state.failures.is_empty() {
                ui.label("No failed downloads to display");
            } else {
                ui.label("No failures match the current filters");
            }
        });
    } else {
        egui::ScrollArea::vertical().show(ui, |ui| {
            for idx in filtered_indices {
                let failure = &state.failures[idx];
                render_failure_item(ui, failure, &mut state.selected_ids);
                ui.add_space(5.0);
            }
        });
    }
    
    ui.add_space(10.0);
    
    // Bulk actions
    if !state.selected_ids.is_empty() {
        ui.horizontal(|ui| {
            ui.label(format!("{} selected", state.selected_ids.len()));
            
            if ui.button("Retry Selected").clicked() {
                // TODO: Implement bulk retry
            }
            
            if ui.button("Resolve Selected").clicked() {
                if let Ok(db) = FailureDatabase::new() {
                    for id in &state.selected_ids {
                        let _ = db.mark_resolved(id, true, None);
                    }
                    state.selected_ids.clear();
                    state.refresh();
                }
            }
            
            if ui.button("Clear Selection").clicked() {
                state.selected_ids.clear();
            }
        });
    }
}

/// Render a single failure item
fn render_failure_item(
    ui: &mut egui::Ui,
    failure: &FailureRecord,
    selected_ids: &mut std::collections::HashSet<String>,
) {
    egui::Frame::none()
        .fill(egui::Color32::from_rgb(40, 42, 54))
        .rounding(5.0)
        .inner_margin(10.0)
        .show(ui, |ui| {
            ui.horizontal(|ui| {
                // Checkbox
                let mut selected = selected_ids.contains(&failure.id);
                if ui.checkbox(&mut selected, "").changed() {
                    if selected {
                        selected_ids.insert(failure.id.clone());
                    } else {
                        selected_ids.remove(&failure.id);
                    }
                }
                
                // Title
                ui.label(egui::RichText::new(failure.display_name()).strong());
                
                // Language badge
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    ui.label(
                        egui::RichText::new(failure.language.to_uppercase())
                            .small()
                            .background_color(egui::Color32::from_rgb(68, 71, 90))
                    );
                });
            });
            
            // Error message
            if let Some(ref error) = failure.error_message {
                ui.label(
                    egui::RichText::new(format!("Error: {}", error))
                        .small()
                        .color(egui::Color32::from_rgb(255, 85, 85))
                );
            }
            
            // Status info
            ui.horizontal(|ui| {
                ui.label(
                    egui::RichText::new(format!("Attempts: {}", failure.attempt_count))
                        .small()
                );
                ui.separator();
                
                let elapsed = chrono::Utc::now() - failure.last_failed_at;
                let time_ago = format_duration(elapsed);
                ui.label(egui::RichText::new(format!("Last: {}", time_ago)).small());
                
                if let Some(next) = failure.next_retry_at {
                    if next > chrono::Utc::now() {
                        let until = next - chrono::Utc::now();
                        ui.separator();
                        ui.label(
                            egui::RichText::new(format!("Retry in: {}", format_duration(until)))
                                .small()
                                .color(egui::Color32::from_rgb(139, 233, 253))
                        );
                    }
                }
            });
            
            // Action buttons
            ui.horizontal(|ui| {
                if ui.small_button("Retry Now").clicked() {
                    // TODO: Trigger immediate retry
                }
                
                if ui.small_button("Search Online").clicked() {
                    // Open search URLs in browser
                    let title = &failure.title;
                    let url = format!(
                        "https://www.opensubtitles.org/en/search/sublanguageid-{}/moviename-{}",
                        failure.language,
                        urlencoding_encode(title)
                    );
                    let _ = open::that(&url);
                }
                
                if ui.small_button("Mark Resolved").clicked() {
                    if let Ok(db) = FailureDatabase::new() {
                        let _ = db.mark_resolved(&failure.id, true, None);
                    }
                }
            });
        });
}

/// Format a duration for display
fn format_duration(duration: chrono::Duration) -> String {
    let seconds = duration.num_seconds().abs();
    
    if seconds < 60 {
        format!("{}s", seconds)
    } else if seconds < 3600 {
        format!("{}m", seconds / 60)
    } else if seconds < 86400 {
        format!("{}h", seconds / 3600)
    } else {
        format!("{}d", seconds / 86400)
    }
}

/// URL encode a string for search queries
fn urlencoding_encode(s: &str) -> String {
    urlencoding::encode(s).to_string()
}

/// Extract server URL (scheme + host + port) from a Plex URL
fn extract_server_url(url: &str) -> Option<String> {
    // Handle URLs like:
    // http://localhost:32400/web/index.html#!/media/...
    // https://192.168.1.100:32400/web/...
    // http://plex.local:32400/...
    
    let url = url.trim();
    
    // Find the scheme
    let scheme_end = url.find("://")?;
    let scheme = &url[..scheme_end];
    
    if scheme != "http" && scheme != "https" {
        return None;
    }
    
    // Find the host:port portion
    let after_scheme = &url[scheme_end + 3..];
    
    // Find the end of host:port (first / or end of string)
    let host_end = after_scheme.find('/').unwrap_or(after_scheme.len());
    let host_port = &after_scheme[..host_end];
    
    // Remove any query parameters or fragments from host (shouldn't be there, but just in case)
    let host_port = host_port.split('?').next().unwrap_or(host_port);
    let host_port = host_port.split('#').next().unwrap_or(host_port);
    
    if host_port.is_empty() {
        return None;
    }
    
    Some(format!("{}://{}", scheme, host_port))
}

/// Extract X-Plex-Token from a URL if present
fn extract_token_from_url(url: &str) -> Option<String> {
    // Look for X-Plex-Token= in the URL (case-insensitive)
    let lower_url = url.to_lowercase();
    
    // Try different token parameter formats
    for param in ["x-plex-token=", "plex_token=", "token="] {
        if let Some(start) = lower_url.find(param) {
            let value_start = start + param.len();
            let remaining = &url[value_start..];
            
            // Token ends at & or end of string
            let token_end = remaining.find('&')
                .or_else(|| remaining.find('#'))
                .or_else(|| remaining.find(' '))
                .unwrap_or(remaining.len());
            
            let token = remaining[..token_end].trim();
            if !token.is_empty() {
                return Some(token.to_string());
            }
        }
    }
    
    None
}

/// Find Plex token by searching common installation locations
fn find_plex_token() -> Option<String> {
    // Try to find token from Preferences.xml in various locations
    let mut paths_to_check = Vec::new();
    
    #[cfg(windows)]
    {
        // Windows paths
        if let Ok(local_app_data) = std::env::var("LOCALAPPDATA") {
            paths_to_check.push(std::path::PathBuf::from(&local_app_data).join("Plex Media Server").join("Preferences.xml"));
        }
        if let Ok(app_data) = std::env::var("APPDATA") {
            paths_to_check.push(std::path::PathBuf::from(&app_data).join("Plex Media Server").join("Preferences.xml"));
        }
        if let Ok(program_data) = std::env::var("PROGRAMDATA") {
            paths_to_check.push(std::path::PathBuf::from(&program_data).join("Plex Media Server").join("Preferences.xml"));
        }
        // Also check user profile
        if let Ok(user_profile) = std::env::var("USERPROFILE") {
            paths_to_check.push(std::path::PathBuf::from(&user_profile).join("AppData").join("Local").join("Plex Media Server").join("Preferences.xml"));
        }
        
        // Try to read from Windows Registry
        if let Some(token) = find_plex_token_from_registry() {
            return Some(token);
        }
    }
    
    #[cfg(target_os = "macos")]
    {
        if let Ok(home) = std::env::var("HOME") {
            paths_to_check.push(std::path::PathBuf::from(&home).join("Library/Application Support/Plex Media Server/Preferences.xml"));
        }
    }
    
    #[cfg(target_os = "linux")]
    {
        paths_to_check.push(std::path::PathBuf::from("/var/lib/plexmediaserver/Library/Application Support/Plex Media Server/Preferences.xml"));
        if let Ok(home) = std::env::var("HOME") {
            paths_to_check.push(std::path::PathBuf::from(&home).join(".plex/Preferences.xml"));
        }
    }
    
    // Try each path
    for path in paths_to_check {
        if let Some(token) = extract_token_from_preferences_file(&path) {
            return Some(token);
        }
    }
    
    None
}

/// Extract token from a Preferences.xml file
fn extract_token_from_preferences_file(path: &std::path::Path) -> Option<String> {
    let content = std::fs::read_to_string(path).ok()?;
    
    // Look for PlexOnlineToken="..." or PlexOnlineToken='...'
    for pattern in ["PlexOnlineToken=\"", "PlexOnlineToken='"] {
        if let Some(start) = content.find(pattern) {
            let value_start = start + pattern.len();
            let remaining = &content[value_start..];
            let end_char = if pattern.ends_with('"') { '"' } else { '\'' };
            if let Some(end) = remaining.find(end_char) {
                let token = &remaining[..end];
                if !token.is_empty() {
                    return Some(token.to_string());
                }
            }
        }
    }
    
    None
}

/// Try to find Plex token from Windows Registry
#[cfg(windows)]
fn find_plex_token_from_registry() -> Option<String> {
    use std::process::Command;
    
    // Query registry for Plex token
    let output = Command::new("reg")
        .args(["query", r"HKCU\Software\Plex, Inc.\Plex Media Server", "/v", "PlexOnlineToken"])
        .output()
        .ok()?;
    
    if output.status.success() {
        let stdout = String::from_utf8_lossy(&output.stdout);
        // Parse the registry output - format: "    PlexOnlineToken    REG_SZ    TOKEN_VALUE"
        for line in stdout.lines() {
            if line.contains("PlexOnlineToken") && line.contains("REG_SZ") {
                let parts: Vec<&str> = line.split_whitespace().collect();
                if let Some(token) = parts.last() {
                    if !token.is_empty() && *token != "REG_SZ" {
                        return Some(token.to_string());
                    }
                }
            }
        }
    }
    
    None
}

#[cfg(not(windows))]
fn find_plex_token_from_registry() -> Option<String> {
    None
}
