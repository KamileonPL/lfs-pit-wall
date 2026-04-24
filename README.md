# LFS Pit Wall by KamileonPL

LFS Pit Wall is a lightweight live timing and race-monitoring dashboard for [Live for Speed](https://www.lfs.net), powered by InSim and built with ASP.NET Core, SignalR, and a simple HTML/JavaScript frontend.

It is designed to be fast to run, easy to understand, and practical for real sessions: local testing, hosted racing, and future race-history features.

## What It Does

- Live standings for practice, qualifying, and race sessions
- Live track map with driver markers, selection, and synchronized driver legend
- Session best lap, best sectors, and session top speed
- Driver lap history tooltip on demand
- Driver country flags and cached LFS World profile hover cards
- Live race clock with smooth frontend interpolation
- Estimated remaining race time for lap-based races
- Race progress overlay with laps completed and track name
- LFS chat panel fed from InSim message packets
- Multiplayer host name display with LFS color support
- Lightweight web UI with no database requirement

## Tech Stack

- .NET 9 / ASP.NET Core
- SignalR for real-time updates
- Live for Speed InSim over TCP
- Plain HTML, CSS, and JavaScript frontend

## Quick Start

1. Install the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).
2. Start Live for Speed and enable InSim, for example:

```txt
/insim 29999
```

3. Configure the target host in [LfsPitWall.Server/appsettings.json](LfsPitWall.Server/appsettings.json):

```json
{
    "InSim": {
        "Host": "127.0.0.1",
        "Port": 29999,
        "AdminPassword": "YourPasswordHere"
    },
    // ...
    "PlayerOnboarding": {
        "PublicUrl": "YourWebsiteUrlHere"
    },
    // ...
    "Pubstat": {
        "IdentKey": "YourIdentKeyHere",
    }
}
```

4. Run the server:

```bash
dotnet run --project "LfsPitWall.Server/LfsPitWall.Server.csproj"
```

5. Open the local dashboard in your browser.

## Current Focus

`v0.3` focuses on a stronger live race-day dashboard plus a lightweight archive browser:

- real-time session visibility with a lightweight web UI
- a live track map that stays readable during racing, spectating, and pit activity
- lower frontend overhead through cached map geometry and paused hidden-map rendering
- a clean in-memory session model that is ready for future history and persistence features

## New In v0.3

- Live standings can be toggled into a live track map view
- Driver legend is compact, scrollable, and synchronized with visible map drivers
- Map rendering ignores stale telemetry, pit-lane noise, and race-start grid distortion
- Same-track race restarts preserve collected map geometry instead of rebuilding from scratch
- Version metadata is surfaced consistently through the app footer and app metadata endpoint
- Archived sessions can be browsed through a JS-first archive viewer with official LFS results when available
- Archive indexing reuses cached file summaries to reduce repeated JSON parsing and disk I/O

## Project Structure

- [LfsPitWall.Server](LfsPitWall.Server) - ASP.NET Core app, InSim client, SignalR hub, session model
- [LfsPitWall.Server/wwwroot](LfsPitWall.Server/wwwroot) - frontend dashboard
- [Docs](Docs) - reference documents for InSim and related protocol notes

<details>
<summary><strong>Local Development</strong></summary>

### Recommended Flow

Use the standard project run command:

```bash
dotnet run --project "LfsPitWall.Server/LfsPitWall.Server.csproj"
```

### Notes

- `appsettings.json` is the main runtime config.
- `appsettings.Development.json` is optional and can stay empty if you do not need environment-specific overrides.
- The current development flow relies on InSim only. OutSim and OutGauge are not required for the existing dashboard.

### Driver Profiles And Flags

The live timing table can enrich drivers with country flags and a small hover profile sourced from LFS World Pubstat.

The implementation is designed for cheap VPS hosting:

- profile data is cached on disk in `LfsPitWall.Server/data/drivers`
- only lightweight summary data is included in the live SignalR session payload
- detailed hover data is loaded on demand
- cached entries are refreshed in the background with a controlled request interval

Configure Pubstat in [LfsPitWall.Server/appsettings.json](LfsPitWall.Server/appsettings.json):

