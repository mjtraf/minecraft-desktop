# Development

Windows 11 x64, Godot .NET 4.5.2, and .NET SDK 8.0.414. The bootstrap script downloads the pinned tools into `.tools/`.

The complete resource pack used in the screenshots is not included. See [Assets](../ASSETS.md) before running the renderer. Core and transport tests work without it.

## Tests

```powershell
./scripts/bootstrap.ps1
& ./.tools/dotnet/dotnet.exe run --project tests/Cave.Tests
& ./.tools/dotnet/dotnet.exe run --project tests/Cave.Transport.Tests
```

## Build and run

With a compatible resource pack in place:

```powershell
./scripts/build.ps1 -Test -Package
./scripts/run.ps1 -Windowed
```

The packaged launcher is `dist/CozyCave/Cave.Desktop.exe`. The `scripts/` directory contains tool setup, build, launch, and procedural asset generation. Asset generation requires Python and Pillow.

YouTube uses Microsoft Edge WebView2. Villager tasks require an installed, authenticated Codex CLI. Voice input requires a microphone and compatible Windows speech recognition engine.

## Controls

| Action | Control |
| --- | --- |
| Move / look | WASD / mouse |
| Jump / sprint | Space / Shift |
| Inventory | E or B |
| Select slot | Mouse wheel or 1–9 |
| Toggle app and block hotbars | Backtick |
| Place or interact | Right-click |
| Break and collect | Hold left-click |
| Close menu | Escape |
| Release walking capture | Hold Escape |
| Dictate to a villager: aim nearby, or say its name from anywhere in the cave | Hold V; release to review |

## Screens

The workbench provides TV and computer variants of black concrete. A short click zooms into the display; holding left-click while walking mines the block. Escape leaves screen interaction. Adjacent blocks with the same source and facing join into a larger screen. TV blocks share one player. A separate computer creates an independent villager; a fresh computer block placed adjacent to an existing screen enlarges that workstation. Moving established computers together does not merge their agents. Breaking a computer does not interrupt its agent task.

On first use, choose a unique villager name and project folder. Click the name button later to rename it or change whether it approaches for questions. Your original workstation keeps its saved conversation.

Villagers gesture quietly when finished. If enabled, a villager needing your answer walks nearby only while you are exploring; it stops short and never opens a panel automatically. A chair is added when there is clear floor space beside a new workstation. You can move or remove it like any other stair block.

Inside the workstation, click to select controls, type or paste text, drag to select, and scroll the transcript. Project paths and agent questions open within the same screen. Sign-in and Open folder intentionally use their normal Windows applications.

## Local state

Saves live in `%LOCALAPPDATA%/CozyCave/`. Internal identifiers and paths retain the original name for save compatibility. This directory includes file paths, notes, and agent transcripts and is not part of the repository.

Voice is push-to-talk in the cave, not an always-listening wake word or a connection to an external voice chat. Say “Alex, research…” while holding V. The review shows the recipient and text; unrecognized names require choosing a villager. Microphone recognition quality depends on Windows and needs testing with your voice.
