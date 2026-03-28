<#
.SYNOPSIS
    Sets up the PowerShell profile for PerplexityXPC integration.

.DESCRIPTION
    Install-PerplexityXPCProfile configures the current user's PowerShell profile
    to automatically load the PerplexityXPC module and register useful aliases and
    helper functions. The script appends integration blocks to the profile without
    overwriting any existing content.

    The following items are added to the profile:
    - Auto-import of the PerplexityXPC module (if installed)
    - Aliases: pplx, pplxcode, pplxnet, pplxevt, pplxclip, pplxerr
    - Ask-LastError function to analyze the most recent PowerShell error
    - Explain-Output function to explain piped command output
    - Get-XPCPromptTag function for prompt status indicator

    Use -Uninstall to remove all PerplexityXPC entries previously added by this script.

.PARAMETER SkipModuleInstall
    Do not copy the PerplexityXPC module to the Modules path. Only add profile entries.

.PARAMETER NoAliases
    Do not add aliases to the profile.

.PARAMETER NoPrompt
    Do not add the prompt enhancement function (Get-XPCPromptTag) to the profile.

.PARAMETER Uninstall
    Remove all PerplexityXPC integration entries from the profile.

.EXAMPLE
    .\Install-PerplexityXPCProfile.ps1
    Runs the full install: checks module, then appends the integration block to the profile.

.EXAMPLE
    .\Install-PerplexityXPCProfile.ps1 -SkipModuleInstall
    Adds profile entries without attempting to copy the module.

.EXAMPLE
    .\Install-PerplexityXPCProfile.ps1 -NoAliases -NoPrompt
    Adds only the module auto-load block - no aliases or prompt helpers.

.EXAMPLE
    .\Install-PerplexityXPCProfile.ps1 -Uninstall
    Removes all PerplexityXPC entries from the profile.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$SkipModuleInstall,
    [switch]$NoAliases,
    [switch]$NoPrompt,
    [switch]$Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Helpers ---

function Write-Step {
    param([string]$Message)
    Write-Host "  --> ${Message}" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "  [OK] ${Message}" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host "  [WARN] ${Message}" -ForegroundColor Yellow
}

function Write-Fail {
    param([string]$Message)
    Write-Host "  [ERR] ${Message}" -ForegroundColor Red
}

# --- Module check / install ---

function Test-ModuleInstalled {
    $null -ne (Get-Module -ListAvailable -Name PerplexityXPC)
}

function Install-ModuleFromRepo {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $moduleSource = Join-Path $repoRoot 'module'

    if (-not (Test-Path $moduleSource)) {
        Write-Warn "Module source not found at: ${moduleSource}"
        Write-Warn "Run with -SkipModuleInstall if you will install the module separately."
        return $false
    }

    # Find user module path
    $modulePaths = $env:PSModulePath -split [System.IO.Path]::PathSeparator
    $userModulePath = $null
    foreach ($p in $modulePaths) {
        if ($p -like "*$env:USERPROFILE*" -or $p -like "*Documents*") {
            $userModulePath = $p
            break
        }
    }
    if (-not $userModulePath) {
        $userModulePath = $modulePaths[0]
    }

    $destPath = Join-Path $userModulePath 'PerplexityXPC'
    Write-Step "Copying module to: ${destPath}"

    if ($PSCmdlet.ShouldProcess($destPath, 'Copy PerplexityXPC module')) {
        try {
            Copy-Item -Path $moduleSource -Destination $destPath -Recurse -Force
            Write-Success "Module copied successfully."
            return $true
        } catch {
            Write-Fail "Failed to copy module: $($_.Exception.Message)"
            return $false
        }
    }
    return $false
}

# --- Profile content builders ---

