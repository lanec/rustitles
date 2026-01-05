# Failure Management UI

## Overview

Provide a user interface for:
- Viewing failed subtitle downloads
- Understanding why downloads failed
- Manually searching for subtitles
- Retrying failed downloads
- Marking items as resolved

---

## UI Approaches

### Option A: Integrated egui Tab
Add a new tab to the existing Rustitles GUI.

**Pros**: Single application, consistent styling
**Cons**: Must run GUI to manage failures

### Option B: Web Interface
Separate web UI accessible via browser.

**Pros**: Accessible remotely, mobile-friendly
**Cons**: Additional dependency (web framework)

### Option C: Hybrid
Web UI for remote access, with summary in egui.

**Recommendation**: Start with Option A, add Option B later for remote management.

---

## egui Integration Design

### New Tab: "Plex Failures"

```
┌─────────────────────────────────────────────────────────────────────────┐
│  [Download] [Languages] [Settings] [Plex Failures]                      │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌─ Stats ─────────────────────────────────────────────────────────┐   │
│  │  Unresolved: 12    Pending Retry: 3    Total: 47                │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌─ Filters ───────────────────────────────────────────────────────┐   │
│  │  [All ▼]  [Movies ▼]  [English ▼]  [🔍 Search...]  [Refresh]   │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌─ Failed Items ──────────────────────────────────────────────────┐   │
│  │ ┌─────────────────────────────────────────────────────────────┐ │   │
│  │ │ ☐ The Matrix (1999)                                 [EN]    │ │   │
│  │ │   Error: No subtitles found                                 │ │   │
│  │ │   Attempts: 3 | Last: 2h ago | [Retry] [Search] [Resolve]   │ │   │
│  │ └─────────────────────────────────────────────────────────────┘ │   │
│  │ ┌─────────────────────────────────────────────────────────────┐ │   │
│  │ │ ☐ Breaking Bad S01E01 - Pilot                       [EN]    │ │   │
│  │ │   Error: Provider timeout                                   │ │   │
│  │ │   Attempts: 1 | Next retry: 5m | [Retry] [Search] [Resolve] │ │   │
│  │ └─────────────────────────────────────────────────────────────┘ │   │
│  │ ┌─────────────────────────────────────────────────────────────┐ │   │
│  │ │ ☐ Some Foreign Film (2023)                          [EN]    │ │   │
│  │ │   Error: No matching video hash                             │ │   │
│  │ │   Attempts: 3 | Exhausted | [Retry] [Search] [Resolve]      │ │   │
│  │ └─────────────────────────────────────────────────────────────┘ │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌─ Bulk Actions ──────────────────────────────────────────────────┐   │
│  │  [Select All] [Retry Selected] [Resolve Selected]               │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Implementation

### App State Extension

```rust
// Add to src/app.rs

pub struct SubtitleDownloader {
    // ... existing fields ...
    
    // Plex integration
    plex_config: PlexServiceConfig,
    plex_service_running: bool,
    failure_tracker: Option<Arc<FailureTracker>>,
    
    // Failure UI state
    failure_list: Vec<FailureRecord>,
    failure_stats: FailureStats,
    selected_failures: HashSet<String>,
    failure_filter: FailureFilter,
    failure_search: String,
    last_failure_refresh: Option<Instant>,
}

#[derive(Default)]
pub struct FailureFilter {
    pub media_type: Option<String>,    // "movie", "episode", or None for all
    pub language: Option<String>,
    pub show_resolved: bool,
}
```

### Failure Tab Rendering

```rust
// src/gui.rs - Add new tab

