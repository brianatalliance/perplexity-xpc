<#
.SYNOPSIS
    Installs the PerplexityXPC profile and keybindings into Windows Terminal settings.json.

.DESCRIPTION
    Install-TerminalProfile locates the Windows Terminal settings.json file, creates a
    timestamped backup, then merges the PerplexityXPC profile and keybindings into it.

    Supported install locations (checked in order):
    - Stable store release:  %LOCALAPPDATA%\Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState\settings.json
    - Preview store release: %LOCALAPPDATA%\Packages\Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe\LocalState\settings.json
    - Sideload / unpackaged: %LOCALAPPDATA%\Microsoft\Windows Terminal\settings.json

    The script does not replace existing profiles or keybindings that share the same
    name or key combo. Duplicate detection is based on the "name" field for profiles
    and the "name" field for keybinding actions.

    Use -Uninstall to remove the PerplexityXPC profile and keybindings added by this script.

.PARAMETER Uninstall
    Remove the PerplexityXPC profile and keybindings from Windows Terminal settings.

.EXAMPLE
    .\Install-TerminalProfile.ps1
    Installs the PerplexityXPC terminal profile and keybindings.

.EXAMPLE
    .\Install-TerminalProfile.ps1 -Uninstall
    Removes all PerplexityXPC entries from Windows Terminal settings.

.EXAMPLE
    .\Install-TerminalProfile.ps1 -WhatIf
    Shows what changes would be made without actually writing anything.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Requires .NET JSON via ConvertFrom-Json / ConvertTo-Json
# PS 5.1 ConvertTo-Json depth defaults to 2 - always specify depth explicitly.

# --- Helpers ---

function Write-Step  { param([string]$m); Write-Host "  --> ${m}" -ForegroundColor Cyan }
function Write-OK    { param([string]$m); Write-Host "  [OK] ${m}" -ForegroundColor Green }
function Write-Warn  { param([string]$m); Write-Host "  [WARN] ${m}" -ForegroundColor Yellow }
function Write-Fail  { param([string]$m); Write-Host "  [ERR] ${m}" -ForegroundColor Red }

# --- Locate settings.json ---

function Find-TerminalSettings {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState\settings.json'),
        (Join-Path $env:LOCALAPPDATA 'Packages\Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe\LocalState\settings.json'),
        (Join-Path $env:LOCALAPPDATA 'Microsoft\Windows Terminal\settings.json')
    )

    foreach ($c in $candidates) {
        if (Test-Path $c) {
            return $c
        }
    }
    return $null
}

# --- Backup ---

function Backup-Settings {
    param([string]$Path)
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $backup = "${Path}.backup_${stamp}"
    Copy-Item -Path $Path -Destination $backup
    Write-Step "Backed up settings to: ${backup}"
}

# --- JSON helpers compatible with PS 5.1 ---

# ConvertFrom-Json on PS 5.1 returns PSCustomObject trees.
# We work with them via Add-Member / array manipulation.

function Get-ProfileEntry {
    # Returns a hashtable representing the PerplexityXPC terminal profile.
    return @{
        name             = 'PerplexityXPC Shell'
        commandline      = 'powershell.exe -NoExit -Command "Import-Module PerplexityXPC; Write-Host ''PerplexityXPC loaded. Type pplx <query> to search.'' -ForegroundColor Cyan"'
        icon             = 'ms-appx:///ProfileIcons/{574e775e-4f2a-5b96-ac1e-a2962a402336}.png'
        colorScheme      = 'One Half Dark'
        font             = @{ face = 'Cascadia Code'; size = 11 }
        startingDirectory = '%USERPROFILE%'
        tabTitle         = 'Perplexity'
        hidden           = $false
    }
}

function Get-KeybindingEntries {
    # Returns an array of hashtables representing keybindings to add.
    return @(
        @{
            command = @{ action = 'sendInput'; input = "Invoke-PerplexityClipboard`r" }
            keys    = 'ctrl+shift+alt+p'
            name    = 'Query clipboard with Perplexity'
        },
        @{
            command = @{ action = 'sendInput'; input = "Get-XPCStatus`r" }
            keys    = 'ctrl+shift+alt+s'
            name    = 'PerplexityXPC Status'
        }
    )
}