function Get-AliasBlock {
    return @'

# Aliases for quick access
Set-Alias -Name pplx -Value Invoke-Perplexity
Set-Alias -Name pplxcode -Value Invoke-PerplexityCodeReview
Set-Alias -Name pplxnet -Value Invoke-PerplexityNetDiag
Set-Alias -Name pplxevt -Value Invoke-PerplexityEventAnalysis
Set-Alias -Name pplxclip -Value Invoke-PerplexityClipboard

# Quick function: ask Perplexity about the last error
function Ask-LastError {
    if ($Error.Count -eq 0) { Write-Warning 'No errors in session.'; return }
    $lastErr = $Error[0]
    $errText = '{0}: {1}' -f $lastErr.Exception.GetType().Name, $lastErr.Exception.Message
    if ($lastErr.InvocationInfo) {
        $errText += (' at {0} line {1}' -f $lastErr.InvocationInfo.ScriptName, $lastErr.InvocationInfo.ScriptLineNumber)
    }
    Invoke-Perplexity -Query ('PowerShell error - explain and fix: {0}' -f $errText) -Model sonar-pro
}
Set-Alias -Name pplxerr -Value Ask-LastError

# Quick function: explain last command output
function Explain-Output {
    param([Parameter(ValueFromPipeline)][string]$InputText)
    begin { $lines = [System.Collections.ArrayList]::new() }
    process { $null = $lines.Add($InputText) }
    end {
        $text = $lines -join "`n"
        if (-not $text) { Write-Warning 'No input received.'; return }
        Invoke-Perplexity -Query ('Explain this command output:' + "`n`n" + $text) -Model sonar
    }
}
'@
}

function Get-PromptBlock {
    return @'

# Prompt enhancement: show XPC status indicator
function Get-XPCPromptTag {
    try {
        $null = Invoke-RestMethod -Uri 'http://127.0.0.1:47777/status' -TimeoutSec 1 -ErrorAction Stop
        return '[XPC]'
    } catch {
        return '[XPC:OFF]'
    }
}
'@
}

function Build-ProfileBlock {
    param(
        [bool]$IncludeAliases,
        [bool]$IncludePrompt
    )

    $sb = [System.Text.StringBuilder]::new()
    $null = $sb.AppendLine('')
    $null = $sb.AppendLine('# --- PerplexityXPC Integration ---')
    $null = $sb.AppendLine('# Auto-load the PerplexityXPC module')
    $null = $sb.AppendLine('if (Get-Module -ListAvailable -Name PerplexityXPC) {')
    $null = $sb.AppendLine('    Import-Module PerplexityXPC')
    $null = $sb.AppendLine('}')

    if ($IncludeAliases) {
        $null = $sb.Append((Get-AliasBlock))
    }

    if ($IncludePrompt) {
        $null = $sb.Append((Get-PromptBlock))
    }

    $null = $sb.AppendLine('# --- End PerplexityXPC Integration ---')
    return $sb.ToString()
}

# --- Profile read / write helpers ---

function Get-ProfileContent {
    param([string]$ProfilePath)
    if (Test-Path $ProfilePath) {
        return [System.IO.File]::ReadAllText($ProfilePath)
    }
    return ''
}

function Set-ProfileContent {
    param(
        [string]$ProfilePath,
        [string]$Content
    )
    $dir = Split-Path -Parent $ProfilePath
    if (-not (Test-Path $dir)) {
        $null = New-Item -ItemType Directory -Path $dir -Force
    }
    [System.IO.File]::WriteAllText($ProfilePath, $Content, [System.Text.Encoding]::UTF8)
}

function Backup-Profile {
    param([string]$ProfilePath)
    if (-not (Test-Path $ProfilePath)) { return }
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $backupPath = "${ProfilePath}.backup_${stamp}"
    Copy-Item -Path $ProfilePath -Destination $backupPath
    Write-Step "Profile backed up to: ${backupPath}"
}

# --- Uninstall logic ---

