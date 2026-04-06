<#
.SYNOPSIS
    Builds all PerplexityXPC projects and assembles a ready-to-install bin\ folder.

.DESCRIPTION
    Publishes PerplexityXPC.Service, PerplexityXPC.Tray, and PerplexityXPC.ContextMenu
    as self-contained win-x64 single-file executables, then copies scripts and default
    configuration files into the bin\ output folder.

    After a successful build, run .\scripts\Install-PerplexityXPC.ps1 from the repo root.

.PARAMETER Configuration
    Build configuration. Default: Release. Use "Debug" for development builds.

.PARAMETER OutputPath
    Output directory for the assembled bin. Default: <repo root>\bin

.PARAMETER Clean
    Remove the output directory before building.

.PARAMETER SkipService
    Skip building PerplexityXPC.Service.

.PARAMETER SkipTray
    Skip building PerplexityXPC.Tray.

.PARAMETER SkipContextMenu
    Skip building PerplexityXPC.ContextMenu.

.PARAMETER Quiet
    Pass -nologo and suppress most dotnet output.

.EXAMPLE
    .\scripts\Build-PerplexityXPC.ps1

.EXAMPLE
    .\scripts\Build-PerplexityXPC.ps1 -Configuration Debug -Clean

.EXAMPLE
    .\scripts\Build-PerplexityXPC.ps1 -OutputPath D:\build\xpc -Quiet

.NOTES
    Requires .NET 8 SDK (dotnet CLI) to be on PATH.
    Compatible with PowerShell 5.1 and PowerShell 7+.
#>

#region Parameters
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(HelpMessage = "Build configuration (Release or Debug).")]
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [Parameter(HelpMessage = "Output directory for assembled bin folder.")]
    [string]$OutputPath,

    [Parameter(HelpMessage = "Clean output directory before building.")]
    [switch]$Clean,

    [Parameter(HelpMessage = "Skip building PerplexityXPC.Service.")]
    [switch]$SkipService,

    [Parameter(HelpMessage = "Skip building PerplexityXPC.Tray.")]
    [switch]$SkipTray,

    [Parameter(HelpMessage = "Skip building PerplexityXPC.ContextMenu.")]
    [switch]$SkipContextMenu,

    [Parameter(HelpMessage = "Suppress dotnet build output.")]
    [switch]$Quiet
)
#endregion

#region Helpers
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Track build start time
$buildStart = Get-Date

function Write-Status {
    param([string]$Message, [string]$Color = 'Cyan')
    if (-not $Quiet) { Write-Host $Message -ForegroundColor $Color }
}
function Write-Success { param([string]$Message) Write-Host "  [OK]   $Message" -ForegroundColor Green }
function Write-Warn    { param([string]$Message) Write-Host "  [WARN] $Message" -ForegroundColor Yellow }
function Write-Err     { param([string]$Message) Write-Host "  [ERR]  $Message" -ForegroundColor Red }

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-DotnetPublish {
    param(
        [string]$ProjectPath,
        [string]$ProjectName,
        [string]$OutDir
    )

    Write-Step "Publishing $ProjectName..."

    if (-not (Test-Path $ProjectPath)) {
        Write-Warn "Project not found: $ProjectPath — skipping."
        return $false
    }

    $pubArgs = @(
        'publish', $ProjectPath,
        '--configuration', $Configuration,
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '--output', $OutDir,
        '/p:PublishSingleFile=true',
        '/p:EnableCompressionInSingleFile=true',
        '/p:PublishReadyToRun=true'
    )
    if ($Quiet) { $pubArgs += '--nologo'; $pubArgs += '--verbosity'; $pubArgs += 'minimal' }

    Write-Status "  dotnet $($pubArgs -join ' ')" -Color DarkGray

    if ($PSCmdlet.ShouldProcess($ProjectName, "dotnet publish")) {
        & dotnet @pubArgs
        if ($LASTEXITCODE -ne 0) {
            Write-Err "dotnet publish failed for $ProjectName (exit code $LASTEXITCODE)"
            return $false
        }
    }

    Write-Success "$ProjectName published to $OutDir"
    return $true
}
#endregion

