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
| Dictate to a nearby targeted villager | Hold V |

## Screens

The workbench provides TV and computer variants of black concrete. A short click zooms into the display; holding left-click while walking mines the block. Escape leaves screen interaction. Adjacent blocks with the same source and facing join into a larger screen. TV blocks share one player; computer blocks share one workstation session. Breaking a computer does not interrupt its agent task.

Inside the workstation, click to select controls, type or paste text, drag to select, and scroll the transcript. Project paths and agent questions open within the same screen. Sign-in and Open folder intentionally use their normal Windows applications.

## Local state

Saves live in `%LOCALAPPDATA%/CozyCave/`. Internal identifiers and paths retain the original name for save compatibility. This directory includes file paths, notes, and agent transcripts and is not part of the repository.
