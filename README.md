
# Rustitles - A Subtitle Downloader Tool

Rustitles will scan a given folder and automatically download subtitles in the selected language(s). It will scan recursively in the given folder for all video files, if missing subtitles are found, it will download them. This is built with media servers in mind, so if you have a large library of movies/tv-shows, just select the root folder used for your media server and wait for it to complete. This is a portable application.

![rustitles_woHCspsmUA](https://github.com/user-attachments/assets/99a76449-a243-4dc3-a32f-4d87ab5ef63f)

## How to install

- Download and install the latest version of [Microsoft Visual C++ Redistributable](https://aka.ms/vs/17/release/vc_redist.x64.exe)
- Download the latest release of Rustitles
- Save rustitles.exe somewhere memorable, or just run it from your downloads folder.

## How do I use it?

- Once opening, click "Install Python" if you do not have Python installed (only required on first run)
- Follow the on screen prompts & wait for Rustitles to install Python and Subliminal (this only happens on the first run of Rustitles)
- Select your desired language(s)
- Set your maximum concurrent downloads or leave this number as default. This is the amount of subtitles that will be downloaded at the same time. (More concurrent downloads = more Python processes = more RAM used)
- Select the folder with your movies/tv-shows that you want subtitles for
- Wait for the processes to complete

## Why does this exist?

I spent about 45 minutes of my life trying to find a GUI utility for windows that would automatically scan a folder and download subtitles. All of the programs I found were either paid, did not work, confusing and bloated, or a command line tool. I then found Subliminal, and had the idea to create a simple GUI to accomplish basic tasks. I am teaching myself rust, so I decided to code in that language as a personal challenge.

This tool is here for the "me" of yesterday (you) who was trying to find a tool exactly like this lmao

## Dependencies

- [Microsoft Visual C++ Redistributable](https://aka.ms/vs/17/release/vc_redist.x64.exe)
- [Python](https://www.python.org/downloads/)
- [Subliminal](https://github.com/Diaoul/subliminal)

Rustitles will automatically install Python & Subliminal, but if you'd prefer to do that manually, download the latest version of Python and select "add to path" when installing. After this, open cmd or powershell and enter 
```pip install subliminal``` . Aditionally, make sure the latest version of Microsoft Visual C++ Redistributable is installed. Once this is done, Rustitles is ready to go.

If you are unaware of Subliminal, it is a command line tool that uses python to find and download subtitles. If you prefer a CLI, just use Subliminal.
