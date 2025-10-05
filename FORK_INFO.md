# Fork Information

## About This Fork

This is a fork of the original **Rustitles** project by [fosterbarnes](https://github.com/fosterbarnes).

- **Original Repository**: https://github.com/fosterbarnes/rustitles
- **This Fork**: https://github.com/lanec/rustitles
- **Fork Author**: lanec
- **Original Author**: fosterbarnes

## What's Different in This Fork

### Primary Addition: macOS Support

This fork adds native macOS support while maintaining full compatibility with Windows and Linux platforms.

#### macOS-Specific Features
- ✅ Native Apple Silicon (M1/M2/M3) and Intel Mac support
- ✅ Homebrew Python detection
- ✅ Standard macOS directory structure:
  - Settings: `~/Library/Application Support/rustitles/`
  - Logs: `~/Library/Logs/rustitles/`
  - Python Scripts: `~/Library/Python/3.x/bin/`
- ✅ Proper `.app` bundle creation
- ✅ Info.plist configuration
- ✅ DMG packaging support

### Technical Changes

#### Modified Files
1. **src/settings.rs** - Added macOS Application Support directory
2. **src/python_manager.rs** - Added Homebrew Python detection, macOS PATH handling
3. **src/app.rs** - Updated platform conditionals for macOS
4. **src/gui.rs** - Added macOS-specific installation instructions
5. **src/main.rs** - Updated icon loading for macOS
6. **src/subtitle_utils.rs** - Updated FFprobe handling for macOS
7. **src/logging.rs** - Added macOS Logs directory
8. **src/config.rs** - Added macOS platform constants
9. **build.rs** - Added macOS build configuration
10. **Cargo.toml** - Updated metadata for fork

#### New Files
1. **buildForMacOS.sh** - Automated build script
2. **Info.plist.template** - macOS app metadata
3. **BUILD_MACOS.md** - macOS build documentation
4. **MACOS_PORT_SUMMARY.md** - Technical changes summary
5. **MACOS_TESTING_CHECKLIST.md** - QA checklist
6. **TEST_RESULTS.md** - Build and test results
7. **FORK_INFO.md** - This file

## Attribution

### Original Project
**Rustitles** was created by [fosterbarnes](https://github.com/fosterbarnes) as a learning project in Rust, providing a simple GUI wrapper around the Subliminal subtitle downloader.

**Original Author's Statement:**
> I spent about 45 minutes of my life trying to find a GUI utility for windows that would automatically scan a folder and download subtitles. All of the programs I found were either paid, did not work, confusing and bloated, or a command line tool. I then found Subliminal, and had the idea to create a simple GUI to accomplish basic tasks. I am teaching myself rust, so I decided to code in that language as a personal challenge.

### Support the Original Author
If you find this tool useful, please consider supporting the original creator:
- **Twitch**: https://www.twitch.tv/fosterbarnes
- **Donation**: https://coff.ee/fosterbarnes

## License

This project maintains the original **MIT License** from the upstream project.

```
MIT License

Copyright (c) 2025 fosterbarnes (original)
Copyright (c) 2025 lanec (macOS port)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Contributing

### For macOS-Specific Issues
Please report macOS-specific issues or enhancements to:
- https://github.com/lanec/rustitles/issues

### For General Issues
For Windows/Linux issues or general functionality:
- https://github.com/fosterbarnes/rustitles/issues

### Upstream Contributions
Major features and improvements that benefit all platforms may be considered for contribution back to the original project.

## Versioning

This fork maintains version compatibility with the upstream project:
- **Current Version**: 2.1.3 (matching upstream)
- Fork-specific changes are documented in commit history

## Acknowledgments

- **fosterbarnes** - Original creator and maintainer of Rustitles
- **Subliminal Project** - The underlying subtitle download engine
- **egui/eframe** - The Rust GUI framework used
- **Rust Community** - For excellent cross-platform tooling

---

**Last Updated**: October 5, 2025