function Remove-XPCProfileEntries {
    param([string]$ProfilePath)

    if (-not (Test-Path $ProfilePath)) {
        Write-Warn "Profile not found at: ${ProfilePath}"
        return
    }

    $content = Get-ProfileContent -ProfilePath $ProfilePath
    $startMarker = '# --- PerplexityXPC Integration ---'
    $endMarker = '# --- End PerplexityXPC Integration ---'

    $startIdx = $content.IndexOf($startMarker)
    if ($startIdx -lt 0) {
        Write-Warn "No PerplexityXPC block found in profile - nothing to remove."
        return
    }

    $endIdx = $content.IndexOf($endMarker)
    if ($endIdx -lt 0) {
        Write-Warn "Found start marker but no end marker. Manual review recommended."
        return
    }

    # Include the newline after the end marker if present
    $blockEnd = $endIdx + $endMarker.Length
    if ($blockEnd -lt $content.Length -and $content[$blockEnd] -eq "`n") {
        $blockEnd++
    }
    # Also consume a leading newline before the block if present
    $blockStart = $startIdx
    if ($blockStart -gt 0 -and $content[$blockStart - 1] -eq "`n") {
        $blockStart--
    }

    $newContent = $content.Substring(0, $blockStart) + $content.Substring($blockEnd)

    if ($PSCmdlet.ShouldProcess($ProfilePath, 'Remove PerplexityXPC integration block')) {
        Backup-Profile -ProfilePath $ProfilePath
        Set-ProfileContent -ProfilePath $ProfilePath -Content $newContent
        Write-Success "PerplexityXPC entries removed from profile."
    }
}

# --- Main ---

Write-Host ''
Write-Host 'PerplexityXPC Profile Installer' -ForegroundColor White
Write-Host '================================' -ForegroundColor White
Write-Host ''

$profilePath = $PROFILE.CurrentUserAllHosts
Write-Step "Profile path: ${profilePath}"

if ($Uninstall) {
    Write-Step "Uninstall mode - removing PerplexityXPC entries..."
    Remove-XPCProfileEntries -ProfilePath $profilePath
    Write-Host ''
    Write-Success "Uninstall complete."
    exit 0
}

# Module installation
if (-not $SkipModuleInstall) {
    Write-Step "Checking for PerplexityXPC module..."
    if (Test-ModuleInstalled) {
        Write-Success "PerplexityXPC module is already installed."
    } else {
        Write-Warn "PerplexityXPC module not found in module paths."
        $answer = Read-Host "  Copy module from repo? [Y/N]"
        if ($answer -match '^[Yy]') {
            $installed = Install-ModuleFromRepo
            if (-not $installed) {
                Write-Warn "Module not installed. Profile entries will still be added."
                Write-Warn "Install the module manually and ensure it is on PSModulePath."
            }
        } else {
            Write-Step "Skipping module install. Add manually later."
        }
    }
} else {
    Write-Step "Skipping module install check (-SkipModuleInstall specified)."
}

# Check if block already exists
$existing = Get-ProfileContent -ProfilePath $profilePath
if ($existing -match '# --- PerplexityXPC Integration ---') {
    Write-Warn "PerplexityXPC integration block already present in profile."
    $answer = Read-Host "  Re-add anyway? [Y/N]"
    if ($answer -notmatch '^[Yy]') {
        Write-Host ''
        Write-Success "No changes made."
        exit 0
    }
    # Remove existing block first so we don't duplicate
    Remove-XPCProfileEntries -ProfilePath $profilePath
    $existing = Get-ProfileContent -ProfilePath $profilePath
}

# Build and append block
$block = Build-ProfileBlock -IncludeAliases (-not $NoAliases) -IncludePrompt (-not $NoPrompt)

if ($PSCmdlet.ShouldProcess($profilePath, 'Append PerplexityXPC integration block')) {
    Backup-Profile -ProfilePath $profilePath
    $newContent = $existing + $block
    Set-ProfileContent -ProfilePath $profilePath -Content $newContent
    Write-Success "Integration block appended to profile."
}

Write-Host ''
Write-Host 'Install complete.' -ForegroundColor Green
Write-Host ''
Write-Host 'Aliases registered (after reloading profile):' -ForegroundColor White
Write-Host '  pplx       - Invoke-Perplexity'
Write-Host '  pplxcode   - Invoke-PerplexityCodeReview'
Write-Host '  pplxnet    - Invoke-PerplexityNetDiag'
Write-Host '  pplxevt    - Invoke-PerplexityEventAnalysis'
Write-Host '  pplxclip   - Invoke-PerplexityClipboard'
Write-Host '  pplxerr    - Ask-LastError'
Write-Host ''
Write-Host "Reload now with: . `"${profilePath}`"" -ForegroundColor Cyan
Write-Host ''
