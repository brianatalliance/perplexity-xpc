#Requires -Version 5.1
<#
.SYNOPSIS
    PerplexityXPC PowerShell Module
.DESCRIPTION
    PowerShell module for the PerplexityXPC broker. The broker runs as a Windows
    Service on http://127.0.0.1:47777 and proxies requests to the Perplexity Sonar
    API with MCP server management.
.NOTES
    Author: brianatalliance
    Version: 1.0.0
    Compatible with PowerShell 5.1 and PowerShell 7+
#>

Set-StrictMode -Version Latest

#region Module-Level Variables

$script:DefaultPort = 47777
$script:BaseUrl = 'http://127.0.0.1'

#endregion

#region Private Helper Functions

function Test-XPCConnection {
    <#
    .SYNOPSIS
        Tests whether the PerplexityXPC broker is reachable.
    .DESCRIPTION
        Performs a quick connectivity check against the PerplexityXPC broker status
        endpoint. Returns $true if the broker responds, $false otherwise.
    .PARAMETER Port
        The port the broker is listening on. Default: 47777.
    .OUTPUTS
        System.Boolean
    #>
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [int]$Port = $script:DefaultPort
    )

    try {
        $url = ('{0}:{1}/status' -f $script:BaseUrl, $Port)
        $null = Invoke-RestMethod -Uri $url -Method Get -TimeoutSec 3 -ErrorAction Stop
        return $true
    }
    catch {
        return $false
    }
}

function ConvertTo-XPCBody {
    <#
    .SYNOPSIS
        Builds the JSON request body for the /perplexity endpoint.
    .DESCRIPTION
        Constructs a hashtable with all relevant parameters for a Perplexity Sonar
        API request, then serializes it to JSON. Used internally by Invoke-Perplexity.
    .PARAMETER Query
        The user question or prompt.
    .PARAMETER Model
        The Perplexity model to use.
    .PARAMETER SystemPrompt
        Optional system-level instruction.
    .PARAMETER Temperature
        Sampling temperature (0-2).
    .PARAMETER MaxTokens
        Maximum tokens in the response.
    .PARAMETER SearchMode
        Search scope: web, academic, or sec.
    .PARAMETER DomainFilter
        Array of domains to restrict search to.
    .PARAMETER RecencyFilter
        Recency constraint: hour, day, week, month, or year.
    .OUTPUTS
        System.String (JSON body)
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Query,

        [string]$Model = 'sonar',

        [string]$SystemPrompt,

        [double]$Temperature = -1,

        [int]$MaxTokens = 0,

        [string]$SearchMode = 'web',

        [string[]]$DomainFilter,

        [string]$RecencyFilter
    )

    $body = @{
        query  = $Query
        model  = $Model
        search_mode = $SearchMode
    }

    if ($SystemPrompt) {
        $body['system_prompt'] = $SystemPrompt
    }

    if ($Temperature -ge 0) {
        $body['temperature'] = $Temperature
    }

    if ($MaxTokens -gt 0) {
        $body['max_tokens'] = $MaxTokens
    }

    if ($DomainFilter -and $DomainFilter.Count -gt 0) {
        $body['search_domain_filter'] = $DomainFilter
    }

    if ($RecencyFilter) {
        $body['search_recency_filter'] = $RecencyFilter
    }

    return ($body | ConvertTo-Json -Depth 10 -Compress)
}

#endregion

#region Core Functions

