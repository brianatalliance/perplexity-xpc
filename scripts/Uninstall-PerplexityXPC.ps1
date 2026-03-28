<#
.SYNOPSIS
    Uninstalls PerplexityXPC — removes the service, context menus, firewall rule, and files.

.DESCRIPTION
    Stops and removes the PerplexityXPC Windows Service, kills the tray application,
    removes Explorer context menu registry entries, removes the startup registry entry,
    removes the Windows Firewall rule, and deletes installation files.

    By default the user data directory (%LOCALAPPDATA%\PerplexityXPC) is also removed.
    Use -KeepData to preserve configuration, logs, and the encrypted API key.

.PARAMETER KeepData
    Preserve the data directory (%LOCALAPPDATA%\PerplexityXPC) — keeps config, logs,
    and the encrypted API key.

.PARAMETER InstallPath
    Custom installation path to remove. Defaults to C:\Program Files\PerplexityXPC.

.PARAMETER Quiet
    Suppress informational output; only errors are shown.

.EXAMPLE
    .\Uninstall-PerplexityXPC.ps1

.EXAMPLE
    .\Uninstall-PerplexityXPC.ps1 -KeepData -Quiet

.NOTES
    Must be run as Administrator (the script will self-elevate if needed).
    Compatible with PowerShell 5.1 and PowerShell 7+.
#>

#region Parameters
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(HelpMessage = "Keep user data directory (config, logs, encrypted API key).")]
    [switch]$KeepData,

    [Parameter(HelpMessage = "Custom installation path to remove. Default: C:\Program Files\PerplexityXPC")]
    [string]$InstallPath = "C:\Program Files\PerplexityXPC",

    [Parameter(HelpMessage = "Suppress non-error output.")]
    [switch]$Quiet
)
#endregion

