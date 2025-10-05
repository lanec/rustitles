# macOS Port Summary

This document summarizes the changes made to port Rustitles to macOS.

## Changes Made

### Code Changes

#### 1. **src/settings.rs**
- Added macOS-specific settings path: `~/Library/Application Support/rustitles/settings.json`
- Split `#[cfg(not(windows))]` into separate `#[cfg(target_os = "macos")]` and `#[cfg(target_os = "linux")]`

#### 2. **src/python_manager.rs**
- Added macOS Python detection with Homebrew paths priority:
  - `/opt/homebrew/bin/python3` (Apple Silicon)
  - `/usr/local/bin/python3` (Intel Mac)
  - Then falls back to system `python3`
- Updated `install_subliminal()` to use pip with `--user` flag on macOS
- Updated `add_scripts_to_path()` to handle macOS Python user scripts directory (`~/Library/Python/3.x/bin/`)
- Updated `refresh_environment()` with macOS-specific PATH handling

#### 3. **src/app.rs**
- Updated pipx checks to only apply to Linux (not used on macOS or Windows)
- Changed `#[cfg(not(windows))]` to `#[cfg(target_os = "linux")]` for pipx-specific code
- macOS uses pip directly like Windows, not pipx

#### 4. **src/gui.rs**
- Updated Python installation instructions for macOS users
- Added Homebrew installation suggestion
- Pipx status only shown on Linux

#### 5. **src/main.rs**
- Updated icon loading to use `#[cfg(any(target_os = "linux", target_os = "macos"))]`
- Updated window positioning for Unix systems

#### 6. **src/subtitle_utils.rs**
- Updated FFprobe output redirection for Unix systems (Linux + macOS)

#### 7. **src/logging.rs**
- Added macOS-specific log path: `~/Library/Logs/rustitles/rustitles.log`

#### 8. **src/config.rs**
- Added macOS Python installer URL (for completeness, not currently used)

#### 9. **build.rs**
- Added macOS build configuration
- Sets `MACOSX_DEPLOYMENT_TARGET=10.13` for compatibility

#### 10. **Cargo.toml**
- Added `[package.metadata.bundle]` section for macOS app bundling

### New Files Created

1. **buildForMacOS.sh**
   - Automated build script that creates a proper `.app` bundle
   - Includes instructions for DMG creation

2. **Info.plist.template**
   - macOS application property list
   - Contains app metadata, bundle identifier, version info

3. **BUILD_MACOS.md**
   - Comprehensive build documentation
   - Covers universal binaries, DMG creation, code signing, notarization
   - Includes CI/CD examples and troubleshooting

4. **MACOS_PORT_SUMMARY.md** (this file)
   - Summary of all changes

### Documentation Updates

- **README.md**: Added macOS installation and build instructions
- Dependencies section updated with macOS-specific info
- Building/Compiling section includes macOS build steps

## Platform-Specific Behavior

### Python Detection Order
1. **Windows**: `python`, `py`, `python3`
2. **macOS**: Homebrew paths first, then system python3
3. **Linux**: `python3`, `python`, `py`

### Subliminal Installation
- **Windows**: `pip install subliminal`
- **macOS**: `pip install --user subliminal`
- **Linux**: `pipx install subliminal` (preferred)

### File Storage Locations

#### Settings
- **Windows**: Next to executable
- **macOS**: `~/Library/Application Support/rustitles/`
- **Linux**: `~/.config/rustitles/` or `~/.rustitles/`

#### Logs
- **Windows**: Next to executable
- **macOS**: `~/Library/Logs/rustitles/`
- **Linux**: `~/.cache/rustitles/` or `~/.rustitles/`

#### Python Scripts
- **Windows**: `%APPDATA%\Python\Python3x\Scripts\`
- **macOS**: `~/Library/Python/3.x/bin/`
- **Linux**: `~/.local/bin/`

## Testing Requirements

Before releasing, test on:

### macOS Versions
- [ ] macOS 14 Sonoma (latest)
- [ ] macOS 13 Ventura
- [ ] macOS 12 Monterey
- [ ] macOS 11 Big Sur

### Architectures
- [ ] Apple Silicon (M1/M2/M3) - arm64
- [ ] Intel Mac - x86_64
- [ ] Universal binary (both)

### Python Scenarios
- [ ] No Python installed
- [ ] Homebrew Python (Apple Silicon path)
- [ ] Homebrew Python (Intel path)
- [ ] System Python only
- [ ] Multiple Python versions

### Functional Tests
- [ ] App launches successfully
- [ ] Settings persistence
- [ ] Folder selection dialog
- [ ] Video file scanning (1000+ files)
- [ ] Subtitle downloads (multiple languages)
- [ ] FFprobe embedded subtitle detection
- [ ] Error handling and logging
- [ ] Concurrent downloads (25+ simultaneous)

## Known Limitations

1. **No Python Auto-Install**: Unlike Windows, macOS version doesn't auto-install Python. Users must install via Homebrew or python.org.

2. **Unsigned Binary Warning**: Without code signing ($99/year Apple Developer), users see Gatekeeper warning and must right-click → Open.

3. **No App Store Distribution**: Would require Apple Developer membership and sandboxing compliance.

## Build Requirements

### Minimal
- Xcode Command Line Tools
- Rust toolchain
- No special dependencies

### For Distribution
- Code signing certificate (Apple Developer Program)
- Notarization credentials
- DMG creation tools (hdiutil, built-in)

## Next Steps

1. **Test the Build**
   ```bash
   ./buildForMacOS.sh
   open Rustitles.app
   ```

2. **Test Functionality**
   - Verify Python detection
   - Test Subliminal installation
   - Download subtitles for test videos

3. **Create Universal Binary** (if building for distribution)
   ```bash
   rustup target add aarch64-apple-darwin x86_64-apple-darwin
   # Follow BUILD_MACOS.md instructions
   ```

4. **Package DMG**
   ```bash
   hdiutil create -volname "Rustitles" -srcfolder Rustitles.app -ov -format UDZO Rustitles-v2.1.3-macOS.dmg
   ```

5. **Optional: Code Sign** (requires Apple Developer certificate)
   ```bash
   codesign --force --deep --sign "Developer ID Application: Your Name" Rustitles.app
   ```

6. **Optional: Notarize** (requires Apple Developer account)
   - Follow BUILD_MACOS.md notarization section

## Compatibility

**Minimum macOS Version**: 10.13 High Sierra (set via MACOSX_DEPLOYMENT_TARGET)

**Recommended**: macOS 11.0 Big Sur or later

## Dependencies on User System

### Required
- macOS 10.13+
- Python 3 (user must install)

### Optional
- FFmpeg (for embedded subtitle detection)
- Homebrew (recommended for easy Python installation)

## Changes Summary

- **Files Modified**: 10 source files
- **New Files**: 4 (build script, Info.plist, documentation)
- **Lines Changed**: ~200-300 lines (mostly cfg conditionals)
- **Breaking Changes**: None (backward compatible)
- **Platform Support**: Windows + Linux + **macOS** ✅