function Invoke-Perplexity {
    <#
    .SYNOPSIS
        Sends a query to the Perplexity Sonar API via the PerplexityXPC broker.
    .DESCRIPTION
        Posts a question to the PerplexityXPC broker running on localhost. By default,
        returns only the answer text and prints citations below. Use -Raw to receive
        the full JSON response object for advanced processing.
    .PARAMETER Query
        The question or prompt to send to Perplexity. This is a mandatory positional
        parameter.
    .PARAMETER Model
        The Perplexity Sonar model to use for the query.
        Valid values: sonar, sonar-pro, sonar-reasoning-pro, sonar-deep-research
        Default: sonar
    .PARAMETER SystemPrompt
        Optional system-level instruction to shape the model's behavior and response
        style.
    .PARAMETER Temperature
        Sampling temperature between 0 and 2. Higher values increase randomness.
        Default: not set (model default applies).
    .PARAMETER MaxTokens
        Maximum number of tokens in the response.
    .PARAMETER SearchMode
        Search scope to use. Valid values: web, academic, sec. Default: web.
    .PARAMETER DomainFilter
        One or more domain names to restrict the search to (e.g., 'docs.microsoft.com').
    .PARAMETER RecencyFilter
        Limit results to a time window. Valid values: hour, day, week, month, year.
    .PARAMETER Raw
        Return the full response object instead of just the answer text.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Invoke-Perplexity -Query 'What is the latest version of PowerShell?'

        Sends a simple query and prints the answer text with citations.
    .EXAMPLE
        Invoke-Perplexity 'Explain Zero Trust networking' -Model sonar-pro -SearchMode web -RecencyFilter month

        Queries with the sonar-pro model, restricted to results from the past month.
    .EXAMPLE
        $result = Invoke-Perplexity 'List S&P 500 movers today' -SearchMode web -Raw
        $result.choices[0].message.content

        Returns the raw response object for custom processing.
    .EXAMPLE
        Invoke-Perplexity 'CVE-2024-1234 details' -DomainFilter 'nvd.nist.gov','cve.mitre.org'

        Restricts search results to the NVD and MITRE CVE databases.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string]$Query,

        [Parameter()]
        [ValidateSet('sonar', 'sonar-pro', 'sonar-reasoning-pro', 'sonar-deep-research')]
        [string]$Model = 'sonar',

        [Parameter()]
        [string]$SystemPrompt,

        [Parameter()]
        [ValidateRange(0, 2)]
        [double]$Temperature = -1,

        [Parameter()]
        [int]$MaxTokens,

        [Parameter()]
        [ValidateSet('web', 'academic', 'sec')]
        [string]$SearchMode = 'web',

        [Parameter()]
        [string[]]$DomainFilter,

        [Parameter()]
        [ValidateSet('hour', 'day', 'week', 'month', 'year')]
        [string]$RecencyFilter,

        [Parameter()]
        [switch]$Raw,

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    if (-not (Test-XPCConnection -Port $Port)) {
        Write-Error ('PerplexityXPC broker is not reachable at {0}:{1}. Ensure the PerplexityXPC Windows Service is running.' -f $script:BaseUrl, $Port)
        return
    }

    $bodyParams = @{
        Query       = $Query
        Model       = $Model
        SearchMode  = $SearchMode
    }

    if ($SystemPrompt) { $bodyParams['SystemPrompt'] = $SystemPrompt }
    if ($Temperature -ge 0) { $bodyParams['Temperature'] = $Temperature }
    if ($MaxTokens -gt 0) { $bodyParams['MaxTokens'] = $MaxTokens }
    if ($DomainFilter) { $bodyParams['DomainFilter'] = $DomainFilter }
    if ($RecencyFilter) { $bodyParams['RecencyFilter'] = $RecencyFilter }

    $jsonBody = ConvertTo-XPCBody @bodyParams

    $url = ('{0}:{1}/perplexity' -f $script:BaseUrl, $Port)

    Write-Verbose ('POST {0}' -f $url)
    Write-Verbose ('Body: {0}' -f $jsonBody)

    try {
        $response = Invoke-RestMethod -Uri $url -Method Post -Body $jsonBody -ContentType 'application/json' -ErrorAction Stop
    }
    catch {
        $statusCode = $null
        if ($_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }
        if ($statusCode) {
            Write-Error ('Broker returned HTTP {0}: {1}' -f $statusCode, $_.Exception.Message)
        }
        else {
            Write-Error ('Failed to reach broker: {0}' -f $_.Exception.Message)
        }
        return
    }

    if ($Raw) {
        return $response
    }

    # Extract answer text
    $content = $null
    if ($response.choices -and $response.choices.Count -gt 0) {
        $content = $response.choices[0].message.content
    }
    else {
        $content = $response | ConvertTo-Json -Depth 5
    }

    Write-Output $content

    # Display citations
    if ($response.citations -and $response.citations.Count -gt 0) {
        Write-Output ''
        Write-Output '--- Citations ---'
        $i = 1
        foreach ($citation in $response.citations) {
            Write-Output ('[{0}] {1}' -f $i, $citation)
            $i++
        }
    }
}

function Get-XPCStatus {
    <#
    .SYNOPSIS
        Retrieves the status of the PerplexityXPC broker.
    .DESCRIPTION
        Queries the broker's /status endpoint and displays a formatted summary
        including version, uptime, and MCP server statuses.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Get-XPCStatus

        Displays the broker status using the default port.
    .EXAMPLE
        Get-XPCStatus -Port 48000

        Displays broker status on a non-default port.
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    $url = ('{0}:{1}/status' -f $script:BaseUrl, $Port)
    Write-Verbose ('GET {0}' -f $url)

    try {
        $status = Invoke-RestMethod -Uri $url -Method Get -ErrorAction Stop
    }
    catch {
        Write-Error ('Failed to get broker status: {0}' -f $_.Exception.Message)
        return
    }

    # Display broker summary
    Write-Output ''
    Write-Output '=== PerplexityXPC Broker Status ==='
    Write-Output ('Version : {0}' -f $status.version)
    Write-Output ('Status  : {0}' -f $status.status)
    Write-Output ('Uptime  : {0}' -f $status.uptime)
    Write-Output ''

    # Display MCP server table if present
    if ($status.mcp_servers -and $status.mcp_servers.Count -gt 0) {
        Write-Output '--- MCP Servers ---'
        $status.mcp_servers | Format-Table -AutoSize
    }
    else {
        Write-Output 'No MCP servers registered.'
    }

    return $status
}

function Get-XPCConfig {
    <#
    .SYNOPSIS
        Retrieves the current configuration of the PerplexityXPC broker.
    .DESCRIPTION
        Queries the broker's /config endpoint and returns the configuration object.
        Useful for inspecting current settings before making changes.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Get-XPCConfig

        Returns the current broker configuration.
    .EXAMPLE
        $cfg = Get-XPCConfig
        $cfg.api_key

        Retrieves and inspects a specific configuration value.
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    $url = ('{0}:{1}/config' -f $script:BaseUrl, $Port)
    Write-Verbose ('GET {0}' -f $url)

    try {
        $config = Invoke-RestMethod -Uri $url -Method Get -ErrorAction Stop
        return $config
    }
    catch {
        Write-Error ('Failed to get broker config: {0}' -f $_.Exception.Message)
        return
    }
}

