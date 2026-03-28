<#
.SYNOPSIS
    Registers or unregisters PerplexityXPC Explorer context menu entries.

.DESCRIPTION
    Adds right-click menu entries to Windows Explorer for:
    - Text-based files  → "Ask Perplexity about this file"
    - Folders           → "Ask Perplexity about this folder"
    - Folder background → "Ask Perplexity about this folder"
    - A "Send To" shortcut for multi-file selection

    All entries are written to HKCU (no elevation required for registration itself).
    Pass -Unregister to remove all entries.

.PARAMETER InstallPath
    Directory containing PerplexityXPC.ContextMenu.exe and the application icon.
    Defaults to C:\Program Files\PerplexityXPC.

.PARAMETER Unregister
    Remove all context menu registry entries and the SendTo shortcut.

.PARAMETER Quiet
    Suppress informational output.

.EXAMPLE
    .\Register-ContextMenu.ps1

.EXAMPLE
    .\Register-ContextMenu.ps1 -InstallPath "D:\Apps\PerplexityXPC"

.EXAMPLE
    .\Register-ContextMenu.ps1 -Unregister

.NOTES
    Compatible with PowerShell 5.1 and PowerShell 7+.
    HKCU-only - does NOT require elevation.
#>

#region Parameters
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(HelpMessage = "Installation directory containing the ContextMenu exe and icon.")]
    [string]$InstallPath = "C:\Program Files\PerplexityXPC",

    [Parameter(HelpMessage = "Remove all context menu registry entries.")]
    [switch]$Unregister,

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
function Write-Success { param([string]$Message) Write-Status "  [OK] $Message" -Color Green }
function Write-Warn    { param([string]$Message) Write-Host "  [WARN] $Message" -ForegroundColor Yellow }

function Write-Step {
    param([string]$Message)
    if (-not $Quiet) {
        Write-Host ""
        Write-Host "==> $Message" -ForegroundColor Cyan
    }
}

# Ensure a registry key and its parents exist
function Ensure-RegistryKey {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        New-Item -Path $Path -Force | Out-Null
    }
}
#endregion

#region Constants
$contextMenuExe = Join-Path $InstallPath "PerplexityXPC.ContextMenu.exe"
$iconPath       = Join-Path $InstallPath "perplexity.ico"

# Text-based file extensions that make sense to send to Perplexity
$textExtensions = @(
    '.txt', '.md', '.markdown',
    '.ps1', '.psm1', '.psd1',
    '.py', '.rb', '.js', '.ts', '.jsx', '.tsx',
    '.cs', '.vb', '.fs', '.cpp', '.c', '.h', '.java', '.go', '.rs',
    '.json', '.jsonc',
    '.xml', '.html', '.htm', '.xhtml', '.svg',
    '.yaml', '.yml',
    '.toml', '.cfg', '.conf', '.ini', '.env',
    '.log',
    '.csv', '.tsv',
    '.bat', '.cmd', '.sh', '.bash', '.zsh', '.fish',
    '.sql',
    '.dockerfile', '.makefile'
)

