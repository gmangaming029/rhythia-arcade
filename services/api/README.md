# Local repository API

The API is a local-development scaffold. It stores package ZIP files under `repository-data/packages/` and mutable metadata in SQLite. Package metadata is persisted only after automated ZIP/manifest checks. Ordinary valid packages publish as unranked; ranked nomination and review are a separate process.

## Run

From the repository root:

```powershell
dotnet run --project services/api/Rhythia.Repository.Api.csproj --launch-profile local
```

The service listens at `http://localhost:5080`. Configure an OIDC-compatible issuer and audience before using protected endpoints:

```powershell
$env:Authentication__Authority = "https://your-issuer.example/"
$env:Authentication__Audience = "your-api-audience"
```

Do not expose this development listener to the public internet. `localhost` is machine-local, and a static website hosted elsewhere cannot reach it. Worldwide access requires deploying the API and package origin to a stable public host with HTTPS, DNS, authentication, persistent storage, capacity planning, and tested backup/restore.

## Package envelope

An upload is a ZIP containing a root `manifest.json` and a data file at the manifest's `chartDataPath`. The archive may include audio, images, and other associated files. The API limits compressed size, entry count, and total expanded size and rejects unsafe paths. The current manifest envelope does not replace or redefine Rhythia's native chart format; native compatibility and import behavior must be confirmed before in-game installation is enabled.

`manifest.json` fields: `title`, `creator`, `description`, `difficulty`, `tags`, `formatVersion`, `chartDataPath`, optional `coverPath`. Example:

```json
{
  "title": "Example",
  "creator": "Player",
  "description": "",
  "difficulty": "Advanced",
  "tags": ["Community"],
  "formatVersion": "1",
  "chartDataPath": "chart/example.phxm",
  "coverPath": "assets/cover.png"
}
```

`GET /v1/health` is public. All catalog, download, upload, nomination, review, and player endpoints require a valid bearer token. Reviewer actions additionally require the `reviewer` or `maintainer` role. Authentication provider registration, role provisioning, token claims, and user-facing sign-in are deployment configuration—not development credentials embedded in this repository.

The local API applies a fixed-window limit of 120 requests per minute and a separate limit of 6 upload sessions/packages per 10 minutes. Rejection uses HTTP 429. Package receiving is additionally capped by `Repository:MaxPackageBytes`.

## In-game sign-in

The game client uses OAuth authorization-code flow with PKCE in the system browser and a temporary loopback callback. Configure these client-side environment variables before launching Rhythia:

```powershell
$env:RHYTHIA_REPOSITORY_API = "http://localhost:5080"
$env:RHYTHIA_REPOSITORY_OIDC_AUTHORITY = "https://your-issuer.example/"
$env:RHYTHIA_REPOSITORY_OIDC_CLIENT_ID = "your-public-desktop-client-id"
$env:RHYTHIA_REPOSITORY_OIDC_SCOPE = "openid profile your-api-scope"
```

Register the game as a public/native client with authorization-code + PKCE enabled and loopback redirect URIs allowed. By default the client selects an available `127.0.0.1` port for its callback; set `RHYTHIA_REPOSITORY_OIDC_CALLBACK_PORT` to a fixed available port if the identity provider requires an exact callback. The API's `Authentication:Authority` and `Authentication:Audience` must accept the access token issued for the requested API scope. The access token stays in memory and is not persisted by the game. Do not configure a client secret in the game client.

The in-game Online Charts panel is opt-in and off by default. Enable **Online Chart Repository** in Settings → Other to show the button in the Play menu. Turning it off hides the repository window; the normal local chart browser and gameplay remain unchanged. When enabled, it searches by title/tags, filters by creator, difficulty, and tags, sorts by update time or popularity, and paginates results. It downloads packages with SHA-256 verification and imports packages whose manifest points to a native `.phxm` chart. Other chart formats remain downloadable from the website but are not installed into the game.

Ranked result submission deliberately responds with `503` until an approved versioned scoring algorithm is configured. Do not treat community availability or a client-submitted score as ranked approval or RP.
