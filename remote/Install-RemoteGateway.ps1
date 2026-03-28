<#
.SYNOPSIS
    Builds, installs, and configures the PerplexityXPC RemoteGateway as a Windows Service.

.DESCRIPTION
    This script:
      1. Builds the RemoteGateway project (dotnet publish)
      2. Generates a secure random API token (32-char hex, 64 hex chars output) if not supplied
      3. Stores the token in the gateway's appsettings.json AND saves it locally for reference
      4. Registers the RemoteGateway as a Windows Service named "PerplexityXPCRemote"
      5. Configures Windows Firewall to block external direct access (Cloudflare Tunnel only)
      6. Starts the service

    The RemoteGateway listens on http://127.0.0.1:47778 by default (loopback only).
    All external access must flow through the Cloudflare Tunnel.

.PARAMETER ApiToken
    Pre-supply an API token instead of auto-generating one.
    Token is used as a Bearer token in Authorization headers.
    If omitted, a 64-character hex token is generated automatically.

.PARAMETER InstallPath
    The directory where the gateway binaries are installed.
    Default: C:\Program Files\PerplexityXPC\Remote\

.PARAMETER Port
    The port the RemoteGateway listens on.
    Default: 47778

.PARAMETER SourcePath
    Path to the RemoteGateway source directory (where the .csproj lives).
    Default: searches relative to this script's location.

.PARAMETER Uninstall
    Stops and removes the PerplexityXPCRemote service and optionally deletes binaries.

.EXAMPLE
    .\Install-RemoteGateway.ps1
    Auto-generates a token and installs the gateway.

.EXAMPLE
    .\Install-RemoteGateway.ps1 -ApiToken "your64hextoken"
    Install with a pre-defined token.

.EXAMPLE
    .\Install-RemoteGateway.ps1 -Port 47779 -InstallPath "D:\PerplexityXPC\Remote\"
    Custom port and install path.

.EXAMPLE
    .\Install-RemoteGateway.ps1 -Uninstall
    Remove the service.

.NOTES
    Requires: .NET 8+ SDK (dotnet CLI), PowerShell 5.1 or 7+
    Requires: Administrator privileges
    Service Name: PerplexityXPCRemote
    Token file saved to: %USERPROFILE%\.perplexityxpc\remote-token.txt
#>

[CmdletBinding(DefaultParameterSetName = 'Install')]
param(
    [Parameter(ParameterSetName = 'Install')]
    [string]$ApiToken = '',

    [Parameter(ParameterSetName = 'Install')]
    [string]$InstallPath = 'C:\Program Files\PerplexityXPC\Remote\',

    [Parameter(ParameterSetName = 'Install')]
    [int]$Port = 47778,

    [Parameter(ParameterSetName = 'Install')]
    [string]$SourcePath = '',

    [Parameter(ParameterSetName = 'Uninstall')]
    [switch]$Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------
$ServiceName    = 'PerplexityXPCRemote'
$ServiceDisplay = 'PerplexityXPC Remote Gateway'
$ServiceDesc    = 'Exposes PerplexityXPC broker capabilities via a secure HTTP API for remote access through Cloudflare Tunnel.'
$TokenDir       = Join-Path $env:USERPROFILE '.perplexityxpc'
$TokenFile      = Join-Path $TokenDir 'remote-token.txt'
$FirewallRule   = 'PerplexityXPC-RemoteGateway-Block'

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

function New-SecureToken {
    param([int]$ByteLength = 32)
    $rng   = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $bytes = New-Object byte[] $ByteLength
    $rng.GetBytes($bytes)
    $rng.Dispose()
    return ($bytes | ForEach-Object { $_.ToString('x2') }) -join ''
}

function Find-SourcePath {
    # Look for RemoteGateway project relative to script location
    $candidates = @(
        (Join-Path $PSScriptRoot '..\..\src\RemoteGateway'),
        (Join-Path $PSScriptRoot '..\RemoteGateway'),
        (Join-Path $PSScriptRoot 'RemoteGateway')
    )
    foreach ($c in $candidates) {
        $resolved = Resolve-Path $c -ErrorAction SilentlyContinue
        if ($resolved -and (Test-Path (Join-Path $resolved '*.csproj'))) {
            return $resolved.Path
        }
    }
    return $null
}

# ---------------------------------------------------------------------------
# Uninstall mode
# ---------------------------------------------------------------------------
if ($Uninstall) {
    Write-Header 'RemoteGateway - Uninstall'

    if (-not (Test-Admin)) {
        Write-Fail 'Uninstall requires Administrator privileges.'
        exit 1
    }

    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc) {
        Write-Step 1 "Stopping service '$ServiceName'..."
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        & sc.exe delete $ServiceName | Out-Null
        Write-OK 'Service removed.'
    } else {
        Write-Info 'Service not found, skipping.'
    }

    Write-Step 2 'Removing firewall rule...'
    Remove-NetFirewallRule -Name $FirewallRule -ErrorAction SilentlyContinue
    Write-OK 'Firewall rule removed.'

    Write-Step 3 'Checking installed files...'
    if (Test-Path $InstallPath) {
        $confirm = Read-Host "Delete install directory '$InstallPath'? (Y/N)"
        if ($confirm -match '^[Yy]') {
            Remove-Item -Path $InstallPath -Recurse -Force
            Write-OK "Deleted $InstallPath"
        } else {
            Write-Info 'Install directory kept.'
        }
    }

    Write-OK 'Uninstall complete.'
    exit 0
}

