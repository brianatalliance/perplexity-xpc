<#
.SYNOPSIS
    Installs PerplexityXPC — a local AI broker for Perplexity API and MCP servers.

.DESCRIPTION
    This installer performs pre-flight checks, creates the install directory, registers
    the Windows Service, configures firewall rules, registers Explorer context menu
    entries, and optionally stores your Perplexity API key securely via DPAPI.

.PARAMETER ApiKey
    Pre-supply your Perplexity API key to skip the interactive prompt.

.PARAMETER InstallPath
    Custom installation directory. Defaults to C:\Program Files\PerplexityXPC.

.PARAMETER NoService
    Skip Windows Service registration. The service must then be started manually.

.PARAMETER NoContextMenu
    Skip Windows Explorer context menu registration.

.PARAMETER NoTrayStartup
    Skip adding the tray application to Windows startup.

.PARAMETER Quiet
    Suppress informational output; only errors are shown.

.EXAMPLE
    .\Install-PerplexityXPC.ps1

.EXAMPLE
    .\Install-PerplexityXPC.ps1 -ApiKey "pplx-abc123" -Quiet

.EXAMPLE
    .\Install-PerplexityXPC.ps1 -InstallPath "D:\Apps\PerplexityXPC" -NoService

.NOTES
    Requires Windows 10 build 1809 or later (Windows 11 also supported).
    Must be run as Administrator (the script will self-elevate if needed).
    Compatible with PowerShell 5.1 and PowerShell 7+.
#>

#region Parameters
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(HelpMessage = "Perplexity API key (pplx-...). Leave blank to be prompted.")]
    [string]$ApiKey,

    [Parameter(HelpMessage = "Custom installation path. Default: C:\Program Files\PerplexityXPC")]
    [string]$InstallPath = "C:\Program Files\PerplexityXPC",

    [Parameter(HelpMessage = "Skip Windows Service registration.")]
    [switch]$NoService,

    [Parameter(HelpMessage = "Skip Explorer context menu registration.")]
    [switch]$NoContextMenu,

    [Parameter(HelpMessage = "Skip adding tray app to Windows startup.")]
    [switch]$NoTrayStartup,

    [Parameter(HelpMessage = "Suppress non-error output.")]
    [switch]$Quiet
)
#endregion

#region Helpers
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Status {
    param([string]$Message, [string]$Color = 'Cyan', [switch]$NoNewLine)
    if (-not $Quiet) {
        $params = @{ ForegroundColor = $Color; Object = $Message }
        if ($NoNewLine) { $params['NoNewline'] = $true }
        Write-Host @params
    }
}

function Write-Success { param([string]$Message) Write-Status "  [OK] $Message" -Color Green }
function Write-Warn    { param([string]$Message) Write-Host "  [WARN] $Message" -ForegroundColor Yellow }
function Write-Err     { param([string]$Message) Write-Host "  [ERR] $Message"  -ForegroundColor Red }

function Write-Step {
    param([string]$Message)
    if (-not $Quiet) {
        Write-Host ""
        Write-Host "==> $Message" -ForegroundColor Cyan
    }
}

function Test-IsAdmin {
    $identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]$identity
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
#endregion

