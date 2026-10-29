# Project Guide

## Project

- Native rhythm game built with Godot 4.7.2 .NET and C#; this is not a browser game.
- `Rhythia.csproj` targets .NET 10. Use the project-local SDK and Godot installation when available.
- Follow existing C# and Godot scene patterns. Keep changes focused, preserve current behavior, and update related documentation.
- This is currently a single-player game. Do not add multiplayer, extra player windows, or device-routing behavior unless explicitly requested.

## Architecture

- `scripts/scenes/` contains scene-level flows such as the main menu, gameplay, and results.
- `scripts/game/Runner.cs` advances attempts, processes notes, and records results. `scripts/game/Attempt.cs` holds per-run state.
- `scripts/game/managers/ReplayManager.cs` handles recorded gameplay playback. Idle attract replays are started from `scripts/scenes/MainMenu.cs`, return to the attract screen when complete, and can be exited with input; they must not consume arcade credits or create player results.
- `scripts/map/` handles chart parsing, map data, selection, and cache management.
- `scripts/map/Leaderboard.cs` stores local score records. Use its read-only `TryRead` path for attract/display features rather than the constructor that may reset and save a damaged leaderboard.
- `scripts/CampaignManager.cs` builds a campaign from installed charts and saves local progression. Campaign map selection and UI live in `scripts/scenes/MainMenu.cs`.
- `scripts/ArcadeService.cs` provides the localhost credit/free-play API. The browser controller is only a control panel; gameplay remains native.
- After 60 seconds without input, idle attract mode immediately plays a randomly selected qualifying local top-score replay when one is available. Without playable replays it displays and rotates local scores. Attract playback is credit-free and keyboard, mouse, or touch input returns to the menu. See `ARCADE_MODE.md` for arcade API, credit, campaign, and attract behavior.
- User data, including the arcade API token and campaign progress, belongs in the Rhythia user folder, not in the repository.

## Build and Run (Windows)

From the project directory:

```powershell
$env:DOTNET_ROOT = (Resolve-Path .dotnet).Path
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
.\.dotnet\dotnet.exe build Rhythia.csproj --no-restore -c Debug
.\.dotnet\dotnet.exe build Rhythia.csproj --no-restore -c Release
```

Launch the desktop client with `.\Play Rhythia.bat`. Run `git diff --check` after edits. No automated test suite is currently documented; use the smallest applicable build or runtime check.

## Safety and Data

- Never read, print, copy, or commit `arcade-api.token` or other credentials. Do not include tokens in logs, documentation, or tool output.
- Preserve user charts, settings, skins, campaign progress, and other user data. Do not delete or overwrite them as part of routine development.
- Do not bundle third-party charts, audio, or video without permission; campaign stages use charts installed by the player.
