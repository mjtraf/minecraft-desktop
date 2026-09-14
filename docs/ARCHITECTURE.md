# How it works

Godot renders the world. A C# Windows helper connects it to files, applications, browser video, and Codex. They communicate locally, with separate channels for commands and screen frames.

| Location | Role |
| --- | --- |
| `Game/` | World rendering, movement, building, inventory, and interaction |
| `src/Cave.Desktop/` | Windows integration, browser host, and agent workstation |
| `src/Cave.Core/` | File links, saved state, placement, and water simulation |
| `Shared/` | IPC and frame transport |
| `tests/` | File, persistence, simulation, and transport checks |

## Villager agent

A villager represents a persistent Codex session. You can type a task or hold V and address a villager by name from anywhere in the cave to dictate, then review the text before sending. While the agent works, the villager sits at its computer. Click the computer to zoom in and operate the same transcript and controls inside the world.

The [session client](../src/Cave.Desktop/VillagerSession.cs) communicates with `codex app-server` over JSON-RPC on standard input/output. It resumes threads, streams responses, tracks task status, and interrupts turns. The [workstation](../src/Cave.Desktop/VillagerWorkstation.cs) handles input, approval requests, speech recognition, and transcript persistence.

```mermaid
flowchart LR
    Cave[Villager and monitor] <-->|Local IPC and frames| Workstation[Windows workstation]
    Workstation <-->|JSON-RPC| Codex[Codex app-server]
    Codex --> Tools[Files, commands, desktop tools]
```

The [desktop adapter](../src/Cave.Desktop/AgentDesktop.cs) exposes screenshots, clicks, scrolling, text, and keys. These operate on the real desktop with the user's access. Each workstation owns a separate Codex session and requires a separately authenticated CLI. Agent sessions can run concurrently, but desktop-control actions still operate on the same physical Windows session.

## Web TV

The TV is a live [WebView2 browser](../src/Cave.Desktop/CaveTv.cs). Captured frames pass through [shared frame transport](../Shared/TvFrameBuffer.cs) to a Godot texture. The [TV controller](../Game/Television.cs) maps input on the 3D screen back to the browser, including clicks, scrolling, and typing.

Browser rendering and game rendering run separately. The player supports navigation, mute, volume, power, and a larger viewing window. Frame rate depends on browser state and the host machine.

## Desktop and input

The [Windows helper](../src/Cave.Desktop/Program.cs) attaches the cave to the desktop, manages application windows, and restores desktop icons on exit. A watchdog handles helper failure.

Movement uses a captured pointer; menus, web content, and Windows need a free pointer. Focus transitions explicitly release capture and reset held inputs to prevent stuck movement or repeated clicks. The app dock includes grouped window previews and close controls.

## Files and saves

Chest contents are links managed by the [file service](../src/Cave.Core/FileService.cs). Moving a link between chests leaves its target in place. A packed chest keeps its identity and contents when placed again.

The [state store](../src/Cave.Core/StateStore.cs) writes versioned records atomically and keeps recovery backups. A session lock and stale-write checks protect against two instances overwriting the same world. Notes, placed objects, terrain edits, and inventory persist alongside file organization.

## Tests and current limits

[GitHub Actions](https://github.com/mjtraf/minecraft-desktop/actions) runs 58 core checks and three transport checks on Windows. These cover source-file integrity, discovery, duplicate links, save recovery, placement, water flow, message ordering, and stalled peers.

Additional in-app checks require the resource pack and an interactive desktop. Desktop attachment, microphone input, recording, and multi-monitor behavior need broader testing across PCs. Block interactions cover a subset of Minecraft behavior.

## Agent identity and routing

Computer blocks retain an agent ID through inventory, drops, and relocation. Fresh adjacent screens inherit one neighbour’s ID; existing agents never merge. Version 8 world saves contain names and approach preferences. The original agent retains `villager-agent.json`; new agents store separate histories and session locks under `agents/<id>/`.

Every workstation input, status event, and frame channel is addressed by agent ID. Native controls remain off-screen and are operated through the in-world display. Voice recording is push-to-talk; a leading name selects the recipient, followed by explicit text review. Villager questions and completion gestures do not activate application windows.
