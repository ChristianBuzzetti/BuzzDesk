# BuzzDesk

**Work and personal organizer for Windows.** Today at a glance, tasks with recurrence and subtasks, Windows reminders with snooze, topic follow-up ("waiting on…", review dates, a timeline of notes), useful links with a quick launcher, a quick-capture window (`Ctrl+Alt+Space`) and search across everything. Everything lives in a local SQLite database with automatic daily backups. With Boberto, the live companion you can dress up, and a built-in guide (`F1`). Optional **AI**, off until you turn it on. English by default, Spanish built in. MIT licensed.

BuzzDesk is the sibling of [BuzzPrint](https://github.com/ChristianBuzzetti/BuzzPrint): same author, same look and feel.

> Work in progress: see [Status](#status).

**Requirements:** Windows 10/11 and Microsoft Edge WebView2 Runtime (included with Windows 11). To build: .NET 10 SDK.

## Build from source

```powershell
winget install Git.Git
winget install Microsoft.DotNet.SDK.10
git clone https://github.com/ChristianBuzzetti/BuzzDesk.git
cd BuzzDesk
powershell -ExecutionPolicy Bypass -File .\build.ps1          # dist\BuzzDesk\BuzzDesk.exe
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Zip     # also artifacts\BuzzDesk-<version>-win-x64.zip
```

`dist\BuzzDesk` is the complete, portable app (it includes .NET).

## Folders

| What | Where |
|---|---|
| Database | `%LOCALAPPDATA%\BuzzDesk\buzzdesk.db` (SQLite, WAL) |
| Backups | `%LOCALAPPDATA%\BuzzDesk\backups` |
| Settings, appearance, Boberto | `%LOCALAPPDATA%\BuzzDesk\*.json` |

## Status

| Stage | |
|---|---|
| 1. Skeleton, appearance, Boberto, icon | done |
| 2. SQLite, Today, Tasks | done |
| 3. Reminders, tray | done |
| 4. Topics | done |
| 5. Links | done |
| 6. Quick capture, search | done |
| 7. Guide, README with screenshots, `build -Zip` | |

## Code shared with BuzzPrint

`src/BuzzDesk/Appearance/` (palettes, fonts, Personalize, live logo, Boberto Studio / Stage / Dock), `Assets/Boberto/`, `Assets/Fonts/`, `IconMaker`, `Core/Loc`, `Core/Dependencies`, the guide window and the optional AI (`Core/Ai`, `AiSettingsWindow`) are **copied** from BuzzPrint for now. Later they move to a shared library, **Buzz.Appearance**, used by both apps.

## Roadmap (phase 2)

- Outlook / Google Calendar.
- Jira.
- Attach BuzzPrint captures to a topic.
- Daily and weekly summary with AI.
- Export a weekly report to Word / PDF with BuzzPrint's templates.

## License

MIT (see `LICENSE`). Fonts and packages: see `THIRD-PARTY-NOTICES.md`.
