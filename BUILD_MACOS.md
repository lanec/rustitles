# Building Rustitles for macOS

> **Note:** This is a fork of the original [Rustitles by fosterbarnes](https://github.com/fosterbarnes/rustitles) with macOS support added.

This guide covers building and distributing Rustitles on macOS.

## Prerequisites

### For Building
1. **Xcode Command Line Tools**
   ```bash
   xcode-select --install
   ```

2. **Rust**
   ```bash
   curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
   ```

3. **Python 3** (for running the app)
   - Via Homebrew: `brew install python3`
   - Or download from [python.org](https://www.python.org/downloads/)

4. **FFmpeg** (optional, for embedded subtitle detection)
   ```bash
   brew install ffmpeg
   ```

## Building

### Quick Build
```bash
chmod +x buildForMacOS.sh
./buildForMacOS.sh
```

This creates a `Rustitles.app` bundle ready to use.

### Manual Build
```bash
# Build release binary
cargo build --release

# Create app bundle structure
mkdir -p Rustitles.app/Contents/{MacOS,Resources}

# Copy binary
cp target/release/rustitles Rustitles.app/Contents/MacOS/

# Copy Info.plist
cp Info.plist.template Rustitles.app/Contents/Info.plist

# Copy icon
cp resources/rustitles_icon.png Rustitles.app/Contents/Resources/

# Make executable
chmod +x Rustitles.app/Contents/MacOS/rustitles
```

### Build for Universal Binary (Apple Silicon + Intel)
```bash
# Install targets
rustup target add x86_64-apple-darwin
rustup target add aarch64-apple-darwin

# Build for both architectures
cargo build --release --target x86_64-apple-darwin
cargo build --release --target aarch64-apple-darwin

# Create universal binary
lipo -create \
    target/x86_64-apple-darwin/release/rustitles \
    target/aarch64-apple-darwin/release/rustitles \
    -output target/release/rustitles-universal

# Use universal binary in app bundle
cp target/release/rustitles-universal Rustitles.app/Contents/MacOS/rustitles
```

## Creating a DMG Installer

```bash
# Create DMG
hdiutil create -volname "Rustitles" \
    -srcfolder Rustitles.app \
    -ov -format UDZO \
    Rustitles-v2.1.3-macOS.dmg
```

### Fancy DMG with Background
```bash
# Create temp DMG
hdiutil create -size 200m -fs HFS+ -volname "Rustitles" temp.dmg

# Mount it
hdiutil attach temp.dmg

# Copy app
cp -r Rustitles.app /Volumes/Rustitles/

# Create Applications symlink
ln -s /Applications /Volumes/Rustitles/Applications

# Eject
hdiutil detach /Volumes/Rustitles

# Convert to compressed DMG
hdiutil convert temp.dmg -format UDZO -o Rustitles-v2.1.3-macOS.dmg
rm temp.dmg
```

## Code Signing (Optional but Recommended)

### Get a Developer Certificate
1. Join [Apple Developer Program](https://developer.apple.com/programs/)
2. Create a Developer ID Application certificate in Xcode

### Sign the Application
```bash
# Sign the binary
codesign --force --deep --sign "Developer ID Application: Your Name" Rustitles.app

# Verify signature
codesign --verify --deep --strict --verbose=2 Rustitles.app
spctl -a -vv Rustitles.app
```

## Notarization (For Distribution Outside App Store)

Notarization allows Gatekeeper to verify your app.

```bash
# Create a zip for notarization
ditto -c -k --keepParent Rustitles.app Rustitles.zip

# Submit for notarization (requires Apple Developer account)
xcrun notarytool submit Rustitles.zip \
    --apple-id "your@email.com" \
    --password "app-specific-password" \
    --team-id "TEAM_ID" \
    --wait

# Staple the notarization ticket
xcrun stapler staple Rustitles.app
```

## Distribution

### GitHub Release
```bash
# Create DMG as shown above
# Upload to GitHub Releases

# Add to README download links:
# [Download for macOS (Apple Silicon)](https://github.com/fosterbarnes/rustitles/releases/download/v2.1.3/rustitles.v2.1.3-arm64.dmg)
# [Download for macOS (Intel)](https://github.com/fosterbarnes/rustitles/releases/download/v2.1.3/rustitles.v2.1.3-x86_64.dmg)
```

## File Locations on macOS

The app stores files in standard macOS locations:

- **Settings**: `~/Library/Application Support/rustitles/settings.json`
- **Logs**: `~/Library/Logs/rustitles/rustitles.log`
- **Subliminal Cache**: `~/Library/Python/3.x/bin/` (pip user install)

## Dependencies

### Python Installation Detection
Rustitles checks these locations in order:
1. `/opt/homebrew/bin/python3` (Apple Silicon Homebrew)
2. `/usr/local/bin/python3` (Intel Mac Homebrew)
3. `python3` (system PATH)
4. `python` (fallback)

### Subliminal Installation
On macOS, Subliminal is installed via pip with the `--user` flag:
```bash
python3 -m pip install --user subliminal
```

This installs to `~/Library/Python/3.x/bin/`, which is automatically added to PATH by Rustitles.

## Troubleshooting

### "rustitles" cannot be opened because the developer cannot be verified
This happens with unsigned apps. Users can:
1. Right-click the app → "Open" → "Open" (bypasses Gatekeeper once)
2. Or: System Preferences → Security & Privacy → "Open Anyway"

### Python not found
Install Python 3:
```bash
# Using Homebrew (recommended)
brew install python3

# Or download from python.org
```

### Subliminal installation fails
Try manual installation:
```bash
python3 -m pip install --user subliminal
```

### FFmpeg not found (embedded subtitle detection)
```bash
brew install ffmpeg
```

## CI/CD with GitHub Actions

Example workflow for building macOS releases:

```yaml
name: Build macOS

on:
  push:
    tags:
      - 'v*'

jobs:
  build-macos:
    runs-on: macos-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Install Rust
        uses: actions-rs/toolchain@v1
        with:
          toolchain: stable
          
      - name: Build for Intel
        run: cargo build --release --target x86_64-apple-darwin
        
      - name: Build for Apple Silicon  
        run: cargo build --release --target aarch64-apple-darwin
        
      - name: Create Universal Binary
        run: |
          lipo -create \
            target/x86_64-apple-darwin/release/rustitles \
            target/aarch64-apple-darwin/release/rustitles \
            -output target/release/rustitles
            
      - name: Create App Bundle
        run: ./buildForMacOS.sh
        
      - name: Create DMG
        run: |
          hdiutil create -volname "Rustitles" \
            -srcfolder Rustitles.app \
            -ov -format UDZO \
            Rustitles-v${{ github.ref_name }}-macOS.dmg
            
      - name: Upload Release Asset
        uses: actions/upload-release-asset@v1
        with:
          upload_url: ${{ github.event.release.upload_url }}
          asset_path: ./Rustitles-v${{ github.ref_name }}-macOS.dmg
          asset_name: rustitles.v${{ github.ref_name }}.dmg
          asset_content_type: application/octet-stream
```

## Testing

Test the build on both architectures if possible:
- **Apple Silicon (M1/M2/M3)**: Native arm64
- **Intel**: x86_64 or Rosetta 2 translation

Test scenarios:
1. Fresh install (no Python)
2. Homebrew Python present
3. System Python only
4. Subtitle download with various languages
5. Large folder scanning (1000+ videos)
6. Settings persistence across launches
