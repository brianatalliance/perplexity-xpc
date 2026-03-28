# PerplexityXPC — Windows Integration Helper

A local AI broker that integrates Perplexity AI into Windows via a background service, system tray app, Explorer context menus, and MCP (Model Context Protocol) server management.

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                        PerplexityXPC                                 │
│                                                                     │
│  ┌──────────────────┐    Named Pipe    ┌──────────────────────────┐ │
│  │  System Tray App  │ ◄─────────────► │    Windows Service        │ │
│  │  (Ctrl+Shift+P)  │                 │    (PerplexityXPC)        │ │
│  └──────────────────┘                 │                          │ │
│                                       │  HTTP: 127.0.0.1:47777   │ │
│  ┌──────────────────┐    HTTP/SSE     │  WS:   ws://127.0.0.1:   │ │
│  │  Context Menu     │ ◄─────────────► │        47777/ws          │ │
│  │  (Right-click)   │                 │                          │ │
│  └──────────────────┘                 │  ┌────────────────────┐  │ │
│                                       │  │ Perplexity API     │  │ │
│  ┌──────────────────┐    HTTP/SSE     │  │ Proxy              │  │ │
│  │  PowerShell /     │ ◄─────────────► │  │                    │  │ │
│  │  curl / scripts  │                 │  │ sonar              │  │ │
│  └──────────────────┘                 │  │ sonar-pro          │  │ │
│                                       │  │ sonar-reasoning    │  │ │
│                                       │  │ sonar-deep-research│  │ │
│                                       │  └────────────────────┘  │ │
│                                       │                          │ │
│                                       │  ┌────────────────────┐  │ │
│                                       │  │ MCP Server Manager │  │ │
│                                       │  │                    │  │ │
│                                       │  │ filesystem         │  │ │
│                                       │  │ github             │  │ │
│                                       │  │ brave-search       │  │ │
│                                       │  │ memory             │  │ │
│                                       │  │ sqlite             │  │ │
│                                       │  │ (custom...)        │  │ │
│                                       │  └────────────────────┘  │ │
│                                       └──────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
```

## Components

| Component | Description |
|-----------|-------------|
| **PerplexityXPC.Service** | Windows Service — HTTP/WebSocket broker on `127.0.0.1:47777`, Named Pipe IPC, Perplexity API proxy, MCP server process management |
| **PerplexityXPC.Tray** | System tray app — `Ctrl+Shift+P` hotkey for floating query popup, status indicator (green/yellow/red), settings UI, MCP server control |
| **PerplexityXPC.ContextMenu** | Explorer integration — right-click "Ask Perplexity about this file/folder" on text-based files |
| **Scripts** | PowerShell installer, uninstaller, build script, context menu registrar (PS 5.1 + 7 compatible) |

## Prerequisites

- **Windows 10 build 1809+** or Windows 11
- **.NET 8 SDK** (x64) — [download](https://dotnet.microsoft.com/download/dotnet/8.0) (only for building; self-contained EXEs don't need runtime)
- **Node.js LTS** (x64) — [download](https://nodejs.org) (required for MCP servers via `npx`)
- **Perplexity API Key** — [generate here](https://www.perplexity.ai/settings/api) (format: `pplx-xxxx...`)

## Quick Start

### 1. Build

```powershell
cd PerplexityXPC
.\scripts\Build-PerplexityXPC.ps1
```

This compiles all three projects into the `bin\` folder as self-contained single-file executables.

### 2. Install

```powershell
# Interactive (prompts for API key)
.\scripts\Install-PerplexityXPC.ps1

# Pre-supply API key
.\scripts\Install-PerplexityXPC.ps1 -ApiKey "pplx-your-key-here"

# Custom install path
.\scripts\Install-PerplexityXPC.ps1 -InstallPath "D:\Apps\PerplexityXPC"
```

The installer will:
- Copy binaries to `C:\Program Files\PerplexityXPC\`
- Create config at `%LOCALAPPDATA%\PerplexityXPC\`
- Register and start the Windows Service
- Add firewall rule blocking external access to port 47777
- Register Explorer context menu entries
- Add tray app to Windows startup
- Encrypt your API key via DPAPI

### 3. Use

**Hotkey:** Press `Ctrl+Shift+P` anywhere to open the query popup.

**Tray icon:** Right-click the system tray icon for settings, MCP server management, and service control.

**Context menu:** Right-click any text file or folder in Explorer → "Ask Perplexity about this file/folder".

**HTTP API:**
```powershell
# Simple query
Invoke-RestMethod http://localhost:47777/perplexity -Method Post -ContentType 'application/json' -Body '{"model":"sonar","messages":[{"role":"user","content":"What is VLAN trunking?"}]}'