function Set-XPCConfig {
    <#
    .SYNOPSIS
        Updates the PerplexityXPC broker configuration.
    .DESCRIPTION
        Sends updated configuration settings to the broker's /config endpoint via
        an HTTP PUT request. Pass a hashtable of settings to update.
    .PARAMETER Settings
        A hashtable of configuration key-value pairs to update.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Set-XPCConfig -Settings @{ default_model = 'sonar-pro'; timeout = 60 }

        Updates the default model and timeout settings.
    .EXAMPLE
        Set-XPCConfig -Settings @{ log_level = 'debug' } -Port 48000

        Enables debug logging on a non-default port.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Settings,

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    if (-not (Test-XPCConnection -Port $Port)) {
        Write-Error ('PerplexityXPC broker is not reachable at {0}:{1}.' -f $script:BaseUrl, $Port)
        return
    }

    $url = ('{0}:{1}/config' -f $script:BaseUrl, $Port)
    $jsonBody = $Settings | ConvertTo-Json -Depth 10 -Compress

    Write-Verbose ('PUT {0}' -f $url)
    Write-Verbose ('Body: {0}' -f $jsonBody)

    try {
        $result = Invoke-RestMethod -Uri $url -Method Put -Body $jsonBody -ContentType 'application/json' -ErrorAction Stop
        Write-Output 'Configuration updated successfully.'
        return $result
    }
    catch {
        Write-Error ('Failed to update broker config: {0}' -f $_.Exception.Message)
        return
    }
}

#endregion

#region MCP Server Management Functions

function Get-McpServer {
    <#
    .SYNOPSIS
        Lists all MCP servers managed by the PerplexityXPC broker.
    .DESCRIPTION
        Retrieves the list of registered MCP servers from the broker's /mcp/servers
        endpoint and displays a formatted table showing name, status, PID, and uptime.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Get-McpServer

        Lists all registered MCP servers and their current status.
    .EXAMPLE
        Get-McpServer | Where-Object { $_.status -eq 'running' }

        Filters to show only running MCP servers.
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    $url = ('{0}:{1}/mcp/servers' -f $script:BaseUrl, $Port)
    Write-Verbose ('GET {0}' -f $url)

    try {
        $servers = Invoke-RestMethod -Uri $url -Method Get -ErrorAction Stop
    }
    catch {
        Write-Error ('Failed to retrieve MCP servers: {0}' -f $_.Exception.Message)
        return
    }

    if (-not $servers -or $servers.Count -eq 0) {
        Write-Output 'No MCP servers registered.'
        return
    }

    $servers | Select-Object -Property name, status, pid, uptime | Format-Table -AutoSize
    return $servers
}

function Restart-McpServer {
    <#
    .SYNOPSIS
        Restarts a named MCP server managed by the PerplexityXPC broker.
    .DESCRIPTION
        Posts a restart request to the broker for the specified MCP server. Supports
        -WhatIf and -Confirm via SupportsShouldProcess.
    .PARAMETER Name
        The name of the MCP server to restart. This is mandatory.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Restart-McpServer -Name 'filesystem'

        Restarts the MCP server named 'filesystem'.
    .EXAMPLE
        Restart-McpServer -Name 'github' -WhatIf

        Shows what would happen without actually restarting the server.
    #>
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    if (-not (Test-XPCConnection -Port $Port)) {
        Write-Error ('PerplexityXPC broker is not reachable at {0}:{1}.' -f $script:BaseUrl, $Port)
        return
    }

    $url = ('{0}:{1}/mcp/servers/{2}/restart' -f $script:BaseUrl, $Port, $Name)

    if ($PSCmdlet.ShouldProcess($Name, 'Restart MCP Server')) {
        Write-Verbose ('POST {0}' -f $url)
        try {
            $result = Invoke-RestMethod -Uri $url -Method Post -ContentType 'application/json' -ErrorAction Stop
            Write-Output ('MCP server ''{0}'' restarted successfully.' -f $Name)
            return $result
        }
        catch {
            Write-Error ('Failed to restart MCP server ''{0}'': {1}' -f $Name, $_.Exception.Message)
            return
        }
    }
}

function Invoke-McpRequest {
    <#
    .SYNOPSIS
        Sends a JSON-RPC style request to a specific MCP server via the broker.
    .DESCRIPTION
        Posts a structured request to the broker's /mcp endpoint, targeting a named
        MCP server with a given method and optional parameters.
    .PARAMETER Server
        The name of the target MCP server. This is mandatory.
    .PARAMETER Method
        The MCP method to invoke (e.g., 'tools/list', 'tools/call'). This is mandatory.
    .PARAMETER Params
        Optional hashtable of parameters to pass to the MCP method.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Invoke-McpRequest -Server 'filesystem' -Method 'tools/list'

        Lists all tools available on the 'filesystem' MCP server.
    .EXAMPLE
        Invoke-McpRequest -Server 'github' -Method 'tools/call' -Params @{ name = 'list_repos'; arguments = @{ owner = 'myorg' } }

        Calls the 'list_repos' tool on the 'github' MCP server.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Server,

        [Parameter(Mandatory = $true)]
        [string]$Method,

        [Parameter()]
        [hashtable]$Params,

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    if (-not (Test-XPCConnection -Port $Port)) {
        Write-Error ('PerplexityXPC broker is not reachable at {0}:{1}.' -f $script:BaseUrl, $Port)
        return
    }

    $url = ('{0}:{1}/mcp' -f $script:BaseUrl, $Port)

    $body = @{
        server = $Server
        method = $Method
    }

    if ($Params -and $Params.Count -gt 0) {
        $body['params'] = $Params
    }

    $jsonBody = $body | ConvertTo-Json -Depth 10 -Compress
    Write-Verbose ('POST {0}' -f $url)
    Write-Verbose ('Body: {0}' -f $jsonBody)

    try {
        $result = Invoke-RestMethod -Uri $url -Method Post -Body $jsonBody -ContentType 'application/json' -ErrorAction Stop
        return $result
    }
    catch {
        Write-Error ('Failed to invoke MCP request on server ''{0}'': {1}' -f $Server, $_.Exception.Message)
        return
    }
}

#endregion

#region File Analysis Functions

