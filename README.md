# Rustitles - A Subtitle Downloader Tool

> **Note:** This is a fork of the original [Rustitles by fosterbarnes](https://github.com/fosterbarnes/rustitles) with macOS support added.

Rustitles will scan a given folder and automatically download subtitles in the selected language(s). It will scan recursively in the given folder for all video files, if missing subtitles are found, it will download them. This is built with media servers in mind, so if you have a large library of movies/tv-shows, just select the root folder used for your media server and wait for it to complete. This is a portable cross-platform application.

![rustitles_v2.1.2](https://i.postimg.cc/XvdsfrDg/rustitles-2.png)

## What's New in This Fork

- ✅ **macOS Support** - Native Apple Silicon and Intel Mac support
- ✅ **Homebrew Python Detection** - Automatic detection of Homebrew-installed Python
- ✅ **Standard macOS Paths** - Uses Application Support and Logs directories
- ✅ **Improved Cross-Platform Code** - Better platform-specific conditionals

## How to install

### Windows
- Download and install the latest version of [Microsoft Visual C++ Redistributable](https://aka.ms/vs/17/release/vc_redist.x64.exe)
- Download the [latest release](https://github.com/lanec/rustitles/releases) of Rustitles
- Save rustitles.exe somewhere memorable, or just run it from your downloads folder.

### Linux
- Download the [latest release](https://github.com/lanec/rustitles/releases) of Rustitles
- Save rustitles.AppImage somewhere memorable, or just run it from your downloads folder.
- Make it executable. Example: `chmod +x rustitles.AppImage`

### macOS
- Download the [latest release](https://github.com/lanec/rustitles/releases) of Rustitles
- Open the DMG and drag Rustitles to your Applications folder
- On first launch, right-click the app and select "Open" to bypass Gatekeeper (unsigned app warning)
- Install Python 3 if not already installed: `brew install python3` or download from [python.org](https://www.python.org/downloads/)

## How do I use it?

- Once open, click "Install Python" if you do not have Python installed (only required on first run)
- Follow the on screen prompts & wait for Rustitles to install Python and Subliminal (this only happens on the first run of Rustitles)
- Select your desired language(s)
- Set your maximum concurrent downloads or leave this number as default. This is the amount of subtitles that will be downloaded at the same time. (More concurrent downloads = more Python processes = more RAM used)
- Select the folder with your movies/tv-shows that you want subtitles for
- Wait for the processes to complete

### Virtual Machines

- Certain OpenGL calls can cause issues in Windows VMs. Mesa 3d (an open source implementation of OpenGL) can be used to fix this issue on certain VMs, this fix works for me in VirtualBox. Just download [mesa3d-25.2.1-release-mingw.7z](https://github.com/pal1000/mesa-dist-win/releases/download/25.2.1/mesa3d-25.2.1-release-mingw.7z) or [mesa3d-25.2.1-release-msvc.7z](https://github.com/pal1000/mesa-dist-win/releases/download/25.2.1/mesa3d-25.2.1-release-msvc.7z) from <https://github.com/pal1000/mesa-dist-win/releases> unzip, and then run `systemwidedeploy.cmd` as admin, selecting "1. Core desktop OpenGL drivers".


## Why does this exist?

**Original project by [fosterbarnes](https://github.com/fosterbarnes):**
> I spent about 45 minutes of my life trying to find a GUI utility for windows that would automatically scan a folder and download subtitles. All of the programs I found were either paid, did not work, confusing and bloated, or a command line tool. I then found Subliminal, and had the idea to create a simple GUI to accomplish basic tasks. I am teaching myself rust, so I decided to code in that language as a personal challenge.

This fork adds native macOS support while maintaining full compatibility with Windows and Linux.

## Dependencies

### Windows
- [Microsoft Visual C++ Redistributable](https://aka.ms/vs/17/release/vc_redist.x64.exe)
- [Python](https://www.python.org/downloads/)
- [Subliminal](https://github.com/Diaoul/subliminal)
- [FFmpeg](https://ffmpeg.org/about.html)

### Linux
- [Python](https://www.python.org/downloads/)
- [Pipx](https://github.com/pypa/pipx)
- [Subliminal](https://github.com/Diaoul/subliminal)
- [FFmpeg](https://ffmpeg.org/about.html)

### macOS
- [Python 3](https://www.python.org/downloads/) (or via Homebrew: `brew install python3`)
- [Subliminal](https://github.com/Diaoul/subliminal)
- [FFmpeg](https://ffmpeg.org/about.html) (optional, via Homebrew: `brew install ffmpeg`)

Rustitles will automatically install Subliminal on Windows and macOS. On Linux, it may require pipx. If you'd prefer to install manually:

**Windows**: Download the latest version of Python and select "add to path" when installing. After this, open cmd or powershell and enter `pip install subliminal`. Additionally, make sure the latest version of Microsoft Visual C++ Redistributable is installed.

**macOS**: Install Python 3 and run `python3 -m pip install --user subliminal` in Terminal.

**Linux**: Install pipx and run `pipx install subliminal`.

If you are unaware of Subliminal, it is a command line tool that uses python to find and download subtitles. If you prefer a CLI, just use Subliminal.

## Antivirus False Positives

I've tested for Windows Defender false postives in a virtual machine, and nothing was detected. However, any app that is not codesigned has a chance of tripping your antivirus (codesigning is very, very expensive). If this happens, add "rustitles.exe" or the folder therein as an exclusion for your antivirus. 

[How to set exclusions for Windows Defender](https://www.elevenforum.com/t/add-or-remove-exclusions-for-microsoft-defender-antivirus-in-windows-11.8797/#One)

VirusTotal scans:
- [rustitles v2.1.3.exe](https://www.virustotal.com/gui/file/edd2a5b9a994dae4151deee657ba1c9a36df796e647cf02a38da3475adac2e90)
- [rustitles v2.1.3.AppImage](https://www.virustotal.com/gui/file/626928dd4540bf912b560a601c367d86dba1c48c422b47b5abb5c781b99b7c5f)

Any detections seen can be assumed as false positives due to the app checking for and installing python if needed. That being said, ALWAYS be cautious when running scripts or .exe's from random people on GitHub. 

## Building/Compiling

### Windows
If you'd prefer to build the `.exe` yourself: 
- Download [Visual Studio Community Installer](https://visualstudio.microsoft.com/downloads/) and select "Desktop development with C++" in the installer.
- Download and install [Rust](https://www.rust-lang.org/tools/install)
- Clone or [download](https://github.com/fosterbarnes/rustitles/archive/refs/heads/main.zip) this repository. (Unzip if you used this download link)
- Open Command Prompt or Powershell
- cd to rustitles-main e.g. ```cd "C:\Users\Foster\Downloads\rustitles-main\rustitles-main"```
- enter ```cargo build --release```
- You'll find your newly built .exe at `\rustitles-main\target\release\rustitles.exe`

Without any changes to `\src\main.rs` or `Cargo.toml` this will be identical to the official release. Just leaving this method here in case anyone feels more comfortable building themselves, or wants to tinker and make some changes.

### macOS
If you'd prefer to build the `.app` yourself:
- Install [Xcode Command Line Tools](https://developer.apple.com/xcode/): `xcode-select --install`
- Download and install [Rust](https://www.rust-lang.org/tools/install)
- Clone or [download](https://github.com/fosterbarnes/rustitles/archive/refs/heads/main.zip) this repository
- Open Terminal
- cd to rustitles-main e.g. `cd ~/Downloads/rustitles-main`
- Run the build script: `chmod +x buildForMacOS.sh && ./buildForMacOS.sh`
- You'll find your newly built app at `Rustitles.app`

For detailed build instructions including universal binaries, DMG creation, and code signing, see [BUILD_MACOS.md](BUILD_MACOS.md).

### Linux
- Install Rust: `curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh`
- Clone or download this repository
- cd to rustitles-main
- Run: `cargo build --release`
- You'll find your binary at `target/release/rustitles`

## Support

### For This Fork (macOS-specific issues)
If you have issues with the macOS version or this fork, create an issue at [lanec/rustitles](https://github.com/lanec/rustitles/issues).

### For the Original Project
For Windows/Linux issues or general questions, see the [original project by fosterbarnes](https://github.com/fosterbarnes/rustitles).

### Support the Original Author
If you'd like to support the original creator (fosterbarnes):
- Follow on Twitch: https://www.twitch.tv/fosterbarnes
- Donate: https://coff.ee/fosterbarnes
