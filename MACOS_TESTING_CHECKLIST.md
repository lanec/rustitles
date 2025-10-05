# macOS Testing Checklist

Use this checklist to verify the macOS port is working correctly.

## Pre-Build Testing

- [ ] Verify Rust is installed: `rustc --version`
- [ ] Verify Xcode Command Line Tools: `xcode-select -p`
- [ ] Code compiles without errors: `cargo check`
- [ ] No compilation warnings in macOS-specific code

## Build Testing

- [ ] Build script is executable: `ls -l buildForMacOS.sh`
- [ ] Build completes successfully: `./buildForMacOS.sh`
- [ ] App bundle created: `ls -la Rustitles.app`
- [ ] Binary is executable: `ls -l Rustitles.app/Contents/MacOS/rustitles`
- [ ] Info.plist exists: `cat Rustitles.app/Contents/Info.plist`
- [ ] Icon copied to Resources: `ls -l Rustitles.app/Contents/Resources/`

## Launch Testing

- [ ] App launches: `open Rustitles.app`
- [ ] Window appears centered
- [ ] Dracula theme applied correctly
- [ ] No crash on startup
- [ ] Check Console.app for errors/warnings

## Python Detection

### No Python Scenario
- [ ] App detects Python is not installed
- [ ] Shows appropriate message with Homebrew suggestion
- [ ] No crash when Python missing

### Homebrew Python (Apple Silicon)
- [ ] Detects `/opt/homebrew/bin/python3`
- [ ] Shows correct Python version
- [ ] Can install Subliminal

### Homebrew Python (Intel Mac)
- [ ] Detects `/usr/local/bin/python3`
- [ ] Shows correct Python version
- [ ] Can install Subliminal

### System Python
- [ ] Detects system `python3`
- [ ] Shows correct Python version
- [ ] Can install Subliminal

## Subliminal Installation

- [ ] "Install Subliminal" button appears when Python detected
- [ ] Installation starts when clicked
- [ ] Shows "Installing..." status
- [ ] Completes successfully
- [ ] Subliminal detected after installation: `python3 -m pip show subliminal`
- [ ] Subliminal command works: `subliminal --version`
- [ ] PATH updated correctly to include `~/Library/Python/3.x/bin/`

## Settings Persistence

- [ ] Settings directory created: `ls ~/Library/Application\ Support/rustitles/`
- [ ] Select languages and close app
- [ ] Reopen app
- [ ] Selected languages still selected
- [ ] Settings file exists: `cat ~/Library/Application\ Support/rustitles/settings.json`

## Logging

- [ ] Log directory created: `ls ~/Library/Logs/rustitles/`
- [ ] Log file created on first run
- [ ] Log entries appear: `tail ~/Library/Logs/rustitles/rustitles.log`
- [ ] Timestamps are correct
- [ ] No permission errors

## Folder Selection

- [ ] Click "Select Folder" button
- [ ] macOS file picker appears
- [ ] Can navigate to test folder
- [ ] Folder path displays correctly after selection
- [ ] Can handle paths with spaces
- [ ] Can handle paths with special characters
- [ ] Can handle very long paths

## Video Scanning

### Small Library (< 100 videos)
- [ ] Scan completes in reasonable time
- [ ] Correct video count displayed
- [ ] Missing subtitles count correct
- [ ] No false positives/negatives

### Large Library (1000+ videos)
- [ ] App remains responsive during scan
- [ ] No UI freezing
- [ ] Scan completes successfully
- [ ] Memory usage reasonable (check Activity Monitor)
- [ ] CPU usage reasonable

### Edge Cases
- [ ] Nested folders work
- [ ] Hidden files ignored
- [ ] Symlinks handled correctly
- [ ] "Ignore Extra Folders" option works
  - [ ] Skips "Behind The Scenes"
  - [ ] Skips "Deleted Scenes"
  - [ ] Skips "Featurettes"
  - [ ] Shows ignored folder count

## Subtitle Downloads

### Single Language
- [ ] Select one language (e.g., English)
- [ ] Start downloads
- [ ] Progress bar updates
- [ ] Status shows "X completed, Y running"
- [ ] Subtitles downloaded successfully
- [ ] Correct file naming (e.g., `movie.en.srt`)