function Invoke-PerplexityFileAnalysis {
    <#
    .SYNOPSIS
        Analyzes a file using the Perplexity Sonar API via the PerplexityXPC broker.
    .DESCRIPTION
        Reads the content of a file (up to 10,000 characters), detects the file type
        from its extension, and sends the content to Perplexity with a descriptive
        prompt. Supports pipeline input from Get-ChildItem.
    .PARAMETER Path
        The path to the file to analyze. Accepts pipeline input.
    .PARAMETER Prompt
        Custom prompt to use for the analysis. Default: "Analyze this file and explain
        its contents, purpose, and any notable elements".
    .PARAMETER Model
        The Perplexity model to use. Default: sonar-pro.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Invoke-PerplexityFileAnalysis -Path 'C:\scripts\deploy.ps1'

        Analyzes a PowerShell script and explains its purpose.
    .EXAMPLE
        Get-ChildItem 'C:\scripts\' -Filter '*.ps1' | Invoke-PerplexityFileAnalysis

        Analyzes all PowerShell files in the scripts directory via pipeline.
    .EXAMPLE
        Invoke-PerplexityFileAnalysis -Path 'C:\logs\error.log' -Prompt 'Identify any critical errors and summarize root causes'

        Analyzes a log file with a custom prompt.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true, ValueFromPipelineByPropertyName = $true)]
        [Alias('FullName')]
        [string]$Path,

        [Parameter()]
        [string]$Prompt = 'Analyze this file and explain its contents, purpose, and any notable elements',

        [Parameter()]
        [ValidateSet('sonar', 'sonar-pro', 'sonar-reasoning-pro', 'sonar-deep-research')]
        [string]$Model = 'sonar-pro',

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    process {
        if (-not (Test-Path -Path $Path -PathType Leaf)) {
            Write-Warning ('File not found: {0}' -f $Path)
            return
        }

        $fileInfo = Get-Item -Path $Path
        $extension = $fileInfo.Extension.TrimStart('.')
        $fileName = $fileInfo.Name

        Write-Verbose ('Reading file: {0}' -f $Path)

        try {
            $rawContent = Get-Content -Path $Path -Raw -Encoding UTF8 -ErrorAction Stop
        }
        catch {
            Write-Warning ('Could not read file {0}: {1}' -f $Path, $_.Exception.Message)
            return
        }

        # Limit to first 10000 characters for large files
        $maxChars = 10000
        $truncated = $false
        if ($rawContent -and $rawContent.Length -gt $maxChars) {
            $rawContent = $rawContent.Substring(0, $maxChars)
            $truncated = $true
        }

        if ($truncated) {
            Write-Warning ('File {0} is large; only the first {1} characters were sent.' -f $fileName, $maxChars)
        }

        $query = ('{0}{1}Filename: {2}{3}Type: {4}{5}{6}```{7}{8}{9}```' -f `
            $Prompt, [System.Environment]::NewLine + [System.Environment]::NewLine, `
            $fileName, [System.Environment]::NewLine, `
            $extension, [System.Environment]::NewLine + [System.Environment]::NewLine, `
            [System.Environment]::NewLine, [System.Environment]::NewLine, `
            $rawContent, [System.Environment]::NewLine)

        Write-Output ('--- Analysis: {0} ---' -f $fileName)
        Invoke-Perplexity -Query $query -Model $Model -Port $Port
        Write-Output ''
    }
}

function Invoke-PerplexityFolderAnalysis {
    <#
    .SYNOPSIS
        Analyzes a folder structure using the Perplexity Sonar API.
    .DESCRIPTION
        Generates a tree-like listing of a directory (up to 200 items) and sends
        it to Perplexity with a prompt about the folder's structure, purpose, and
        organization.
    .PARAMETER Path
        The path to the folder to analyze.
    .PARAMETER Prompt
        Custom prompt for the analysis. Default: "Analyze this folder structure and
        describe the project organization, purpose, and any notable patterns".
    .PARAMETER Model
        The Perplexity model to use. Default: sonar-pro.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Invoke-PerplexityFolderAnalysis -Path 'C:\Projects\MyApp'

        Analyzes the folder structure of a project directory.
    .EXAMPLE
        Invoke-PerplexityFolderAnalysis -Path 'C:\scripts' -Prompt 'What kind of automation does this script library support?'

        Asks a specific question about a scripts folder.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter()]
        [string]$Prompt = 'Analyze this folder structure and describe the project organization, purpose, and any notable patterns',

        [Parameter()]
        [ValidateSet('sonar', 'sonar-pro', 'sonar-reasoning-pro', 'sonar-deep-research')]
        [string]$Model = 'sonar-pro',

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    if (-not (Test-Path -Path $Path -PathType Container)) {
        Write-Error ('Folder not found: {0}' -f $Path)
        return
    }

    $folderInfo = Get-Item -Path $Path
    Write-Verbose ('Building directory listing for: {0}' -f $Path)

    # Build tree-like listing (max 200 items)
    $maxItems = 200
    $allItems = Get-ChildItem -Path $Path -Recurse -ErrorAction SilentlyContinue | Select-Object -First $maxItems

    $lines = [System.Collections.ArrayList]@()
    $null = $lines.Add($folderInfo.Name + '/')

    foreach ($item in $allItems) {
        $relativePath = $item.FullName.Substring($Path.Length).TrimStart('\').TrimStart('/')
        if ($item.PSIsContainer) {
            $null = $lines.Add('  [DIR]  ' + $relativePath + '/')
        }
        else {
            $sizeKb = [math]::Round($item.Length / 1KB, 1)
            $null = $lines.Add(('  [FILE] {0} ({1} KB)' -f $relativePath, $sizeKb))
        }
    }

    if ($allItems.Count -ge $maxItems) {
        $null = $lines.Add('  ... (listing truncated at {0} items)' -f $maxItems)
    }

    $listing = $lines -join [System.Environment]::NewLine

    $query = ('{0}{1}Folder: {2}{3}{4}```{5}{6}{7}```' -f `
        $Prompt, [System.Environment]::NewLine + [System.Environment]::NewLine, `
        $folderInfo.FullName, [System.Environment]::NewLine + [System.Environment]::NewLine, `
        [System.Environment]::NewLine, [System.Environment]::NewLine, `
        $listing, [System.Environment]::NewLine)

    Invoke-Perplexity -Query $query -Model $Model -Port $Port
}

#endregion

#region Batch Query Functions

function Invoke-PerplexityBatch {
    <#
    .SYNOPSIS
        Sends multiple queries to Perplexity and collects the results.
    .DESCRIPTION
        Iterates through an array of queries, sending each to the PerplexityXPC broker
        with a configurable delay between requests. Displays a progress bar and returns
        results as PSCustomObjects. Optionally exports results to CSV or JSON.
    .PARAMETER Queries
        An array of query strings to send. This is mandatory.
    .PARAMETER Model
        The Perplexity model to use for all queries. Default: sonar.
    .PARAMETER OutputPath
        Optional path for exporting results. Use .csv for CSV format, .json for JSON.
    .PARAMETER DelayMs
        Milliseconds to wait between queries to avoid rate limiting. Default: 500.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        $queries = @('What is Azure AD?', 'What is Intune?', 'What is Defender for Endpoint?')
        Invoke-PerplexityBatch -Queries $queries -Model sonar-pro

        Sends three queries and returns results as objects.
    .EXAMPLE
        Invoke-PerplexityBatch -Queries $queries -OutputPath 'C:\reports\results.csv' -DelayMs 1000

        Sends queries, waits 1 second between each, and exports results to CSV.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Queries,

        [Parameter()]
        [ValidateSet('sonar', 'sonar-pro', 'sonar-reasoning-pro', 'sonar-deep-research')]
        [string]$Model = 'sonar',

        [Parameter()]
        [string]$OutputPath,

        [Parameter()]
        [int]$DelayMs = 500,

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    if (-not (Test-XPCConnection -Port $Port)) {
        Write-Error ('PerplexityXPC broker is not reachable at {0}:{1}.' -f $script:BaseUrl, $Port)
        return
    }

    $results = [System.Collections.ArrayList]@()
    $total = $Queries.Count

    for ($i = 0; $i -lt $total; $i++) {
        $currentQuery = $Queries[$i]
        $percentComplete = [int](($i / $total) * 100)

        Write-Progress -Activity 'Invoke-PerplexityBatch' `
            -Status ('Query {0} of {1}: {2}' -f ($i + 1), $total, ($currentQuery.Substring(0, [Math]::Min(60, $currentQuery.Length)))) `
            -PercentComplete $percentComplete

        Write-Verbose ('Sending query {0}/{1}: {2}' -f ($i + 1), $total, $currentQuery)

        $responseText = $null
        $citations = @()
        $timestamp = Get-Date -Format 'yyyy-MM-ddTHH:mm:ss'

        try {
            $rawResponse = Invoke-Perplexity -Query $currentQuery -Model $Model -Port $Port -Raw

            if ($rawResponse) {
                if ($rawResponse.choices -and $rawResponse.choices.Count -gt 0) {
                    $responseText = $rawResponse.choices[0].message.content
                }
                if ($rawResponse.citations) {
                    $citations = $rawResponse.citations
                }
            }
        }
        catch {
            Write-Warning ('Query {0} failed: {1}' -f ($i + 1), $_.Exception.Message)
            $responseText = 'ERROR: ' + $_.Exception.Message
        }

        $resultObj = [PSCustomObject]@{
            Query      = $currentQuery
            Response   = $responseText
            Citations  = ($citations -join '; ')
            Model      = $Model
            Timestamp  = $timestamp
        }

        $null = $results.Add($resultObj)

        # Delay between queries (skip after last one)
        if ($i -lt ($total - 1) -and $DelayMs -gt 0) {
            Start-Sleep -Milliseconds $DelayMs
        }
    }

    Write-Progress -Activity 'Invoke-PerplexityBatch' -Completed

    # Export if OutputPath specified
    if ($OutputPath) {
        $ext = [System.IO.Path]::GetExtension($OutputPath).ToLower()
        try {
            if ($ext -eq '.csv') {
                $results | Export-Csv -Path $OutputPath -NoTypeInformation -Encoding UTF8
                Write-Output ('Results exported to CSV: {0}' -f $OutputPath)
            }
            elseif ($ext -eq '.json') {
                $results | ConvertTo-Json -Depth 10 | Out-File -FilePath $OutputPath -Encoding UTF8
                Write-Output ('Results exported to JSON: {0}' -f $OutputPath)
            }
            else {
                Write-Warning ('Unrecognized output format for {0}. Supported: .csv, .json' -f $OutputPath)
            }
        }
        catch {
            Write-Warning ('Failed to export results: {0}' -f $_.Exception.Message)
        }
    }

    return $results.ToArray()
}

function Invoke-PerplexityReport {
    <#
    .SYNOPSIS
        Generates a structured research report on a topic using Perplexity.
    .DESCRIPTION
        Researches a topic by querying Perplexity on multiple sub-questions. If no
        questions are provided, Perplexity is first asked to generate 5 key questions
        about the topic. All responses are compiled into a structured markdown report
        with citations. The report can be saved to a file.
    .PARAMETER Topic
        The research topic. This is mandatory.
    .PARAMETER Questions
        Specific sub-questions to research. If omitted, Perplexity auto-generates 5
        key questions about the topic.
    .PARAMETER Model
        The Perplexity model to use. Default: sonar-pro.
    .PARAMETER OutputPath
        Optional path to save the markdown report.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Invoke-PerplexityReport -Topic 'Windows 11 security hardening for enterprise'

        Generates a full research report with auto-generated questions.
    .EXAMPLE
        $questions = @('What is Zero Trust?', 'How does Conditional Access work?', 'Best practices for Intune compliance policies')
        Invoke-PerplexityReport -Topic 'Zero Trust with Intune' -Questions $questions -OutputPath 'C:\reports\zerotrust.md'

        Generates a targeted report and saves it to a file.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Topic,

        [Parameter()]
        [string[]]$Questions,

        [Parameter()]
        [ValidateSet('sonar', 'sonar-pro', 'sonar-reasoning-pro', 'sonar-deep-research')]
        [string]$Model = 'sonar-pro',

        [Parameter()]
        [string]$OutputPath,

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    if (-not (Test-XPCConnection -Port $Port)) {
        Write-Error ('PerplexityXPC broker is not reachable at {0}:{1}.' -f $script:BaseUrl, $Port)
        return
    }

    # Auto-generate questions if not provided
    if (-not $Questions -or $Questions.Count -eq 0) {
        Write-Verbose 'No questions provided - asking Perplexity to generate key questions.'
        Write-Output ('Generating key questions for topic: {0}' -f $Topic)

        $questionPrompt = ('Generate exactly 5 key research questions about the following topic. ' + `
            'Return ONLY a numbered list of questions, one per line, with no other text.{0}{0}Topic: {1}' -f `
            [System.Environment]::NewLine, $Topic)

        $questionResponse = Invoke-Perplexity -Query $questionPrompt -Model $Model -Port $Port -Raw

        $questionText = $null
        if ($questionResponse -and $questionResponse.choices -and $questionResponse.choices.Count -gt 0) {
            $questionText = $questionResponse.choices[0].message.content
        }

        if ($questionText) {
            # Parse numbered list
            $Questions = $questionText -split "`n" |
                Where-Object { $_ -match '^\s*\d+[\.\)]\s+.+' } |
                ForEach-Object { ($_ -replace '^\s*\d+[\.\)]\s+', '').Trim() } |
                Where-Object { $_.Length -gt 0 }
        }

        if (-not $Questions -or $Questions.Count -eq 0) {
            Write-Warning 'Could not parse generated questions. Using default questions.'
            $Questions = @(
                ('What is {0}?' -f $Topic),
                ('What are the key components of {0}?' -f $Topic),
                ('What are best practices for {0}?' -f $Topic),
                ('What are common challenges with {0}?' -f $Topic),
                ('What are recent developments in {0}?' -f $Topic)
            )
        }

        Write-Output 'Generated questions:'
        $qi = 1
        foreach ($q in $Questions) {
            Write-Output ('  {0}. {1}' -f $qi, $q)
            $qi++
        }
        Write-Output ''
    }

    # Research each question
    $reportSections = [System.Collections.ArrayList]@()
    $allCitations = [System.Collections.ArrayList]@()
    $total = $Questions.Count

    for ($i = 0; $i -lt $total; $i++) {
        $question = $Questions[$i]
        $percentComplete = [int](($i / $total) * 100)

        Write-Progress -Activity 'Invoke-PerplexityReport' `
            -Status ('Researching question {0} of {1}' -f ($i + 1), $total) `
            -PercentComplete $percentComplete

        Write-Verbose ('Researching: {0}' -f $question)

        $rawResponse = Invoke-Perplexity -Query $question -Model $Model -Port $Port -Raw

        $answerText = '(No response)'
        if ($rawResponse -and $rawResponse.choices -and $rawResponse.choices.Count -gt 0) {
            $answerText = $rawResponse.choices[0].message.content
        }

        if ($rawResponse -and $rawResponse.citations) {
            foreach ($cit in $rawResponse.citations) {
                if ($allCitations -notcontains $cit) {
                    $null = $allCitations.Add($cit)
                }
            }
        }

        $section = ('## {0}. {1}{2}{3}{4}' -f `
            ($i + 1), $question, [System.Environment]::NewLine + [System.Environment]::NewLine, `
            $answerText, [System.Environment]::NewLine)

        $null = $reportSections.Add($section)

        if ($i -lt ($total - 1)) {
            Start-Sleep -Milliseconds 500
        }
    }

    Write-Progress -Activity 'Invoke-PerplexityReport' -Completed

    # Compile the report
    $reportDate = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'

    $reportLines = [System.Collections.ArrayList]@()
    $null = $reportLines.Add('# Research Report: {0}' -f $Topic)
    $null = $reportLines.Add('')
    $null = $reportLines.Add(('**Generated:** {0}' -f $reportDate))
    $null = $reportLines.Add(('**Model:** {0}' -f $Model))
    $null = $reportLines.Add(('**Questions Researched:** {0}' -f $total))
    $null = $reportLines.Add('')
    $null = $reportLines.Add('---')
    $null = $reportLines.Add('')

    foreach ($section in $reportSections) {
        $null = $reportLines.Add($section)
        $null = $reportLines.Add('')
    }

    if ($allCitations.Count -gt 0) {
        $null = $reportLines.Add('---')
        $null = $reportLines.Add('')
        $null = $reportLines.Add('## Citations')
        $null = $reportLines.Add('')
        $ci = 1
        foreach ($citation in $allCitations) {
            $null = $reportLines.Add(('[{0}] {1}' -f $ci, $citation))
            $ci++
        }
    }

    $report = $reportLines -join [System.Environment]::NewLine

    if ($OutputPath) {
        try {
            $report | Out-File -FilePath $OutputPath -Encoding UTF8
            Write-Output ('Report saved to: {0}' -f $OutputPath)
        }
        catch {
            Write-Warning ('Failed to save report: {0}' -f $_.Exception.Message)
        }
    }

    return $report
}