#region Self-elevation
if (-not (Test-IsAdmin)) {
    Write-Warn "Not running as Administrator — re-launching elevated..."
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    if ($ApiKey)         { $argList += "-ApiKey `"$ApiKey`"" }
    if ($InstallPath -ne "C:\Program Files\PerplexityXPC") { $argList += "-InstallPath `"$InstallPath`"" }
    if ($NoService)      { $argList += '-NoService' }
    if ($NoContextMenu)  { $argList += '-NoContextMenu' }
    if ($NoTrayStartup)  { $argList += '-NoTrayStartup' }
    if ($Quiet)          { $argList += '-Quiet' }

    Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -Verb RunAs -Wait
    exit $LASTEXITCODE
}
#endregion

#region Banner
if (-not $Quiet) {
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Magenta
    Write-Host "   PerplexityXPC Installer v1.0.0" -ForegroundColor Magenta
    Write-Host "==========================================" -ForegroundColor Magenta
    Write-Host ""
}
#endregion

#region Pre-flight checks
Write-Step "Running pre-flight checks..."

# --- Windows version check ---
$osInfo = Get-CimInstance -ClassName Win32_OperatingSystem
$build  = [int]($osInfo.BuildNumber)
if ($build -lt 17763) {
    Write-Err "Windows 10 build 1809 (17763) or later is required. Detected build: $build"
    exit 1
}
Write-Success "Windows version OK (Build $build)"

# --- .NET 8 runtime check ---
$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetOk  = $false
if ($dotnetCmd) {
    $runtimes = & dotnet --list-runtimes 2>$null | Where-Object { $_ -match 'Microsoft\.NETCore\.App 8\.' }
    if ($runtimes) { $dotnetOk = $true }
}
if ($dotnetOk) {
    Write-Success ".NET 8 runtime detected"
} else {
    Write-Warn ".NET 8 runtime not found. PerplexityXPC uses self-contained executables, so this is non-fatal, but installing .NET 8 is recommended."
    Write-Warn "  Download from: https://dotnet.microsoft.com/download/dotnet/8.0"
}

# --- Node.js / npx check ---
$nodeCmd = Get-Command node -ErrorAction SilentlyContinue
$npxCmd  = Get-Command npx  -ErrorAction SilentlyContinue
if ($nodeCmd -and $npxCmd) {
    $nodeVersion = & node --version 2>$null
    Write-Success "Node.js detected ($nodeVersion) — MCP servers will work"
} else {
    Write-Warn "Node.js / npx not found. MCP server features will be unavailable."
    Write-Warn "  Install from: https://nodejs.org"
}
#endregion

#region Locate source binaries
Write-Step "Locating source binaries..."

# Determine script's own directory (works in PS 5.1 and PS 7)
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Definition }
$binDir    = Join-Path (Split-Path -Parent $scriptDir) "bin"

$serviceExe     = Join-Path $binDir "PerplexityXPC.Service.exe"
$trayExe        = Join-Path $binDir "PerplexityXPC.Tray.exe"
$contextMenuExe = Join-Path $binDir "PerplexityXPC.ContextMenu.exe"

foreach ($exe in @($serviceExe, $trayExe, $contextMenuExe)) {
    if (-not (Test-Path $exe)) {
        Write-Err "Binary not found: $exe"
        Write-Err "Run .\Build-PerplexityXPC.ps1 first to produce the bin\ folder."
        exit 1
    }
}
Write-Success "All binaries located in $binDir"
#endregion

#region Create install directory
Write-Step "Creating installation directory: $InstallPath"

if ($PSCmdlet.ShouldProcess($InstallPath, "Create install directory")) {
    if (-not (Test-Path $InstallPath)) {
        New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
    }

    # Copy executables
    foreach ($exe in @($serviceExe, $trayExe, $contextMenuExe)) {
        $dest = Join-Path $InstallPath (Split-Path -Leaf $exe)
        Copy-Item -Path $exe -Destination $dest -Force
        Write-Success "Copied $(Split-Path -Leaf $exe)"
    }
}
#endregion

#region Create data directory and default config files
Write-Step "Creating data directory and default configuration..."

$dataDir = Join-Path $env:LOCALAPPDATA "PerplexityXPC"
$logsDir = Join-Path $dataDir "logs"

if ($PSCmdlet.ShouldProcess($dataDir, "Create data directory")) {
    @($dataDir, $logsDir) | ForEach-Object {
        if (-not (Test-Path $_)) { New-Item -ItemType Directory -Path $_ -Force | Out-Null }
    }
    Write-Success "Data directory: $dataDir"

    # --- default appsettings.json ---
    $appSettingsPath = Join-Path $dataDir "appsettings.json"
    if (-not (Test-Path $appSettingsPath)) {
        $appSettings = @{
            PerplexityXPC = @{
                ApiEndpoint    = "https://api.perplexity.ai"
                PipeServerName = "PerplexityXPCPipe"
                HttpPort       = 47777
                LogLevel       = "Information"
                LogDirectory   = (Join-Path $dataDir "logs")
                MaxFileSizeKB  = 10240
            }
            Mcp = @{
                ConfigFile  = (Join-Path $dataDir "mcp-servers.json")
                AutoRestart = $true
                TimeoutSec  = 30
            }
        } | ConvertTo-Json -Depth 5
        Set-Content -Path $appSettingsPath -Value $appSettings -Encoding UTF8
        Write-Success "Created default appsettings.json"
    } else {
        Write-Status "  [SKIP] appsettings.json already exists — not overwritten" -Color DarkGray
    }

    # --- default mcp-servers.json ---
    $mcpConfigPath = Join-Path $dataDir "mcp-servers.json"
    if (-not (Test-Path $mcpConfigPath)) {
        # Note: The "disabled" field is a PerplexityXPC extension to allow commented-out servers.
        $mcpConfig = @{
            "__comment" = "Add MCP server definitions here. Set 'disabled': true to skip a server without removing it."
            mcpServers  = @{
                "filesystem-example" = @{
                    disabled    = $true
                    command     = "npx"
                    args        = @("-y", "@modelcontextprotocol/server-filesystem", "C:\Users\$env:USERNAME\Documents")
                    description = "Filesystem MCP server (example — set disabled: false to activate)"
                }
            }
        } | ConvertTo-Json -Depth 6
        Set-Content -Path $mcpConfigPath -Value $mcpConfig -Encoding UTF8
        Write-Success "Created default mcp-servers.json"
    } else {
        Write-Status "  [SKIP] mcp-servers.json already exists — not overwritten" -Color DarkGray
    }
}
#endregion