#region Helpers
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Status {
    param([string]$Message, [string]$Color = 'Cyan')
    if (-not $Quiet) { Write-Host $Message -ForegroundColor $Color }
}
function Write-Success { param([string]$Message) Write-Status "  [OK]   $Message" -Color Green }
function Write-Warn    { param([string]$Message) Write-Host "  [WARN] $Message" -ForegroundColor Yellow }
function Write-Err     { param([string]$Message) Write-Host "  [ERR]  $Message" -ForegroundColor Red }

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
    if ($KeepData)   { $argList += '-KeepData' }
    if ($Quiet)      { $argList += '-Quiet' }
    if ($InstallPath -ne "C:\Program Files\PerplexityXPC") { $argList += "-InstallPath `"$InstallPath`"" }

    Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -Verb RunAs -Wait
    exit $LASTEXITCODE
}
#endregion

#region Confirmation prompt
if (-not $Quiet -and -not $PSCmdlet.ShouldProcess("PerplexityXPC", "Uninstall")) {
    Write-Host "Uninstall cancelled." -ForegroundColor Yellow
    exit 0
}
#endregion

#region Banner
if (-not $Quiet) {
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Magenta
    Write-Host "   PerplexityXPC Uninstaller v1.0.0" -ForegroundColor Magenta
    Write-Host "==========================================" -ForegroundColor Magenta
    Write-Host ""
}
#endregion

#region Stop and remove Windows Service
Write-Step "Stopping and removing Windows Service..."

try {
    $svc = Get-Service -Name "PerplexityXPC" -ErrorAction SilentlyContinue
    if ($svc) {
        if ($svc.Status -in @('Running', 'Paused', 'StartPending', 'ContinuePending')) {
            Write-Status "  Stopping service..." -Color DarkGray
            Stop-Service -Name "PerplexityXPC" -Force -ErrorAction SilentlyContinue
            # Wait up to 15 s for the service to stop
            $waited = 0
            while ((Get-Service -Name "PerplexityXPC").Status -ne 'Stopped' -and $waited -lt 15) {
                Start-Sleep -Seconds 1
                $waited++
            }
        }
        & sc.exe delete PerplexityXPC | Out-Null
        Write-Success "Windows Service 'PerplexityXPC' removed"
    } else {
        Write-Status "  [SKIP] Service 'PerplexityXPC' not found — already removed or never installed." -Color DarkGray
    }
} catch {
    Write-Warn "Error removing service: $_"
}
#endregion

#region Kill tray app process
Write-Step "Stopping tray application..."

try {
    $trayProcs = Get-Process -Name "PerplexityXPC.Tray" -ErrorAction SilentlyContinue
    if ($trayProcs) {
        $trayProcs | Stop-Process -Force
        Write-Success "Tray application process terminated"
    } else {
        Write-Status "  [SKIP] Tray process not running." -Color DarkGray
    }
} catch {
    Write-Warn "Error killing tray process: $_"
}

# Also kill context menu helper if somehow still running
try {
    Get-Process -Name "PerplexityXPC.ContextMenu" -ErrorAction SilentlyContinue | Stop-Process -Force
} catch { }
#endregion

#region Remove context menu registry entries
Write-Step "Removing Explorer context menu registry entries..."

$contextMenuKeys = @(
    "HKCU:\Software\Classes\*\shell\PerplexityXPC",
    "HKCU:\Software\Classes\Directory\shell\PerplexityXPC",
    "HKCU:\Software\Classes\Directory\Background\shell\PerplexityXPC",
    "HKCU:\Software\Classes\Drive\shell\PerplexityXPC"
)

# Also remove per-extension file-type keys
$textExtensions = @('.txt','.md','.ps1','.py','.cs','.json','.xml','.yaml','.yml',
                    '.log','.csv','.cfg','.conf','.ini','.bat','.cmd','.sh')
foreach ($ext in $textExtensions) {
    $contextMenuKeys += "HKCU:\Software\Classes\$ext\shell\PerplexityXPC"
}

foreach ($key in $contextMenuKeys) {
    try {
        if (Test-Path $key) {
            Remove-Item -Path $key -Recurse -Force
            Write-Success "Removed: $key"
        }
    } catch {
        Write-Warn "Could not remove registry key ${key}: $_"
    }
}
#endregion

#region Remove startup registry entry
Write-Step "Removing startup registry entry..."

try {
    $runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    if ((Get-ItemProperty -Path $runKey -ErrorAction SilentlyContinue).PSObject.Properties.Name -contains "PerplexityXPCTray") {
        Remove-ItemProperty -Path $runKey -Name "PerplexityXPCTray" -Force
        Write-Success "Startup entry 'PerplexityXPCTray' removed"
    } else {
        Write-Status "  [SKIP] Startup entry not found." -Color DarkGray
    }
} catch {
    Write-Warn "Error removing startup entry: $_"
}
#endregion

#region Remove SendTo shortcut
Write-Step "Removing 'Send To' shortcut..."

try {
    $sendToDir      = [Environment]::GetFolderPath("SendTo")
    $sendToShortcut = Join-Path $sendToDir "Ask Perplexity.lnk"
    if (Test-Path $sendToShortcut) {
        Remove-Item -Path $sendToShortcut -Force
        Write-Success "Removed SendTo shortcut"
    } else {
        Write-Status "  [SKIP] SendTo shortcut not found." -Color DarkGray
    }
} catch {
    Write-Warn "Error removing SendTo shortcut: $_"
}
#endregion

#region Remove firewall rule
Write-Step "Removing Windows Firewall rule..."

try {
    $rule = Get-NetFirewallRule -DisplayName "PerplexityXPC Block External" -ErrorAction SilentlyContinue
    if ($rule) {
        Remove-NetFirewallRule -DisplayName "PerplexityXPC Block External"
        Write-Success "Firewall rule removed"
    } else {
        Write-Status "  [SKIP] Firewall rule not found." -Color DarkGray
    }
} catch {
    Write-Warn "Error removing firewall rule: $_"
}
#endregion

#region Remove install directory
Write-Step "Removing installation directory: $InstallPath"

try {
    if (Test-Path $InstallPath) {
        # Retry loop handles files locked by recently-stopped processes
        $maxAttempts = 3
        for ($i = 1; $i -le $maxAttempts; $i++) {
            try {
                Remove-Item -Path $InstallPath -Recurse -Force
                Write-Success "Installation directory removed: $InstallPath"
                break
            } catch {
                if ($i -lt $maxAttempts) {
                    Write-Status "  Waiting for file locks to release (attempt $i/$maxAttempts)..." -Color DarkGray
                    Start-Sleep -Seconds 3
                } else {
                    Write-Warn "Could not fully remove $InstallPath — some files may be locked."
                    Write-Warn "Delete manually after reboot: $InstallPath"
                }
            }
        }
    } else {
        Write-Status "  [SKIP] Install directory not found." -Color DarkGray
    }
} catch {
    Write-Warn "Error removing install directory: $_"
}
#endregion

#region Remove data directory
$dataDir = Join-Path $env:LOCALAPPDATA "PerplexityXPC"

if ($KeepData) {
    Write-Step "Preserving data directory (-KeepData specified)..."
    Write-Status "  [KEPT] $dataDir" -Color Yellow
} else {
    Write-Step "Removing data directory: $dataDir"
    try {
        if (Test-Path $dataDir) {
            Remove-Item -Path $dataDir -Recurse -Force
            Write-Success "Data directory removed: $dataDir"
        } else {
            Write-Status "  [SKIP] Data directory not found." -Color DarkGray
        }
    } catch {
        Write-Warn "Error removing data directory: $_"
        Write-Warn "Delete manually: $dataDir"
    }
}
#endregion

#region Remove PERPLEXITY_API_KEY environment variable
Write-Step "Removing PERPLEXITY_API_KEY environment variable..."

try {
    $existingEnv = [Environment]::GetEnvironmentVariable("PERPLEXITY_API_KEY", "User")
    if ($null -ne $existingEnv) {
        [Environment]::SetEnvironmentVariable("PERPLEXITY_API_KEY", $null, "User")
        Write-Success "PERPLEXITY_API_KEY user environment variable removed"
    } else {
        Write-Status "  [SKIP] PERPLEXITY_API_KEY not set for current user." -Color DarkGray
    }
} catch {
    Write-Warn "Error removing environment variable: $_"
}
#endregion

#region Done
if (-not $Quiet) {
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Green
    Write-Host "   PerplexityXPC uninstalled." -ForegroundColor Green
    Write-Host "==========================================" -ForegroundColor Green

    if ($KeepData) {
        Write-Host ""
        Write-Host "  Your data was preserved at:" -ForegroundColor Yellow
        Write-Host "    $dataDir" -ForegroundColor White
        Write-Host "  Delete it manually to fully clean up." -ForegroundColor Yellow
    }
    Write-Host ""
}
#endregion
