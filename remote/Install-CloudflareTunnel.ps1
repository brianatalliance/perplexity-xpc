<#
.SYNOPSIS
    Sets up Cloudflare Tunnel for PerplexityXPC remote access.

.DESCRIPTION
    This script automates the installation and configuration of a Cloudflare Tunnel
    that routes external HTTPS traffic to the local PerplexityXPC RemoteGateway
    running on http://127.0.0.1:47778.

    Steps performed:
      1. Checks if cloudflared is installed, offers to install via winget
      2. Guides the user through cloudflared login (opens browser for Cloudflare auth)
      3. Creates a tunnel named "perplexity-xpc" (or custom name)
      4. Configures the tunnel to route to http://127.0.0.1:47778
      5. Creates the config file at %USERPROFILE%\.cloudflared\config.yml
      6. Installs cloudflared as a Windows Service
      7. Starts the tunnel
      8. Displays the tunnel URL

.PARAMETER Hostname
    The full hostname to use for the tunnel (e.g., "xpc.yourdomain.com").
    Required for initial setup. Must be a domain managed in Cloudflare DNS.

.PARAMETER TunnelName
    The name of the Cloudflare Tunnel to create. Default: "perplexity-xpc"

.PARAMETER Uninstall
    Removes the cloudflared Windows Service, deletes the tunnel, and removes
    the config file. Use to cleanly tear down remote access.

.PARAMETER Status
    Checks the current status of the cloudflared Windows Service and tunnel.

.EXAMPLE
    .\Install-CloudflareTunnel.ps1 -Hostname "xpc.yourdomain.com"
    Full setup with a custom hostname.

.EXAMPLE
    .\Install-CloudflareTunnel.ps1 -Hostname "xpc.yourdomain.com" -TunnelName "my-tunnel"
    Full setup with a custom tunnel name.

.EXAMPLE
    .\Install-CloudflareTunnel.ps1 -Status
    Check tunnel service status.

.EXAMPLE
    .\Install-CloudflareTunnel.ps1 -Uninstall
    Remove the tunnel and service.

.NOTES
    Requires: Windows 10/11, PowerShell 5.1 or 7+
    Requires: cloudflared (auto-installed via winget if not present)
    Requires: A Cloudflare account with a domain added
    Port: 47778 (PerplexityXPC RemoteGateway)
#>

