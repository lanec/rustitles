# Phase 9: Wire Up Plex UI

## Overview
Integrate the Plex settings and failure management UI into the existing Rustitles GUI.

## Files to Modify

### 1. Add Plex State to SubtitleDownloader
In `src/data_structures.rs`, add:
```rust
// Plex UI state
pub plex_failures_state: crate::plex::ui::PlexFailuresState,
pub show_plex_settings: bool,
```

### 2. Initialize Plex State in App
In `src/app.rs`, initialize the new fields in the `Default` implementation:
```rust
plex_failures_state: Default::default(),
show_plex_settings: false,
```

### 3. Add Plex Tab to GUI
In `src/gui.rs`, add a new section for Plex:

```rust
// Add to render method
if self.is_python_installed() && self.is_subliminal_installed() {
    ui.add_space(10.0);
    ui.separator();
    ui.add_space(10.0);
    
    // Plex Integration Header
    ui.horizontal(|ui| {
        ui.heading("Plex Integration");
        if ui.button(if self.show_plex_settings { "Hide Settings" } else { "Show Settings" }).clicked() {
            self.show_plex_settings = !self.show_plex_settings;
        }
    });
    
    if self.show_plex_settings {
        crate::plex::ui::render_plex_settings(
            ui,
            &mut self.plex_config,
            self.plex_testing_connection,
            &self.plex_connection_status,
        );
    }
    
    // Failures section (always visible if enabled)
    if self.plex_config.enabled {
        ui.add_space(10.0);
        ui.heading("Failed Downloads");
        crate::plex::ui::render_failures_tab(ui, &mut self.plex_failures_state);
    }
}
```

### 4. Handle Test Connection Button
Wire up the test connection functionality:
```rust
// In render_plex_settings, return whether test was clicked
// Then in gui.rs, call self.test_plex_connection() when clicked
```

### 5. Save Settings on Change
Ensure Plex settings are saved when changed:
```rust
// At the end of the settings section
if settings_changed {
    self.save_current_settings();
}
```

## UI Layout

```
┌─────────────────────────────────────────────────┐
│ Rustitles v0.x                                  │
├─────────────────────────────────────────────────┤
│ [Existing Folder Selection UI]                  │
│ [Existing Language Selection UI]                │
│ [Existing Download Options UI]                  │
├─────────────────────────────────────────────────┤
│ ▼ Plex Integration                [Show Settings]│
│   ┌─────────────────────────────────────────────┐
│   │ ☑ Enable Plex Integration                   │
│   │ Server URL: [http://192.168.1.x:32400     ] │
│   │ Token: [••••••••••••]        [Test]  ✓OK   │
│   │ Webhook Port: [9876]                        │
│   └─────────────────────────────────────────────┘
│                                                 │
│ Failed Downloads (3 unresolved)      [Refresh] │
│   ┌─────────────────────────────────────────────┐
│   │ ☐ The Matrix (1999)              EN        │
│   │   Error: No subtitles found                │
│   │   Attempts: 3 | Last: 2h ago               │
│   │   [Retry] [Search Online] [Resolve]        │
│   └─────────────────────────────────────────────┘
└─────────────────────────────────────────────────┘
```

## Expected Outcome
- Plex settings section visible in GUI
- Test connection button works
- Failed downloads list displays and updates
- Settings persist between sessions