# Convert a PSCustomObject (from ConvertFrom-Json) into a plain hashtable recursively.
# Needed so we can manipulate arrays and re-serialize cleanly.
function ConvertTo-Hashtable {
    param([Parameter(ValueFromPipeline)]$InputObject)
    process {
        if ($null -eq $InputObject)                       { return $null }
        if ($InputObject -is [System.Collections.IList]) {
            $arr = [System.Collections.ArrayList]::new()
            foreach ($item in $InputObject) {
                $null = $arr.Add((ConvertTo-Hashtable $item))
            }
            return ,$arr
        }
        if ($InputObject -is [PSCustomObject]) {
            $ht = @{}
            foreach ($prop in $InputObject.PSObject.Properties) {
                $ht[$prop.Name] = ConvertTo-Hashtable $prop.Value
            }
            return $ht
        }
        return $InputObject
    }
}

# --- Install logic ---

function Install-TerminalSettings {
    param([string]$SettingsPath)

    Write-Step "Reading: ${SettingsPath}"
    $raw = [System.IO.File]::ReadAllText($SettingsPath, [System.Text.Encoding]::UTF8)

    # Windows Terminal settings.json may contain JSONC comments - strip them
    # using a simple regex before parsing.
    $stripped = $raw -replace '(?m)^\s*//.*$', '' -replace ',\s*(\]|\})', '$1'

    $settings = ConvertFrom-Json -InputObject $stripped
    $settingsHt = ConvertTo-Hashtable $settings

    # -- Profiles --
    if (-not $settingsHt.ContainsKey('profiles')) {
        $settingsHt['profiles'] = @{ list = [System.Collections.ArrayList]::new() }
    }
    $profilesSection = $settingsHt['profiles']
    if ($profilesSection -is [System.Collections.Hashtable]) {
        if (-not $profilesSection.ContainsKey('list')) {
            $profilesSection['list'] = [System.Collections.ArrayList]::new()
        }
        $profileList = $profilesSection['list']
    } else {
        Write-Warn "Unexpected 'profiles' structure. Skipping profile merge."
        $profileList = $null
    }

    $newProfile  = Get-ProfileEntry
    $profileName = $newProfile['name']

    if ($null -ne $profileList) {
        $exists = $false
        foreach ($p in $profileList) {
            $pName = if ($p -is [System.Collections.Hashtable]) { $p['name'] } else { $p.name }
            if ($pName -eq $profileName) { $exists = $true; break }
        }
        if ($exists) {
            Write-Warn "Profile '${profileName}' already exists - skipping."
        } else {
            $null = $profileList.Add($newProfile)
            Write-OK "Added profile: ${profileName}"
        }
    }

    # -- Keybindings (actions) --
    if (-not $settingsHt.ContainsKey('actions')) {
        $settingsHt['actions'] = [System.Collections.ArrayList]::new()
    }
    $actionList = $settingsHt['actions']
    if ($actionList -isnot [System.Collections.ArrayList]) {
        $actionList = [System.Collections.ArrayList]$actionList
        $settingsHt['actions'] = $actionList
    }

    foreach ($kb in (Get-KeybindingEntries)) {
        $kbName = $kb['name']
        $exists = $false
        foreach ($a in $actionList) {
            $aName = if ($a -is [System.Collections.Hashtable]) { $a['name'] } else { $a.name }
            if ($aName -eq $kbName) { $exists = $true; break }
        }
        if ($exists) {
            Write-Warn "Keybinding '${kbName}' already exists - skipping."
        } else {
            $null = $actionList.Add($kb)
            Write-OK "Added keybinding: ${kbName}"
        }
    }

    # Write back
    $newJson = ConvertTo-Json -InputObject $settingsHt -Depth 10
    if ($PSCmdlet.ShouldProcess($SettingsPath, 'Write updated settings.json')) {
        Backup-Settings -Path $SettingsPath
        [System.IO.File]::WriteAllText($SettingsPath, $newJson, [System.Text.Encoding]::UTF8)
        Write-OK "Settings written."
    }
}

