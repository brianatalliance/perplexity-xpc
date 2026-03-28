<#
.SYNOPSIS
    Installs PerplexityXPC keybindings and tasks into VS Code user configuration.

.DESCRIPTION
    Install-VSCodeIntegration locates the VS Code user configuration directory
    (%APPDATA%\Code\User\) and merges PerplexityXPC keybindings and tasks into
    keybindings.json and tasks.json. Existing entries are preserved.

    The script backs up each file before modifying it.

    Merging rules:
    - keybindings.json: Adds PerplexityXPC keybindings. Skips any entry whose
      "key" + "command" combination already exists.
    - tasks.json: Adds PerplexityXPC tasks. Skips any task whose "label"
      already exists.

    Use -Uninstall to remove entries added by this script.
    Use -TasksOnly to only install/remove tasks.
    Use -KeybindingsOnly to only install/remove keybindings.

.PARAMETER Uninstall
    Remove all PerplexityXPC entries from keybindings.json and tasks.json.

.PARAMETER TasksOnly
    Only install (or uninstall) tasks.json entries.

.PARAMETER KeybindingsOnly
    Only install (or uninstall) keybindings.json entries.

.EXAMPLE
    .\Install-VSCodeIntegration.ps1
    Installs both keybindings and tasks.

.EXAMPLE
    .\Install-VSCodeIntegration.ps1 -TasksOnly
    Installs only the tasks.json entries.

.EXAMPLE
    .\Install-VSCodeIntegration.ps1 -Uninstall
    Removes all PerplexityXPC keybindings and tasks.

.EXAMPLE
    .\Install-VSCodeIntegration.ps1 -KeybindingsOnly -Uninstall
    Removes only PerplexityXPC keybindings.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$Uninstall,
    [switch]$TasksOnly,
    [switch]$KeybindingsOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Helpers ---

function Write-Step { param([string]$m); Write-Host "  --> ${m}" -ForegroundColor Cyan }
function Write-OK   { param([string]$m); Write-Host "  [OK] ${m}" -ForegroundColor Green }
function Write-Warn { param([string]$m); Write-Host "  [WARN] ${m}" -ForegroundColor Yellow }
function Write-Fail { param([string]$m); Write-Host "  [ERR] ${m}" -ForegroundColor Red }

function Backup-File {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return }
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $backup = "${Path}.backup_${stamp}"
    Copy-Item -Path $Path -Destination $backup
    Write-Step "Backed up to: ${backup}"
}

# Strip JSONC single-line comments before parsing.
function Remove-JsonComments {
    param([string]$Json)
    # Remove single-line comments (// ...) that are not inside strings.
    # This regex approach handles the common cases in VS Code config files.
    $lines = $Json -split "`n"
    $cleaned = foreach ($line in $lines) {
        # Remove trailing // comments (simple - does not handle // inside strings)
        $line -replace '\s*//.*$', ''
    }
    $result = $cleaned -join "`n"
    # Remove trailing commas before ] or } (common in JSONC)
    $result = $result -replace ',(\s*[\]\}])','$1'
    return $result
}

function Read-JsonFile {
    param([string]$Path, [bool]$IsArray = $false)
    if (-not (Test-Path $Path)) {
        if ($IsArray) { return [System.Collections.ArrayList]::new() }
        return @{}
    }
    $raw = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    $stripped = Remove-JsonComments -Json $raw
    if ([string]::IsNullOrWhiteSpace($stripped)) {
        if ($IsArray) { return [System.Collections.ArrayList]::new() }
        return @{}
    }
    try {
        $parsed = ConvertFrom-Json -InputObject $stripped
        return $parsed
    } catch {
        Write-Warn "Could not parse ${Path}: $($_.Exception.Message)"
        if ($IsArray) { return [System.Collections.ArrayList]::new() }
        return @{}
    }
}

function Write-JsonFile {
    param([string]$Path, $Data)
    $json = ConvertTo-Json -InputObject $Data -Depth 10
    [System.IO.File]::WriteAllText($Path, $json, [System.Text.Encoding]::UTF8)
}

