<#
.SYNOPSIS
    Tests PerplexityXPC RemoteGateway endpoints locally and through Cloudflare Tunnel.

.DESCRIPTION
    Verifies that the RemoteGateway is running and accepting authenticated requests.
    Tests the following:
      - Local gateway health (http://127.0.0.1:47778/health)
      - Bearer token authentication
      - All API endpoints: /api/query, /api/execute, /api/system, /api/files
      - Cloudflare Tunnel URL (if provided)

    Outputs color-coded PASS/FAIL results for each test.

.PARAMETER TunnelUrl
    Optional. The public Cloudflare Tunnel URL (e.g., https://xpc.yourdomain.com).
    If provided, also tests external access through the tunnel.

.PARAMETER ApiToken
    The Bearer token for authenticating API requests.
    If omitted, tries to read from %USERPROFILE%\.perplexityxpc\remote-token.txt.

.PARAMETER LocalPort
    The local port the RemoteGateway listens on.
    Default: 47778

.PARAMETER SkipLocalTests
    Skip local endpoint tests and only test the tunnel URL.
    Requires -TunnelUrl.

.PARAMETER Verbose
    Show full request/response details for each test.

.EXAMPLE
    .\Test-RemoteAccess.ps1 -ApiToken "your64hextoken"
    Test local endpoints only.

.EXAMPLE
    .\Test-RemoteAccess.ps1 -TunnelUrl https://xpc.yourdomain.com -ApiToken "your64hextoken"
    Test both local endpoints and the external Cloudflare Tunnel.

.EXAMPLE
    .\Test-RemoteAccess.ps1
    Auto-loads token from token file and tests local endpoints.

.NOTES
    Requires: PowerShell 5.1 or 7+
    Requires: RemoteGateway service running (PerplexityXPCRemote)
#>

[CmdletBinding()]
param(
    [string]$TunnelUrl   = '',
    [string]$ApiToken    = '',
    [int]$LocalPort      = 47778,
    [switch]$SkipLocalTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'SilentlyContinue'

$LocalBase  = "http://127.0.0.1:$LocalPort"
$TokenFile  = Join-Path $env:USERPROFILE '.perplexityxpc\remote-token.txt'

# Track results
$Passed = 0
$Failed = 0
$Results = @()

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

function Write-TestResult {
    param(
        [string]$TestName,
        [bool]$Passed,
        [string]$Detail = ''
    )
    if ($Passed) {
        Write-Host "  [PASS] $TestName" -ForegroundColor Green
        if ($Detail) { Write-Host "         $Detail" -ForegroundColor Gray }
    } else {
        Write-Host "  [FAIL] $TestName" -ForegroundColor Red
        if ($Detail) { Write-Host "         $Detail" -ForegroundColor Yellow }
    }
}

function Invoke-GatewayRequest {
    param(
        [string]$Method = 'GET',
        [string]$Url,
        [string]$Token,
        [hashtable]$Body = $null,
        [int]$TimeoutSec = 10
    )
    try {
        $headers = @{}
        if ($Token) {
            $headers['Authorization'] = "Bearer $Token"
        }

        $params = @{
            Method             = $Method
            Uri                = $Url
            Headers            = $headers
            TimeoutSec         = $TimeoutSec
            UseBasicParsing    = $true
        }

        if ($Body -and $Method -ne 'GET') {
            $params['Body']        = ($Body | ConvertTo-Json -Compress)
            $params['ContentType'] = 'application/json'
        }

        $response = Invoke-WebRequest @params
        return [PSCustomObject]@{
            Success    = $true
            StatusCode = $response.StatusCode
            Content    = $response.Content
            Error      = $null
        }
    } catch {
        $sc = 0
        if ($_.Exception.Response) {
            $sc = [int]$_.Exception.Response.StatusCode
        }
        return [PSCustomObject]@{
            Success    = $false
            StatusCode = $sc
            Content    = $null
            Error      = $_.Exception.Message
        }
    }
}

function Test-Endpoint {
    param(
        [string]$Name,
        [string]$Method = 'GET',
        [string]$BaseUrl,
        [string]$Path,
        [string]$Token,
        [hashtable]$Body = $null,
        [int]$ExpectedStatus = 200,
        [bool]$RequireAuth = $true
    )

    $url = "$BaseUrl$Path"
    $result = Invoke-GatewayRequest -Method $Method -Url $url -Token $Token -Body $Body

    $pass = ($result.StatusCode -eq $ExpectedStatus)
    $detail = "HTTP $($result.StatusCode) | $url"
    if (-not $pass -and $result.Error) {
        $detail = "$($result.Error) | $url"
    }
    if ($pass -and $result.Content) {
        # Show a snippet of the response
        $snippet = ($result.Content -replace '[\r\n]', ' ').Substring(0, [Math]::Min(80, $result.Content.Length))
        $detail = "HTTP $($result.StatusCode) - $snippet..."
    }

    Write-TestResult -TestName $Name -Passed $pass -Detail $detail

    $script:Results += [PSCustomObject]@{
        Test   = $Name
        Passed = $pass
        Status = $result.StatusCode
        Url    = $url
    }

    if ($pass) { $script:Passed++ } else { $script:Failed++ }
    return $pass
}

# ---------------------------------------------------------------------------
# Load token
# ---------------------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($ApiToken)) {
    if (Test-Path $TokenFile) {
        $ApiToken = (Get-Content $TokenFile -Raw).Trim()
        Write-Host "[INFO] Loaded API token from: $TokenFile" -ForegroundColor Gray
    } else {
        Write-Host "[WARN] No -ApiToken supplied and token file not found at $TokenFile" -ForegroundColor Yellow
        Write-Host "       Auth tests will be skipped or may fail." -ForegroundColor Yellow
    }
}

# ---------------------------------------------------------------------------
# Check service
# ---------------------------------------------------------------------------
Write-Header 'PerplexityXPC Remote Access - Test Suite'

Write-Host '  Checking Windows Service...' -ForegroundColor White
$svc = Get-Service -Name 'PerplexityXPCRemote' -ErrorAction SilentlyContinue
if ($svc) {
    $color = if ($svc.Status -eq 'Running') { 'Green' } else { 'Red' }
    Write-Host "  Service 'PerplexityXPCRemote': $($svc.Status)" -ForegroundColor $color
} else {
    Write-Host "  Service 'PerplexityXPCRemote': Not installed" -ForegroundColor Yellow
}
Write-Host ''

# ---------------------------------------------------------------------------
# LOCAL TESTS
# ---------------------------------------------------------------------------
if (-not $SkipLocalTests) {
    Write-Host '[ Local Gateway Tests ]' -ForegroundColor Cyan
    Write-Host "  Base URL: $LocalBase" -ForegroundColor Gray
    Write-Host ''

    # Health check (no auth required)
    Test-Endpoint -Name 'Health check (no auth)' `
        -BaseUrl $LocalBase -Path '/health' `
        -Token '' -ExpectedStatus 200

    # Auth tests - unauthenticated should return 401
    Test-Endpoint -Name 'Auth gate - no token (expect 401)' `
        -BaseUrl $LocalBase -Path '/api/system' `
        -Token '' -ExpectedStatus 401

    # Auth tests - wrong token should return 401
    Test-Endpoint -Name 'Auth gate - wrong token (expect 401)' `
        -BaseUrl $LocalBase -Path '/api/system' `
        -Token 'wrongtoken123' -ExpectedStatus 401

    if ($ApiToken) {
        Write-Host ''
        Write-Host '  Authenticated endpoint tests:' -ForegroundColor White

        # System endpoints
        Test-Endpoint -Name 'GET /api/system' `
            -BaseUrl $LocalBase -Path '/api/system' -Token $ApiToken

        Test-Endpoint -Name 'GET /api/system/processes' `
            -BaseUrl $LocalBase -Path '/api/system/processes' -Token $ApiToken

        Test-Endpoint -Name 'GET /api/system/services' `
            -BaseUrl $LocalBase -Path '/api/system/services' -Token $ApiToken

        Test-Endpoint -Name 'GET /api/system/events' `
            -BaseUrl $LocalBase -Path '/api/system/events' -Token $ApiToken

        Test-Endpoint -Name 'GET /api/system/disks' `
            -BaseUrl $LocalBase -Path '/api/system/disks' -Token $ApiToken

        # Broker status
        Test-Endpoint -Name 'GET /api/status' `
            -BaseUrl $LocalBase -Path '/api/status' -Token $ApiToken

        # File endpoints
        Test-Endpoint -Name 'GET /api/files (root listing)' `
            -BaseUrl $LocalBase -Path '/api/files' -Token $ApiToken

        Test-Endpoint -Name 'GET /api/files/exists' `
            -BaseUrl $LocalBase -Path '/api/files/exists?path=C%3A%5CWindows' -Token $ApiToken

        # Query endpoint (POST)
        $queryBody = @{ query = 'What is 2 + 2?'; timeout = 5 }
        Test-Endpoint -Name 'POST /api/query' `
            -Method 'POST' -BaseUrl $LocalBase -Path '/api/query' `
            -Token $ApiToken -Body $queryBody

        # Execute endpoint (POST) - simple safe command
        $execBody = @{ command = 'Get-Date'; timeout = 5 }
        Test-Endpoint -Name 'POST /api/execute' `
            -Method 'POST' -BaseUrl $LocalBase -Path '/api/execute' `
            -Token $ApiToken -Body $execBody

        # Rate limiting - rapid requests should eventually return 429
        Write-Host ''
        Write-Host '  Rate limit test (sending 5 rapid requests)...' -ForegroundColor White
        $rateLimited = $false
        for ($i = 1; $i -le 5; $i++) {
            $r = Invoke-GatewayRequest -Method 'GET' -Url "$LocalBase/api/system" -Token $ApiToken -TimeoutSec 3
            if ($r.StatusCode -eq 429) {
                $rateLimited = $true
                break
            }
        }
        # 429 is not strictly expected on only 5 requests - note result informatively
        Write-Host "  [INFO] Rate limit triggered on burst: $rateLimited (limit may be higher)" -ForegroundColor Gray
    } else {
        Write-Host ''
        Write-Host '  [SKIP] Authenticated tests skipped (no token available).' -ForegroundColor Yellow
    }
}

# ---------------------------------------------------------------------------
# TUNNEL TESTS
# ---------------------------------------------------------------------------
if ($TunnelUrl) {
    $TunnelUrl = $TunnelUrl.TrimEnd('/')
    Write-Host ''
    Write-Host '[ Cloudflare Tunnel Tests ]' -ForegroundColor Cyan
    Write-Host "  Tunnel URL: $TunnelUrl" -ForegroundColor Gray
    Write-Host ''

    # Health through tunnel (no auth)
    Test-Endpoint -Name 'Tunnel - health check' `
        -BaseUrl $TunnelUrl -Path '/health' `
        -Token '' -ExpectedStatus 200

    if ($ApiToken) {
        Test-Endpoint -Name 'Tunnel - GET /api/system' `
            -BaseUrl $TunnelUrl -Path '/api/system' -Token $ApiToken

        Test-Endpoint -Name 'Tunnel - GET /api/system/disks' `
            -BaseUrl $TunnelUrl -Path '/api/system/disks' -Token $ApiToken

        $queryBody = @{ query = 'ping test'; timeout = 5 }
        Test-Endpoint -Name 'Tunnel - POST /api/query' `
            -Method 'POST' -BaseUrl $TunnelUrl -Path '/api/query' `
            -Token $ApiToken -Body $queryBody
    } else {
        Write-Host '  [SKIP] Authenticated tunnel tests skipped (no token).' -ForegroundColor Yellow
    }
} elseif (-not $SkipLocalTests) {
    Write-Host ''
    Write-Host '[INFO] Tunnel tests skipped. Pass -TunnelUrl to also test external access.' -ForegroundColor Gray
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host ('=' * 60) -ForegroundColor Cyan
Write-Host '  Test Summary' -ForegroundColor Cyan
Write-Host ('=' * 60) -ForegroundColor Cyan
Write-Host ''
Write-Host "  Passed: $Passed" -ForegroundColor Green
Write-Host "  Failed: $Failed" -ForegroundColor $(if ($Failed -gt 0) { 'Red' } else { 'Green' })
Write-Host "  Total:  $($Passed + $Failed)" -ForegroundColor White
Write-Host ''

if ($Failed -gt 0) {
    Write-Host '  Failed tests:' -ForegroundColor Red
    $Results | Where-Object { -not $_.Passed } | ForEach-Object {
        Write-Host "    - $($_.Test) (HTTP $($_.Status))" -ForegroundColor Red
    }
    Write-Host ''
    Write-Host '  Troubleshooting tips:' -ForegroundColor Yellow
    Write-Host '    - Check service: Get-Service PerplexityXPCRemote' -ForegroundColor White
    Write-Host '    - Check logs: Get-EventLog Application -Source PerplexityXPCRemote -Newest 20' -ForegroundColor White
    Write-Host '    - Verify token: compare with content of remote-token.txt' -ForegroundColor White
    Write-Host '    - Tunnel issues: cloudflared tunnel info perplexity-xpc' -ForegroundColor White
    Write-Host ''
}

if ($Failed -eq 0 -and ($Passed + $Failed) -gt 0) {
    Write-Host '  All tests passed. Remote access is working correctly.' -ForegroundColor Green
}
