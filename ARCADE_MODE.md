# Arcade mode

The arcade integration runs inside the native Godot client. It does not use a browser or a payment processor: a cabinet controller reports accepted payments to the localhost API, and the client turns those reports into shared play credits.

## Packaging

Run `.\package.ps1` in PowerShell to build and export the configured **Windows Desktop** preset to `build\packages\windows.zip`. The script uses the project's bundled Godot .NET executable at `.godot-sdk\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe`; the matching Windows export templates and an export preset named `Windows Desktop` in `export_presets.cfg` are also required. Use `-Preset`, `-ExportFileName`, or `-ArchiveName` to change the preset, executable name, or ZIP name.

The package includes `rhythia-free-play-default.txt` beside the executable with the exact contents `true` (no newline). When this file contains exactly `true`, free play is forced on for that session, even if saved arcade state disables it. The override is not saved to `arcade-state.json`; removing or changing the marker takes effect on the next launch. Running the project from the editor does not use the packaged default.

## Local API

The client listens on `127.0.0.1:8765` by default. It generates a 256-bit bearer token in `arcade-api.token` inside the Rhythia user folder on first launch. Keep that file private. Requests from other computers are not accepted; run the cabinet controller on the game machine or use a local bridge.

Open `http://127.0.0.1:8765/controller` in a browser while the game is running to use the browser control panel. Enter the token from `%APPDATA%\Rhythia\arcade-api.token` to connect. The panel is served by the game from the same localhost origin, so it does not require browser CORS access. It can add credits and toggle free play; use the JSON API below for automatic payment integration. The browser panel refreshes status every three seconds. Tokens are kept only for the current tab unless “Remember token in this browser” is selected.

All routes require `Authorization: Bearer <token>` and JSON request bodies. The API does not enable browser CORS.

| Method | Route | JSON body | Result |
| --- | --- | --- | --- |
| `POST` | `/api/v1/credits` | `{"amount":1,"transactionId":"payment-123"}` | Adds 1–100 shared credits; each normal map start consumes one. `transactionId` is optional and deduplicates payment retries across restarts. |
| `PUT` | `/api/v1/free-play` | `{"enabled":true}` | Enables or disables free play. Free-play starts do not consume credits. |
| `GET` | `/api/v1/status` | — | Returns the current credits and free-play state. |

PowerShell example (replace the user-folder path if it has been customized):

```powershell
$token = (Get-Content "$env:APPDATA\Rhythia\arcade-api.token" -Raw).Trim()
$headers = @{ Authorization = "Bearer $token" }
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:8765/api/v1/credits `
  -Headers $headers -ContentType "application/json" -Body '{"amount":1}'
Invoke-RestMethod -Method Put -Uri http://127.0.0.1:8765/api/v1/free-play `
  -Headers $headers -ContentType "application/json" -Body '{"enabled":true}'
Invoke-RestMethod -Method Get -Uri http://127.0.0.1:8765/api/v1/status -Headers $headers
```

Credits and free-play state are saved in `arcade-state.json` in the Rhythia user folder. Credit additions are acknowledged only after they are saved; credit spending is also saved before a map starts. The most recent 4,096 payment transaction IDs are saved with the balance, so retries across restarts do not add those payments twice. Set `RHYTHIA_ARCADE_PORT` before launch to use a different API port.
Setting a new qualifying top score on a map awards one additional credit. Only full-length, non-autoplay runs that qualify for the leaderboard can earn this reward; replay, practice-start, and failed runs do not.

## Campaign

Choose **CAMPAIGN** from the home screen or top navigation to play *The Signal Run*, a 12-stage progression assembled from installed charts. The first campaign is curated from the available maps in ascending difficulty; each stage uses standard speed and no modifiers. Clear a full stage without failing, skipping, or using autoplay to unlock the next. Campaign progress is saved in `campaign-progress.json` in the Rhythia user folder. Starting a campaign stage uses the normal credit/free-play rules.

## Idle attract display

After 60 seconds without input, the attract display immediately plays a randomly selected qualifying local top score with a saved replay. When playback finishes, it selects another record and repeats. Keyboard, mouse, or touch input exits the replay to the menu. If no playable replay exists, the display shows local top scores and rotates records every eight seconds. Attract replays do not consume credits. To add a background video, place an Ogg Theora `.ogv` file at `arcade/attract.ogv` inside the Rhythia user folder, or set `RHYTHIA_ATTRACT_VIDEO` to an absolute `.ogv` path before launch. The idle high-score display works without a video. The video is optional; no third-party clip is bundled.