#endregion

#region Atera/Intune Integration Functions

function Invoke-PerplexityTicketAnalysis {
    <#
    .SYNOPSIS
        Analyzes an IT support ticket using the Perplexity Sonar API.
    .DESCRIPTION
        Takes an Atera ticket object or any hashtable/PSObject with ticket fields and
        sends a structured analysis request to Perplexity. Provides root cause analysis,
        priority assessment, and troubleshooting steps. Optionally includes a full
        resolution guide.
    .PARAMETER TicketData
        A PSObject or hashtable with ticket fields: Title, Description, Category, etc.
        Accepts pipeline input.
    .PARAMETER Model
        The Perplexity model to use. Default: sonar-pro.
    .PARAMETER IncludeResolution
        If specified, also requests a step-by-step resolution guide.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        $ticket = @{ Title = 'Outlook not syncing'; Description = 'User cannot send emails since this morning'; Category = 'Email' }
        Invoke-PerplexityTicketAnalysis -TicketData $ticket

        Analyzes a support ticket hashtable.
    .EXAMPLE
        Get-AteraTickets | Invoke-PerplexityTicketAnalysis -IncludeResolution -Model sonar-reasoning-pro

        Pipelines Atera ticket objects for analysis with resolution steps.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
        [PSObject]$TicketData,

        [Parameter()]
        [ValidateSet('sonar', 'sonar-pro', 'sonar-reasoning-pro', 'sonar-deep-research')]
        [string]$Model = 'sonar-pro',

        [Parameter()]
        [switch]$IncludeResolution,

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    process {
        # Extract ticket fields - handle both hashtable and PSObject
        $title       = ''
        $description = ''
        $category    = ''
        $priority    = ''
        $status      = ''

        if ($TicketData -is [hashtable]) {
            $title       = if ($TicketData.ContainsKey('Title'))       { $TicketData['Title'] }       else { '' }
            $description = if ($TicketData.ContainsKey('Description')) { $TicketData['Description'] } else { '' }
            $category    = if ($TicketData.ContainsKey('Category'))    { $TicketData['Category'] }    else { '' }
            $priority    = if ($TicketData.ContainsKey('Priority'))    { $TicketData['Priority'] }    else { '' }
            $status      = if ($TicketData.ContainsKey('Status'))      { $TicketData['Status'] }      else { '' }
        }
        else {
            $title       = if ($null -ne $TicketData.Title)       { $TicketData.Title }       else { '' }
            $description = if ($null -ne $TicketData.Description) { $TicketData.Description } else { '' }
            $category    = if ($null -ne $TicketData.Category)    { $TicketData.Category }    else { '' }
            $priority    = if ($null -ne $TicketData.Priority)    { $TicketData.Priority }    else { '' }
            $status      = if ($null -ne $TicketData.Status)      { $TicketData.Status }      else { '' }
        }

        $analysisRequest = ('You are an IT helpdesk expert. Analyze this support ticket and provide:' + `
            [System.Environment]::NewLine + '1) Root cause analysis' + `
            [System.Environment]::NewLine + '2) Priority assessment' + `
            [System.Environment]::NewLine + '3) Recommended troubleshooting steps')

        if ($IncludeResolution) {
            $analysisRequest += [System.Environment]::NewLine + '4) Step-by-step resolution guide'
        }

        $analysisRequest += ('{0}{0}Ticket: {1}{0}Description: {2}' -f [System.Environment]::NewLine, $title, $description)

        if ($category) {
            $analysisRequest += ('{0}Category: {1}' -f [System.Environment]::NewLine, $category)
        }
        if ($priority) {
            $analysisRequest += ('{0}Priority: {1}' -f [System.Environment]::NewLine, $priority)
        }
        if ($status) {
            $analysisRequest += ('{0}Status: {1}' -f [System.Environment]::NewLine, $status)
        }

        $systemPrompt = ('You are an expert IT helpdesk analyst with deep knowledge of Windows environments, ' + `
            'Microsoft 365, Azure AD, Intune, and enterprise networking. Provide concise, actionable analysis.')

        Invoke-Perplexity -Query $analysisRequest -Model $Model -SystemPrompt $systemPrompt -Port $Port
    }
}

