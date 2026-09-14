# Minecraft Desktop

A personal Windows 11 project that turns the desktop into a walkable, cozy voxel cave. Chests organize links to real files, a Minecraft-style dock opens applications, and an in-world desk brings notes, pictures, video, and a Codex agent into the room.

**Portfolio source preview.** Source code and original generated assets are included. Minecraft artwork, models, sounds, music, personal files, saved worlds, credentials, and packaged executables are excluded. A fresh clone cannot run the complete cave without a compatible resource pack. See [ASSETS.md](ASSETS.md).

This is an unofficial fan project, not affiliated with or endorsed by Mojang or Microsoft. The internal project and save-directory name remains **CozyCave** so existing installations keep their saved worlds.

## Features

- Walk, build, collect blocks, sit, and arrange a furnished cave overlooking a rainy valley.
- Organize desktop file links in named chests with Windows icons, thumbnails, folder navigation, and pagination.
- Pack and move a chest while preserving its contents. Empty chests become file or game-item chests according to their contents.
- Toggle between building slots and an application dock with grouped window previews and window controls.
- Keep persistent notes in a book, display local pictures in frames, and interact with YouTube on an in-world screen.
- Give a villager tasks through a dedicated Codex workstation, with a live monitor and optional local speech recognition.
- Save terrain edits, furniture, links, notes, inventory, and player state locally, with atomic writes and recovery backups.

File organization changes saved links rather than moving or deleting the original files. Opening a linked file hands it to its normal Windows application. The optional Codex agent is separate: when used, it can run commands and edit real files with the user's desktop access.

## Architecture

| Component | Responsibility |
| --- | --- |
| `Game/` | Godot C# world, interaction, inventory, audio, and in-world interfaces |
| `src/Cave.Desktop/` | Windows desktop embedding, taskbar integration, thumbnails, WebView2 video, and Codex workstation |
| `src/Cave.Core/` | File links, chest storage, persistence, placement, and water simulation |
| `Shared/` | Local IPC and shared video-frame transport |
| `tests/` | Core behavior and transport checks using temporary fixtures |

The Windows helper restores desktop icon visibility after exit or renderer failure. Desktop attachment uses Windows shell behavior and is experimental. The renderer can also run in a diagnostic window.

The villager uses `codex app-server` and a dedicated native workstation window, rather than mirroring an existing Codex conversation. It requires a separately installed, authenticated Codex CLI. Desktop automation supports screenshots, clicks, scrolling, text, and keys. Speech requires a compatible installed Windows recognition engine and microphone. Video uses Microsoft Edge WebView2. Those optional services may connect to the internet; saves and helper IPC are local.

## Development

Windows 11 x64 is the only target. Tool versions are pinned to Godot .NET 4.5.2 and .NET SDK 8.0.414. PowerShell bootstrap downloads them into the ignored `.tools/` directory.

Core tests do not need the private resource pack:

```powershell
./scripts/bootstrap.ps1
& ./.tools/dotnet/dotnet.exe run --project tests/Cave.Tests
& ./.tools/dotnet/dotnet.exe run --project tests/Cave.Transport.Tests
```

With a complete compatible resource pack available locally:

```powershell
./scripts/build.ps1 -Test -Package
./scripts/run.ps1 -Windowed
```

The package entry point is `dist/CozyCave/Cave.Desktop.exe`. Packages are not part of this source preview. Generated legacy textures and effects can be recreated with Python and Pillow using `scripts/generate_assets.py`.

## Controls

| Action | Control |
| --- | --- |
| Move / look | WASD / mouse |
| Jump / sprint | Space / Shift |
| Open inventory | E or B |
| Change hotbar slot | Mouse wheel or 1–9 |
| Toggle application and block hotbars | Backtick |
| Place or interact | Right-click |
| Break and collect | Hold left-click |
| Close an open menu | Escape |
| Release walking capture | Hold Escape |
| Speak to a nearby targeted villager | Hold V, then review before sending |

## Persistence and limitations

Local state lives under `%LOCALAPPDATA%/CozyCave/`, including `state.json` and the separate villager session record. These files can contain personal paths, notes, and conversation text and must not be committed. This repository does not include the developer's desktop contents, world, or account configuration.

This is an evolving personal prototype, not a complete Minecraft engine or a production Windows shell. Block interactions cover a subset of Minecraft behavior. Multi-monitor desktop recovery, native focus changes, speech, recording, and complex agent workflows need broader testing on other machines. A clean-machine installation and a complete distributable resource pack have not been validated.

## Credits

- [Godot](https://godotengine.org/) and [.NET](https://dotnet.microsoft.com/) power the application.
- Press Start 2P is included under the [SIL Open Font License](Assets/Fonts/OFL.txt).
- Minecraft provided the visual and interaction inspiration. Its proprietary resources are not included.

No general source-code license has been selected yet. Included third-party notices retain their own terms.