#region API Key handling
Write-Step "Configuring Perplexity API key..."

if (-not $ApiKey) {
    if (-not $Quiet) {
        Write-Host "  Enter your Perplexity API key (starts with 'pplx-'), or press Enter to skip" -ForegroundColor Yellow
        Write-Host "  You can set it later via the tray icon > Settings." -ForegroundColor Yellow
    }
    $ApiKey = Read-Host "  API Key"
}

if ($ApiKey -and $ApiKey.Trim() -ne '') {
    $ApiKey = $ApiKey.Trim()

    if ($PSCmdlet.ShouldProcess("DPAPI credential store", "Store Perplexity API key")) {
        # Store encrypted via DPAPI — only decryptable by same user on same machine
        $secureKey   = ConvertTo-SecureString -String $ApiKey -AsPlainText -Force
        $encryptedB64 = $secureKey | ConvertFrom-SecureString
        $credFile     = Join-Path $dataDir "api-key.enc"
        Set-Content -Path $credFile -Value $encryptedB64 -Encoding UTF8
        Write-Success "API key encrypted and stored at: $credFile"

        # Also set user-level environment variable (non-sensitive convenience)
        [Environment]::SetEnvironmentVariable("PERPLEXITY_API_KEY", $ApiKey, "User")
        Write-Success "PERPLEXITY_API_KEY set as current-user environment variable"
    }
} else {
    Write-Warn "No API key supplied. Set it later via tray icon > Settings or re-run with -ApiKey."
}
#endregion

#region Firewall rule
Write-Step "Configuring Windows Firewall rule (block external access to port 47777)..."

if ($PSCmdlet.ShouldProcess("Windows Firewall", "Add rule to block external access to port 47777")) {
    try {
        # Remove existing rule if present
        Remove-NetFirewallRule -DisplayName "PerplexityXPC Block External" -ErrorAction SilentlyContinue

        New-NetFirewallRule `
            -DisplayName   "PerplexityXPC Block External" `
            -Description   "Blocks all non-localhost inbound connections on PerplexityXPC HTTP port 47777" `
            -Direction     Inbound `
            -LocalPort     47777 `
            -Protocol      TCP `
            -RemoteAddress "0.0.0.0-126.255.255.255","128.0.0.0-255.255.255.255" `
            -Action        Block `
            -Profile       Any `
            -Enabled       True | Out-Null

        Write-Success "Firewall rule added — port 47777 blocked from non-localhost addresses"
    } catch {
        Write-Warn "Could not create firewall rule: $_"
        Write-Warn "Manually ensure port 47777 is not publicly accessible."
    }
}
#endregion

#region Windows Service registration
if (-not $NoService) {
    Write-Step "Registering Windows Service (PerplexityXPC)..."

    if ($PSCmdlet.ShouldProcess("PerplexityXPC", "Register Windows Service")) {
        try {
            # Remove existing service first if present
            $existingSvc = Get-Service -Name "PerplexityXPC" -ErrorAction SilentlyContinue
            if ($existingSvc) {
                if ($existingSvc.Status -eq 'Running') {
                    Stop-Service -Name "PerplexityXPC" -Force
                    Start-Sleep -Seconds 2
                }
                & sc.exe delete PerplexityXPC | Out-Null
                Start-Sleep -Seconds 1
            }

            $serviceExeDest = Join-Path $InstallPath "PerplexityXPC.Service.exe"

            & sc.exe create PerplexityXPC `
                binPath= "`"${serviceExeDest}`"" `
                start= auto `
                DisplayName= "PerplexityXPC Broker" | Out-Null

            & sc.exe description PerplexityXPC `
                "Local AI broker for Perplexity API and MCP servers" | Out-Null

            # Failure actions: restart after 5 s, 10 s, 30 s; reset counter after 24 h
            & sc.exe failure PerplexityXPC `
                reset= 86400 `
                actions= restart/5000/restart/10000/restart/30000 | Out-Null

            Write-Success "Service 'PerplexityXPC' registered (auto-start)"
        } catch {
            Write-Err "Failed to register service: $_"
            exit 1
        }
    }
}
#endregion

