# Ilot

[![CI](https://github.com/Elian-BROLY/Ilot/actions/workflows/ci.yml/badge.svg)](https://github.com/Elian-BROLY/Ilot/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

[Français](README.md) | English

A capsule at the top of your Windows 11 screen, inspired by the iPhone's pill-shaped status area. Hover it to reach your music, mail, weather, volume and more. Not affiliated with Apple.

![Ilot demo](docs/demo.gif)


## Features

- **Media player:** controls for any app that reports to Windows, with a default player (Deezer, Spotify, Apple Music) and a small banner when the next track starts.
- **Mail:** unread Gmail and Outlook messages, with up to two Gmail accounts, or any IMAP account with an app password. Open, mark as read or delete in one click.
- **Calendar:** today's and tomorrow's events, a countdown in the capsule and a reminder with a join button (Google or Outlook, read-only).
- **Everyday pages:** weather, per-app volume, audio output, timer with focus mode and Pomodoro, notes with checkboxes, file shelf and system stats.
- **Indicators and alerts:** Windows notifications with per-app muting and quiet hours, microphone and camera dots, PC and Bluetooth headset battery.
- **Customizable keyboard shortcuts**, light and dark themes, several screens, English and French.

## Install

Download `Ilot-win-Setup.exe` from [Releases](https://github.com/Elian-BROLY/Ilot/releases). It installs the .NET 10 Desktop runtime if needed, which requires administrator rights once. Ilot then updates itself.

The app is not code-signed yet. Windows may show a SmartScreen warning, and **Smart App Control**, if enabled, may refuse to run a given version.

Clicking the notification area icon, or launching Ilot again while it is running, opens the settings. Uninstalling removes autostart, mail credentials, settings and the log. Only the note stays, in `%APPDATA%\Ilot\note.txt`.

## Privacy

Ilot only shows the unread count, sender and subject of your mail. Deleting mail requires a broad permission (`gmail.modify`, `Mail.ReadWrite`), which technically gives access to the mailbox. Tokens are encrypted with DPAPI on your PC. The city name goes to Open-Meteo.

## Develop

Requires the .NET 10 SDK. See [CONTRIBUTING.md](CONTRIBUTING.md) and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

```
dotnet run --project src\Ilot
dotnet test tests\Ilot.Tests
```

## License

[MIT](LICENSE)