# Convert PSCustomObject tree to nested hashtables / ArrayLists (PS 5.1 compatible).
function ConvertTo-PlainObject {
    param([Parameter(ValueFromPipeline)]$Obj)
    process {
        if ($null -eq $Obj)                           { return $null }
        if ($Obj -is [System.Collections.IList]) {
            $list = [System.Collections.ArrayList]::new()
            foreach ($item in $Obj) { $null = $list.Add((ConvertTo-PlainObject $item)) }
            return ,$list
        }
        if ($Obj -is [PSCustomObject]) {
            $ht = [ordered]@{}
            foreach ($p in $Obj.PSObject.Properties) { $ht[$p.Name] = ConvertTo-PlainObject $p.Value }
            return $ht
        }
        return $Obj
    }
}

# --- Keybindings data ---

function Get-XPCKeybindings {
    return @(
        [ordered]@{
            key     = 'ctrl+shift+alt+p'
            command = 'workbench.action.terminal.sendSequence'
            args    = [ordered]@{ text = "Invoke-PerplexityClipboard`n" }
            when    = 'terminalFocus'
            _xpc    = $true
        },
        [ordered]@{
            key     = 'ctrl+shift+alt+r'
            command = 'workbench.action.terminal.sendSequence'
            args    = [ordered]@{ text = "Invoke-PerplexityCodeReview -Path '`${file}' -Focus review`n" }
            when    = 'terminalFocus'
            _xpc    = $true
        },
        [ordered]@{
            key     = 'ctrl+shift+alt+d'
            command = 'workbench.action.terminal.sendSequence'
            args    = [ordered]@{ text = "Invoke-PerplexityCodeReview -Path '`${file}' -Focus debug`n" }
            when    = 'terminalFocus'
            _xpc    = $true
        },
        [ordered]@{
            key     = 'ctrl+shift+alt+s'
            command = 'workbench.action.terminal.sendSequence'
            args    = [ordered]@{ text = "Invoke-PerplexityCodeReview -Path '`${file}' -Focus security`n" }
            when    = 'terminalFocus'
            _xpc    = $true
        }
    )
}

# --- Tasks data ---

function Get-XPCTasks {
    $presentation = [ordered]@{ reveal = 'always'; panel = 'shared'; focus = $true }
    $tasks = @(
        [ordered]@{ label = 'Perplexity: Review Current File';        type = 'shell'; command = "Invoke-PerplexityCodeReview -Path '`${file}' -Focus review";    problemMatcher = @(); group = 'none'; presentation = $presentation; _xpc = $true },
        [ordered]@{ label = 'Perplexity: Security Audit Current File'; type = 'shell'; command = "Invoke-PerplexityCodeReview -Path '`${file}' -Focus security";  problemMatcher = @(); group = 'none'; presentation = $presentation; _xpc = $true },
        [ordered]@{ label = 'Perplexity: Debug Current File';          type = 'shell'; command = "Invoke-PerplexityCodeReview -Path '`${file}' -Focus debug";     problemMatcher = @(); group = 'none'; presentation = $presentation; _xpc = $true },
        [ordered]@{ label = 'Perplexity: Explain Current File';        type = 'shell'; command = "Invoke-PerplexityCodeReview -Path '`${file}' -Focus explain";   problemMatcher = @(); group = 'none'; presentation = $presentation; _xpc = $true },
        [ordered]@{ label = 'Perplexity: Analyze Event Logs';          type = 'shell'; command = 'Invoke-PerplexityEventAnalysis -GroupBySource';                  problemMatcher = @(); group = 'none'; presentation = $presentation; _xpc = $true },
        [ordered]@{ label = 'Perplexity: XPC Broker Status';           type = 'shell'; command = 'Get-XPCStatus';                                                  problemMatcher = @(); group = 'none'; presentation = $presentation; _xpc = $true },
        [ordered]@{ label = 'Perplexity: Query Clipboard';             type = 'shell'; command = 'Invoke-PerplexityClipboard';                                     problemMatcher = @(); group = 'none'; presentation = $presentation; _xpc = $true }
    )
    return $tasks
}