#region Resolve paths
# Script is in <repo>\scripts\ — repo root is one level up
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Definition }
$repoRoot  = Split-Path -Parent $scriptDir

if (-not $OutputPath) {
    $OutputPath = Join-Path $repoRoot "bin"
}

$srcDir     = Join-Path $repoRoot "src"
$configDir  = Join-Path $repoRoot "config"

$projects = @(
    @{ Name = "PerplexityXPC.Service";     Path = Join-Path $srcDir "PerplexityXPC.Service\PerplexityXPC.Service.csproj";         Skip = $SkipService.IsPresent }
    @{ Name = "PerplexityXPC.Tray";        Path = Join-Path $srcDir "PerplexityXPC.Tray\PerplexityXPC.Tray.csproj";               Skip = $SkipTray.IsPresent }
    @{ Name = "PerplexityXPC.ContextMenu"; Path = Join-Path $srcDir "PerplexityXPC.ContextMenu\PerplexityXPC.ContextMenu.csproj"; Skip = $SkipContextMenu.IsPresent }
)
#endregion

#region Banner
if (-not $Quiet) {
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Magenta
    Write-Host "   PerplexityXPC Build Script v1.0.0" -ForegroundColor Magenta
    Write-Host "==========================================" -ForegroundColor Magenta
    Write-Host "  Configuration : $Configuration" -ForegroundColor White
    Write-Host "  Output        : $OutputPath"    -ForegroundColor White
    Write-Host ""
}
#endregion

#region Pre-flight: check for dotnet CLI
Write-Step "Checking prerequisites..."

$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetCmd) {
    Write-Err "dotnet CLI not found on PATH."
    Write-Err "Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
}

$dotnetVersion = & dotnet --version 2>$null
Write-Success "dotnet CLI found: $dotnetVersion"

# Warn if not .NET 8.x
if ($dotnetVersion -and -not $dotnetVersion.StartsWith('8.')) {
    Write-Warn ".NET 8 SDK is recommended. Detected: $dotnetVersion"
}
#endregion

#region Clean output directory
if ($Clean -and (Test-Path $OutputPath)) {
    Write-Step "Cleaning output directory: $OutputPath"
    if ($PSCmdlet.ShouldProcess($OutputPath, "Remove directory")) {
        Remove-Item -Path $OutputPath -Recurse -Force
        Write-Success "Output directory cleaned"
    }
}
#endregion