function Invoke-PerplexityDeviceAnalysis {
    <#
    .SYNOPSIS
        Analyzes an Intune device object using the Perplexity Sonar API.
    .DESCRIPTION
        Takes an Intune device object or hashtable with device properties and sends a
        focused analysis request to Perplexity. The analysis focus can be set to
        compliance, security, performance, or general.
    .PARAMETER DeviceData
        A PSObject or hashtable with device properties. Accepts pipeline input.
        Common fields: DeviceName, OS, ComplianceState, LastSyncDateTime, etc.
    .PARAMETER Focus
        The analysis focus area.
        - compliance: Analyze compliance status and recommend remediations.
        - security: Assess security posture and identify vulnerabilities.
        - performance: Evaluate performance indicators and optimization.
        - general: Provide an overall device health assessment.
        Default: general.
    .PARAMETER Model
        The Perplexity model to use. Default: sonar-pro.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        $device = @{ DeviceName = 'DESKTOP-ABC123'; OS = 'Windows 11 22H2'; ComplianceState = 'NonCompliant'; LastSyncDateTime = '2026-03-25' }
        Invoke-PerplexityDeviceAnalysis -DeviceData $device -Focus compliance

        Analyzes a device's compliance state and suggests remediations.
    .EXAMPLE
        Get-IntuneDevice | Where-Object { $_.ComplianceState -eq 'NonCompliant' } | Invoke-PerplexityDeviceAnalysis -Focus security

        Pipelines non-compliant devices for security analysis.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
        [PSObject]$DeviceData,

        [Parameter()]
        [ValidateSet('compliance', 'security', 'performance', 'general')]
        [string]$Focus = 'general',

        [Parameter()]
        [ValidateSet('sonar', 'sonar-pro', 'sonar-reasoning-pro', 'sonar-deep-research')]
        [string]$Model = 'sonar-pro',

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    process {
        # Build device property summary
        $deviceProps = [System.Collections.ArrayList]@()

        $propNames = @(
            'DeviceName', 'DeviceId', 'OS', 'OSVersion', 'ComplianceState',
            'LastSyncDateTime', 'ManagementAgent', 'OwnerType', 'EnrolledDateTime',
            'Manufacturer', 'Model', 'SerialNumber', 'UserPrincipalName',
            'AADRegistered', 'MDMStatus', 'EncryptionStatus', 'JailBroken'
        )

        foreach ($prop in $propNames) {
            $val = $null
            if ($DeviceData -is [hashtable]) {
                if ($DeviceData.ContainsKey($prop)) { $val = $DeviceData[$prop] }
            }
            else {
                $propObj = $DeviceData.PSObject.Properties[$prop]
                if ($null -ne $propObj) { $val = $propObj.Value }
            }

            if ($null -ne $val -and $val -ne '') {
                $null = $deviceProps.Add(('{0}: {1}' -f $prop, $val))
            }
        }

        $deviceSummary = $deviceProps -join [System.Environment]::NewLine

        # Build focus-specific prompt
        switch ($Focus) {
            'compliance' {
                $prompt = ('Analyze this device''s compliance status and recommend specific remediation steps. ' + `
                    'Include Intune policies, PowerShell commands, or configuration changes needed to achieve compliance.')
            }
            'security' {
                $prompt = ('Assess the security posture of this device and identify potential vulnerabilities or risks. ' + `
                    'Include specific hardening recommendations, relevant CVEs if applicable, and Intune OMA-URI policies.')
            }
            'performance' {
                $prompt = ('Evaluate the performance indicators of this device and provide optimization recommendations. ' + `
                    'Include Windows configuration settings, drivers, and management policies that could improve performance.')
            }
            default {
                $prompt = ('Provide a comprehensive health assessment for this managed device. ' + `
                    'Cover compliance, security, connectivity, and management status. Flag any concerns.')
            }
        }

        $fullQuery = ('{0}{1}{1}Device Information:{1}{2}' -f $prompt, [System.Environment]::NewLine, $deviceSummary)

        $systemPrompt = ('You are an expert Microsoft Intune and endpoint management specialist. ' + `
            'Provide clear, actionable analysis with specific PowerShell commands, ' + `
            'Intune configuration profiles, and remediation steps where applicable.')

        Invoke-Perplexity -Query $fullQuery -Model $Model -SystemPrompt $systemPrompt -Port $Port
    }
}