#region Context menu registration
if (-not $NoContextMenu) {
    Write-Step "Registering Explorer context menu entries..."

    if ($PSCmdlet.ShouldProcess("HKCU registry", "Register context menu entries")) {
        $registerScript = Join-Path $scriptDir "Register-ContextMenu.ps1"
        if (Test-Path $registerScript) {
            & $registerScript -InstallPath $InstallPath -ErrorAction Continue
        } else {
            Write-Warn "Register-ContextMenu.ps1 not found in $scriptDir — skipping context menu setup."
            Write-Warn "Run Register-ContextMenu.ps1 manually after copying it to the scripts folder."
        }
    }
}
#endregion

#region Tray app startup registration
if (-not $NoTrayStartup) {
    Write-Step "Adding tray application to Windows startup..."

    if ($PSCmdlet.ShouldProcess("HKCU Run registry", "Add PerplexityXPC.Tray to startup")) {
        try {
            $runKey     = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
            $trayExeDest = Join-Path $InstallPath "PerplexityXPC.Tray.exe"
            Set-ItemProperty -Path $runKey -Name "PerplexityXPCTray" -Value "`"${trayExeDest}`""
            Write-Success "Tray app added to startup (HKCU\\...\\Run\\PerplexityXPCTray)"
        } catch {
            Write-Warn "Failed to add tray to startup: $_"
        }
    }
}
#endregion

#region Start service and tray app
Write-Step "Starting PerplexityXPC..."

if (-not $NoService) {
    if ($PSCmdlet.ShouldProcess("PerplexityXPC service", "Start service")) {
        try {
            Start-Service -Name "PerplexityXPC"
            Start-Sleep -Seconds 2
            $svc = Get-Service -Name "PerplexityXPC"
            if ($svc.Status -eq 'Running') {
                Write-Success "Service started successfully"
            } else {
                Write-Warn "Service did not reach Running state (status: $($svc.Status))"
            }
        } catch {
            Write-Warn "Could not start service: $_"
        }
    }
}

if (-not $NoTrayStartup) {
    if ($PSCmdlet.ShouldProcess("PerplexityXPC.Tray.exe", "Launch tray application")) {
        try {
            $trayExeDest = Join-Path $InstallPath "PerplexityXPC.Tray.exe"
            Start-Process -FilePath $trayExeDest -WindowStyle Hidden
            Write-Success "Tray application launched"
        } catch {
            Write-Warn "Could not start tray app: $_"
        }
    }
}
#endregion

#region Success summary
if (-not $Quiet) {
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Green
    Write-Host "   PerplexityXPC installed successfully!" -ForegroundColor Green
    Write-Host "==========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Install path : $InstallPath" -ForegroundColor White
    Write-Host "  Data path    : $dataDir"     -ForegroundColor White
    Write-Host "  HTTP endpoint: http://localhost:47777"  -ForegroundColor White
    Write-Host "  Named pipe   : \\.\pipe\PerplexityXPCPipe" -ForegroundColor White
    Write-Host ""
    Write-Host "  Quick-start commands:" -ForegroundColor Yellow
    Write-Host "    Query via HTTP:"   -ForegroundColor DarkGray
    Write-Host '    Invoke-RestMethod http://localhost:47777/v1/query -Method Post -Body ''{"query":"Hello!"}'' -ContentType application/json' -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "    Check service status:" -ForegroundColor DarkGray
    Write-Host "    Get-Service PerplexityXPC" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "    View logs:" -ForegroundColor DarkGray
    Write-Host "    Get-Content `"$logsDir\service.log`" -Tail 50 -Wait" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  Right-click any file or folder in Explorer to use 'Ask Perplexity'." -ForegroundColor Cyan
    Write-Host ""
}
#endregion