fn render_failure_tab(&mut self, ui: &mut egui::Ui) {
    // Auto-refresh every 30 seconds
    if self.last_failure_refresh.map_or(true, |t| t.elapsed() > Duration::from_secs(30)) {
        self.refresh_failures();
    }
    
    // Stats header
    ui.horizontal(|ui| {
        ui.label(format!("Unresolved: {}", self.failure_stats.unresolved));
        ui.separator();
        ui.label(format!("Pending Retry: {}", self.failure_stats.pending_retry));
        ui.separator();
        ui.label(format!("Total: {}", self.failure_stats.total_failures));
    });
    
    ui.add_space(10.0);
    
    // Filters
    ui.horizontal(|ui| {
        egui::ComboBox::from_label("Type")
            .selected_text(self.failure_filter.media_type.as_deref().unwrap_or("All"))
            .show_ui(ui, |ui| {
                ui.selectable_value(&mut self.failure_filter.media_type, None, "All");
                ui.selectable_value(&mut self.failure_filter.media_type, Some("movie".to_string()), "Movies");
                ui.selectable_value(&mut self.failure_filter.media_type, Some("episode".to_string()), "TV Episodes");
            });
        
        ui.text_edit_singleline(&mut self.failure_search);
        
        if ui.button("🔄 Refresh").clicked() {
            self.refresh_failures();
        }
    });
    
    ui.add_space(10.0);
    
    // Failure list
    egui::ScrollArea::vertical().show(ui, |ui| {
        let filtered = self.get_filtered_failures();
        
        for failure in filtered {
            self.render_failure_item(ui, &failure);
            ui.add_space(5.0);
        }
        
        if filtered.is_empty() {
            ui.centered_and_justified(|ui| {
                ui.label("No failures to display");
            });
        }
    });
    
    ui.add_space(10.0);
    
    // Bulk actions
    ui.horizontal(|ui| {
        if ui.button("Select All").clicked() {
            self.selected_failures = self.failure_list.iter().map(|f| f.id.clone()).collect();
        }
        
        if ui.button("Retry Selected").clicked() {
            self.retry_selected_failures();
        }
        
        if ui.button("Resolve Selected").clicked() {
            self.resolve_selected_failures();
        }
    });
}

fn render_failure_item(&mut self, ui: &mut egui::Ui, failure: &FailureRecord) {
    egui::Frame::none()
        .fill(egui::Color32::from_rgb(40, 42, 54))
        .rounding(5.0)
        .inner_margin(10.0)
        .show(ui, |ui| {
            ui.horizontal(|ui| {
                // Checkbox
                let mut selected = self.selected_failures.contains(&failure.id);
                if ui.checkbox(&mut selected, "").changed() {
                    if selected {
                        self.selected_failures.insert(failure.id.clone());
                    } else {
                        self.selected_failures.remove(&failure.id);
                    }
                }
                
                // Title and language badge
                ui.label(egui::RichText::new(&failure.display_name()).strong());
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    ui.label(
                        egui::RichText::new(&failure.language.to_uppercase())
                            .background_color(egui::Color32::from_rgb(68, 71, 90))
                            .small()
                    );
                });
            });
            
            // Error message
            if let Some(ref error) = failure.error_message {
                ui.label(
                    egui::RichText::new(format!("Error: {}", error))
                        .color(egui::Color32::from_rgb(255, 85, 85))
                        .small()
                );
            }
            
            // Status line
            ui.horizontal(|ui| {
                ui.label(
                    egui::RichText::new(format!("Attempts: {}", failure.attempt_count))
                        .small()
                );
                ui.separator();
                
                let time_ago = format_duration(failure.time_since_last_attempt());
                ui.label(egui::RichText::new(format!("Last: {}", time_ago)).small());
                
                if let Some(next) = failure.next_retry_at {
                    if next > Utc::now() {
                        let until_retry = next - Utc::now();
                        ui.separator();
                        ui.label(
                            egui::RichText::new(format!("Next retry: {}", format_duration(until_retry)))
                                .small()
                                .color(egui::Color32::from_rgb(139, 233, 253))
                        );
                    }
                }
            });
            
            // Action buttons
            ui.horizontal(|ui| {
                if ui.small_button("Retry").clicked() {
                    self.retry_failure(&failure.id);
                }
                
                if ui.small_button("Search").clicked() {
                    self.open_manual_search(failure);
                }
                
                if ui.small_button("Resolve").clicked() {
                    self.mark_resolved(&failure.id);
                }
            });
        });
}
```

### Manual Search Dialog

```rust
fn open_manual_search(&mut self, failure: &FailureRecord) {
    // Build search URLs for popular subtitle sites
    let title = &failure.title;
    let year = failure.year.map_or(String::new(), |y| y.to_string());
    
    let searches = vec![
        ("OpenSubtitles", format!(
            "https://www.opensubtitles.org/en/search/sublanguageid-{}/moviename-{}",
            failure.language,
            urlencoding::encode(title)
        )),
        ("Subscene", format!(
            "https://subscene.com/subtitles/searchbytitle?query={}",
            urlencoding::encode(title)
        )),
        ("YIFY Subtitles", format!(
            "https://yifysubtitles.ch/search?q={}",
            urlencoding::encode(title)
        )),
    ];
    
    // Open search dialog with links
    self.manual_search_state = Some(ManualSearchState {
        failure: failure.clone(),
        search_links: searches,
    });
}