# --- Uninstall logic ---

function Uninstall-TerminalSettings {
    param([string]$SettingsPath)

    Write-Step "Reading: ${SettingsPath}"
    $raw = [System.IO.File]::ReadAllText($SettingsPath, [System.Text.Encoding]::UTF8)
    $stripped = $raw -replace '(?m)^\s*//.*$', '' -replace ',\s*(\]|\})', '$1'

    $settings   = ConvertFrom-Json -InputObject $stripped
    $settingsHt = ConvertTo-Hashtable $settings

    $profileName = 'PerplexityXPC Shell'
    $kbNames     = @('Query clipboard with Perplexity', 'PerplexityXPC Status')

    # Remove profile
    $profilesSection = $settingsHt['profiles']
    if ($profilesSection -is [System.Collections.Hashtable] -and $profilesSection.ContainsKey('list')) {
        $list = $profilesSection['list']
        $toRemove = [System.Collections.ArrayList]::new()
        foreach ($p in $list) {
            $pName = if ($p -is [System.Collections.Hashtable]) { $p['name'] } else { $p.name }
            if ($pName -eq $profileName) { $null = $toRemove.Add($p) }
        }
        foreach ($r in $toRemove) { $null = $list.Remove($r); Write-OK "Removed profile: ${profileName}" }
        if ($toRemove.Count -eq 0) { Write-Warn "Profile '${profileName}' not found." }
    }

    # Remove keybindings
    if ($settingsHt.ContainsKey('actions')) {
        $actionList = $settingsHt['actions']
        foreach ($kbName in $kbNames) {
            $toRemove = [System.Collections.ArrayList]::new()
            foreach ($a in $actionList) {
                $aName = if ($a -is [System.Collections.Hashtable]) { $a['name'] } else { $a.name }
                if ($aName -eq $kbName) { $null = $toRemove.Add($a) }
            }
            foreach ($r in $toRemove) { $null = $actionList.Remove($r); Write-OK "Removed keybinding: ${kbName}" }
            if ($toRemove.Count -eq 0) { Write-Warn "Keybinding '${kbName}' not found." }
        }
    }

    $newJson = ConvertTo-Json -InputObject $settingsHt -Depth 10
    if ($PSCmdlet.ShouldProcess($SettingsPath, 'Write updated settings.json')) {
        Backup-Settings -Path $SettingsPath
        [System.IO.File]::WriteAllText($SettingsPath, $newJson, [System.Text.Encoding]::UTF8)
        Write-OK "Settings written."
    }
}

# --- Entry point ---

Write-Host ''
Write-Host 'PerplexityXPC Windows Terminal Installer' -ForegroundColor White
Write-Host '=========================================' -ForegroundColor White
Write-Host ''

$settingsPath = Find-TerminalSettings
if ($null -eq $settingsPath) {
    Write-Fail "Windows Terminal settings.json not found."
    Write-Host "  Checked locations:"
    Write-Host "    $env:LOCALAPPDATA\Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState\settings.json"
    Write-Host "    $env:LOCALAPPDATA\Packages\Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe\LocalState\settings.json"
    Write-Host "    $env:LOCALAPPDATA\Microsoft\Windows Terminal\settings.json"
    Write-Host ''
    Write-Host "  Make sure Windows Terminal has been launched at least once to generate settings.json." -ForegroundColor Yellow
    exit 1
}

Write-Step "Found settings at: ${settingsPath}"

if ($Uninstall) {
    Uninstall-TerminalSettings -SettingsPath $settingsPath
} else {
    Install-TerminalSettings -SettingsPath $settingsPath
}

Write-Host ''
Write-Host 'Done. Restart Windows Terminal for changes to take effect.' -ForegroundColor Green
Write-Host ''