# ---------------------------------------------------------------------------
# Install mode
# ---------------------------------------------------------------------------
Write-Header 'PerplexityXPC RemoteGateway - Install'

if (-not (Test-Admin)) {
    Write-Fail 'Install requires Administrator privileges. Run PowerShell as Administrator.'
    exit 1
}

# ---- Step 1: Locate source ----
Write-Step 1 'Locating RemoteGateway source...'

if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $SourcePath = Find-SourcePath
}

if (-not $SourcePath -or -not (Test-Path $SourcePath)) {
    Write-Fail "RemoteGateway source not found. Specify -SourcePath explicitly."
    Write-Info "Expected a directory containing a .csproj file."
    exit 1
}

$projFile = Get-ChildItem -Path $SourcePath -Filter '*.csproj' | Select-Object -First 1
if (-not $projFile) {
    Write-Fail "No .csproj file found in $SourcePath"
    exit 1
}

Write-OK "Source found: $SourcePath"
Write-OK "Project: $($projFile.Name)"

# ---- Step 2: Check .NET SDK ----
Write-Step 2 'Checking .NET SDK...'
$dotnet = Get-Command 'dotnet' -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Fail '.NET SDK not found. Install from https://dot.net'
    exit 1
}
$dotnetVer = (& dotnet --version 2>&1)
Write-OK ".NET SDK: $dotnetVer"

# ---- Step 3: Generate / validate API token ----
Write-Step 3 'Preparing API token...'

if ([string]::IsNullOrWhiteSpace($ApiToken)) {
    $ApiToken = New-SecureToken -ByteLength 32
    Write-OK "Generated API token (64 hex chars)."
} else {
    if ($ApiToken.Length -lt 16) {
        Write-Fail 'Supplied token is too short (minimum 16 characters). Use a stronger token.'
        exit 1
    }
    Write-OK 'Using supplied API token.'
}

# Save token for the user
if (-not (Test-Path $TokenDir)) {
    New-Item -ItemType Directory -Path $TokenDir -Force | Out-Null
}
Set-Content -Path $TokenFile -Value $ApiToken -Encoding UTF8

# Restrict token file permissions to current user only
$acl = Get-Acl $TokenFile
$acl.SetAccessRuleProtection($true, $false)
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    $env:USERNAME, 'FullControl', 'Allow'
)
$acl.AddAccessRule($rule)
Set-Acl $TokenFile $acl

Write-OK "Token saved to: $TokenFile"
Write-Info "(Keep this file secure - treat it like a password)"

# ---- Step 4: Build and publish ----
Write-Step 4 'Building RemoteGateway (dotnet publish)...'

$publishDir = Join-Path $env:TEMP "perplexityxpc-remote-publish"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

& dotnet publish $projFile.FullName `
    --configuration Release `
    --output $publishDir `
    --self-contained false `
    --runtime win-x64 `
    -p:PublishSingleFile=false `
    2>&1 | ForEach-Object { Write-Verbose $_ }

if ($LASTEXITCODE -ne 0) {
    Write-Fail 'Build failed. Check error output above.'
    exit 1
}
Write-OK 'Build successful.'

# ---- Step 5: Install binaries ----
Write-Step 5 "Installing to $InstallPath..."

if (-not (Test-Path $InstallPath)) {
    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
}

Copy-Item -Path (Join-Path $publishDir '*') -Destination $InstallPath -Recurse -Force
Write-OK "Files installed to $InstallPath"

# ---- Step 6: Write API token into appsettings.json ----
Write-Step 6 'Configuring API token in appsettings.json...'