# --- Install keybindings ---

function Install-Keybindings {
    param([string]$KeybindingsPath)

    Write-Step "Processing keybindings: ${KeybindingsPath}"

    $existing = Read-JsonFile -Path $KeybindingsPath -IsArray $true
    $list = ConvertTo-PlainObject $existing
    if ($list -isnot [System.Collections.ArrayList]) {
        $list = [System.Collections.ArrayList]::new()
    }

    $added = 0
    foreach ($kb in (Get-XPCKeybindings)) {
        $kbKey = $kb['key']
        $kbCmd = $kb['command']
        $exists = $false
        foreach ($e in $list) {
            $eKey = if ($e -is [System.Collections.IDictionary]) { $e['key'] } else { $e.key }
            $eCmd = if ($e -is [System.Collections.IDictionary]) { $e['command'] } else { $e.command }
            if ($eKey -eq $kbKey -and $eCmd -eq $kbCmd) { $exists = $true; break }
        }
        if ($exists) {
            Write-Warn "Keybinding '${kbKey}' already exists - skipping."
        } else {
            $null = $list.Add($kb)
            $added++
            Write-OK "Added keybinding: ${kbKey}"
        }
    }

    if ($added -gt 0) {
        if ($PSCmdlet.ShouldProcess($KeybindingsPath, 'Write keybindings.json')) {
            Backup-File -Path $KeybindingsPath
            $dir = Split-Path -Parent $KeybindingsPath
            if (-not (Test-Path $dir)) { $null = New-Item -ItemType Directory -Path $dir -Force }
            Write-JsonFile -Path $KeybindingsPath -Data $list
            Write-OK "keybindings.json updated."
        }
    } else {
        Write-OK "No new keybindings to add."
    }
}

# --- Uninstall keybindings ---

function Uninstall-Keybindings {
    param([string]$KeybindingsPath)

    if (-not (Test-Path $KeybindingsPath)) {
        Write-Warn "keybindings.json not found - nothing to do."
        return
    }

    Write-Step "Removing XPC keybindings from: ${KeybindingsPath}"
    $existing = Read-JsonFile -Path $KeybindingsPath -IsArray $true
    $list = ConvertTo-PlainObject $existing
    if ($list -isnot [System.Collections.ArrayList]) { return }

    $xpcKeys = (Get-XPCKeybindings | ForEach-Object { $_.key })
    $toRemove = [System.Collections.ArrayList]::new()
    foreach ($e in $list) {
        $eKey = if ($e -is [System.Collections.IDictionary]) { $e['key'] } else { $e.key }
        if ($xpcKeys -contains $eKey) { $null = $toRemove.Add($e) }
    }
    foreach ($r in $toRemove) { $null = $list.Remove($r); Write-OK "Removed keybinding: $($r['key'])" }
    if ($toRemove.Count -eq 0) { Write-Warn "No XPC keybindings found to remove." }

    if ($toRemove.Count -gt 0 -and $PSCmdlet.ShouldProcess($KeybindingsPath, 'Write keybindings.json')) {
        Backup-File -Path $KeybindingsPath
        Write-JsonFile -Path $KeybindingsPath -Data $list
        Write-OK "keybindings.json updated."
    }
}

# --- Install tasks ---

