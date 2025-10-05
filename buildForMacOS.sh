#!/bin/bash
# Build script for macOS
# This script builds Rustitles and creates a .app bundle

set -e

echo "Building Rustitles for macOS..."

# Build the release binary
cargo build --release

echo "Creating .app bundle..."

# Create bundle structure
APP_NAME="Rustitles.app"
CONTENTS_DIR="$APP_NAME/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
RESOURCES_DIR="$CONTENTS_DIR/Resources"

# Remove existing bundle if it exists
rm -rf "$APP_NAME"

# Create directories
mkdir -p "$MACOS_DIR"
mkdir -p "$RESOURCES_DIR"

# Copy binary
cp target/release/rustitles "$MACOS_DIR/"

# Copy Info.plist
cp Info.plist.template "$CONTENTS_DIR/Info.plist"

# Copy icon if it exists
if [ -f "resources/rustitles_icon.png" ]; then
    cp resources/rustitles_icon.png "$RESOURCES_DIR/"
fi

# Make the binary executable
chmod +x "$MACOS_DIR/rustitles"

echo "✅ Build complete!"
echo "App bundle created at: $APP_NAME"
echo ""
echo "To run: open $APP_NAME"
echo "To create DMG: hdiutil create -volname Rustitles -srcfolder $APP_NAME -ov -format UDZO Rustitles-v2.1.3-macOS.dmg"
