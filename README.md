# PerplexityXPC - Windows Integration Helper

A local AI broker that integrates Perplexity AI into Windows via a background service, system tray app, Explorer context menus, and MCP (Model Context Protocol) server management.

PerplexityXPC runs a Windows Service that binds exclusively to `127.0.0.1:47777`, proxies requests to the Perplexity Sonar API, manages MCP server processes, and exposes a REST/WebSocket API for use by the tray app, context menu, PowerShell module, scripts, and any other local tooling. Your API key is encrypted at rest with DPAPI and never exposed via any HTTP endpoint.

---

## Table of Contents

- [Architecture](#architecture)
- [Features](#features)
- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
- [Usage](#usage)
  - [Hotkey and Tray App](#hotkey-and-tray-app)
  - [HTTP API](#http-api)
  - [PowerShell Module](#powershell-module)
  - [Context Menu](#context-menu)
  - [MCP Servers](#mcp-servers)
- [Configuration](#configuration)
- [Security](#security)
- [Troubleshooting](#troubleshooting)
- [Project Structure](#project-structure)
- [Contributing](#contributing)
- [License](#license)
- [Related Projects](#related-projects)

---

## Architecture

```
+---------------------------------------------------------------------+
|                          PerplexityXPC                              |
|                                                                     |
|  +------------------+    Named Pipe    +--------------------------+ |
|  |  System Tray App  | <-------------> |    Windows Service       | |
|  |  (Ctrl+Alt+P)  |                 |    (PerplexityXPC)       | |
|  +------------------+                 |                          | |
|                                       |  HTTP: 127.0.0.1:47777   | |
|  +------------------+    HTTP/SSE     |  WS:   ws://127.0.0.1:   | |
|  |  Context Menu     | <-------------> |        47777/ws          | |
|  |  (Right-click)   |                 |                          | |
|  +------------------+                 |  +--------------------+  | |
|                                       |  | Perplexity API     |  | |
|  +------------------+    HTTP/SSE     |  | Proxy              |  | |
|  |  PowerShell /     | <-------------> |  |                    |  | |
|  |  curl / scripts  |                 |  | sonar              |  | |
|  +------------------+                 |  | sonar-pro          |  | |
|                                       |  | sonar-reasoning    |  | |
|                                       |  | sonar-deep-research|  | |
|                                       |  +--------------------+  | |
|                                       |                          | |
|                                       |  +--------------------+  | |
|                                       |  | MCP Server Manager |  | |
|                                       |  |                    |  | |
|                                       |  | filesystem         |  | |
|                                       |  | github             |  | |
|                                       |  | brave-search       |  | |
|                                       |  | memory             |  | |
|                                       |  | sqlite             |  | |
|                                       |  | (custom...)        |  | |
|                                       |  +--------------------+  | |
|                                       +--------------------------+ |
+---------------------------------------------------------------------+
```

---

## Features

- **Windows Service broker** - Kestrel HTTP/WebSocket server bound to `127.0.0.1:47777` only
- **System tray application** - `Ctrl+Alt+P` global hotkey for a floating query popup
- **Explorer context menu** - Right-click any text file or folder to send it to Perplexity
- **MCP server manager** - Start, stop, and restart MCP servers via JSON-RPC 2.0 over stdio
- **Perplexity Sonar proxy** - Supports all four Sonar models with full parameter pass-through
- **SSE streaming** - Real-time token streaming via Server-Sent Events
- **WebSocket interface** - Persistent connection for streaming responses
- **PowerShell module** - 14 functions covering queries, file analysis, batch research, and IT integration
- **DPAPI-encrypted key storage** - API key encrypted at machine+user scope; never exposed via HTTP
- **Named Pipe IPC** - Per-user ACL via `PipeAccessRule` for secure inter-process communication
- **PowerShell installer** - Single-script install/uninstall compatible with PS 5.1 and PS 7+
- **Firewall rule** - Automatic rule to block external access to port 47777
- **Dark/light theme** - Tray popup adapts to Windows system theme
- **Atera and Intune integration** - Dedicated PowerShell functions for IT management workflows
- **Batch queries and report generation** - Research multiple topics and compile structured reports
- **Self-contained executables** - No .NET runtime required on target machines

---

## Prerequisites

| Requirement | Notes |
|-------------|-------|
| **Windows 10 build 1809+** or Windows 11 | Required |
| **.NET 8 SDK (x64)** | [Download](https://dotnet.microsoft.com/download/dotnet/8.0) - build only; compiled EXEs are self-contained |
| **Node.js LTS (x64)** | [Download](https://nodejs.org) - required at runtime for MCP servers via `npx` |
| **Perplexity API key** | [Generate here](https://www.perplexity.ai/settings/api) - format: `pplx-...` |
| **Administrator rights** | Required for the installer (service registration, firewall, context menu) |

---

## Quick Start

### 1. Build

```powershell
cd PerplexityXPC
.\scripts\Build-PerplexityXPC.ps1
```

The build script compiles all three projects into `bin\` as self-contained single-file executables.

**Common build errors:**

```powershell
# Missing NuGet source
dotnet nuget add source https://api.nuget.org/v3/index.json --name nuget.org

# Execution policy
Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned
```

### 2. Install

```powershell
# Interactive - prompts for API key
.\scripts\Install-PerplexityXPC.ps1

# Pre-supply API key
.\scripts\Install-PerplexityXPC.ps1 -ApiKey "pplx-<your-api-key>"

# Custom install path
.\scripts\Install-PerplexityXPC.ps1 -InstallPath "D:\Apps\PerplexityXPC"

# Quiet install (CI/RMM)
.\scripts\Install-PerplexityXPC.ps1 -ApiKey "pplx-<your-api-key>" -Quiet
```

The installer:
- Copies binaries to `C:\Program Files\PerplexityXPC\`
- Creates config at `%LOCALAPPDATA%\PerplexityXPC\`
- Registers and starts the Windows Service
- Adds a Windows Firewall inbound block rule for port 47777
- Registers Explorer context menu entries
- Adds the tray app to Windows startup
- Encrypts your API key via DPAPI

### 3. Verify

```powershell
# Check service is running
Get-Service PerplexityXPC

# Test the HTTP endpoint
Invoke-RestMethod http://localhost:47777/status

# Send a test query
Invoke-RestMethod http://localhost:47777/perplexity -Method Post `
  -ContentType 'application/json' `
  -Body '{"model":"sonar","messages":[{"role":"user","content":"Hello!"}]}'
```

---

## Usage

### Hotkey and Tray App

Press `Ctrl+Alt+P` from anywhere in Windows to open the floating query popup.

The system tray icon shows service health:
- **Green** - Service running, API key configured
- **Yellow** - Service running, no API key or degraded
- **Red** - Service not reachable

Right-click the tray icon to:
- Open the query popup
- View and restart MCP servers
- Open settings
- Control the service (start/stop/restart)
- Exit the tray app

### HTTP API

All endpoints are available at `http://127.0.0.1:47777`. See [docs/API.md](docs/API.md) for full request/response schemas.

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/perplexity` | Proxy a chat request to the Perplexity Sonar API |
| `POST` | `/perplexity/stream` | SSE streaming proxy - returns `text/event-stream` |
| `GET`  | `/status` | Service health, version, uptime, MCP server list |
| `POST` | `/mcp` | Send a JSON-RPC 2.0 request to a named MCP server |
| `GET`  | `/mcp/servers` | List all registered MCP servers and their status |
| `POST` | `/mcp/servers/{name}/restart` | Restart a specific MCP server by name |
| `GET`  | `/config` | Read non-sensitive broker configuration |
| `PUT`  | `/config` | Update broker configuration at runtime |
| `WS`   | `/ws` | WebSocket connection for streaming responses |

**Quick examples:**

```powershell
# Query Perplexity
$body = @{
    model    = 'sonar-pro'
    messages = @(@{ role = 'user'; content = 'What is VLAN trunking?' })
} | ConvertTo-Json
Invoke-RestMethod http://localhost:47777/perplexity -Method Post -ContentType 'application/json' -Body $body

# Check status
Invoke-RestMethod http://localhost:47777/status

# List MCP servers
Invoke-RestMethod http://localhost:47777/mcp/servers

# Restart an MCP server
Invoke-RestMethod http://localhost:47777/mcp/servers/filesystem/restart -Method Post
```

```bash
# curl
curl -s -X POST http://127.0.0.1:47777/perplexity \
  -H "Content-Type: application/json" \
  -d '{"model":"sonar","messages":[{"role":"user","content":"What is BGP?"}]}'
```

### PowerShell Module

Install and import:

```powershell
# Install to user module path
$dest = "$env:USERPROFILE\Documents\PowerShell\Modules\PerplexityXPC"
Copy-Item -Path ".\module\PerplexityXPC" -Destination $dest -Recurse
Import-Module PerplexityXPC

# Basic usage
Invoke-Perplexity 'What is zero trust networking?'
Get-XPCStatus
Get-McpServer
```

See [docs/MODULE.md](docs/MODULE.md) for the complete function reference with parameter tables and examples.

**Available functions:**

| Category | Functions |
|----------|-----------|
| Core | `Invoke-Perplexity`, `Get-XPCStatus`, `Get-XPCConfig`, `Set-XPCConfig` |
| MCP Management | `Get-McpServer`, `Restart-McpServer`, `Invoke-McpRequest` |
| File Analysis | `Invoke-PerplexityFileAnalysis`, `Invoke-PerplexityFolderAnalysis` |
| Batch Research | `Invoke-PerplexityBatch`, `Invoke-PerplexityReport` |
| IT Integration | `Invoke-PerplexityTicketAnalysis`, `Invoke-PerplexityDeviceAnalysis`, `Invoke-PerplexitySecurityAnalysis` |

### Context Menu

Right-click any text-based file or folder in Windows Explorer:

- **"Ask Perplexity about this file"** - Reads the file content (up to the configured size limit) and opens the tray popup pre-loaded with an analysis query
- **"Ask Perplexity about this folder"** - Sends a directory listing and asks for a structural analysis

The context menu is registered per-user under `HKCU\Software\Classes`. Use `.\scripts\Register-ContextMenu.ps1` to re-register if entries disappear after an update.

### MCP Servers

MCP (Model Context Protocol) servers extend Perplexity with access to local files, GitHub, web search, SQLite databases, and more.

**Configuration file:** `%LOCALAPPDATA%\PerplexityXPC\mcp-servers.json`

```json
{
  "mcpServers": {
    "filesystem": {
      "disabled": false,
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-filesystem", "C:\\Users\\YourUsername\\Documents"],
      "env": {}
    },
    "github": {
      "disabled": false,
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": {
        "GITHUB_PERSONAL_ACCESS_TOKEN": "<your-github-token>"
      }
    },
    "brave-search": {
      "disabled": false,
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-brave-search"],
      "env": {
        "BRAVE_API_KEY": "<your-brave-api-key>"
      }
    }
  }
}
```

Copy `config\mcp-servers.template.json` as a starting point. Set `"disabled": false` to activate a server. Servers start automatically with the Windows Service.

**Manage via PowerShell:**

```powershell
# List all servers
Get-McpServer

# Restart one
Restart-McpServer -Name 'filesystem'

# Send a request
Invoke-McpRequest -Server 'filesystem' -Method 'tools/list'
Invoke-McpRequest -Server 'filesystem' -Method 'tools/call' -Params @{
    name      = 'read_file'
    arguments = @{ path = 'C:\Users\YourUsername\Documents\notes.txt' }
}
```

---

## Configuration

### appsettings.json

Located at `%LOCALAPPDATA%\PerplexityXPC\appsettings.json` after installation. Template at `config\appsettings.template.json`.

| Key | Default | Description |
|-----|---------|-------------|
| `PerplexityXPC.ApiEndpoint` | `https://api.perplexity.ai` | Perplexity API base URL |
| `PerplexityXPC.HttpPort` | `47777` | Local HTTP port |
| `PerplexityXPC.PipeServerName` | `PerplexityXPCPipe` | Named Pipe server name |
| `PerplexityXPC.LogLevel` | `Information` | Logging level (Trace/Debug/Information/Warning/Error) |
| `PerplexityXPC.DefaultModel` | `sonar` | Default Sonar model |
| `PerplexityXPC.ApiTimeoutSec` | `60` | API call timeout in seconds |
| `PerplexityXPC.MaxTokens` | `2048` | Default max tokens per response |
| `PerplexityXPC.MaxFileSizeKB` | `10240` | Max file size for context menu reads |
| `Mcp.AutoRestart` | `true` | Auto-restart crashed MCP processes |
| `Mcp.TimeoutSec` | `30` | MCP request timeout |
| `Mcp.MaxConcurrentServers` | `5` | Max simultaneous MCP processes |

### mcp-servers.json

Located at `%LOCALAPPDATA%\PerplexityXPC\mcp-servers.json`. Each entry under `mcpServers` defines one MCP server:

| Field | Type | Description |
|-------|------|-------------|
| `disabled` | boolean | Set to `true` to exclude from auto-start |
| `description` | string | Human-readable description (optional) |
| `command` | string | Executable to launch (e.g., `npx`, `node`) |
| `args` | array | Arguments passed to the command |
| `env` | object | Environment variables injected into the process |

### Environment Variables

| Variable | Description |
|----------|-------------|
| `PERPLEXITYXPC_DEBUG` | Set to `1` to enable verbose debug logging |
| `PERPLEXITYXPC_PORT` | Override the HTTP port (default: 47777) |

---

## Security

- **Kestrel binds to `127.0.0.1` only** - The service cannot be accessed from other machines at the network layer
- **Windows Firewall rule** - An inbound block rule on port 47777 provides defense-in-depth against loopback bypass techniques
- **Named Pipe ACL** - The IPC pipe is restricted to the current user SID via `PipeAccessRule`; no other users or services can connect
- **DPAPI encryption** - The API key is encrypted with `ProtectedData.Protect` using `DataProtectionScope.CurrentUser` and `LocalMachine` entropy; the plaintext key exists only in memory during API calls
- **No API key in HTTP responses** - The `/config` endpoint deliberately omits the API key; it cannot be retrieved via HTTP
- **No admin required at runtime** - The Windows Service runs as LocalService; administrator rights are only needed during installation

---

## Troubleshooting

### Service does not start

```powershell
# Check Windows Event Log for errors
Get-EventLog -LogName System -Source PerplexityXPC -Newest 20

# Check service status
Get-Service PerplexityXPC | Select-Object Status, StartType

# Try starting manually and watching output
Start-Service PerplexityXPC
Get-Content "$env:LOCALAPPDATA\PerplexityXPC\logs\service-*.log" -Tail 30
```

### API key not accepted

```powershell
# Re-run the installer to re-encrypt the key
.\scripts\Install-PerplexityXPC.ps1 -ApiKey "pplx-<your-api-key>"

# Verify the key file exists
Test-Path "$env:LOCALAPPDATA\PerplexityXPC\api-key.enc"
```

### Port 47777 already in use

```powershell
# Find the conflicting process
netstat -ano | findstr :47777

# Change the port in appsettings.json
# Then restart the service
Restart-Service PerplexityXPC
```

### MCP servers not starting

```powershell
# Check Node.js is installed
node --version
npx --version

# Test an MCP server manually
npx -y @modelcontextprotocol/server-filesystem "C:\Users\YourUsername\Documents"

# Check MCP logs
Get-Content "$env:LOCALAPPDATA\PerplexityXPC\logs\service-*.log" -Tail 50 | Select-String "MCP"
```

### Context menu entries missing

```powershell
# Re-register context menu
.\scripts\Register-ContextMenu.ps1

# Or check the registry directly
Get-Item "HKCU:\Software\Classes\*\shell\PerplexityXPC"
```

### Execution policy error

```powershell
Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned
```

### ASR (Attack Surface Reduction) blocking the installer

If Windows Defender ASR rules block PowerShell script execution, temporarily disable the relevant ASR rule or use a signed script. See [docs/INSTALL.md](docs/INSTALL.md) for details.

### Enable debug logging

```powershell
[Environment]::SetEnvironmentVariable("PERPLEXITYXPC_DEBUG", "1", "Machine")
Restart-Service PerplexityXPC
Get-Content "$env:LOCALAPPDATA\PerplexityXPC\logs\service-*.log" -Tail 50 -Wait
```

---

## Project Structure

```
PerplexityXPC/
+-- PerplexityXPC.sln
+-- README.md
+-- CHANGELOG.md
+-- LICENSE
+-- .gitignore
+-- config/
|   +-- appsettings.template.json
|   +-- mcp-servers.template.json
+-- docs/
|   +-- INSTALL.md
|   +-- MODULE.md
|   +-- API.md
+-- module/
|   +-- PerplexityXPC/
|       +-- PerplexityXPC.psd1
|       +-- PerplexityXPC.psm1
+-- scripts/
|   +-- Build-PerplexityXPC.ps1
|   +-- Install-PerplexityXPC.ps1
|   +-- Uninstall-PerplexityXPC.ps1
|   +-- Register-ContextMenu.ps1
+-- src/
    +-- PerplexityXPC.Service/
    |   +-- Configuration/
    |   |   +-- AppConfig.cs
    |   +-- Models/
    |   |   +-- ChatRequest.cs
    |   |   +-- ChatResponse.cs
    |   |   +-- McpServerConfig.cs
    |   |   +-- McpServerInfo.cs
    |   +-- Services/
    |   |   +-- HttpBroker.cs
    |   |   +-- McpServerManager.cs
    |   |   +-- NamedPipeServer.cs
    |   |   +-- PerplexityApiClient.cs
    |   +-- Program.cs
    |   +-- appsettings.json
    +-- PerplexityXPC.Tray/
    |   +-- Forms/
    |   |   +-- QueryPopup.cs
    |   |   +-- SettingsForm.cs
    |   +-- Helpers/
    |   |   +-- HotkeyManager.cs
    |   |   +-- StartupManager.cs
    |   |   +-- ThemeManager.cs
    |   +-- Services/
    |   |   +-- ServiceClient.cs
    |   +-- TrayApplicationContext.cs
    |   +-- Program.cs
    |   +-- Properties/Settings.cs
    +-- PerplexityXPC.ContextMenu/
        +-- ContextMenuHandler.cs
        +-- app.manifest
```

---

## Contributing

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/my-feature`
3. Make your changes and ensure no personal information is included
4. Test on Windows 10/11 with both PowerShell 5.1 and PowerShell 7
5. Submit a pull request

**Code standards:**
- C# code follows standard .NET naming conventions
- PowerShell follows the Verb-Noun function naming standard
- No em dashes in any documentation - use hyphens only
- All strings with user-visible text should use single quotes in PowerShell
- Error handling must use `try/catch` with descriptive `Write-Error` messages

---

## License

MIT License - see [LICENSE](LICENSE) for details.

---

## Related Projects

- [perplexity-connector](https://github.com/brianatalliance/perplexity-connector) - Direct Perplexity API connector without the Windows service layer
- [atera-connector](https://github.com/brianatalliance/atera-connector) - PowerShell module for the Atera RMM REST API, used alongside PerplexityXPC for IT ticket analysis
