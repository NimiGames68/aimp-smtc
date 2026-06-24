# AIMP SMTC

<img src="https://gitlab.com/NimiGames68/aimp-smtc/-/raw/main/assets/aimp-smtc-logo.png" width="140" align="right">

A small tray app in C# that connects [AIMP](https://www.aimp.ru/) to the Windows **System Media Transport Controls** (SMTC)

AIMP has no native SMTC support, and the [plugin that was on the store](https://aimp.ru/?do=catalog&rec_id=1097) wasn't enough for me, so i built my own.

## Features

- Track title, artist, album and cover art (works for local files, online radio and podcasts)
- Play / pause / next / previous, shuffle and repeat
- A popup in the system tray with controls, with a Windows 11-style, and clickable title/artist/album rows that take you to [last.fm](https://last.fm)
- Light and dark theme, following the Windows system theme automatically

## Install

Run the `install.bat` from **[releases](https://github.com/NimiGames68/aimp-smtc/releases/latest)**. That file will do everything automatically. (except install .NET 8, you have to do that manually)

`Install.bat` Will download a zip from assets/AIMPSMTC.zip, extract that zip, (with powershell) build and install the app into `%LOCALAPPDATA%\AimpSmtc`, create a start menu shortcut, a shortcut to start the program with Windows and start the program when the install finishes.

‼️**This program requires [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)**‼️

**[Direct Download of .NET 8](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-8.0.422-windows-x64-installer)**

If you don't want the program to start when you boot windows, remove the program from shell:startup (run that command in the win+r box)

## Build manually from source

If you want to build this from source, and don't want to download the install.bat, here how it's done.

Download Git (if you haven't already) and run this command
```bash
git clone https://github.com/NimiGames68/aimp-smtc
```

then
```bash
cd aimp-smtc
```

and finally the .NET build command. You need [.NET 8](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) to use this command
```
dotnet build
```


## How it works

```
AIMP <--[WM_AIMP_PROPERTY / WM_AIMP_COMMAND]--> AimpRemote.cs
                                                       |
                                                       v
                                                 SmtcBridge.cs
                                                       |
                                                       v
                                                Windows SMTC

any SMTC button --> SmtcBridge --> AimpRemote --> AIMP

AIMP_RA_CMD_GET_ALBUMART --> WM_COPYDATA --> AlbumArtReceiver (TrayApp)
```

State, position, duration, shuffle, repeat and seeking all go through `WM_AIMP_PROPERTY`; playback commands go through `WM_AIMP_COMMAND`. See the header comment in lines 21 - 65 in the file `AimpRemote.cs` for the full reverse-engineered protocol reference.

The tray popup (`PopupMenu.cs`) is a separate independent UI. It talks to `SmtcBridge`/`AimpRemote` directly and doesn't go through Windows SMTC.

## Screenshots

|[FluentFlyout](https://github.com/unchihugo/FluentFlyout)|Built-in Pop-up|Windows|[Music Presence](https://github.com/ungive/discord-music-presence)|
|-|-|-|-|
| <img src="https://gitlab.com/NimiGames68/aimp-smtc/-/raw/main/assets/Captura_de_ecr%C3%A3_2026-06-22_141956.png" width="245"> | <img src="https://gitlab.com/NimiGames68/aimp-smtc/-/raw/main/assets/Captura_de_ecr%C3%A3_2026-06-22_165634.png" width="230"> | <img src="https://gitlab.com/NimiGames68/aimp-smtc/-/raw/main/assets/Captura_de_ecr%C3%A3_2026-06-22_170021.png" width="245"> | <img src="https://gitlab.com/NimiGames68/aimp-smtc/-/raw/main/assets/Captura_de_ecr%C3%A3_2026-06-22_202005.png" width="260"> 

## Windows 10 Media Control Plugin vs AIMP SMTC

||AIMP SMTC|Windows 10 Media Control|
|--------|---------|------------------------|
|Song Title|Yes|Yes
|Artist Name|Yes|Yes
|Album Title|Yes|No
|Previous|Yes|Yes
|Next|Yes|Yes
|Song length|Yes|No
|Album Cover|Yes|Yes
|Shuffle|Yes|No
|Repeat|Yes|No
|Runs in the background?|Yes|No
|Needs much setup?|Yes|No

## Project layout

```
AimpSmtc/
├── AimpRemote.cs       - AIMP Remote API client
├── SmtcBridge.cs       - wires AimpRemote up to 6he SMTC
├── TrayApp.cs          - tray icon, poll loop
├── PopupMenu.cs        - the click-to-open popup
├── Win11Slider.cs      - the popup's progress slider control
├── IconLoader.cs       - tray/popup icon loading,  it's aware of the theme you are using
├── LastFm.cs           - last.fm url builder and generic "open in browser" helper
├── AppIdentity.cs      - AUMID/shortcut setup
├── Program.cs          - entry point, single-instance guard, error logging
└── Resources/          - embedded icons (tray, app, popup controls)
```

## Some random thing you should know

the app consumes from 10mb to 30mb of ram in the background.


## Licence

[BSD 2-Clause License](https://gitlab.com/NimiGames68/aimp-smtc/-/blob/main/LICENSE)

---

#### Want to report a bug, or request a feature?

Feel free to contact me 

[Discord](https://discordapp.com/users/842801927904559128) - @NimiGames68

[Telegram](https://t.me/nimig68) - @nimig68