#region Create output directory
if (-not (Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}
#endregion

#region Restore solution
Write-Step "Restoring NuGet packages..."

$slnPath = Join-Path $repoRoot "PerplexityXPC.sln"
if (Test-Path $slnPath) {
    if ($PSCmdlet.ShouldProcess($slnPath, "dotnet restore")) {
        $restoreArgs = @('restore', $slnPath)
        if ($Quiet) { $restoreArgs += '--nologo'; $restoreArgs += '--verbosity'; $restoreArgs += 'minimal' }
        & dotnet @restoreArgs
        if ($LASTEXITCODE -ne 0) {
            Write-Err "dotnet restore failed (exit code $LASTEXITCODE)"
            exit 1
        }
    }
    Write-Success "NuGet packages restored"
} else {
    Write-Warn "PerplexityXPC.sln not found — skipping solution restore. Projects will restore individually."
}
#endregion

#region Publish each project
$failedProjects = @()

foreach ($proj in $projects) {
    if ($proj.Skip) {
        Write-Status "`n  [SKIP] $($proj.Name) — excluded by parameter" -Color DarkGray
        continue
    }

    $success = Invoke-DotnetPublish -ProjectPath $proj.Path -ProjectName $proj.Name -OutDir $OutputPath
    if (-not $success) {
        $failedProjects += $proj.Name
    }
}

if ($failedProjects.Count -gt 0) {
    Write-Err "The following projects failed to build: $($failedProjects -join ', ')"
    Write-Err "Fix build errors and re-run."
    exit 1
}
#endregion

#region Copy PowerShell scripts
Write-Step "Copying PowerShell scripts to $OutputPath\scripts..."

$outputScriptsDir = Join-Path $OutputPath "scripts"
if (-not (Test-Path $outputScriptsDir)) {
    New-Item -ItemType Directory -Path $outputScriptsDir -Force | Out-Null
}

$scriptsToInclude = @(
    "Install-PerplexityXPC.ps1",
    "Uninstall-PerplexityXPC.ps1",
    "Register-ContextMenu.ps1"
)

foreach ($script in $scriptsToInclude) {
    $src = Join-Path $scriptDir $script
    if (Test-Path $src) {
        if ($PSCmdlet.ShouldProcess($script, "Copy script")) {
            Copy-Item -Path $src -Destination (Join-Path $outputScriptsDir $script) -Force
            Write-Success "Copied: $script"
        }
    } else {
        Write-Warn "Script not found, skipping: $src"
    }
}
#endregion

#region Copy default configuration files
Write-Step "Copying default configuration files..."

$outputConfigDir = Join-Path $OutputPath "config"
if (-not (Test-Path $outputConfigDir)) {
    New-Item -ItemType Directory -Path $outputConfigDir -Force | Out-Null
}

# Look for config templates in the repo's config\ directory
if (Test-Path $configDir) {
    $configFiles = Get-ChildItem -Path $configDir -File
    foreach ($cf in $configFiles) {
        if ($PSCmdlet.ShouldProcess($cf.Name, "Copy config file")) {
            Copy-Item -Path $cf.FullName -Destination (Join-Path $outputConfigDir $cf.Name) -Force
            Write-Success "Copied config: $($cf.Name)"
        }
    }
} else {
    Write-Warn "Config directory not found ($configDir) — installer will generate defaults at runtime."
}
#endregion

#region Generate placeholder appsettings.json in bin root (for portable use)
$binAppSettings = Join-Path $OutputPath "appsettings.json"
if (-not (Test-Path $binAppSettings)) {
    $defaultSettings = @{
        PerplexityXPC = @{
            ApiEndpoint    = "https://api.perplexity.ai"
            PipeServerName = "PerplexityXPCPipe"
            HttpPort       = 47777
            LogLevel       = "Information"
        }
        Mcp = @{
            AutoRestart = $true
            TimeoutSec  = 30
        }
    } | ConvertTo-Json -Depth 4
    Set-Content -Path $binAppSettings -Value $defaultSettings -Encoding UTF8
    Write-Success "Generated default appsettings.json in bin"
}
#endregion

#region List output
Write-Step "Build output: $OutputPath"

if (-not $Quiet) {
    Get-ChildItem -Path $OutputPath -File | Sort-Object Name | ForEach-Object {
        $sizeKb = [Math]::Round($_.Length / 1KB, 1)
        Write-Host ("    {0,-45} {1,8} KB" -f $_.Name, $sizeKb) -ForegroundColor White
    }
}
#endregion

#region Done
$elapsed = (Get-Date) - $buildStart

if (-not $Quiet) {
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Green
    Write-Host "   Build completed in $([Math]::Round($elapsed.TotalSeconds, 1))s" -ForegroundColor Green
    Write-Host "==========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Next step: run the installer" -ForegroundColor Yellow
    Write-Host "    cd `"$(Split-Path -Parent $OutputPath)`"" -ForegroundColor DarkGray
    Write-Host "    .\bin\scripts\Install-PerplexityXPC.ps1" -ForegroundColor DarkGray
    Write-Host ""
}
#endregion
