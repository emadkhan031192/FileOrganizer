# File Organizer — Windows Explorer Companion

> **Select a file → click a saved destination → file moves immediately.**

A lightweight, fully offline Windows app that works *with* File Explorer instead of replacing it:
a floating **Quick Bar**, an Explorer **right-click menu**, one-click **⚡ ORGANIZE**, custom rules,
batch rename, duplicate finder, fast search and **Undo** for everything.

![stack](https://img.shields.io/badge/.NET-8.0-512BD4) ![platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-blue) ![license](https://img.shields.io/badge/license-MIT-green)

---

## The core workflow

1. In Explorer, select one or more files.
2. Click a destination on the Quick Bar (or right-click → **Move to Organizer → Jobs**).
3. Done — the files move instantly. Name conflicts are auto-renamed (`poster (1).jpg`), never overwritten.

No browsing for folders, no heavy file manager, no account, no internet.

## Features

| Area | What's included |
|---|---|
| **Quick "Move To" destinations** | Saved destination buttons (icon, colour, sub-destinations, drag-to-reorder via right-click menu). Moves **files and folders**. Seeded with *Afzal E Services → Jobs / Advertisements / School*, Documents, Personal. Manage them in the main window or in Settings (add / remove / rename). |
| **Quick Destination Bar (§6)** | A small always-on-top floating bar. Click = move Explorer's current selection (files or folders). Drag & drop onto a button also works. Ships with a single **⚡ Organize** button (organizes the folder open in Explorer, with preview; falls back to a folder picker, never forces the main window open), an **✏ Rename** button, a **☰ Options** menu (Open File Organizer, Settings, Organize, Rename, Hide) and a 🏠 button for the main window. Settings customizes the bar: show/hide ⚡/✏, icons-only vs icons+names, button size S/M/L, dock mode (free / inside Explorer ribbon area / inside Explorer bottom), accent colour, Light/Dark theme. |
| **Sorted Documents profile** | ⚡ Organize can file documents into your own tree — `Sorted Documents\Sorted Pdfs`, `Sorted Word Files`, `Sorted Excel Files`, `Sorted Powerpoint files`, `Sorted Text`, `PSDs`, `Illustrator Files`, `Html Files`, `Affinity designer` — while images/videos keep the standard categories. Choose Standard vs Sorted Documents in Settings. |
| **Explorer integration (§2)** | Only supported Windows mechanisms: classic context menu for **files and folders** (HKCU, shows under *Show more options* on Windows 11), **Send To** shortcuts, and *Add as Organizer Destination* on folders. Install from Settings or `scripts/Install-ContextMenu.ps1`. |
| **⚡ Auto Organize (§3)** | Scans a folder and files everything by extension: `PDF → Documents\PDF`, `JPG/PNG → Images`, `MP4 → Videos`, `ZIP → Archives`, … The map is fully editable in the config. Move or Copy. |
| **Custom rules (§4)** | `IF filename contains "CV" THEN Jobs\CV`, extension + name + size + date conditions, AND/OR logic, priorities. Rules run before the extension map. |
| **Smart date organization (§5)** | `Jobs\2026\October\…` by created / modified / filename date, optionally with the category folder appended. |
| **Preview before organizing (§10)** | Every bulk run shows a checkable preview (file → destination + *why*). Nothing moves until you click **Organize**. |
| **Batch rename (§7)** | For files and folders: presets (Capitalize Words, Make Web-safe, Number Files, Time-Stamp Names, Underscores to Spaces, Unique Number) plus your own saved presets; ✎ Simple Rename for a single item; Add date (`Poster_2026-10-03.jpg`, 4 date formats, beginning/end), numbering (`Photo_001`), Title Case / UPPER / lower / Capitalize, replace / remove / prefix / suffix. Case-only renames work correctly on Windows. Also on the Quick Bar (✏). |
| **Duplicate finder (§8)** | Exact duplicates by size + SHA-256. Keep First / Newest / Oldest, then move the rest to a folder or delete them — deletion always asks first. |
| **Fast search (§9)** | By name, extension, size and date, across the selected folder tree. |
| **Undo (§11)** | Every move / copy / rename is logged (`history.json`). **↶ Undo Last Action** reverses the last batch; copies are undone by deleting the copy. |
| **Safety (§15)** | Destinations are created on demand, files are **never silently overwritten** (Skip / Auto-rename / Replace-only-after-confirmation), cross-volume moves fall back to copy+delete, long operations can be cancelled from the preview step. |
| **Config (§16)** | Everything local: `%AppData%\FileOrganizer\config.json` (+ `history.json`). Export / Import from Settings. Works **completely offline** (§14). |

## Quick start

### Option A — installer (recommended)

1. Download `File Organizer Setup.exe` from the GitHub Actions artifact **file-organizer-setup** (or build it, see below).
2. Run it. It installs per-user (no admin), with optional desktop shortcut, context menu and start-with-Windows.
3. Open File Organizer once → **Settings → Install / Refresh Context Menu** so the right-click entries match your destinations.

### Option B — portable exe

1. Download the **fileorganizer-win-x64** artifact and unzip.
2. Run `FileOrganizer.exe`. It's self-contained — no .NET install needed.
3. Same Settings step for the context menu.

### First run

The app seeds these destinations under your Documents folder so the buttons work immediately:

```
📁 Afzal E Services
    ├── 💼 Jobs
    ├── 📢 Advertisements
    └── 🏫 School
📄 Documents     🔒 Personal
```

Right-click any destination button to rename it, recolour it, add sub-destinations, or point it at your real folders. **+ Add Destination** saves any folder as a new button.

## Command line (used by the context menu — handy for scripts too)

```bat
FileOrganizer.exe --move-to <destinationId-or-Name> <file1> [file2 ...]
FileOrganizer.exe --add-destination <folder>
FileOrganizer.exe --organize <folder>
```

## Building from source

Requirements: .NET 8 SDK on Windows (the WPF UI only *runs* on Windows; the Core library and its tests build and run anywhere).

```bat
:: run the core test suite (24 checks: move/undo, organize preview+execute, rules,
:: rename, duplicates, search, config round-trip)
dotnet run --project tests\FileOrganizer.Core.Tests -c Release

:: build everything
dotnet build FileOrganizer.sln -c Release

:: portable single-file exe
dotnet publish src\FileOrganizer.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish

:: installer (needs Inno Setup 6)
installer\build-setup.bat
```

Every push to GitHub runs the same steps in `.github/workflows/build.yml` and uploads two artifacts: **fileorganizer-win-x64** (portable zip) and **file-organizer-setup** (`File Organizer Setup.exe`).

## Project layout

```
src/FileOrganizer.Core/    Platform-agnostic engine (net8.0, zero external packages)
  Models.cs                  Destination, AppConfig, rules, rename options, preview rows
  ConfigService.cs           Local JSON config + export/import        (§16)
  HistoryService.cs          Operation log that powers Undo         (§11)
  FileOperationService.cs    Safe move/copy + Undo                  (§1, §15)
  OrganizeEngine.cs          ⚡ ORGANIZE preview builder             (§3, §5, §10)
  RulesEngine.cs             Custom rules + filename-date parsing   (§4, §5)
  RenameEngine.cs            Batch rename                           (§7)
  DuplicateFinder.cs         SHA-256 duplicate groups               (§8)
  SearchService.cs           Fast local search                      (§9)
src/FileOrganizer.App/     WPF UI (net8.0-windows)
  MainWindow                 Quick destinations + organize/search   (§13)
  QuickBarWindow             Floating Quick Destination Bar         (§6)
  PreviewWindow / RenameWindow / RulesWindow / DuplicatesWindow / SettingsWindow
  ExplorerSelection.cs       Reads Explorer's current selection (Shell automation)
  ContextMenuService.cs      Registry + Send To registration        (§2)
tests/FileOrganizer.Core.Tests/  Dependency-free console test runner
scripts/                   Install/Uninstall-ContextMenu.ps1        (§2)
installer/                 Inno Setup script + build-setup.bat      (§17)
```

## Deliberate scope notes

- **Why not a native Explorer toolbar/tab?** Modern Windows Explorer offers no supported API for third-party toolbars/tabs. The closest *reliable* integrations are the context menu and Send To, which is exactly what's implemented (§2 fallback strategy) — nothing patches or hooks Explorer internals.
- **Why per-user (HKCU) registration?** No admin rights needed, the installer stays lightweight, and uninstall is clean.
- **Where are the files stored?** Config/history live in `%AppData%\FileOrganizer\`. Nothing is uploaded anywhere; disable nothing, because there's nothing to disable — there's no network code at all.

## License

MIT — see `LICENSE` if present, or treat this repository as MIT-licensed.
