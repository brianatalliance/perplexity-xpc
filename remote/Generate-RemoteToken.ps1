<#
.SYNOPSIS
    Generates a secure random API token for PerplexityXPC RemoteGateway.

.DESCRIPTION
    Uses [System.Security.Cryptography.RandomNumberGenerator] to generate
    a cryptographically secure random token. Outputs a 64-character hex string
    (32 random bytes encoded as hex).

    Optionally updates the token in the RemoteGateway's appsettings.json
    and saves it to %USERPROFILE%\.perplexityxpc\remote-token.txt.

    Use this script to:
      - Generate an initial token before running Install-RemoteGateway.ps1
      - Rotate the token periodically for security

.PARAMETER ByteLength
    Number of random bytes to generate. The output will be ByteLength * 2
    hex characters long. Default: 32 (produces a 64-char hex string).

.PARAMETER UpdateConfig
    If specified, writes the new token into the RemoteGateway appsettings.json.
    Requires -InstallPath to point to the gateway install directory.

.PARAMETER InstallPath
    Path to the RemoteGateway install directory containing appsettings.json.
    Default: C:\Program Files\PerplexityXPC\Remote\

.PARAMETER SaveToFile
    If specified, saves the token to %USERPROFILE%\.perplexityxpc\remote-token.txt
    with permissions restricted to the current user.

.PARAMETER Quiet
    Suppress decorative output. Only print the raw token string.
    Useful for piping: $token = .\Generate-RemoteToken.ps1 -Quiet

.EXAMPLE
    .\Generate-RemoteToken.ps1
    Generates and displays a 64-char hex token.

.EXAMPLE
    .\Generate-RemoteToken.ps1 -SaveToFile
    Generates token and saves it to the token file.

.EXAMPLE
    .\Generate-RemoteToken.ps1 -UpdateConfig -SaveToFile
    Generates token, updates appsettings.json, and saves to file.

.EXAMPLE
    $token = .\Generate-RemoteToken.ps1 -Quiet
    Capture the token into a variable silently.

.NOTES
    Requires: PowerShell 5.1 or 7+
    Token strength: 256-bit entropy (32 bytes = 2^256 possible values)
    Encoding: lowercase hex (0-9, a-f)
#>

[CmdletBinding()]
param(
    [ValidateRange(16, 64)]
    [int]$ByteLength = 32,

    [switch]$UpdateConfig,

    [string]$InstallPath = 'C:\Program Files\PerplexityXPC\Remote\',

    [switch]$SaveToFile,

    [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$TokenDir  = Join-Path $env:USERPROFILE '.perplexityxpc'
$TokenFile = Join-Path $TokenDir 'remote-token.txt'

# ---------------------------------------------------------------------------
# Generate token
# ---------------------------------------------------------------------------
$rng   = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$bytes = New-Object byte[] $ByteLength
$rng.GetBytes($bytes)
$rng.Dispose()

$token = ($bytes | ForEach-Object { $_.ToString('x2') }) -join ''

# ---------------------------------------------------------------------------
# Quiet mode - just print the token
# ---------------------------------------------------------------------------
if ($Quiet) {
    Write-Output $token
    exit 0
}

# ---------------------------------------------------------------------------
# Normal mode - formatted output
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host 'PerplexityXPC - Remote API Token Generator' -ForegroundColor Cyan
Write-Host ('=' * 50) -ForegroundColor Cyan
Write-Host ''
Write-Host "Generated token ($($ByteLength * 2) hex chars, ${ByteLength}-byte entropy):" -ForegroundColor Yellow
Write-Host ''
Write-Host "  $token" -ForegroundColor Green
Write-Host ''
Write-Host 'Use this as a Bearer token in Authorization headers:' -ForegroundColor Gray
Write-Host "  Authorization: Bearer $token" -ForegroundColor Gray
Write-Host ''

# ---------------------------------------------------------------------------
# Save to file
# ---------------------------------------------------------------------------
if ($SaveToFile) {
    if (-not (Test-Path $TokenDir)) {
        New-Item -ItemType Directory -Path $TokenDir -Force | Out-Null
    }

    Set-Content -Path $TokenFile -Value $token -Encoding UTF8

    # Restrict permissions to the current user only
    try {
        $acl = Get-Acl $TokenFile
        $acl.SetAccessRuleProtection($true, $false)
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
            $env:USERNAME, 'FullControl', 'Allow'
        )
        $acl.AddAccessRule($rule)
        Set-Acl $TokenFile $acl
        Write-Host "[OK] Token saved (owner-only): $TokenFile" -ForegroundColor Green
    } catch {
        Write-Host "[OK] Token saved: $TokenFile" -ForegroundColor Green
        Write-Host "[WARN] Could not restrict file permissions: $_" -ForegroundColor Yellow
    }
}

# ---------------------------------------------------------------------------
# Update appsettings.json
# ---------------------------------------------------------------------------
if ($UpdateConfig) {
    $appSettingsPath = Join-Path $InstallPath 'appsettings.json'

    if (-not (Test-Path $appSettingsPath)) {
        Write-Host "[FAIL] appsettings.json not found at: $appSettingsPath" -ForegroundColor Red
        Write-Host "       Specify -InstallPath to point to your RemoteGateway install directory." -ForegroundColor Red
        exit 1
    }

    $settings = Get-Content $appSettingsPath -Raw | ConvertFrom-Json

    if (-not ($settings | Get-Member -Name 'RemoteGateway' -ErrorAction SilentlyContinue)) {
        $settings | Add-Member -NotePropertyName 'RemoteGateway' -NotePropertyValue ([PSCustomObject]@{})
    }

    $settings.RemoteGateway | Add-Member -NotePropertyName 'ApiToken' -NotePropertyValue $token -Force
    $settings | ConvertTo-Json -Depth 10 | Set-Content $appSettingsPath -Encoding UTF8

    Write-Host "[OK] Token written to: $appSettingsPath" -ForegroundColor Green
    Write-Host ''
    Write-Host '[INFO] Restart the PerplexityXPCRemote service to apply the new token:' -ForegroundColor Yellow
    Write-Host '       Restart-Service -Name PerplexityXPCRemote' -ForegroundColor White
}

Write-Host ''
Write-Host 'Token generation complete.' -ForegroundColor Cyan