$menuLabel    = "Ask Perplexity about this file"
$folderLabel  = "Ask Perplexity about this folder"
$iconValue    = "`"${iconPath}`",0"
#endregion

#region Unregister path
if ($Unregister) {
    Write-Step "Removing PerplexityXPC context menu entries..."

    $keysToRemove = @(
        "HKCU:\Software\Classes\*\shell\PerplexityXPC",
        "HKCU:\Software\Classes\Directory\shell\PerplexityXPC",
        "HKCU:\Software\Classes\Directory\Background\shell\PerplexityXPC",
        "HKCU:\Software\Classes\Drive\shell\PerplexityXPC"
    )
    foreach ($ext in $textExtensions) {
        # Normalize: extensions like .dockerfile have no registry-friendly alias; register by extension directly
        $keysToRemove += "HKCU:\Software\Classes\${ext}\shell\PerplexityXPC"
    }

    foreach ($key in $keysToRemove) {
        if (Test-Path $key) {
            if ($PSCmdlet.ShouldProcess($key, "Remove registry key")) {
                Remove-Item -Path $key -Recurse -Force
                Write-Success "Removed: $key"
            }
        }
    }

    # Remove SendTo shortcut
    $sendToDir      = [Environment]::GetFolderPath("SendTo")
    $sendToShortcut = Join-Path $sendToDir "Ask Perplexity.lnk"
    if (Test-Path $sendToShortcut) {
        if ($PSCmdlet.ShouldProcess($sendToShortcut, "Remove SendTo shortcut")) {
            Remove-Item -Path $sendToShortcut -Force
            Write-Success "Removed SendTo shortcut: $sendToShortcut"
        }
    }

    Write-Status "`nContext menu entries removed." -Color Green
    return
}
#endregion

#region Validate executable exists
if (-not (Test-Path $contextMenuExe)) {
    Write-Warn "ContextMenu executable not found at: $contextMenuExe"
    Write-Warn "Proceeding with registration - ensure the exe is placed there before using the menu."
}
#endregion

#region Register generic wildcard file handler (all files)
Write-Step "Registering file context menu (all files via *\shell)..."

# HKCU:\Software\Classes\*\shell\PerplexityXPC
$fileShellKey   = "HKCU:\Software\Classes\*\shell\PerplexityXPC"
$fileCmdKey     = "$fileShellKey\command"

if ($PSCmdlet.ShouldProcess($fileShellKey, "Create registry key")) {
    Ensure-RegistryKey $fileShellKey
    Set-ItemProperty -Path $fileShellKey -Name '(Default)'      -Value $menuLabel
    Set-ItemProperty -Path $fileShellKey -Name 'Icon'           -Value $iconValue
    Set-ItemProperty -Path $fileShellKey -Name 'MUIVerb'        -Value $menuLabel

    # AppliesTo restricts to text-based extensions only
    $appliesTo = ($textExtensions | ForEach-Object { "System.FileName:*$_" }) -join " OR "
    Set-ItemProperty -Path $fileShellKey -Name 'AppliesTo' -Value $appliesTo

    Ensure-RegistryKey $fileCmdKey
    Set-ItemProperty -Path $fileCmdKey -Name '(Default)' -Value "`"${contextMenuExe}`" --file `"%1`""
    Write-Success "File context menu registered (*\shell\PerplexityXPC)"
}
#endregion

#region Register per-extension keys for reliability
Write-Step "Registering per-extension file type entries..."

foreach ($ext in $textExtensions) {
    $extKey  = "HKCU:\Software\Classes\${ext}\shell\PerplexityXPC"
    $extCmd  = "${extKey}\command"

    if ($PSCmdlet.ShouldProcess($extKey, "Create registry key for $ext")) {
        try {
            Ensure-RegistryKey $extKey
            Set-ItemProperty -Path $extKey -Name '(Default)' -Value $menuLabel
            Set-ItemProperty -Path $extKey -Name 'Icon'      -Value $iconValue

            Ensure-RegistryKey $extCmd
            Set-ItemProperty -Path $extCmd -Name '(Default)' -Value "`"${contextMenuExe}`" --file `"%1`""
        } catch {
            Write-Warn "Skipped $ext : $_"
        }
    }
}
Write-Success "Per-extension entries registered ($($textExtensions.Count) extensions)"
#endregion

#region Register folder context menu
Write-Step "Registering folder context menu (Directory\shell)..."

$dirShellKey = "HKCU:\Software\Classes\Directory\shell\PerplexityXPC"
$dirCmdKey   = "${dirShellKey}\command"