function Invoke-PerplexitySecurityAnalysis {
    <#
    .SYNOPSIS
        Analyzes a security finding or vulnerability using the Perplexity Sonar API.
    .DESCRIPTION
        Takes a security finding description and optional context, then queries Perplexity
        using a cybersecurity expert system prompt. Returns actionable remediation steps
        including PowerShell commands, Intune OMA-URI policies, and registry changes.
    .PARAMETER Finding
        Description of the security finding, vulnerability, or alert to analyze.
        This is mandatory.
    .PARAMETER Context
        Additional context about the environment (e.g., "Windows 11 22H2 enterprise,
        domain-joined, Intune-managed").
    .PARAMETER Model
        The Perplexity model to use. Default: sonar-reasoning-pro.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Invoke-PerplexitySecurityAnalysis -Finding 'CVE-2024-21447 detected on 15 endpoints'

        Analyzes a CVE finding and returns remediation steps.
    .EXAMPLE
        Invoke-PerplexitySecurityAnalysis `
            -Finding 'Suspicious PowerShell execution policy bypass detected via Defender for Endpoint' `
            -Context 'Windows 11 22H2 enterprise, Intune-managed, Azure AD joined'

        Analyzes a Defender alert with environmental context.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Finding,

        [Parameter()]
        [string]$Context,

        [Parameter()]
        [ValidateSet('sonar', 'sonar-pro', 'sonar-reasoning-pro', 'sonar-deep-research')]
        [string]$Model = 'sonar-reasoning-pro',

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    $systemPrompt = ('You are a cybersecurity expert specializing in enterprise Windows environments, ' + `
        'Intune MDM, and network security hardening. Provide actionable remediation steps ' + `
        'including PowerShell commands, Intune OMA-URI policies, and registry changes where applicable.')

    $query = ('Analyze this security finding and provide detailed remediation guidance:{0}{0}Finding: {1}' -f `
        [System.Environment]::NewLine, $Finding)

    if ($Context) {
        $query += ('{0}Environment Context: {1}' -f [System.Environment]::NewLine, $Context)
    }

    $query += ('{0}{0}Provide:{0}1) Severity assessment and CVSS considerations{0}' + `
        '2) Immediate containment steps{0}' + `
        '3) Root cause and attack vector explanation{0}' + `
        '4) Detailed remediation steps with specific commands or policy settings{0}' + `
        '5) Long-term hardening recommendations' -f [System.Environment]::NewLine)

    Invoke-Perplexity -Query $query -Model $Model -SystemPrompt $systemPrompt -Port $Port
}

#endregion