[CmdletBinding(DefaultParameterSetName = 'Setup')]
param(
    [Parameter(ParameterSetName = 'Setup')]
    [string]$Hostname = '',

    [Parameter(ParameterSetName = 'Setup')]
    [string]$TunnelName = 'perplexity-xpc',

    [Parameter(ParameterSetName = 'Uninstall')]
    [switch]$Uninstall,

    [Parameter(ParameterSetName = 'Status')]
    [switch]$Status
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------
$ServiceName    = 'cloudflared'
$GatewayPort    = 47778
$GatewayUrl     = "http://127.0.0.1:$GatewayPort"
$CloudflaredDir = Join-Path $env:USERPROFILE '.cloudflared'
$ConfigPath     = Join-Path $CloudflaredDir 'config.yml'

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
function Write-Header {
    param([string]$Text)
    Write-Host ''
    Write-Host ('=' * 60) -ForegroundColor Cyan
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host ('=' * 60) -ForegroundColor Cyan
    Write-Host ''
}

function Write-Step {
    param([int]$Number, [string]$Text)
    Write-Host "[Step $Number] $Text" -ForegroundColor Yellow
}

function Write-OK {
    param([string]$Text)
    Write-Host "[OK] $Text" -ForegroundColor Green
}

function Write-Fail {
    param([string]$Text)
    Write-Host "[FAIL] $Text" -ForegroundColor Red
}

function Write-Info {
    param([string]$Text)
    Write-Host "[INFO] $Text" -ForegroundColor White
}

function Test-Admin {
    $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $p  = [System.Security.Principal.WindowsPrincipal]$id
    return $p.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-CloudflaredInstalled {
    $cmd = Get-Command 'cloudflared' -ErrorAction SilentlyContinue
    return ($null -ne $cmd)
}

function Install-Cloudflared {
    Write-Step 1 'Installing cloudflared via winget...'

    $winget = Get-Command 'winget' -ErrorAction SilentlyContinue
    if ($null -eq $winget) {
        Write-Fail 'winget not found. Please install cloudflared manually from:'
        Write-Host '  https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads/' -ForegroundColor Cyan
        exit 1
    }

    & winget install --id Cloudflare.cloudflared --silent --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        Write-Fail 'winget install failed. Check your internet connection and try again.'
        exit 1
    }

    # Refresh PATH so cloudflared is available in the current session
    $env:PATH = [System.Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' +
                [System.Environment]::GetEnvironmentVariable('PATH', 'User')

    if (-not (Test-CloudflaredInstalled)) {
        Write-Fail 'cloudflared still not found after install. Please restart your terminal.'
        exit 1
    }

    Write-OK 'cloudflared installed successfully.'
}

function Get-TunnelId {
    param([string]$Name)
    $output = & cloudflared tunnel list 2>&1
    foreach ($line in $output) {
        # Lines look like: <id>  <name>  <created>  ...
        if ($line -match '^\s*([0-9a-f\-]{36})\s+' + [regex]::Escape($Name)) {
            return $Matches[1]
        }
    }
    return $null
}

# ---------------------------------------------------------------------------
# Status mode
# ---------------------------------------------------------------------------
if ($Status) {
    Write-Header 'PerplexityXPC Cloudflare Tunnel - Status'

    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -eq $svc) {
        Write-Info "Windows Service '$ServiceName' is not installed."
    } else {
        $color = if ($svc.Status -eq 'Running') { 'Green' } else { 'Red' }
        Write-Host "Service '$ServiceName': $($svc.Status)" -ForegroundColor $color
    }

    if (Test-Path $ConfigPath) {
        Write-OK "Config file found: $ConfigPath"
        Write-Host ''
        Get-Content $ConfigPath
    } else {
        Write-Info "No config file at: $ConfigPath"
    }

    if (Test-CloudflaredInstalled) {
        Write-Host ''
        Write-Info 'Tunnel list:'
        & cloudflared tunnel list
    }

    exit 0
}

# ---------------------------------------------------------------------------
# Uninstall mode
# ---------------------------------------------------------------------------
if ($Uninstall) {
    Write-Header 'PerplexityXPC Cloudflare Tunnel - Uninstall'

    if (-not (Test-Admin)) {
        Write-Fail 'Uninstall requires Administrator privileges. Run PowerShell as Administrator.'
        exit 1
    }

    # Stop and remove service
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc) {
        Write-Step 1 "Stopping service '$ServiceName'..."
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        & cloudflared service uninstall 2>&1 | Out-Null
        Write-OK 'Service removed.'
    } else {
        Write-Info 'Service not found, skipping.'
    }

    # Delete tunnel by reading ID from config
    if (Test-Path $ConfigPath) {
        $configContent = Get-Content $ConfigPath -Raw
        if ($configContent -match 'tunnel:\s*([0-9a-f\-]{36})') {
            $tid = $Matches[1]
            Write-Step 2 "Deleting tunnel $tid..."
            & cloudflared tunnel delete $tid --force 2>&1 | Out-Null
            Write-OK 'Tunnel deleted.'
        }

        Write-Step 3 "Removing config file: $ConfigPath"
        Remove-Item $ConfigPath -Force
        Write-OK 'Config removed.'
    } else {
        Write-Info 'No config file found, skipping tunnel deletion.'
    }

    Write-OK 'Uninstall complete.'
    exit 0
}

# ---------------------------------------------------------------------------
# Setup mode
# ---------------------------------------------------------------------------
Write-Header 'PerplexityXPC Cloudflare Tunnel - Setup'

if (-not (Test-Admin)) {
    Write-Fail 'Setup requires Administrator privileges. Run PowerShell as Administrator.'
    exit 1
}

if ([string]::IsNullOrWhiteSpace($Hostname)) {
    Write-Fail '-Hostname is required for setup. Example: -Hostname "xpc.yourdomain.com"'
    exit 1
}

# ---- Step 1: Check / install cloudflared ----
Write-Step 1 'Checking for cloudflared...'
if (Test-CloudflaredInstalled) {
    $ver = (& cloudflared --version 2>&1) | Select-Object -First 1
    Write-OK "cloudflared is installed: $ver"
} else {
    Write-Info 'cloudflared not found.'
    $install = Read-Host 'Install cloudflared via winget? (Y/N)'
    if ($install -notmatch '^[Yy]') {
        Write-Fail 'cloudflared is required. Exiting.'
        exit 1
    }
    Install-Cloudflared
}

# ---- Step 2: Login to Cloudflare ----
Write-Step 2 'Logging in to Cloudflare (browser will open)...'
Write-Info 'If a browser window does not open, visit the URL shown in the output.'

$certPath = Join-Path $CloudflaredDir 'cert.pem'
if (Test-Path $certPath) {
    Write-OK "Already authenticated (cert.pem found at $certPath)"
} else {
    & cloudflared tunnel login
    if ($LASTEXITCODE -ne 0) {
        Write-Fail 'Cloudflare login failed. Please try again.'
        exit 1
    }
    Write-OK 'Login successful.'
}