function Install-Tasks {
    param([string]$TasksPath)

    Write-Step "Processing tasks: ${TasksPath}"

    $existing = Read-JsonFile -Path $TasksPath -IsArray $false
    $settingsHt = ConvertTo-PlainObject $existing
    if ($settingsHt -isnot [System.Collections.IDictionary]) { $settingsHt = [ordered]@{} }

    if (-not $settingsHt.ContainsKey('version')) { $settingsHt['version'] = '2.0.0' }

    if (-not $settingsHt.ContainsKey('tasks')) {
        $settingsHt['tasks'] = [System.Collections.ArrayList]::new()
    }
    $taskList = $settingsHt['tasks']
    if ($taskList -isnot [System.Collections.ArrayList]) {
        $al = [System.Collections.ArrayList]::new()
        foreach ($t in $taskList) { $null = $al.Add($t) }
        $taskList = $al
        $settingsHt['tasks'] = $taskList
    }

    $added = 0
    foreach ($task in (Get-XPCTasks)) {
        $taskLabel = $task['label']
        $exists = $false
        foreach ($t in $taskList) {
            $tLabel = if ($t -is [System.Collections.IDictionary]) { $t['label'] } else { $t.label }
            if ($tLabel -eq $taskLabel) { $exists = $true; break }
        }
        if ($exists) {
            Write-Warn "Task '${taskLabel}' already exists - skipping."
        } else {
            $null = $taskList.Add($task)
            $added++
            Write-OK "Added task: ${taskLabel}"
        }
    }

    if ($added -gt 0) {
        if ($PSCmdlet.ShouldProcess($TasksPath, 'Write tasks.json')) {
            Backup-File -Path $TasksPath
            $dir = Split-Path -Parent $TasksPath
            if (-not (Test-Path $dir)) { $null = New-Item -ItemType Directory -Path $dir -Force }
            Write-JsonFile -Path $TasksPath -Data $settingsHt
            Write-OK "tasks.json updated."
        }
    } else {
        Write-OK "No new tasks to add."
    }
}

# --- Uninstall tasks ---

function Uninstall-Tasks {
    param([string]$TasksPath)

    if (-not (Test-Path $TasksPath)) {
        Write-Warn "tasks.json not found - nothing to do."
        return
    }

    Write-Step "Removing XPC tasks from: ${TasksPath}"
    $existing = Read-JsonFile -Path $TasksPath -IsArray $false
    $settingsHt = ConvertTo-PlainObject $existing
    if ($settingsHt -isnot [System.Collections.IDictionary]) { return }
    if (-not $settingsHt.ContainsKey('tasks')) { Write-Warn "No tasks found."; return }

    $taskList = $settingsHt['tasks']
    if ($taskList -isnot [System.Collections.ArrayList]) { return }

    $xpcLabels = Get-XPCTasks | ForEach-Object { $_['label'] }
    $toRemove = [System.Collections.ArrayList]::new()
    foreach ($t in $taskList) {
        $tLabel = if ($t -is [System.Collections.IDictionary]) { $t['label'] } else { $t.label }
        if ($xpcLabels -contains $tLabel) { $null = $toRemove.Add($t) }
    }
    foreach ($r in $toRemove) { $null = $taskList.Remove($r); Write-OK "Removed task: $($r['label'])" }
    if ($toRemove.Count -eq 0) { Write-Warn "No XPC tasks found to remove." }

    if ($toRemove.Count -gt 0 -and $PSCmdlet.ShouldProcess($TasksPath, 'Write tasks.json')) {
        Backup-File -Path $TasksPath
        Write-JsonFile -Path $TasksPath -Data $settingsHt
        Write-OK "tasks.json updated."
    }
}

# --- Entry point ---

Write-Host ''
Write-Host 'PerplexityXPC VS Code Integration Installer' -ForegroundColor White
Write-Host '============================================' -ForegroundColor White
Write-Host ''

$vsCodeUserDir   = Join-Path $env:APPDATA 'Code\User'
$keybindingsPath = Join-Path $vsCodeUserDir 'keybindings.json'
$tasksPath       = Join-Path $vsCodeUserDir 'tasks.json'

Write-Step "VS Code user directory: ${vsCodeUserDir}"

$doKeybindings = -not $TasksOnly
$doTasks       = -not $KeybindingsOnly

if ($Uninstall) {
    if ($doKeybindings) { Uninstall-Keybindings -KeybindingsPath $keybindingsPath }
    if ($doTasks)       { Uninstall-Tasks -TasksPath $tasksPath }
} else {
    if ($doKeybindings) { Install-Keybindings -KeybindingsPath $keybindingsPath }
    if ($doTasks)       { Install-Tasks -TasksPath $tasksPath }
}

Write-Host ''
Write-Host 'Done. Reload VS Code window (Ctrl+Shift+A > "Developer: Reload Window") to apply changes.' -ForegroundColor Green
Write-Host ''
