# **LFS Pit Wall** by Kamileon

Lightweight Live Timing server for [Live for Speed](https://www.lfs.net) racing simulator.

Key Features:
- Live Timing for Practice/Qual/Race with friendly HTML Frontend.

---

### 🤝 Contributing & Open Source
This project is **fully open-source** and I'm more than happy to see the community involved! Also AI-based code is welcome, but always PR-based to let me review the quality and complience.

Feel free to **open a Pull Request** or report an issue. Let's build the best LFS telemetry tool together!

--- 

### 🚀 Production build (e.g. for VPS Server)

For production build with `<PublishAot>true</PublishAot>` please install [C++ Build Tools]() from "Desktop development with C++" package.

Set your target [LFS server config](https://www.lfs.net/hosting/admin) in [appsettings.json](/LfsPitWall.Server/appsettings.json) config file:
```json
"InSim": {
"Host": "127.0.0.1",
"Port": 29999
}
```

Please uncomment `<PublishAot>true</PublishAot>`
in [LfsPitWall.Server.csproj](/LfsPitWall.Server/LfsPitWall.Server.csproj).

#### Option 1: Local Production Build (Windows)
Native AOT compilation on your machine:

```Bash
dotnet publish -c Release -r win-x64 --self-contained
```
Output:
> bin/Release/net9.0/win-x64/publish/LfsPitWall.Server.exe


#### Option 2: Remote Deployment (Linux)
Generate the binary for your Linux server:

```Bash
dotnet publish -c Release -r linux-x64 --self-contained
```
Output:
> bin/Release/net9.0/linux-x64/publish/LfsPitWall.Server

## 🛠️ Development (Local)

### How to build?
1. Install [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0):
    ```
    dotnet --version
    ```
    For development in Visual Studio Code please install `C# Dev Kit dla VS Code` (recommended).

2. Setup LFS ports in your `cfg.txt` config file in `LFS` folder:
    ```yaml
    OutSim Mode 1
    OutSim Delay 1
    OutSim IP 127.0.0.1
    OutSim Port 30001
    OutSim ID 0
    OutSim Opts 1ff
    OutGauge Mode 2
    OutGauge Delay 1
    OutGauge IP 127.0.0.1
    OutGauge Port 30000
    OutGauge ID 0
    ```
3. Launch `Live For Speed` locally and type `t` and start your local "insim" server:
    ```c
    /insim 29999
    ```
4. Set your connection config inside [appsettings.Development.json](/LfsPitWall.Server/appsettings.Development.json):
    ```json
    {
    "InSim": {
        "Host": "127.0.0.1",
        "Port": 29999
    }
    ```
5. Launch **LFS Pit Wall** by Kamileon
    ```bash
    cd LfsPitWall.Server
    dotnet watch
    ```
6. Have Fun!

### 📁 Project Structure
- [/LfsPitWall.Server](/LfsPitWall.Server/) - Main ASP.NET Core application & InSim logic.
- [/Docs](/Docs/) - App architecture, InSim protocol specifications, and development roadmap.
- [/wwwroot](/LfsPitWall.Server/wwwroot/) - Live Timing Web Dashboard (Frontend).

### 📜 Changelog

### [v0.1] - Initial Setup
- Created project structure with .NET 9 and Web API template.
- Added InSim protocol documentation in `/Docs`.
- Configured Native AOT support for high-performance builds.
- Added basic `appsettings.json` configuration for InSim connection.
- Initialized Git repository and GitHub integration.