```json
{
    "Pubstat": {
        "Enabled": true,
        "IdentKey": "YOUR_PUBSTAT_IDENT_KEY",
        "UsePremiumEndpoint": false,
        "PubstatUrl": "https://www.lfsworld.net/pubstat/get_stat2.php?version=1.5",
        "CacheRootPath": "data/drivers",
        "StaleAfterDays": 7,
        "RequestIntervalSeconds": 6
    }
}
```

Notes:

- `IdentKey` stays server-side only and is never exposed to the frontend
- with the free Pubstat tier, keep `RequestIntervalSeconds` at `5` or more
- if `IdentKey` is empty, the app keeps working normally, but driver profile enrichment stays disabled

</details>

<details>
<summary><strong>Remote Server / Hosted LFS Setup</strong></summary>

For a remote LFS host, point [LfsPitWall.Server/appsettings.json](LfsPitWall.Server/appsettings.json) at the target server and InSim port.

Example:

```json
{
    "InSim": {
        "Host": "YOUR_LFS_HOST",
        "Port": 29999,
        "Name": "LFS Pit Wall",
        "AdminPassword": "YOUR_ADMIN_PASSWORD"
    }
}
```

Important:

- The remote LFS server must have InSim enabled.
- If the host uses IP allow-listing for InSim, your application machine must be whitelisted.
- If an admin password is configured on the host, the same password must be provided here.

</details>

<details>
<summary><strong>Production Publish</strong></summary>

### GitHub Actions Publish

The repository includes a minimal GitHub Actions workflow at `.github/workflows/publish.yml`.

It runs only when started manually from the GitHub `Actions` tab and publishes self-contained production binaries for:

- `win-x64`
- `linux-x64`
- `linux-arm64`

Each target is uploaded as a separate GitHub Actions artifact.

### GitHub Release For Users

The repository also includes `.github/workflows/release.yml`.

It runs only when you push a version tag such as `v0.3.1`.

That workflow:

- publishes the same three self-contained builds
- packs them into ZIP files
- creates a GitHub Release
- attaches the ZIP files to that Release for easy user downloads

### First Release Step By Step

If you have never done this before, use the following flow.

1. Make your code changes.
2. Save all files.
3. Commit and push your changes to `main`.
4. Create a version tag.
5. Push that tag to GitHub.

Example for version `v0.3.1`:

```bash
git add .
git commit -m "Release v0.3.1"
git push origin main
git tag v0.3.1
git push origin v0.3.1
```

What each command does:

- `git add .` prepares your changed files for commit
- `git commit -m "Release v0.3.1"` creates a commit in your local repository
- `git push origin main` sends that commit to GitHub
- `git tag v0.3.1` marks the current commit as version `v0.3.1`
- `git push origin v0.3.1` sends the tag to GitHub and runs the release workflow

Important:

- use a new version number each time, for example `v0.3.2`, `v0.3.3`, and so on
- the tag should usually match the version you want users to download
- after pushing the tag, the new release will appear in the GitHub `Releases` section

### Normal Push vs Release

- manual run of `publish.yml` gives you GitHub Actions artifacts
- `git push origin v0.3.1` gives you a public GitHub Release with ZIP files

### Standard Publish

Windows:

```bash
dotnet publish -c Release -r win-x64 --self-contained
```

Linux:

```bash
dotnet publish -c Release -r linux-x64 --self-contained
```

### Native AOT

The project file currently keeps Native AOT optional in [LfsPitWall.Server/LfsPitWall.Server.csproj](LfsPitWall.Server/LfsPitWall.Server.csproj).

If you want to test AOT publishing, enable:

```xml
<PublishAot>true</PublishAot>
```

Only do that when you actually want an AOT build and have the required native toolchain installed.

</details>

## Documentation

Useful reference files in [Docs](Docs):

- [Docs/InSim.txt](Docs/InSim.txt) - main InSim protocol reference
- [Docs/Commands.txt](Docs/Commands.txt) - LFS text commands
- [Docs/ColorCodes.txt](Docs/ColorCodes.txt) - LFS text formatting and color codes

## Contributing

Contributions are welcome.

- Open an issue if something is broken or unclear
- Open a pull request if you want to improve the app
- AI-assisted changes are fine, but reviewable PR quality still matters

## Status

Current project version: `v0.3`

The project already works as a solid live timing dashboard with a usable live map, and is being extended carefully toward a broader pit-wall style tool.