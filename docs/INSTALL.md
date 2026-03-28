# PerplexityXPC Installation Guide

This guide covers building, installing, configuring, upgrading, and uninstalling PerplexityXPC on Windows 10/11.

---

## Table of Contents

- [Prerequisites](#prerequisites)
- [Step 1 - Clone or Download](#step-1---clone-or-download)
- [Step 2 - Build](#step-2---build)
- [Step 3 - Install](#step-3---install)
- [Step 4 - Verify](#step-4---verify)
- [Step 5 - Configure MCP Servers](#step-5---configure-mcp-servers)
- [Upgrading](#upgrading)
- [Uninstalling](#uninstalling)
- [Silent and Automated Installation](#silent-and-automated-installation)
- [Multi-Machine Deployment](#multi-machine-deployment)
- [Troubleshooting](#troubleshooting)

---

## Prerequisites

| Requirement | Version | Download |
|-------------|---------|----------|
| Windows OS | Windows 10 build 1809+ or Windows 11 | - |
| .NET SDK | 8.0 (x64) | https://dotnet.microsoft.com/download/dotnet/8.0 |
| Node.js | LTS (x64) | https://nodejs.org/en/download |
| Perplexity API key | Any active key | https://www.perplexity.ai/settings/api |
| PowerShell | 5.1+ or 7+ | Built-in; PS 7 optional |
| Administrator rights | - | Required for install only |

**Verify .NET SDK:**

```powershell
dotnet --version
# Expected: 8.0.x or later
```

**Verify Node.js:**

```powershell
node --version
# Expected: v20.x.x or later (LTS)

npx --version
# Expected: 10.x.x or later
```

---

## Step 1 - Clone or Download

**Clone with Git:**

```powershell
git clone https://github.com/your-name/PerplexityXPC.git
cd PerplexityXPC
```

**Or download the ZIP from GitHub and extract it:**

```powershell
Expand-Archive -Path "PerplexityXPC-main.zip" -DestinationPath "."
cd PerplexityXPC-main
```

---

## Step 2 - Build

Run the build script from the repository root:

```powershell
.\scripts\Build-PerplexityXPC.ps1
```

The script compiles three projects:
- `PerplexityXPC.Service` - Windows Service executable
- `PerplexityXPC.Tray` - System tray application
- `PerplexityXPC.ContextMenu` - Explorer shell extension

All outputs land in `bin\` as self-contained single-file executables (no .NET runtime needed on target machines).

**Expected output:**

```
[BUILD] Building PerplexityXPC.Service...
  [OK] PerplexityXPC.Service -> bin\PerplexityXPC.Service.exe
[BUILD] Building PerplexityXPC.Tray...
  [OK] PerplexityXPC.Tray -> bin\PerplexityXPC.Tray.exe
[BUILD] Building PerplexityXPC.ContextMenu...
  [OK] PerplexityXPC.ContextMenu -> bin\PerplexityXPC.ContextMenu.dll
[BUILD] All projects built successfully.
```

### Common Build Errors

**Error: No NuGet source found**

```
error : Unable to load the service index for source https://api.nuget.org/v3/index.json
```

Fix:

```powershell
dotnet nuget add source https://api.nuget.org/v3/index.json --name nuget.org
dotnet restore
```

**Error: Execution policy blocks the script**

```
.\scripts\Build-PerplexityXPC.ps1 cannot be loaded because running scripts is disabled
```

Fix:

```powershell
Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned
```

**Error: SDK version mismatch**

```
error NETSDK1045: The current .NET SDK does not support targeting .NET 8.0.
```

Fix: Download .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0. Ensure you install the x64 version.

**Error: Rollback on publish**

If the `dotnet publish` step fails midway, clear the build artifacts and retry:

```powershell
Get-ChildItem -Path . -Include bin,obj -Recurse -Directory | Remove-Item -Recurse -Force
.\scripts\Build-PerplexityXPC.ps1
```

---

## Step 3 - Install

The installer must be run as Administrator. It will self-elevate if needed.

### Basic Installation

```powershell
.\scripts\Install-PerplexityXPC.ps1
```

You will be prompted to enter your Perplexity API key if `-ApiKey` is not supplied.

### Installer Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `-ApiKey` | string | (prompt) | Perplexity API key (`pplx-...`). Omit to be prompted interactively. |
| `-InstallPath` | string | `C:\Program Files\PerplexityXPC` | Destination directory for binaries. |
| `-NoService` | switch | false | Skip Windows Service registration. |
| `-NoContextMenu` | switch | false | Skip Explorer context menu registration. |
| `-NoTrayStartup` | switch | false | Skip adding the tray app to Windows startup. |
| `-Quiet` | switch | false | Suppress non-error output (suitable for CI/RMM). |

### Examples

```powershell
# Supply the API key up front
.\scripts\Install-PerplexityXPC.ps1 -ApiKey "pplx-abc123def456"

# Custom install path
.\scripts\Install-PerplexityXPC.ps1 -InstallPath "D:\Apps\PerplexityXPC"

# Install service only, skip tray startup and context menu
.\scripts\Install-PerplexityXPC.ps1 -NoContextMenu -NoTrayStartup

# Fully silent
.\scripts\Install-PerplexityXPC.ps1 -ApiKey "pplx-abc123def456" -Quiet
```

### What the Installer Does

1. Verifies prerequisites (Windows version, admin rights)
2. Copies binaries to `InstallPath`
3. Creates config directory at `%LOCALAPPDATA%\PerplexityXPC\`
4. Copies `appsettings.template.json` to `%LOCALAPPDATA%\PerplexityXPC\appsettings.json`
5. Copies `mcp-servers.template.json` to `%LOCALAPPDATA%\PerplexityXPC\mcp-servers.json`
6. Encrypts the API key with DPAPI and saves as `api-key.enc`
7. Registers and starts the Windows Service (`sc.exe create`)
8. Adds a Windows Firewall inbound block rule for port 47777
9. Registers the context menu handler under `HKCU\Software\Classes`
10. Adds the tray app to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

---

## Step 4 - Verify

```powershell
# 1. Check the service is running
Get-Service PerplexityXPC
# Expected: Status = Running

# 2. Check the HTTP endpoint
Invoke-RestMethod http://localhost:47777/status
# Expected: JSON object with status, version, uptime fields

# 3. Send a test query
$body = @{
    model    = 'sonar'
    messages = @(@{ role = 'user'; content = 'Say hello in one sentence.' })
} | ConvertTo-Json -Depth 5
Invoke-RestMethod http://localhost:47777/perplexity `
    -Method Post -ContentType 'application/json' -Body $body
# Expected: JSON response with choices[0].message.content

# 4. Check tray app is in system tray
# Look for the PerplexityXPC icon in the notification area (bottom-right)

# 5. Test the hotkey
# Press Ctrl+Alt+P - the query popup should appear
```

---

## Step 5 - Configure MCP Servers

MCP servers are optional but significantly extend what Perplexity can access.

**Edit the configuration file:**

```
%LOCALAPPDATA%\PerplexityXPC\mcp-servers.json
```

Start from the installed template and uncomment/enable the servers you want. Change `"disabled": true` to `"disabled": false` for any server you want active.

**Filesystem server (local files):**

```json
"filesystem": {
  "disabled": false,
  "command": "npx",
  "args": ["-y", "@modelcontextprotocol/server-filesystem", "C:\\Users\\YourUsername\\Documents"],
  "env": {}
}
```

**GitHub server:**

```json
"github": {
  "disabled": false,
  "command": "npx",
  "args": ["-y", "@modelcontextprotocol/server-github"],
  "env": {
    "GITHUB_PERSONAL_ACCESS_TOKEN": "<your-github-pat>"
  }
}
```

**Brave Search server:**

```json
"brave-search": {
  "disabled": false,
  "command": "npx",
  "args": ["-y", "@modelcontextprotocol/server-brave-search"],
  "env": {
    "BRAVE_API_KEY": "<your-brave-api-key>"
  }
}
```

After editing, restart the service to apply changes:

```powershell
Restart-Service PerplexityXPC

# Verify servers started
Invoke-RestMethod http://localhost:47777/mcp/servers
```

---

## Upgrading

1. Run the uninstaller with `-KeepData` to preserve your configuration and API key:

```powershell
.\scripts\Uninstall-PerplexityXPC.ps1 -KeepData
```

2. Build the new version:

```powershell
.\scripts\Build-PerplexityXPC.ps1
```

3. Re-run the installer. It detects existing config and skips re-prompting for the API key if `api-key.enc` exists:

```powershell
.\scripts\Install-PerplexityXPC.ps1
```

4. Verify the service is running:

```powershell
Invoke-RestMethod http://localhost:47777/status
```

---

## Uninstalling

**Full removal (deletes config, logs, and API key):**

```powershell
.\scripts\Uninstall-PerplexityXPC.ps1
```

**Remove binaries only (keep config, logs, and API key):**

```powershell
.\scripts\Uninstall-PerplexityXPC.ps1 -KeepData
```

The uninstaller:
1. Stops and removes the Windows Service
2. Removes the tray app from Windows startup
3. Removes the Explorer context menu entries
4. Removes the Windows Firewall rule
5. Deletes the install directory (unless `-KeepData` is specified)
6. Optionally deletes `%LOCALAPPDATA%\PerplexityXPC\` (omitted with `-KeepData`)

---

## Silent and Automated Installation

For deployment via RMM tools (Atera, NinjaRMM, ConnectWise, Intune, etc.) or PowerShell remoting:

```powershell
# Single-line silent install
& "\\server\share\PerplexityXPC\scripts\Install-PerplexityXPC.ps1" `
    -ApiKey "pplx-<your-api-key>" `
    -Quiet

# With custom path and no interactive features
& ".\scripts\Install-PerplexityXPC.ps1" `
    -ApiKey "pplx-<your-api-key>" `
    -InstallPath "C:\Tools\PerplexityXPC" `
    -NoContextMenu `
    -NoTrayStartup `
    -Quiet
```

**Intune Win32 App packaging:**

1. Package the repository folder as a `.intunewin` using `IntuneWinAppUtil.exe`
2. Install command:
   ```
   powershell.exe -ExecutionPolicy Bypass -File scripts\Install-PerplexityXPC.ps1 -ApiKey "pplx-<key>" -Quiet
   ```
3. Uninstall command:
   ```
   powershell.exe -ExecutionPolicy Bypass -File scripts\Uninstall-PerplexityXPC.ps1 -Quiet
   ```
4. Detection rule: File exists at `C:\Program Files\PerplexityXPC\PerplexityXPC.Service.exe`

---

## Multi-Machine Deployment

- The API key is encrypted per-machine and per-user with DPAPI. You cannot copy `api-key.enc` between machines.
- Each machine needs its own API key encryption pass (supply via `-ApiKey` parameter).
- The `mcp-servers.json` file can be deployed centrally and then copied to `%LOCALAPPDATA%\PerplexityXPC\` as part of your deployment script.
- For domain environments, consider deploying via a startup/logon script or GPO software installation.

---

## Troubleshooting

### ASR (Attack Surface Reduction) blocks the installer

Microsoft Defender ASR rule "Block process creations originating from PSExec and WMI commands" can interfere with scripts launched by RMM agents.

Options:
- Run the script in a user context rather than SYSTEM
- Add the installer script path to the ASR exclusions list in Intune
- Use the `-ExecutionPolicy Bypass` flag explicitly

### Execution policy error

```
File cannot be loaded because running scripts is disabled on this system.
```

Fix:

```powershell
Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned
# Or for machine-wide (requires admin):
Set-ExecutionPolicy -Scope LocalMachine -ExecutionPolicy RemoteSigned
```

### Service fails to start - missing EXE

```
System cannot find the file specified.
```

Ensure the build was completed before running the installer. The installer expects the EXEs in `bin\`:

```powershell
Test-Path ".\bin\PerplexityXPC.Service.exe"  # Must be True
Test-Path ".\bin\PerplexityXPC.Tray.exe"     # Must be True
```

### Service starts then immediately stops

Check the Application event log:

```powershell
Get-EventLog -LogName Application -Source PerplexityXPC -Newest 10 | Format-List
```

Common causes:
- Port 47777 is in use by another application
- `appsettings.json` is malformed (invalid JSON)
- No `api-key.enc` file present (API key was never set)

### NuGet restore fails behind a corporate proxy

```powershell
# Set the proxy for dotnet
$env:HTTP_PROXY  = "http://proxy.example.com:8080"
$env:HTTPS_PROXY = "http://proxy.example.com:8080"
dotnet restore
```

Or configure it permanently:

```powershell
dotnet nuget add source https://api.nuget.org/v3/index.json --name nuget.org
```

And add proxy settings to `%APPDATA%\NuGet\NuGet.Config`.

### PATH issues - `npx` not found

If MCP servers fail to start with "command not found" or similar:

```powershell
# Verify npx is in PATH
Get-Command npx -ErrorAction SilentlyContinue

# If not found, add Node.js to system PATH
$nodePath = "C:\Program Files\nodejs"
[Environment]::SetEnvironmentVariable("PATH", "$env:PATH;$nodePath", "Machine")

# Restart the service so it inherits the new PATH
Restart-Service PerplexityXPC
```

### Tray app does not appear at startup

```powershell
# Check the registry entry
Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" | Select-Object PerplexityXPCTray

# Re-register manually
$trayPath = "C:\Program Files\PerplexityXPC\PerplexityXPC.Tray.exe"
Set-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "PerplexityXPCTray" -Value $trayPath
```

### Firewall rule blocks legitimate access

The installer creates an inbound block rule for port 47777. If you need to allow access from specific machines (not recommended for security), remove the block rule:

```powershell
Remove-NetFirewallRule -DisplayName "PerplexityXPC - Block External"
```