if ($PSCmdlet.ShouldProcess($dirShellKey, "Create folder context menu key")) {
    Ensure-RegistryKey $dirShellKey
    Set-ItemProperty -Path $dirShellKey -Name '(Default)' -Value $folderLabel
    Set-ItemProperty -Path $dirShellKey -Name 'Icon'      -Value $iconValue

    Ensure-RegistryKey $dirCmdKey
    Set-ItemProperty -Path $dirCmdKey -Name '(Default)' -Value "`"${contextMenuExe}`" --folder `"%1`""
    Write-Success "Folder context menu registered (Directory\shell\PerplexityXPC)"
}

# Also register for folder background (right-click inside an empty folder area)
$bgShellKey = "HKCU:\Software\Classes\Directory\Background\shell\PerplexityXPC"
$bgCmdKey   = "${bgShellKey}\command"

if ($PSCmdlet.ShouldProcess($bgShellKey, "Create folder background context menu key")) {
    Ensure-RegistryKey $bgShellKey
    Set-ItemProperty -Path $bgShellKey -Name '(Default)' -Value $folderLabel
    Set-ItemProperty -Path $bgShellKey -Name 'Icon'      -Value $iconValue

    Ensure-RegistryKey $bgCmdKey
    # %V = current folder when right-clicking background; %W = working directory
    Set-ItemProperty -Path $bgCmdKey -Name '(Default)' -Value "`"${contextMenuExe}`" --folder `"%V`""
    Write-Success "Folder background context menu registered (Directory\Background\shell\PerplexityXPC)"
}

# Drive root right-click
$driveShellKey = "HKCU:\Software\Classes\Drive\shell\PerplexityXPC"
$driveCmdKey   = "${driveShellKey}\command"

if ($PSCmdlet.ShouldProcess($driveShellKey, "Create drive context menu key")) {
    Ensure-RegistryKey $driveShellKey
    Set-ItemProperty -Path $driveShellKey -Name '(Default)' -Value "Ask Perplexity about this drive"
    Set-ItemProperty -Path $driveShellKey -Name 'Icon'      -Value $iconValue

    Ensure-RegistryKey $driveCmdKey
    Set-ItemProperty -Path $driveCmdKey -Name '(Default)' -Value "`"${contextMenuExe}`" --folder `"%1`""
    Write-Success "Drive context menu registered (Drive\shell\PerplexityXPC)"
}
#endregion

#region Create SendTo shortcut
Write-Step "Creating 'Send To' shortcut..."

try {
    $sendToDir       = [Environment]::GetFolderPath("SendTo")
    $sendToLinkPath  = Join-Path $sendToDir "Ask Perplexity.lnk"

    if ($PSCmdlet.ShouldProcess($sendToLinkPath, "Create SendTo shortcut")) {
        # Use WScript.Shell COM object - works on both PS 5.1 and PS 7
        $wscriptShell = New-Object -ComObject WScript.Shell
        $shortcut     = $wscriptShell.CreateShortcut($sendToLinkPath)
        $shortcut.TargetPath       = $contextMenuExe
        $shortcut.Arguments        = "--file"
        $shortcut.WorkingDirectory = $InstallPath
        $shortcut.Description      = "Send selected files to Perplexity for analysis"
        if (Test-Path $iconPath) {
            $shortcut.IconLocation = "${iconPath},0"
        }
        $shortcut.Save()

        Write-Success "SendTo shortcut created: $sendToLinkPath"
        Write-Status "  Select one or more text files in Explorer, right-click > Send To > Ask Perplexity." -Color DarkGray
    }
} catch {
    Write-Warn "Could not create SendTo shortcut: $_"
}
#endregion

#region Done
if (-not $Quiet) {
    Write-Host ""
    Write-Host "  Context menu registration complete." -ForegroundColor Green
    Write-Host "  You may need to restart Windows Explorer for changes to take effect." -ForegroundColor Yellow
    Write-Host "  Run: Stop-Process -Name explorer -Force" -ForegroundColor DarkGray
    Write-Host ""
}
#endregion
