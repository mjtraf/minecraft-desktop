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
| Project board | P, or right-click a Project Board bookshelf |
| Dictate to a villager: aim nearby, or say its name from anywhere in the cave | Hold V; release to review |

## Screens

The workbench provides TV and computer variants of black concrete. A short click zooms into the display; holding left-click while walking mines the block. Escape leaves screen interaction. Adjacent blocks with the same source and facing join into a larger screen. TV blocks share one player. A separate computer creates an independent villager; a fresh computer block placed adjacent to an existing screen enlarges that workstation. Moving established computers together does not merge their agents. Breaking a computer does not interrupt its agent task.

On first use, choose a unique villager name and project folder. Click the villager itself to rename it, or use the name button inside its workstation to change its name and whether it approaches for questions. Your original workstation keeps its saved conversation.

Villagers gesture quietly when finished. If enabled, a villager needing your answer walks nearby only while you are exploring; it stops short and never opens a panel automatically. A chair is added when there is clear floor space beside a new workstation. You can move or remove it like any other stair block.

Inside the workstation, click to select controls, type or paste text, drag to select, and scroll the transcript. Project paths and agent questions open within the same screen. Sign-in and Open folder intentionally use their normal Windows applications.

## Local state

Saves live in `%LOCALAPPDATA%/CozyCave/`. Internal identifiers and paths retain the original name for save compatibility. This directory includes file paths, notes, and agent transcripts and is not part of the repository.

Voice is push-to-talk in the cave, not an always-listening wake word or a connection to an external voice chat. Say “Alex, research…” while holding V. The review shows the recipient and text; unrecognized names require choosing a villager. Microphone recognition quality depends on Windows and needs testing with your voice.

## Project studio

Press P to open the board. Minecraft Desktop is the first project; when running from a repository checkout, its folder is found automatically. Choose **Goal & team**, set a concrete milestone, select only the villagers you want involved, and choose the lead. Separate project conversations preserve each villager's personal history.

**Ask lead for a plan** inspects the project in read-only mode. Review the assignments, then choose **Start team**. In this version, teammates run one at a time in the shared project folder, pass their results to dependent assignments, and finish with a read-only lead review. Native subagent spawning is disabled for these tasks. Only assigned villagers receive work.

Use **Send direction** to clarify the active assignment. **View workstation** opens that villager's actual screen, including questions requiring your response. **Stop team** waits for the current turn to stop. Resuming skips completed assignments; inspect any partial changes before retrying an interrupted one. After reviewing the outputs, use **Mark complete**. Editing the goal, folder, team or milestone archives the old plan in the journal.

The journal stores notes, milestones, handoffs and results. Outputs are links to existing files, opened only when clicked. The workbench supplies a placeable Project Board bookshelf; one also starts in your building inventory. All boards show the same project studio. Collecting a board does not delete a project or stop its work.

Projects save to `projects.json` with an atomic backup. An interrupted session reopens paused instead of restarting work automatically. Project turns can use your signed-in Codex account; no task runs just because you opened a board. They can modify the selected project when you start the team, including Minecraft Desktop if that is the assigned project. Live UI behavior still needs broader testing across machines.