### Multiple Languages
- [ ] Select 2-3 languages
- [ ] Start downloads
- [ ] Multiple subtitle files created
- [ ] Correct language codes in filenames

### Concurrent Downloads
- [ ] Set to 25 concurrent
- [ ] Monitor Activity Monitor
- [ ] 25 Python processes spawn
- [ ] All complete successfully
- [ ] Set to 50 concurrent
- [ ] No crashes or freezes

### Error Handling
- [ ] No internet connection → shows error
- [ ] Invalid video file → skips gracefully
- [ ] No subtitles available → shows status
- [ ] Permission denied folder → shows error

## FFprobe Integration

If FFmpeg installed (`brew install ffmpeg`):
- [ ] Embedded subtitles detected
- [ ] Shows "Embedded X subtitles exist" status
- [ ] Doesn't re-download if embedded exists
- [ ] "Ignore Embedded" option forces download

## Performance

### Memory
- [ ] Idle: < 100 MB
- [ ] Scanning 1000 videos: < 300 MB
- [ ] Downloading 25 concurrent: < 500 MB
- [ ] No memory leaks (check over time)

### CPU
- [ ] Idle: < 1%
- [ ] Scanning: < 20%
- [ ] Downloading: Varies with concurrent count
- [ ] No CPU spinning

### Disk I/O
- [ ] No excessive disk writes
- [ ] Log file doesn't grow too large
- [ ] Temp files cleaned up

## UI/UX

- [ ] All text readable
- [ ] No text cutoff
- [ ] Buttons clickable
- [ ] Scrolling smooth
- [ ] Language dropdown works
- [ ] Checkboxes work
- [ ] Tooltips appear on hover
- [ ] Progress indicators animate
- [ ] No visual glitches

## Stability

- [ ] Can run for extended period (30+ min)
- [ ] Can download 100+ subtitles without crash
- [ ] Can scan multiple folders in one session
- [ ] Clean shutdown on quit
- [ ] No zombie processes left behind: `ps aux | grep python`

## Edge Cases

- [ ] Empty folder → handles gracefully
- [ ] No videos found → shows message
- [ ] All videos have subtitles → shows message
- [ ] Cancel during download → stops cleanly
- [ ] Quit during download → stops cleanly
- [ ] Network drops mid-download → handles gracefully
- [ ] Disk full → shows error
- [ ] No write permissions → shows error

## macOS-Specific

- [ ] Works with dark mode
- [ ] Works with light mode
- [ ] Dock icon appears
- [ ] Cmd+Q quits app
- [ ] Cmd+W closes window (if applicable)
- [ ] Can drag window
- [ ] Window remember position (if applicable)
- [ ] Retina display rendering correct
- [ ] Multi-monitor support

## Distribution Testing

If creating DMG:
- [ ] DMG mounts successfully: `hdiutil attach Rustitles.dmg`
- [ ] App can be dragged to Applications
- [ ] Runs from Applications folder
- [ ] Can be moved after installation
- [ ] Uninstall removes all files

## Upgrade Testing

If updating from previous version:
- [ ] Settings migrate correctly
- [ ] No data loss
- [ ] Version number updated
- [ ] Release notes accessible

## Documentation

- [ ] README.md macOS instructions clear
- [ ] BUILD_MACOS.md accurate
- [ ] All links work
- [ ] Screenshots up to date (if any)

## Comparison with Other Platforms

- [ ] Feature parity with Windows version
- [ ] Feature parity with Linux version
- [ ] No platform-specific bugs
- [ ] Consistent behavior across platforms

## Final Checks

- [ ] Version number correct in Info.plist
- [ ] Version number matches Cargo.toml
- [ ] Copyright year correct
- [ ] Bundle identifier unique
- [ ] No hardcoded paths
- [ ] No debug code left in
- [ ] No println!() or dbg!() macros
- [ ] All TODO comments addressed

## Sign-Off

- Date tested: _______________
- macOS version: _______________
- Architecture: [ ] Apple Silicon [ ] Intel
- Tester: _______________
- Result: [ ] PASS [ ] FAIL
- Notes: _______________

---

## Critical Issues (Must Fix Before Release)

List any critical issues found:

1. 
2. 
3. 

## Minor Issues (Can Fix Later)

1. 
2. 
3. 

## Enhancement Ideas

1. 
2. 
3. 