fn render_manual_search_dialog(&mut self, ctx: &egui::Context) {
    if let Some(ref state) = self.manual_search_state {
        egui::Window::new("Manual Subtitle Search")
            .collapsible(false)
            .resizable(false)
            .show(ctx, |ui| {
                ui.label(format!("Searching for: {}", state.failure.display_name()));
                ui.add_space(10.0);
                
                ui.label("Open in browser:");
                for (name, url) in &state.search_links {
                    if ui.hyperlink_to(name, url).clicked() {
                        // Opens in default browser
                    }
                }
                
                ui.add_space(10.0);
                
                ui.label("After downloading subtitle manually:");
                ui.label("1. Place the .srt file next to the video file");
                ui.label("2. Name it to match the video filename");
                ui.label("3. Click 'Mark Resolved' below");
                
                ui.add_space(10.0);
                
                ui.horizontal(|ui| {
                    if ui.button("Mark Resolved").clicked() {
                        self.mark_resolved(&state.failure.id);
                        self.manual_search_state = None;
                    }
                    if ui.button("Close").clicked() {
                        self.manual_search_state = None;
                    }
                });
            });
    }
}
```

---

## Web UI (Future Enhancement)

### Simple Axum Web UI

```rust
// src/plex/web_ui.rs

use axum::{
    response::Html,
    routing::get,
    Router,
    Extension,
};
use std::sync::Arc;

pub fn web_ui_router(failure_tracker: Arc<FailureTracker>) -> Router {
    Router::new()
        .route("/", get(index_handler))
        .route("/failures", get(failures_handler))
        .route("/api/failures", get(api_failures))
        .route("/api/retry/:id", post(api_retry))
        .route("/api/resolve/:id", post(api_resolve))
        .layer(Extension(failure_tracker))
}

async fn index_handler() -> Html<String> {
    Html(include_str!("templates/index.html").to_string())
}

async fn failures_handler(
    Extension(tracker): Extension<Arc<FailureTracker>>,
) -> Html<String> {
    let failures = tracker.get_unresolved_failures().unwrap_or_default();
    // Render template with failures
    let html = render_failures_template(&failures);
    Html(html)
}
```

### HTML Template (Minimal)

```html
<!-- src/plex/templates/index.html -->
<!DOCTYPE html>
<html>
<head>
    <title>Rustitles - Failed Downloads</title>
    <style>
        body { font-family: system-ui; background: #282a36; color: #f8f8f2; padding: 20px; }
        .card { background: #44475a; padding: 15px; margin: 10px 0; border-radius: 8px; }
        .error { color: #ff5555; }
        .btn { background: #bd93f9; border: none; padding: 8px 16px; border-radius: 4px; cursor: pointer; }
        .btn:hover { background: #8be9fd; color: #282a36; }
    </style>
</head>
<body>
    <h1>🎬 Failed Subtitle Downloads</h1>
    <div id="failures">
        <!-- Populated by JavaScript or server-side rendering -->
    </div>
</body>
</html>
```

---

## Notification System

### Desktop Notifications for Failures

```rust
// When downloads fail, optionally notify user

#[cfg(windows)]
fn send_failure_notification(failure: &FailureRecord) {
    use notify_rust::Notification;
    
    Notification::new()
        .summary("Subtitle Download Failed")
        .body(&format!("Could not find subtitles for: {}", failure.display_name()))
        .icon("rustitles")
        .show()
        .ok();
}
```

---

## Dependencies to Add

```toml
# For web UI
axum = "0.7"
tower-http = { version = "0.5", features = ["fs", "cors"] }

# For desktop notifications
notify-rust = "4"

# For URL encoding
urlencoding = "2.1"
```

---

## Next Steps

→ **[07-implementation-phases.md](07-implementation-phases.md)** - Development roadmap and milestones