$appSettingsPath = Join-Path $InstallPath 'appsettings.json'
if (Test-Path $appSettingsPath) {
    $settings = Get-Content $appSettingsPath -Raw | ConvertFrom-Json
    if (-not ($settings | Get-Member -Name 'RemoteGateway' -ErrorAction SilentlyContinue)) {
        $settings | Add-Member -NotePropertyName 'RemoteGateway' -NotePropertyValue ([PSCustomObject]@{})
    }
    $settings.RemoteGateway | Add-Member -NotePropertyName 'ApiToken' -NotePropertyValue $ApiToken -Force
    $settings.RemoteGateway | Add-Member -NotePropertyName 'Port' -NotePropertyValue $Port -Force
    $settings | ConvertTo-Json -Depth 10 | Set-Content $appSettingsPath -Encoding UTF8
} else {
    # Create minimal appsettings.json
    $settings = [ordered]@{
        RemoteGateway = [ordered]@{
            ApiToken = $ApiToken
            Port     = $Port
            AllowedHosts = '127.0.0.1'
        }
        Logging = [ordered]@{
            LogLevel = [ordered]@{
                Default     = 'Information'
                Microsoft   = 'Warning'
            }
        }
    }
    $settings | ConvertTo-Json -Depth 10 | Set-Content $appSettingsPath -Encoding UTF8
}
Write-OK 'appsettings.json updated.'

# ---- Step 7: Register Windows Service ----
Write-Step 7 "Registering Windows Service '$ServiceName'..."

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    Write-Info 'Service already exists. Stopping and replacing...'
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# Find the executable
$exeFiles = Get-ChildItem -Path $InstallPath -Filter '*.exe' | Where-Object {
    $_.Name -notlike 'dotnet*'
}
if (-not $exeFiles) {
    Write-Fail "No .exe found in $InstallPath"
    exit 1
}
$exePath = $exeFiles[0].FullName

& sc.exe create $ServiceName `
    binPath= "`"$exePath`"" `
    DisplayName= $ServiceDisplay `
    start= auto

if ($LASTEXITCODE -ne 0) {
    Write-Fail 'Service registration failed.'
    exit 1
}

& sc.exe description $ServiceName $ServiceDesc | Out-Null
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

Write-OK "Service '$ServiceName' registered."

# ---- Step 8: Configure firewall ----
Write-Step 8 'Configuring Windows Firewall...'
Write-Info "Blocking direct external access to port $Port (loopback only - Cloudflare Tunnel routes internally)."

# Remove old rule if exists
Remove-NetFirewallRule -Name $FirewallRule -ErrorAction SilentlyContinue

# Block inbound connections to this port from non-loopback addresses
New-NetFirewallRule `
    -Name $FirewallRule `
    -DisplayName 'PerplexityXPC Remote Gateway - External Block' `
    -Description "Blocks direct external access to RemoteGateway port $Port. Access via Cloudflare Tunnel only." `
    -Direction Inbound `
    -Protocol TCP `
    -LocalPort $Port `
    -RemoteAddress LocalSubnet `
    -Action Block `
    -Enabled True | Out-Null

Write-OK "Firewall rule added: port $Port blocked from non-loopback sources."

# ---- Step 9: Start service ----
Write-Step 9 "Starting service '$ServiceName'..."
Start-Service -Name $ServiceName
Start-Sleep -Seconds 3

$svc = Get-Service -Name $ServiceName
$color = if ($svc.Status -eq 'Running') { 'Green' } else { 'Red' }
Write-Host "[INFO] Service status: $($svc.Status)" -ForegroundColor $color

if ($svc.Status -ne 'Running') {
    Write-Fail 'Service failed to start. Check Event Viewer -> Application log for errors.'
    exit 1
}

# ---- Summary ----
Write-Header 'Installation Complete'
Write-Host "  Service Name:    $ServiceName" -ForegroundColor White
Write-Host "  Status:          $($svc.Status)" -ForegroundColor Green
Write-Host "  Listening on:    http://127.0.0.1:$Port" -ForegroundColor White
Write-Host "  Install Path:    $InstallPath" -ForegroundColor White
Write-Host "  Token File:      $TokenFile" -ForegroundColor White
Write-Host ''
Write-Host '  API Token (keep this secure):' -ForegroundColor Yellow
Write-Host "  $ApiToken" -ForegroundColor Cyan
Write-Host ''
Write-Host 'Next steps:' -ForegroundColor Yellow
Write-Host "  1. Set up the Cloudflare Tunnel: .\Install-CloudflareTunnel.ps1 -Hostname xpc.yourdomain.com" -ForegroundColor White
Write-Host "  2. Test local access: .\Test-RemoteAccess.ps1 -ApiToken <above>" -ForegroundColor White
Write-Host ''
Write-Host "Your token is also saved at: $TokenFile" -ForegroundColor Gray