# Check status
Invoke-RestMethod http://localhost:47777/status

# List MCP servers
Invoke-RestMethod http://localhost:47777/mcp/servers

# Send MCP request
Invoke-RestMethod http://localhost:47777/mcp -Method Post -ContentType 'application/json' -Body '{"server":"filesystem","method":"tools/list","params":{}}'
```

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/perplexity` | Proxy to Perplexity Sonar API |
| `POST` | `/perplexity/stream` | SSE streaming proxy |
| `GET` | `/status` | Service health and uptime |
| `POST` | `/mcp` | Send JSON-RPC request to an MCP server |
| `GET` | `/mcp/servers` | List all MCP servers and their status |
| `POST` | `/mcp/servers/{name}/restart` | Restart a specific MCP server |
| `GET` | `/config` | Get non-sensitive configuration |
| `PUT` | `/config` | Update configuration |
| `WS` | `/ws` | WebSocket for streaming responses |

## MCP Server Configuration

Edit `%LOCALAPPDATA%\PerplexityXPC\mcp-servers.json`:

```json
{
  "mcpServers": {
    "filesystem": {
      "disabled": false,
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-filesystem", "C:\\Users\\YourName\\Documents"],
      "env": {}
    },
    "github": {
      "disabled": false,
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": {
        "GITHUB_PERSONAL_ACCESS_TOKEN": "github_pat_xxx"
      }
    }
  }
}
```

Servers auto-start at boot. Use the tray app or HTTP API to start/stop/restart individually.

## Security

- **Kestrel binds to `127.0.0.1` only** — no remote access possible at the transport layer
- **Windows Firewall rule** blocks port 47777 from non-localhost (defense in depth)
- **Named Pipe ACL** restricts access to the current user SID via `PipeAccessRule`
- **API key encryption** via DPAPI (machine + user bound)
- **API key is never exposed** via any HTTP endpoint

## Uninstall

```powershell
# Full removal
.\scripts\Uninstall-PerplexityXPC.ps1

# Keep config and logs
.\scripts\Uninstall-PerplexityXPC.ps1 -KeepData
```

## Project Structure

```
PerplexityXPC/
├── PerplexityXPC.sln
├── README.md
├── config/
│   ├── appsettings.template.json
│   └── mcp-servers.template.json
├── scripts/
│   ├── Build-PerplexityXPC.ps1
│   ├── Install-PerplexityXPC.ps1
│   ├── Uninstall-PerplexityXPC.ps1
│   └── Register-ContextMenu.ps1
└── src/
    ├── PerplexityXPC.Service/
    │   ├── Configuration/AppConfig.cs
    │   ├── Models/
    │   │   ├── ChatRequest.cs
    │   │   ├── ChatResponse.cs
    │   │   ├── McpServerConfig.cs
    │   │   └── McpServerInfo.cs
    │   ├── Services/
    │   │   ├── HttpBroker.cs
    │   │   ├── McpServerManager.cs
    │   │   ├── NamedPipeServer.cs
    │   │   └── PerplexityApiClient.cs
    │   ├── Program.cs
    │   └── appsettings.json
    ├── PerplexityXPC.Tray/
    │   ├── Forms/
    │   │   ├── QueryPopup.cs
    │   │   └── SettingsForm.cs
    │   ├── Helpers/
    │   │   ├── HotkeyManager.cs
    │   │   ├── StartupManager.cs
    │   │   └── ThemeManager.cs
    │   ├── Services/ServiceClient.cs
    │   ├── TrayApplicationContext.cs
    │   ├── Program.cs
    │   └── Properties/Settings.cs
    └── PerplexityXPC.ContextMenu/
        └── ContextMenuHandler.cs
```

## Troubleshooting

```powershell
# Check service status
Get-Service PerplexityXPC

# Restart service
Restart-Service PerplexityXPC

# Tail logs
Get-Content "$env:LOCALAPPDATA\PerplexityXPC\logs\service-*.log" -Tail 50 -Wait

# Enable debug logging
[Environment]::SetEnvironmentVariable("PERPLEXITYXPC_DEBUG","1","Machine")
Restart-Service PerplexityXPC

# Test HTTP endpoint
Invoke-RestMethod http://localhost:47777/status
```

## License

MIT