# ---- Step 3: Create tunnel ----
Write-Step 3 "Creating tunnel '$TunnelName'..."
$existingId = Get-TunnelId -Name $TunnelName
if ($existingId) {
    Write-Info "Tunnel '$TunnelName' already exists with ID: $existingId"
    $TunnelId = $existingId
} else {
    $createOutput = & cloudflared tunnel create $TunnelName 2>&1
    Write-Verbose ($createOutput | Out-String)

    $TunnelId = Get-TunnelId -Name $TunnelName
    if (-not $TunnelId) {
        # Try parsing from create output directly
        foreach ($line in $createOutput) {
            if ($line -match 'Created tunnel .+ with id ([0-9a-f\-]{36})') {
                $TunnelId = $Matches[1]
                break
            }
        }
    }

    if (-not $TunnelId) {
        Write-Fail 'Could not retrieve tunnel ID after creation. Check cloudflared output above.'
        exit 1
    }
    Write-OK "Tunnel created. ID: $TunnelId"
}

# ---- Step 4: Create config.yml ----
Write-Step 4 "Writing config file to $ConfigPath..."

if (-not (Test-Path $CloudflaredDir)) {
    New-Item -ItemType Directory -Path $CloudflaredDir -Force | Out-Null
}

$CredentialsFile = Join-Path $CloudflaredDir "$TunnelId.json"

$configYml = @"
# PerplexityXPC Cloudflare Tunnel Configuration
# Generated by Install-CloudflareTunnel.ps1
# Tunnel Name: $TunnelName
# Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')

tunnel: $TunnelId
credentials-file: $CredentialsFile

ingress:
  - hostname: $Hostname
    service: $GatewayUrl
    originRequest:
      noTLSVerify: true
      connectTimeout: 30s
      tcpKeepAlive: 30s
  - service: http_status:404
"@

Set-Content -Path $ConfigPath -Value $configYml -Encoding UTF8
Write-OK "Config written to $ConfigPath"

# ---- Step 5: Create DNS record ----
Write-Step 5 "Creating DNS CNAME record for $Hostname..."
Write-Info "This maps $Hostname to your tunnel in Cloudflare DNS."

& cloudflared tunnel route dns $TunnelName $Hostname 2>&1 | ForEach-Object {
    Write-Host "  $_"
}
Write-OK "DNS route configured."

# ---- Step 6: Install as Windows Service ----
Write-Step 6 'Installing cloudflared as a Windows Service...'

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    Write-Info "Service '$ServiceName' already exists. Stopping to reconfigure..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    & cloudflared service uninstall 2>&1 | Out-Null
}

& cloudflared service install
if ($LASTEXITCODE -ne 0) {
    Write-Fail 'Service install failed. Check cloudflared output above.'
    exit 1
}
Write-OK 'Windows Service installed.'

# ---- Step 7: Start the service ----
Write-Step 7 "Starting service '$ServiceName'..."
Start-Service -Name $ServiceName
Start-Sleep -Seconds 2

$svc = Get-Service -Name $ServiceName
if ($svc.Status -eq 'Running') {
    Write-OK "Service is running."
} else {
    Write-Fail "Service did not start (status: $($svc.Status)). Check Event Viewer for errors."
    exit 1
}

# ---- Step 8: Display summary ----
Write-Header 'Setup Complete'
Write-Host "  Tunnel Name:      $TunnelName" -ForegroundColor White
Write-Host "  Tunnel ID:        $TunnelId" -ForegroundColor White
Write-Host "  Tunnel URL:       https://$Hostname" -ForegroundColor Cyan
Write-Host "  Local Gateway:    $GatewayUrl" -ForegroundColor White
Write-Host "  Config File:      $ConfigPath" -ForegroundColor White
Write-Host "  Service Status:   $($svc.Status)" -ForegroundColor Green
Write-Host ''
Write-Host 'Next steps:' -ForegroundColor Yellow
Write-Host "  1. Set up Cloudflare Access policy at dash.cloudflare.com" -ForegroundColor White
Write-Host "     -> Zero Trust -> Access -> Applications -> Add application" -ForegroundColor White
Write-Host "  2. Get your API token: .\Generate-RemoteToken.ps1" -ForegroundColor White
Write-Host "  3. Test access: .\Test-RemoteAccess.ps1 -TunnelUrl https://$Hostname -ApiToken <token>" -ForegroundColor White
Write-Host ''
