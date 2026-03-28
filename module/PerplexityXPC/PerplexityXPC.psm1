#Requires -Version 5.1
<#
.SYNOPSIS
    PerplexityXPC PowerShell Module
.DESCRIPTION
    PowerShell module for the PerplexityXPC broker. The broker runs as a Windows
    Service on http://127.0.0.1:47777 and proxies requests to the Perplexity Sonar
    API with MCP server management.
.NOTES
    Author: PerplexityXPC Contributors
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

    $messages = [System.Collections.ArrayList]::new()
    if ($SystemPrompt) {
        $null = $messages.Add(@{ role = 'system'; content = $SystemPrompt })
    }
    $null = $messages.Add(@{ role = 'user'; content = $Query })

    $body = @{
        model    = $Model
        messages = @($messages)
    }

    if ($SearchMode -and $SearchMode -ne 'web') {
        $body['search_mode'] = $SearchMode
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

#region Extended Utility Functions

function Invoke-PerplexityEventAnalysis {
    <#
    .SYNOPSIS
        Analyzes Windows Event Log entries through Perplexity.
    .DESCRIPTION
        Reads Windows Event Log entries matching specified criteria, optionally groups
        them by source, and sends the compiled log data to the Perplexity Sonar API
        for root cause analysis and remediation recommendations. Supports both
        PowerShell 5.1 and PowerShell 7+.
    .PARAMETER LogName
        The name of the Windows Event Log to query. Default: System.
    .PARAMETER EntryType
        Filter events by entry type. Valid values: Error, Warning, Critical.
        Default: Error, Critical.
    .PARAMETER After
        Only include events that occurred after this datetime.
        Default: 24 hours ago.
    .PARAMETER MaxEvents
        Maximum number of events to collect and analyze. Default: 20.
    .PARAMETER GroupBySource
        When specified, groups events by ProviderName and shows occurrence counts
        before sending to Perplexity.
    .PARAMETER Model
        The Perplexity model to use. Default: sonar-pro.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Invoke-PerplexityEventAnalysis

        Analyzes Error and Critical events from the System log over the past 24 hours.
    .EXAMPLE
        Invoke-PerplexityEventAnalysis -LogName Application -EntryType Error,Warning -After (Get-Date).AddDays(-7)

        Analyzes Error and Warning events from the Application log over the past 7 days.
    .EXAMPLE
        Invoke-PerplexityEventAnalysis -GroupBySource -Model sonar-reasoning-pro

        Analyzes grouped System log errors using the reasoning-pro model.
    #>
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true)]
        [object[]]$InputObject,

        [Parameter()]
        [string]$LogName = 'System',

        [Parameter()]
        [ValidateSet('Error', 'Warning', 'Critical')]
        [string[]]$EntryType = @('Error', 'Critical'),

        [Parameter()]
        [datetime]$After = (Get-Date).AddHours(-24),

        [Parameter()]
        [int]$MaxEvents = 20,

        [Parameter()]
        [switch]$GroupBySource,

        [Parameter()]
        [ValidateSet('sonar', 'sonar-pro', 'sonar-reasoning-pro', 'sonar-deep-research')]
        [string]$Model = 'sonar-pro',

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    begin {
        $collectedEvents = [System.Collections.ArrayList]::new()
    }

    process {
        if ($InputObject) {
            foreach ($evt in $InputObject) {
                $null = $collectedEvents.Add($evt)
            }
        }
    }

    end {
        # If no pipeline input, fetch from event log
        if ($collectedEvents.Count -eq 0) {
            # Build level filter mapping
            $levels = [System.Collections.ArrayList]::new()
            foreach ($type in $EntryType) {
                switch ($type) {
                    'Critical' { $null = $levels.Add(1) }
                    'Error'    { $null = $levels.Add(2) }
                    'Warning'  { $null = $levels.Add(3) }
                }
            }

            $filterHash = @{
                LogName   = $LogName
                StartTime = $After
                Level     = $levels.ToArray()
            }

            Write-Verbose "Querying event log '$LogName' for levels $($levels -join ',') since $After"

            try {
                $rawEvents = Get-WinEvent -FilterHashtable $filterHash -MaxEvents $MaxEvents -ErrorAction Stop
                foreach ($evt in $rawEvents) {
                    $null = $collectedEvents.Add($evt)
                }
            }
            catch [System.Exception] {
                if ($_.Exception.Message -like '*No events were found*') {
                    Write-Warning "No events found in '$LogName' matching the specified criteria."
                    return
                }
                Write-Warning "Failed to query event log: $($_.Exception.Message)"
                return
            }
        }

        if ($collectedEvents.Count -eq 0) {
            Write-Warning 'No events to analyze.'
            return
        }

        Write-Verbose "Collected $($collectedEvents.Count) event(s) for analysis."

        # Format events
        $formattedLines = [System.Collections.ArrayList]::new()

        if ($GroupBySource) {
            # Group by ProviderName
            $groups = @{}
            foreach ($evt in $collectedEvents) {
                $provider = $evt.ProviderName
                if (-not $groups.ContainsKey($provider)) {
                    $groups[$provider] = [System.Collections.ArrayList]::new()
                }
                $null = $groups[$provider].Add($evt)
            }

            $null = $formattedLines.Add('--- Events grouped by source ---')
            foreach ($provider in ($groups.Keys | Sort-Object)) {
                $evtList = $groups[$provider]
                $null = $formattedLines.Add(("Source: {0} ({1} occurrence(s))" -f $provider, $evtList.Count))
                # Show first event as representative
                $rep = $evtList[0]
                $msg = $rep.Message
                if ($msg -and $msg.Length -gt 500) {
                    $msg = $msg.Substring(0, 500) + '...'
                }
                $null = $formattedLines.Add(("  Time: {0}  Id: {1}  Level: {2}" -f $rep.TimeCreated, $rep.Id, $rep.LevelDisplayName))
                $null = $formattedLines.Add(("  Message: {0}" -f $msg))
                $null = $formattedLines.Add('')
            }
        }
        else {
            foreach ($evt in $collectedEvents) {
                $msg = $evt.Message
                if ($msg -and $msg.Length -gt 500) {
                    $msg = $msg.Substring(0, 500) + '...'
                }
                $null = $formattedLines.Add(("[{0}] Id={1} Source={2} Level={3}" -f $evt.TimeCreated, $evt.Id, $evt.ProviderName, $evt.LevelDisplayName))
                $null = $formattedLines.Add(("  Message: {0}" -f $msg))
                $null = $formattedLines.Add('')
            }
        }

        $eventsText = $formattedLines -join [System.Environment]::NewLine
        $nowStr     = (Get-Date).ToString('u')
        $afterStr   = $After.ToString('u')

        $prompt = ('You are a Windows system administrator. Analyze these Windows Event Log entries and provide:' + `
            ' 1) Summary of issues found 2) Root cause analysis for each distinct error' + `
            ' 3) Recommended remediation steps with PowerShell commands where applicable' + `
            [System.Environment]::NewLine + [System.Environment]::NewLine + `
            'Log: ' + $LogName + [System.Environment]::NewLine + `
            'Time range: ' + $afterStr + ' to ' + $nowStr + [System.Environment]::NewLine + `
            'Events:' + [System.Environment]::NewLine + `
            $eventsText)

        Invoke-Perplexity -Query $prompt -Model $Model -Port $Port
    }
}

function Invoke-PerplexityNetDiag {
    <#
    .SYNOPSIS
        Runs network diagnostic commands and sends results to Perplexity for analysis.
    .DESCRIPTION
        Executes one or more network tests (ping, tracert, nslookup, portcheck) against
        a specified target host or IP address, then sends the compiled output to the
        Perplexity Sonar API for analysis by a network engineering expert persona.
        Compatible with PowerShell 5.1 and PowerShell 7+.
    .PARAMETER Target
        The hostname or IP address to diagnose. Mandatory.
    .PARAMETER Tests
        Which diagnostic tests to run. Valid values: ping, tracert, nslookup, portcheck, all.
        Default: all.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .PARAMETER TestPort
        The TCP port to check during the portcheck test. Default: 443.
    .PARAMETER Model
        The Perplexity model to use. Default: sonar-pro.
    .PARAMETER Context
        Additional environment context to include in the prompt
        (e.g., "This is our domain controller", "UniFi gateway").
    .EXAMPLE
        Invoke-PerplexityNetDiag 8.8.8.8

        Runs all diagnostics against 8.8.8.8 and returns Perplexity analysis.
    .EXAMPLE
        Invoke-PerplexityNetDiag "dc01.domain.local" -Tests ping,nslookup -Context "Primary domain controller"

        Runs ping and nslookup against the domain controller with contextual information.
    .EXAMPLE
        Invoke-PerplexityNetDiag "10.0.1.1" -Tests portcheck -TestPort 22 -Context "UniFi gateway SSH"

        Checks whether TCP port 22 is open on the UniFi gateway.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string]$Target,

        [Parameter()]
        [ValidateSet('ping', 'tracert', 'nslookup', 'portcheck', 'all')]
        [string[]]$Tests = @('all'),

        [Parameter()]
        [int]$Port = $script:DefaultPort,

        [Parameter()]
        [int]$TestPort = 443,

        [Parameter()]
        [ValidateSet('sonar', 'sonar-pro', 'sonar-reasoning-pro', 'sonar-deep-research')]
        [string]$Model = 'sonar-pro',

        [Parameter()]
        [string]$Context = ''
    )

    $runAll      = $Tests -contains 'all'
    $runPing     = $runAll -or ($Tests -contains 'ping')
    $runTracert  = $runAll -or ($Tests -contains 'tracert')
    $runNslookup = $runAll -or ($Tests -contains 'nslookup')
    $runPort     = $runAll -or ($Tests -contains 'portcheck')

    $outputParts = [System.Collections.ArrayList]::new()

    # --- ping ---
    if ($runPing) {
        Write-Progress -Activity 'Network Diagnostics' -Status 'Running ping...' -PercentComplete 10
        Write-Verbose "Running ping against $Target"
        try {
            $pingOut = & ping.exe -n 4 $Target 2>&1
            $null = $outputParts.Add(("=== PING ===`n{0}" -f ($pingOut -join "`n")))
        }
        catch {
            Write-Warning "ping failed: $($_.Exception.Message)"
            $null = $outputParts.Add('=== PING ===
ping.exe not available or failed.')
        }
    }

    # --- tracert ---
    if ($runTracert) {
        Write-Progress -Activity 'Network Diagnostics' -Status 'Running tracert...' -PercentComplete 30
        Write-Verbose "Running tracert against $Target"
        try {
            $traceOut = & tracert.exe -w 2000 -h 20 $Target 2>&1
            $null = $outputParts.Add(("=== TRACERT ===`n{0}" -f ($traceOut -join "`n")))
        }
        catch {
            Write-Warning "tracert failed: $($_.Exception.Message)"
            $null = $outputParts.Add('=== TRACERT ===
tracert.exe not available or failed.')
        }
    }

    # --- nslookup ---
    if ($runNslookup) {
        Write-Progress -Activity 'Network Diagnostics' -Status 'Running nslookup...' -PercentComplete 60
        Write-Verbose "Running nslookup (forward and reverse) for $Target"
        try {
            $nsOut = & nslookup.exe $Target 2>&1
            $null = $outputParts.Add(("=== NSLOOKUP (forward) ===`n{0}" -f ($nsOut -join "`n")))
        }
        catch {
            Write-Warning "nslookup forward failed: $($_.Exception.Message)"
            $null = $outputParts.Add('=== NSLOOKUP (forward) ===
nslookup.exe not available or failed.')
        }
        # Reverse lookup - only meaningful for IPs; attempt anyway
        try {
            $nsRevOut = & nslookup.exe -type=PTR $Target 2>&1
            $null = $outputParts.Add(("=== NSLOOKUP (reverse PTR) ===`n{0}" -f ($nsRevOut -join "`n")))
        }
        catch {
            Write-Verbose 'Reverse nslookup failed (may be expected for hostnames).'
        }
    }

    # --- portcheck ---
    if ($runPort) {
        Write-Progress -Activity 'Network Diagnostics' -Status 'Running port check...' -PercentComplete 85
        Write-Verbose "Checking TCP port ${TestPort} on $Target"
        try {
            $tcResult = Test-NetConnection -ComputerName $Target -Port $TestPort -WarningAction SilentlyContinue -ErrorAction Stop
            $portStatus = if ($tcResult.TcpTestSucceeded) { 'OPEN' } else { 'CLOSED/FILTERED' }
            $null = $outputParts.Add(("=== PORT CHECK (TCP {0}) ===`nTarget: {1}`nPort: {2}`nStatus: {3}`nLatency: {4}ms" -f `
                $TestPort, $Target, $TestPort, $portStatus, $tcResult.PingReplyDetails.RoundtripTime))
        }
        catch {
            Write-Warning "Test-NetConnection failed: $($_.Exception.Message)"
            $null = $outputParts.Add(("=== PORT CHECK (TCP {0}) ===`nTest-NetConnection failed: {1}" -f $TestPort, $_.Exception.Message))
        }
    }

    Write-Progress -Activity 'Network Diagnostics' -Completed

    $diagOutput  = $outputParts -join ([System.Environment]::NewLine + [System.Environment]::NewLine)
    $contextLine = if ($Context) { [System.Environment]::NewLine + 'Context: ' + $Context } else { '' }

    $prompt = ('You are a network engineer specializing in enterprise networking' + `
        ' (Ubiquiti UniFi, WatchGuard, Windows Server DNS). Analyze these network diagnostic results and provide:' + `
        ' 1) Connectivity assessment 2) Potential issues identified' + `
        ' 3) Recommended troubleshooting steps 4) Configuration changes if needed' + `
        [System.Environment]::NewLine + [System.Environment]::NewLine + `
        'Target: ' + $Target + $contextLine + `
        [System.Environment]::NewLine + [System.Environment]::NewLine + `
        'Results:' + [System.Environment]::NewLine + `
        $diagOutput)

    Invoke-Perplexity -Query $prompt -Model $Model -Port $Port
}

function Invoke-PerplexityCodeReview {
    <#
    .SYNOPSIS
        Sends code to Perplexity for review, debugging, or improvement.
    .DESCRIPTION
        Accepts code as a string or reads it from a file, auto-detects the language
        from the file extension when -Path is provided, then sends the code to the
        Perplexity Sonar API using a focus-specific expert system prompt.
        Compatible with PowerShell 5.1 and PowerShell 7+.
    .PARAMETER Code
        The code string to review. Accepts pipeline input.
    .PARAMETER Path
        Path to a file whose contents should be reviewed.
        Language is auto-detected from the file extension.
    .PARAMETER Language
        Language hint for the code. Auto-detected from -Path extension when available.
        Default: PowerShell.
    .PARAMETER Focus
        The type of review to perform.
        review   - Bugs, best practices, and general improvements.
        debug    - Identify and fix bugs and logic errors.
        optimize - Performance and efficiency improvements.
        security - Security vulnerabilities and unsafe practices.
        explain  - Step-by-step explanation of what the code does.
        Default: review.
    .PARAMETER Model
        The Perplexity model to use. Default: sonar-pro.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Get-Content .\script.ps1 -Raw | Invoke-PerplexityCodeReview -Focus security

        Pipes a PowerShell script for a security-focused audit.
    .EXAMPLE
        Invoke-PerplexityCodeReview -Path .\Deploy-Server.ps1 -Focus review

        Reviews a deployment script for best practices and bugs.
    .EXAMPLE
        Invoke-PerplexityCodeReview -Code 'Get-Process | Where-Object {$_.CPU -gt 100}' -Focus optimize

        Analyzes a one-liner for performance optimization opportunities.
    .EXAMPLE
        Invoke-PerplexityCodeReview -Path .\config.yaml -Focus explain

        Explains what a YAML configuration file does.
    #>
    [CmdletBinding(DefaultParameterSetName = 'CodeString')]
    param(
        [Parameter(ParameterSetName = 'CodeString', ValueFromPipeline = $true)]
        [string]$Code,

        [Parameter(ParameterSetName = 'FilePath', Mandatory = $true)]
        [string]$Path,

        [Parameter()]
        [string]$Language = 'PowerShell',

        [Parameter()]
        [ValidateSet('review', 'debug', 'optimize', 'security', 'explain')]
        [string]$Focus = 'review',

        [Parameter()]
        [ValidateSet('sonar', 'sonar-pro', 'sonar-reasoning-pro', 'sonar-deep-research')]
        [string]$Model = 'sonar-pro',

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    begin {
        $codeChunks = [System.Collections.ArrayList]::new()
    }

    process {
        if ($PSCmdlet.ParameterSetName -eq 'CodeString' -and $Code) {
            $null = $codeChunks.Add($Code)
        }
    }

    end {
        $finalCode = ''

        if ($PSCmdlet.ParameterSetName -eq 'FilePath') {
            if (-not (Test-Path -LiteralPath $Path)) {
                Write-Warning "File not found: $Path"
                return
            }

            # Auto-detect language from extension
            $ext = [System.IO.Path]::GetExtension($Path).ToLower()
            switch ($ext) {
                '.ps1'  { $Language = 'PowerShell' }
                '.psm1' { $Language = 'PowerShell' }
                '.psd1' { $Language = 'PowerShell' }
                '.py'   { $Language = 'Python' }
                '.cs'   { $Language = 'C#' }
                '.sh'   { $Language = 'Bash' }
                '.yaml' { $Language = 'YAML' }
                '.yml'  { $Language = 'YAML' }
                '.json' { $Language = 'JSON' }
                '.xml'  { $Language = 'XML' }
            }
            Write-Verbose "Detected language '$Language' from extension '$ext'"

            $rawContent = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop
            if ($rawContent.Length -gt 15000) {
                Write-Warning "File exceeds 15000 characters; truncating to first 15000 chars."
                $rawContent = $rawContent.Substring(0, 15000)
            }
            $finalCode = $rawContent
        }
        else {
            $finalCode = $codeChunks -join [System.Environment]::NewLine
        }

        if ([string]::IsNullOrWhiteSpace($finalCode)) {
            Write-Warning 'No code provided. Supply -Code, -Path, or pipe code as a string.'
            return
        }

        # Build focus-specific system prompt
        $systemPrompt = switch ($Focus) {
            'review' {
                'You are a senior code reviewer. Analyze this code for bugs, security issues,' + `
                ' best practices violations, and potential improvements. Provide specific line-level feedback.'
            }
            'debug' {
                'You are a debugging expert. Identify bugs, logic errors, and runtime issues in this code.' + `
                ' Explain the root cause and provide corrected code.'
            }
            'optimize' {
                'You are a performance optimization expert. Analyze this code for performance bottlenecks,' + `
                ' memory issues, and inefficiencies. Suggest optimized alternatives.'
            }
            'security' {
                'You are a cybersecurity code auditor. Analyze this code for security vulnerabilities,' + `
                ' injection risks, credential exposure, and unsafe practices. Provide remediation.'
            }
            'explain' {
                'You are a code educator. Explain this code step by step in clear language.' + `
                ' Describe what each section does and why.'
            }
        }

        $query = ('Language: {0}{1}{1}```{0}{1}{2}{1}```' -f `
            $Language, [System.Environment]::NewLine, $finalCode)

        Invoke-Perplexity -Query $query -Model $Model -SystemPrompt $systemPrompt -Port $Port
    }
}

function Send-XPCNotification {
    <#
    .SYNOPSIS
        Shows a Windows toast notification with a title and body.
    .DESCRIPTION
        Attempts to display a Windows toast notification using the best available
        method in this order:
        1. Windows.UI.Notifications.ToastNotificationManager (Windows 10/11 native)
        2. BurntToast PowerShell module if installed
        3. System.Windows.Forms.NotifyIcon balloon tip as a final fallback
        Compatible with PowerShell 5.1 and PowerShell 7+.
    .PARAMETER Title
        The notification title text. Mandatory.
    .PARAMETER Body
        The notification body text. Mandatory.
    .PARAMETER ActionUrl
        Optional URL to open when the notification is clicked (where supported).
    .EXAMPLE
        Send-XPCNotification -Title "Query Complete" -Body "VLAN trunking uses 802.1Q tagging to carry multiple VLANs over a single link."

        Shows a toast notification with the result of a query.
    .EXAMPLE
        Invoke-Perplexity "What is OSPF?" | ForEach-Object { Send-XPCNotification -Title "Perplexity Result" -Body $_ }

        Pipes a Perplexity result into a toast notification.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Title,

        [Parameter(Mandatory = $true)]
        [string]$Body,

        [Parameter()]
        [string]$ActionUrl = ''
    )

    # Method 1 - Windows.UI.Notifications (Win10+ native)
    $toastSuccess = $false
    try {
        $null = [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
        $template = [Windows.UI.Notifications.ToastTemplateType]::ToastText02
        $xml      = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent($template)
        $nodes    = $xml.GetElementsByTagName('text')
        $nodes.Item(0).AppendChild($xml.CreateTextNode($Title)) | Out-Null
        $nodes.Item(1).AppendChild($xml.CreateTextNode($Body))  | Out-Null

        $appId  = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe'
        $toast  = [Windows.UI.Notifications.ToastNotification]::new($xml)
        [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($appId).Show($toast)
        $toastSuccess = $true
        Write-Verbose 'Notification shown via Windows.UI.Notifications.'
    }
    catch {
        Write-Verbose "Windows.UI.Notifications not available: $($_.Exception.Message)"
    }

    # Method 2 - BurntToast module
    if (-not $toastSuccess) {
        if (Get-Module -Name BurntToast -ListAvailable -ErrorAction SilentlyContinue) {
            try {
                Import-Module BurntToast -ErrorAction Stop
                if ($ActionUrl) {
                    New-BurntToastNotification -Text $Title, $Body -ActivatedApp $ActionUrl
                }
                else {
                    New-BurntToastNotification -Text $Title, $Body
                }
                $toastSuccess = $true
                Write-Verbose 'Notification shown via BurntToast module.'
            }
            catch {
                Write-Verbose "BurntToast failed: $($_.Exception.Message)"
            }
        }
    }

    # Method 3 - WinForms NotifyIcon balloon tip
    if (-not $toastSuccess) {
        try {
            Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
            $notify = [System.Windows.Forms.NotifyIcon]::new()
            $notify.Icon    = [System.Drawing.SystemIcons]::Information
            $notify.Visible = $true
            $notify.ShowBalloonTip(5000, $Title, $Body, [System.Windows.Forms.ToolTipIcon]::Info)
            Start-Sleep -Seconds 6
            $notify.Dispose()
            $toastSuccess = $true
            Write-Verbose 'Notification shown via NotifyIcon balloon tip.'
        }
        catch {
            Write-Warning "All notification methods failed. Last error: $($_.Exception.Message)"
            Write-Warning "Title: $Title"
            Write-Warning "Body : $Body"
        }
    }

    if ($toastSuccess -and $ActionUrl) {
        Write-Verbose "ActionUrl '$ActionUrl' is set - URL launch supported only in native WinRT toast path."
    }
}

function Watch-XPCClipboard {
    <#
    .SYNOPSIS
        Monitors the clipboard and optionally queries Perplexity when new text is copied.
    .DESCRIPTION
        Polls the Windows clipboard at a configurable interval. When new text is detected
        that differs from the last check, it either notifies the user or automatically
        sends the text to Perplexity (when -AutoQuery is specified).
        Runs continuously until interrupted with Ctrl+C.
        Requires STA threading mode - issues a warning if running in MTA.
        Compatible with PowerShell 5.1 and PowerShell 7+.
    .PARAMETER AutoQuery
        When specified, automatically sends new clipboard text to Perplexity.
        Off by default to prevent unintended API calls.
    .PARAMETER MinLength
        Minimum clipboard text length required to trigger processing. Default: 10.
    .PARAMETER MaxLength
        Maximum clipboard text length to process (longer text is truncated). Default: 5000.
    .PARAMETER Model
        The Perplexity model to use for auto queries. Default: sonar.
    .PARAMETER Notify
        When specified, shows a toast notification with query results via Send-XPCNotification.
    .PARAMETER IntervalMs
        Clipboard poll interval in milliseconds. Default: 1000.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Watch-XPCClipboard

        Monitors clipboard and prints a message when new text is detected.
    .EXAMPLE
        Watch-XPCClipboard -AutoQuery -Notify

        Automatically queries Perplexity on new clipboard text and shows toast notifications.
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [switch]$AutoQuery,

        [Parameter()]
        [int]$MinLength = 10,

        [Parameter()]
        [int]$MaxLength = 5000,

        [Parameter()]
        [ValidateSet('sonar', 'sonar-pro', 'sonar-reasoning-pro', 'sonar-deep-research')]
        [string]$Model = 'sonar',

        [Parameter()]
        [switch]$Notify,

        [Parameter()]
        [int]$IntervalMs = 1000,

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    # STA check
    $apartment = [System.Threading.Thread]::CurrentThread.GetApartmentState()
    if ($apartment -ne [System.Threading.ApartmentState]::STA) {
        Write-Warning 'Clipboard access requires STA threading. Start PowerShell with -STA flag if clipboard reads fail.'
    }

    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
    }
    catch {
        Write-Warning "System.Windows.Forms assembly failed to load: $($_.Exception.Message)"
        return
    }

    $lastText = [System.Windows.Forms.Clipboard]::GetText()
    Write-Host 'Watching clipboard. Press Ctrl+C to stop.' -ForegroundColor Cyan

    while ($true) {
        Start-Sleep -Milliseconds $IntervalMs

        $currentText = ''
        try {
            $currentText = [System.Windows.Forms.Clipboard]::GetText()
        }
        catch {
            Write-Verbose "Clipboard read error: $($_.Exception.Message)"
            continue
        }

        if ($currentText -ne $lastText -and $currentText.Length -ge $MinLength) {
            $lastText = $currentText

            $processText = $currentText
            if ($processText.Length -gt $MaxLength) {
                $processText = $processText.Substring(0, $MaxLength)
                Write-Verbose "Clipboard text truncated to $MaxLength characters."
            }

            if ($AutoQuery) {
                Write-Host "[Clipboard] New text detected ($($processText.Length) chars) - querying Perplexity..." -ForegroundColor Yellow
                $result = Invoke-Perplexity -Query $processText -Model $Model -Port $Port
                Write-Host $result
                if ($Notify) {
                    $bodySnippet = if ($result.Length -gt 200) { $result.Substring(0, 200) + '...' } else { $result }
                    Send-XPCNotification -Title 'Perplexity Clipboard Result' -Body $bodySnippet
                }
            }
            else {
                Write-Host "[Clipboard] New text detected ($($processText.Length) chars). Run Invoke-PerplexityClipboard to query." -ForegroundColor Yellow
            }
        }
    }
}

function Invoke-PerplexityClipboard {
    <#
    .SYNOPSIS
        Reads the current clipboard contents and sends them to Perplexity.
    .DESCRIPTION
        One-shot clipboard query: retrieves the current text from the Windows clipboard,
        prepends a configurable prompt prefix, and sends the combined text to the
        Perplexity Sonar API via Invoke-Perplexity.
        Compatible with PowerShell 5.1 and PowerShell 7+.
    .PARAMETER Prompt
        The prompt prefix to prepend before the clipboard text.
        Default: "Explain the following".
    .PARAMETER Model
        The Perplexity model to use. Default: sonar.
    .PARAMETER Raw
        When specified, passes -Raw to Invoke-Perplexity for unformatted output.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Invoke-PerplexityClipboard

        Reads the clipboard and asks Perplexity to explain it.
    .EXAMPLE
        Invoke-PerplexityClipboard -Prompt "Debug this error message"

        Asks Perplexity to debug the error message currently in the clipboard.
    .EXAMPLE
        Invoke-PerplexityClipboard -Prompt "Translate to PowerShell" -Model sonar-pro

        Translates the clipboard text to PowerShell using the sonar-pro model.
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [string]$Prompt = 'Explain the following',

        [Parameter()]
        [ValidateSet('sonar', 'sonar-pro', 'sonar-reasoning-pro', 'sonar-deep-research')]
        [string]$Model = 'sonar',

        [Parameter()]
        [switch]$Raw,

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
    }
    catch {
        Write-Warning "System.Windows.Forms assembly failed to load: $($_.Exception.Message)"
        return
    }

    $clipText = [System.Windows.Forms.Clipboard]::GetText()

    if ([string]::IsNullOrWhiteSpace($clipText)) {
        Write-Warning 'Clipboard is empty or contains no text.'
        return
    }

    Write-Verbose "Clipboard text length: $($clipText.Length) characters."

    $query = ('{0}:{1}{1}{2}' -f $Prompt, [System.Environment]::NewLine, $clipText)

    if ($Raw) {
        Invoke-Perplexity -Query $query -Model $Model -Port $Port -Raw
    }
    else {
        Invoke-Perplexity -Query $query -Model $Model -Port $Port
    }
}

#endregion

#region Office Integration Functions

function Invoke-PerplexityEmailAnalysis {
    <#
    .SYNOPSIS
        Analyzes an Outlook email using the Perplexity Sonar API.
    .DESCRIPTION
        Reads an email from the currently selected Outlook item via COM automation, or
        accepts a subject and body directly. Sends the email content to the Perplexity
        Sonar API via the PerplexityXPC broker and returns analysis based on the
        specified focus mode (summarize, reply, categorize, sentiment, or action-items).
        Gracefully handles the case where Outlook is not running.
    .PARAMETER Subject
        The email subject line. Used when -FromOutlook is not specified.
    .PARAMETER Body
        The email body text. Used when -FromOutlook is not specified.
    .PARAMETER FromOutlook
        When specified, reads the currently selected email from Outlook via COM
        automation. Requires Outlook to be running with an email selected.
    .PARAMETER Focus
        The analysis mode to apply. Valid values:
          summarize    - Concise summary with key points, requests, and deadlines.
          reply        - Draft a professional reply addressing all points.
          categorize   - Categorize the email and explain the classification.
          sentiment    - Analyze tone, urgency, and emotional undertone.
          action-items - Extract action items, deadlines, and commitments as a checklist.
        Default: summarize
    .PARAMETER Model
        The Perplexity model to use. Default: sonar-pro.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Invoke-PerplexityEmailAnalysis -FromOutlook -Focus summarize

        Summarizes the currently selected Outlook email.
    .EXAMPLE
        Invoke-PerplexityEmailAnalysis -FromOutlook -Focus reply

        Drafts a professional reply to the currently selected Outlook email.
    .EXAMPLE
        Invoke-PerplexityEmailAnalysis -Subject "Server Down" -Body "The web server is not responding since 3pm." -Focus action-items

        Extracts action items from a manually provided email.
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [string]$Subject,

        [Parameter()]
        [string]$Body,

        [Parameter()]
        [switch]$FromOutlook,

        [Parameter()]
        [ValidateSet('summarize', 'reply', 'categorize', 'sentiment', 'action-items')]
        [string]$Focus = 'summarize',

        [Parameter()]
        [string]$Model = 'sonar-pro',

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    $sender = 'Unknown'
    $receivedTime = 'Unknown'

    if ($FromOutlook) {
        try {
            $outlook = [System.Runtime.InteropServices.Marshal]::GetActiveObject('Outlook.Application')
        }
        catch {
            Write-Error 'Outlook does not appear to be running. Start Outlook and select an email, or use -Subject and -Body parameters.'
            return
        }

        try {
            $explorer = $outlook.ActiveExplorer()
            if ($null -eq $explorer) {
                Write-Error 'No active Outlook explorer window found. Open an Outlook window and select an email.'
                return
            }

            $selection = $explorer.Selection
            if ($null -eq $selection -or $selection.Count -eq 0) {
                Write-Error 'No email is selected in Outlook. Click on an email and try again.'
                return
            }

            $item = $selection.Item(1)
            $Subject = $item.Subject
            $Body = $item.Body
            $sender = $item.SenderName
            $receivedTime = $item.ReceivedTime
        }
        catch {
            Write-Error ('Failed to read email from Outlook: {0}' -f $_.Exception.Message)
            return
        }
    }

    if ([string]::IsNullOrWhiteSpace($Subject) -and [string]::IsNullOrWhiteSpace($Body)) {
        Write-Error 'No email content provided. Use -FromOutlook or supply -Subject and -Body.'
        return
    }

    switch ($Focus) {
        'summarize'    { $systemPrompt = 'Summarize this email concisely. Highlight key points, requests, and deadlines.' }
        'reply'        { $systemPrompt = 'Draft a professional reply to this email. Be concise and address all points raised.' }
        'categorize'   { $systemPrompt = 'Categorize this email (e.g., Action Required, FYI, Meeting Request, Support Ticket, Vendor, Personal). Explain why.' }
        'sentiment'    { $systemPrompt = 'Analyze the tone and sentiment of this email. Identify urgency level and emotional undertone.' }
        'action-items' { $systemPrompt = 'Extract all action items, deadlines, and commitments from this email. Format as a checklist.' }
    }

    $query = ('From: {0}{1}Date: {2}{1}Subject: {3}{1}{1}{4}' -f $sender, [System.Environment]::NewLine, $receivedTime, $Subject, $Body)

    Invoke-Perplexity -Query $query -Model $Model -SystemPrompt $systemPrompt -Port $Port
}

function Invoke-PerplexityEmailDraft {
    <#
    .SYNOPSIS
        Drafts an email using Perplexity and optionally creates it in Outlook.
    .DESCRIPTION
        Sends a prompt to the Perplexity Sonar API via the PerplexityXPC broker to
        generate a professional email draft with the specified tone. Optionally creates
        a new draft mail item in Outlook using COM automation and displays it for
        review. Gracefully handles the case where Outlook is not running.
    .PARAMETER Prompt
        A description of what the email should say or accomplish. This is mandatory.
    .PARAMETER To
        The intended recipient name or role, used as context for the draft.
    .PARAMETER Tone
        The writing tone to apply. Valid values:
          professional - Formal business language (default).
          casual       - Relaxed, conversational tone.
          formal       - Highly structured and deferential.
          urgent       - Direct and time-sensitive language.
          friendly     - Warm and personable.
        Default: professional
    .PARAMETER CreateInOutlook
        When specified, creates a new Outlook draft mail item with the generated text
        and opens it for review. Requires Outlook to be running.
    .PARAMETER Model
        The Perplexity model to use. Default: sonar-pro.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Invoke-PerplexityEmailDraft -Prompt "Follow up on the network upgrade project timeline" -To "IT Director" -Tone professional

        Generates a professional follow-up email to the IT Director.
    .EXAMPLE
        Invoke-PerplexityEmailDraft -Prompt "Request vendor pricing for 50 UniFi APs" -CreateInOutlook

        Drafts a vendor pricing request and opens it as a new Outlook email.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prompt,

        [Parameter()]
        [string]$To,

        [Parameter()]
        [ValidateSet('professional', 'casual', 'formal', 'urgent', 'friendly')]
        [string]$Tone = 'professional',

        [Parameter()]
        [switch]$CreateInOutlook,

        [Parameter()]
        [string]$Model = 'sonar-pro',

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    $systemPrompt = ('You are a professional email writer. Draft an email with the following tone: {0}. Output only the email body, no subject line unless asked.' -f $Tone)

    if ($To) {
        $query = ('Draft an email to {0}. {1}' -f $To, $Prompt)
    }
    else {
        $query = ('Draft an email. {0}' -f $Prompt)
    }

    $draftText = Invoke-Perplexity -Query $query -Model $Model -SystemPrompt $systemPrompt -Port $Port

    if (-not $draftText) {
        return
    }

    Write-Output $draftText

    if ($CreateInOutlook) {
        try {
            $outlook = [System.Runtime.InteropServices.Marshal]::GetActiveObject('Outlook.Application')
        }
        catch {
            Write-Warning 'Outlook does not appear to be running. The draft was not created in Outlook. Start Outlook and try again with -CreateInOutlook.'
            return
        }

        try {
            $mailItem = $outlook.CreateItem(0)
            $mailItem.Body = $draftText
            if ($To) {
                $mailItem.To = $To
            }
            $mailItem.Display($false)
            Write-Verbose 'New Outlook draft created and displayed.'
        }
        catch {
            Write-Warning ('Failed to create Outlook draft: {0}' -f $_.Exception.Message)
        }
    }

    return $draftText
}

function Invoke-PerplexityDocumentReview {
    <#
    .SYNOPSIS
        Reviews the active Word document or a provided file using Perplexity.
    .DESCRIPTION
        Reads document content from the active Microsoft Word document via COM
        automation, or from a file path (.docx, .txt, .md). Sends the content to the
        Perplexity Sonar API via the PerplexityXPC broker for analysis based on the
        chosen focus mode. Text is truncated to -MaxChars to stay within API limits.
        Gracefully handles the case where Word is not running.
    .PARAMETER Path
        Path to a .docx, .txt, or .md file to review. Word COM is used for .docx files.
    .PARAMETER FromWord
        When specified, reads content from the active Microsoft Word document via COM
        automation. Requires Word to be running with a document open.
    .PARAMETER Focus
        The review mode to apply. Valid values:
          review     - Clarity, grammar, structure, and professionalism feedback (default).
          fact-check - Verify factual claims and flag inaccuracies.
          summarize  - Comprehensive summary with key points and conclusions.
          improve    - Rewrite for clarity, professionalism, and impact.
          compliance - Review for IT policy, HIPAA, and professional standards compliance.
        Default: review
    .PARAMETER MaxChars
        Maximum characters of document text to send to the API. Default: 20000.
    .PARAMETER Model
        The Perplexity model to use. Default: sonar-pro.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Invoke-PerplexityDocumentReview -FromWord -Focus review

        Reviews the currently active Word document for clarity and grammar.
    .EXAMPLE
        Invoke-PerplexityDocumentReview -Path "C:\Docs\SOP-NetworkSecurity.docx" -Focus compliance

        Reviews a saved document for compliance with enterprise IT and HIPAA policies.
    .EXAMPLE
        Invoke-PerplexityDocumentReview -FromWord -Focus fact-check -Model sonar-reasoning-pro

        Fact-checks the active Word document using the reasoning-focused model.
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [string]$Path,

        [Parameter()]
        [switch]$FromWord,

        [Parameter()]
        [ValidateSet('review', 'fact-check', 'summarize', 'improve', 'compliance')]
        [string]$Focus = 'review',

        [Parameter()]
        [int]$MaxChars = 20000,

        [Parameter()]
        [string]$Model = 'sonar-pro',

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    $documentText = $null

    if ($FromWord) {
        try {
            $word = [System.Runtime.InteropServices.Marshal]::GetActiveObject('Word.Application')
        }
        catch {
            Write-Error 'Microsoft Word does not appear to be running. Open a Word document first, or use the -Path parameter.'
            return
        }

        try {
            $doc = $word.ActiveDocument
            if ($null -eq $doc) {
                Write-Error 'No active Word document found. Open a document in Word and try again.'
                return
            }
            $documentText = $doc.Content.Text
        }
        catch {
            Write-Error ('Failed to read from Word: {0}' -f $_.Exception.Message)
            return
        }
    }
    elseif ($Path) {
        if (-not (Test-Path -LiteralPath $Path)) {
            Write-Error ('File not found: {0}' -f $Path)
            return
        }

        $ext = [System.IO.Path]::GetExtension($Path).ToLower()

        if ($ext -eq '.docx') {
            try {
                $word = New-Object -ComObject Word.Application
                $word.Visible = $false
                $docObj = $word.Documents.Open($Path)
                $documentText = $docObj.Content.Text
                $docObj.Close($false)
                [System.Runtime.InteropServices.Marshal]::ReleaseComObject($docObj) | Out-Null
                $word.Quit()
                [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
            }
            catch {
                Write-Error ('Failed to open .docx via Word COM: {0}. Ensure Microsoft Word is installed.' -f $_.Exception.Message)
                return
            }
        }
        elseif ($ext -eq '.txt' -or $ext -eq '.md') {
            $documentText = Get-Content -LiteralPath $Path -Raw
        }
        else {
            Write-Error ('Unsupported file type: {0}. Supported types: .docx, .txt, .md' -f $ext)
            return
        }
    }
    else {
        Write-Error 'Specify either -FromWord to read from the active Word document, or -Path to provide a file.'
        return
    }

    if ([string]::IsNullOrWhiteSpace($documentText)) {
        Write-Warning 'The document appears to be empty.'
        return
    }

    if ($documentText.Length -gt $MaxChars) {
        Write-Verbose ('Document truncated from {0} to {1} characters.' -f $documentText.Length, $MaxChars)
        $documentText = $documentText.Substring(0, $MaxChars)
    }

    switch ($Focus) {
        'review'     { $systemPrompt = 'Review this document for clarity, grammar, structure, and professionalism. Suggest specific improvements.' }
        'fact-check' { $systemPrompt = 'Identify all factual claims in this document and verify their accuracy. Flag any unverifiable or incorrect statements.' }
        'summarize'  { $systemPrompt = 'Provide a comprehensive summary of this document. Include key points, conclusions, and recommendations.' }
        'improve'    { $systemPrompt = 'Rewrite and improve this document for clarity, professionalism, and impact. Maintain the original intent.' }
        'compliance' { $systemPrompt = 'Review this document for compliance with enterprise IT policies, HIPAA requirements, and professional standards. Flag any issues.' }
    }

    Invoke-Perplexity -Query $documentText -Model $Model -SystemPrompt $systemPrompt -Port $Port
}

function Invoke-PerplexityResearchInsert {
    <#
    .SYNOPSIS
        Researches a topic and optionally inserts the result into the active Word document.
    .DESCRIPTION
        Sends a research query to the Perplexity Sonar API via the PerplexityXPC
        broker and returns the result formatted as paragraph, bullets, numbered list,
        or table text. When -InsertInWord is specified, the result is inserted at the
        current cursor position in the active Microsoft Word document via COM
        automation. Gracefully handles the case where Word is not running.
    .PARAMETER Query
        The research topic or question to send to Perplexity. This is mandatory.
    .PARAMETER InsertInWord
        When specified, inserts the research result at the current Selection cursor
        position in the active Microsoft Word document. Requires Word to be running.
    .PARAMETER Format
        The output format for the research result. Valid values:
          paragraph - Flowing prose text (default).
          bullets   - Bullet-point list.
          numbered  - Numbered list.
          table     - Tabular format.
        Default: paragraph
    .PARAMETER Model
        The Perplexity model to use. Default: sonar-pro.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Invoke-PerplexityResearchInsert -Query "NIST Zero Trust Architecture principles" -Format bullets

        Returns Zero Trust principles as a bullet list.
    .EXAMPLE
        Invoke-PerplexityResearchInsert -Query "History of TCP/IP protocol development" -InsertInWord

        Researches TCP/IP history and inserts the result at the cursor in Word.
    .EXAMPLE
        Invoke-PerplexityResearchInsert -Query "Compare SD-WAN vendors 2025" -Format table -InsertInWord

        Inserts a vendor comparison table at the cursor in the active Word document.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Query,

        [Parameter()]
        [switch]$InsertInWord,

        [Parameter()]
        [ValidateSet('paragraph', 'bullets', 'numbered', 'table')]
        [string]$Format = 'paragraph',

        [Parameter()]
        [string]$Model = 'sonar-pro',

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    $systemPrompt = ('You are a research assistant writing content for a professional document. Format your response as {0}. Be factual and cite sources inline.' -f $Format)

    $result = Invoke-Perplexity -Query $Query -Model $Model -SystemPrompt $systemPrompt -Port $Port

    if (-not $result) {
        return
    }

    if ($InsertInWord) {
        try {
            $word = [System.Runtime.InteropServices.Marshal]::GetActiveObject('Word.Application')
        }
        catch {
            Write-Warning 'Microsoft Word does not appear to be running. The result was not inserted. Open a Word document and try again with -InsertInWord.'
            return $result
        }

        try {
            $selection = $word.Selection
            $selection.TypeText($result)
            Write-Verbose 'Research result inserted at cursor position in Word.'
        }
        catch {
            Write-Warning ('Failed to insert text into Word: {0}' -f $_.Exception.Message)
        }
    }

    return $result
}

function Invoke-PerplexityExcelAnalysis {
    <#
    .SYNOPSIS
        Analyzes data from the active Excel workbook or a provided file using Perplexity.
    .DESCRIPTION
        Reads spreadsheet data from the active Microsoft Excel workbook via COM
        automation (current selection or UsedRange), or from a .csv or .xlsx file.
        Converts the data to tab-separated values and sends it to the Perplexity Sonar
        API via the PerplexityXPC broker for analysis. The focus mode controls what
        kind of analysis is performed. Row count is limited by -MaxRows.
        Gracefully handles the case where Excel is not running.
    .PARAMETER FromExcel
        When specified, reads data from the active Microsoft Excel workbook via COM
        automation. Uses the current selection or the worksheet UsedRange.
        Requires Excel to be running with a workbook open.
    .PARAMETER Path
        Path to a .xlsx or .csv file to analyze.
    .PARAMETER SheetName
        The name of the worksheet to read when using -Path with a .xlsx file.
        If omitted, the first (active) sheet is used.
    .PARAMETER Range
        A cell range string (e.g., "A1:D50") to limit the data read from Excel.
        Default: UsedRange of the active sheet.
    .PARAMETER Focus
        The analysis mode to apply. Valid values:
          analyze       - Patterns, key metrics, and visualization suggestions (default).
          trends        - Growth patterns, trends, and statistical observations.
          anomalies     - Outliers and suspicious values with explanations.
          formula-help  - Useful Excel formula recommendations for this data structure.
          pivot-suggest - Suggested pivot table configurations with rows, columns, values.
        Default: analyze
    .PARAMETER MaxRows
        Maximum number of data rows to include in the analysis. Default: 100.
    .PARAMETER Model
        The Perplexity model to use. Default: sonar-pro.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Invoke-PerplexityExcelAnalysis -FromExcel -Focus analyze

        Analyzes the UsedRange of the active Excel worksheet.
    .EXAMPLE
        Invoke-PerplexityExcelAnalysis -Path "C:\Reports\inventory.csv" -Focus anomalies

        Reads a CSV file and identifies data anomalies.
    .EXAMPLE
        Invoke-PerplexityExcelAnalysis -FromExcel -Focus formula-help -Range "A1:F100"

        Suggests Excel formulas based on data in the range A1:F100.
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [switch]$FromExcel,

        [Parameter()]
        [string]$Path,

        [Parameter()]
        [string]$SheetName,

        [Parameter()]
        [string]$Range,

        [Parameter()]
        [ValidateSet('analyze', 'trends', 'anomalies', 'formula-help', 'pivot-suggest')]
        [string]$Focus = 'analyze',

        [Parameter()]
        [int]$MaxRows = 100,

        [Parameter()]
        [string]$Model = 'sonar-pro',

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    $dataLines = [System.Collections.ArrayList]::new()

    if ($FromExcel) {
        try {
            $excel = [System.Runtime.InteropServices.Marshal]::GetActiveObject('Excel.Application')
        }
        catch {
            Write-Error 'Microsoft Excel does not appear to be running. Open a workbook first, or use the -Path parameter.'
            return
        }

        try {
            $wb = $excel.ActiveWorkbook
            if ($null -eq $wb) {
                Write-Error 'No active Excel workbook found. Open a workbook and try again.'
                return
            }

            $ws = $wb.ActiveSheet

            if ($Range) {
                $rangeObj = $ws.Range($Range)
            }
            else {
                $rangeObj = $ws.UsedRange
            }

            $rowCount = $rangeObj.Rows.Count
            $colCount = $rangeObj.Columns.Count
            $startRow = $rangeObj.Row
            $startCol = $rangeObj.Column
            $limit = [Math]::Min($rowCount, $MaxRows + 1)

            for ($r = 1; $r -le $limit; $r++) {
                $cells = [System.Collections.ArrayList]::new()
                for ($c = 1; $c -le $colCount; $c++) {
                    $cellVal = $ws.Cells($startRow + $r - 1, $startCol + $c - 1).Text
                    $null = $cells.Add($cellVal)
                }
                $null = $dataLines.Add([string]::Join("`t", $cells.ToArray()))
            }
        }
        catch {
            Write-Error ('Failed to read from Excel: {0}' -f $_.Exception.Message)
            return
        }
    }
    elseif ($Path) {
        if (-not (Test-Path -LiteralPath $Path)) {
            Write-Error ('File not found: {0}' -f $Path)
            return
        }

        $ext = [System.IO.Path]::GetExtension($Path).ToLower()

        if ($ext -eq '.csv') {
            $rows = Import-Csv -LiteralPath $Path
            if ($rows.Count -eq 0) {
                Write-Warning 'The CSV file appears to be empty.'
                return
            }

            $headers = $rows[0].PSObject.Properties.Name
            $null = $dataLines.Add([string]::Join("`t", $headers))

            $limit = [Math]::Min($rows.Count, $MaxRows)
            for ($i = 0; $i -lt $limit; $i++) {
                $vals = [System.Collections.ArrayList]::new()
                foreach ($h in $headers) {
                    $null = $vals.Add($rows[$i].$h)
                }
                $null = $dataLines.Add([string]::Join("`t", $vals.ToArray()))
            }
        }
        elseif ($ext -eq '.xlsx') {
            try {
                $excel = New-Object -ComObject Excel.Application
                $excel.Visible = $false
                $excel.DisplayAlerts = $false
                $wb = $excel.Workbooks.Open($Path)

                if ($SheetName) {
                    $ws = $wb.Sheets.Item($SheetName)
                }
                else {
                    $ws = $wb.ActiveSheet
                }

                $usedRange = $ws.UsedRange
                $rowCount = $usedRange.Rows.Count
                $colCount = $usedRange.Columns.Count
                $limit = [Math]::Min($rowCount, $MaxRows + 1)

                for ($r = 1; $r -le $limit; $r++) {
                    $cells = [System.Collections.ArrayList]::new()
                    for ($c = 1; $c -le $colCount; $c++) {
                        $null = $cells.Add($usedRange.Cells($r, $c).Text)
                    }
                    $null = $dataLines.Add([string]::Join("`t", $cells.ToArray()))
                }

                $wb.Close($false)
                [System.Runtime.InteropServices.Marshal]::ReleaseComObject($wb) | Out-Null
                $excel.Quit()
                [System.Runtime.InteropServices.Marshal]::ReleaseComObject($excel) | Out-Null
            }
            catch {
                Write-Error ('Failed to open .xlsx via Excel COM: {0}. Ensure Microsoft Excel is installed.' -f $_.Exception.Message)
                return
            }
        }
        else {
            Write-Error ('Unsupported file type: {0}. Supported types: .xlsx, .csv' -f $ext)
            return
        }
    }
    else {
        Write-Error 'Specify either -FromExcel to read from the active workbook, or -Path to provide a file.'
        return
    }

    if ($dataLines.Count -eq 0) {
        Write-Warning 'No data was found to analyze.'
        return
    }

    $dataText = [string]::Join([System.Environment]::NewLine, $dataLines.ToArray())

    switch ($Focus) {
        'analyze'       { $systemPrompt = 'Analyze this spreadsheet data. Identify patterns, key metrics, and notable findings. Suggest visualizations.' }
        'trends'        { $systemPrompt = 'Identify trends, growth patterns, and projections in this data. Provide statistical observations.' }
        'anomalies'     { $systemPrompt = 'Identify outliers, anomalies, and suspicious values in this data. Explain why each is notable.' }
        'formula-help'  { $systemPrompt = 'Based on this data structure, suggest useful Excel formulas (SUM, VLOOKUP, INDEX/MATCH, COUNTIFS, etc.) that would be valuable.' }
        'pivot-suggest' { $systemPrompt = 'Suggest pivot table configurations for this data. Include recommended rows, columns, values, and filters.' }
    }

    Invoke-Perplexity -Query $dataText -Model $Model -SystemPrompt $systemPrompt -Port $Port
}

function Invoke-PerplexityVBAGenerator {
    <#
    .SYNOPSIS
        Generates an Excel VBA macro based on a plain-language description.
    .DESCRIPTION
        Sends a description of the desired macro behavior to the Perplexity Sonar API
        via the PerplexityXPC broker, which returns a complete, commented VBA Sub
        procedure with error handling. Optionally attempts to insert the generated
        code into the active Excel workbook's VBA editor via COM automation.
        Note: VBA editor insertion requires Trust Center settings to allow programmatic
        access to the VBA project. Gracefully handles the case where Excel is not
        running or access is denied.
    .PARAMETER Description
        A plain-language description of what the VBA macro should do. This is mandatory.
    .PARAMETER Context
        Additional context about the workbook or data structure to help the model
        generate more accurate code.
    .PARAMETER InsertInExcel
        When specified, attempts to insert the generated VBA code into the active
        Excel workbook's VBAProject via COM automation. Requires Excel to be running
        and the Trust Center option "Trust access to the VBA project object model"
        to be enabled.
    .PARAMETER Model
        The Perplexity model to use. Default: sonar-reasoning-pro.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Invoke-PerplexityVBAGenerator -Description "Format all cells with values over 1000 in red bold"

        Generates a VBA macro that applies red bold formatting to high-value cells.
    .EXAMPLE
        Invoke-PerplexityVBAGenerator -Description "Create a summary sheet that pulls totals from all other sheets" -InsertInExcel

        Generates a summary macro and attempts to insert it directly into Excel's VBA editor.
    .EXAMPLE
        Invoke-PerplexityVBAGenerator -Description "Send email via Outlook for each row where column D is Past Due" -Context "Sheet1 has columns: Name, Email, Amount, Status"

        Generates a macro using workbook-specific context about the data structure.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Description,

        [Parameter()]
        [string]$Context,

        [Parameter()]
        [switch]$InsertInExcel,

        [Parameter()]
        [string]$Model = 'sonar-reasoning-pro',

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    $systemPrompt = 'You are an Excel VBA expert. Generate a well-commented VBA macro based on the user''s description. Include error handling (On Error GoTo). Output only the VBA code in a Sub procedure, ready to paste into the VBA editor.'

    if ($Context) {
        $query = ('Description: {0}{1}{1}Context: {2}' -f $Description, [System.Environment]::NewLine, $Context)
    }
    else {
        $query = ('Description: {0}' -f $Description)
    }

    $vbaCode = Invoke-Perplexity -Query $query -Model $Model -SystemPrompt $systemPrompt -Port $Port

    if (-not $vbaCode) {
        return
    }

    Write-Output $vbaCode

    if ($InsertInExcel) {
        try {
            $excel = [System.Runtime.InteropServices.Marshal]::GetActiveObject('Excel.Application')
        }
        catch {
            Write-Warning 'Microsoft Excel does not appear to be running. The VBA code was not inserted. Open a workbook and try again with -InsertInExcel.'
            return $vbaCode
        }

        try {
            $wb = $excel.ActiveWorkbook
            if ($null -eq $wb) {
                Write-Warning 'No active Excel workbook found. The VBA code was not inserted.'
                return $vbaCode
            }

            $vbaProject = $wb.VBProject
            $vbaModule = $vbaProject.VBComponents.Add(1)
            $vbaModule.CodeModule.AddFromString($vbaCode)
            Write-Verbose 'VBA code inserted into a new module in the active workbook.'
            Write-Output ''
            Write-Output 'VBA code inserted into Excel VBA editor. Open the VBA editor (Alt+F11) to review.'
        }
        catch {
            Write-Warning ('Failed to insert VBA code into Excel: {0}. Ensure "Trust access to the VBA project object model" is enabled in the Excel Trust Center (File > Options > Trust Center > Trust Center Settings > Macro Settings).' -f $_.Exception.Message)
        }
    }

    return $vbaCode
}

function Invoke-PerplexityTeamsAnalysis {
    <#
    .SYNOPSIS
        Analyzes Microsoft Teams chat or channel conversation text using Perplexity.
    .DESCRIPTION
        Accepts Teams conversation text either as a direct string parameter, from a
        file path (.txt or .html export), or piped from Get-Clipboard. Sends the
        content to the Perplexity Sonar API via the PerplexityXPC broker for analysis
        based on the chosen focus mode. Text is truncated to 15000 characters to stay
        within API limits.
    .PARAMETER ChatText
        The Teams conversation text to analyze. Can be pasted chat history or piped
        from Get-Clipboard.
    .PARAMETER Path
        Path to an exported Teams chat file (.txt or .html).
    .PARAMETER Focus
        The analysis mode to apply. Valid values:
          summarize    - Main topics, key participants, and outcomes (default).
          action-items - Action items, assignments, and deadlines as a checklist.
          decisions    - All decisions made, with context and conditions.
          sentiment    - Team dynamics, tensions, agreements, and morale indicators.
          meeting-prep - Briefing for an upcoming meeting with open items and agenda.
        Default: summarize
    .PARAMETER Model
        The Perplexity model to use. Default: sonar-pro.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Get-Clipboard | Invoke-PerplexityTeamsAnalysis -Focus action-items

        Analyzes copied Teams chat text for action items.
    .EXAMPLE
        Invoke-PerplexityTeamsAnalysis -Path "C:\Exports\project-chat.txt" -Focus meeting-prep

        Reads an exported chat file and prepares a meeting briefing.
    .EXAMPLE
        Invoke-PerplexityTeamsAnalysis -ChatText $chatLog -Focus decisions

        Analyzes a Teams conversation stored in a variable for decisions made.
    #>
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true)]
        [string]$ChatText,

        [Parameter()]
        [string]$Path,

        [Parameter()]
        [ValidateSet('summarize', 'action-items', 'decisions', 'sentiment', 'meeting-prep')]
        [string]$Focus = 'summarize',

        [Parameter()]
        [string]$Model = 'sonar-pro',

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    process {
        $content = $null

        if ($Path) {
            if (-not (Test-Path -LiteralPath $Path)) {
                Write-Error ('File not found: {0}' -f $Path)
                return
            }
            $content = Get-Content -LiteralPath $Path -Raw
        }
        elseif ($ChatText) {
            $content = $ChatText
        }
        else {
            Write-Error 'Provide chat text via -ChatText, pipe from Get-Clipboard, or use -Path to specify an export file.'
            return
        }

        if ([string]::IsNullOrWhiteSpace($content)) {
            Write-Warning 'The chat content appears to be empty.'
            return
        }

        $maxLen = 15000
        if ($content.Length -gt $maxLen) {
            Write-Verbose ('Chat content truncated from {0} to {1} characters.' -f $content.Length, $maxLen)
            $content = $content.Substring(0, $maxLen)
        }

        switch ($Focus) {
            'summarize'    { $systemPrompt = 'Summarize this Teams conversation. Identify the main topics, key participants, and outcomes.' }
            'action-items' { $systemPrompt = 'Extract all action items, assignments, and deadlines from this Teams conversation. Format as a checklist with assignees.' }
            'decisions'    { $systemPrompt = 'Identify all decisions made in this conversation. Include who made the decision, what was decided, and any conditions.' }
            'sentiment'    { $systemPrompt = 'Analyze the team dynamics in this conversation. Identify any tensions, agreements, blockers, or morale indicators.' }
            'meeting-prep' { $systemPrompt = 'Based on this conversation history, prepare a briefing for an upcoming meeting. Include open items, unresolved questions, and suggested agenda items.' }
        }

        Invoke-Perplexity -Query $content -Model $Model -SystemPrompt $systemPrompt -Port $Port
    }
}

#endregion

#region Windows Native Integration Functions

function Register-XPCScheduledTask {
    <#
    .SYNOPSIS
        Creates a Windows Scheduled Task that runs a PerplexityXPC command on a schedule.
    .DESCRIPTION
        Registers a Windows Scheduled Task in the PerplexityXPC task folder. The task
        imports the PerplexityXPC module, runs the specified PowerShell command, optionally
        saves output to a file, and optionally sends a toast notification on completion.
        Supports daily, weekly, hourly, logon, and startup triggers.
    .PARAMETER TaskName
        Name for the scheduled task. This will be created under \PerplexityXPC\ in
        Task Scheduler.
    .PARAMETER Command
        The PowerShell command to execute. For example:
        "Invoke-PerplexityEventAnalysis -GroupBySource | Out-File C:\Reports\events.md"
    .PARAMETER Trigger
        Schedule trigger type. Valid values: daily, weekly, hourly, logon, startup.
        Default: daily.
    .PARAMETER Time
        Time of day to run the task (e.g., "08:00"). Used for daily and weekly triggers.
        Default: "07:00".
    .PARAMETER DayOfWeek
        Day of week for the weekly trigger (e.g., "Monday"). Required when -Trigger is weekly.
    .PARAMETER OutputPath
        If specified, the command output is saved to this file path.
    .PARAMETER Notify
        If specified, a toast notification is sent via Send-XPCNotification after the
        command completes.
    .PARAMETER Description
        Human-readable description for the scheduled task.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .PARAMETER Model
        Perplexity model to use in the scheduled command context. Default: sonar-pro.
    .EXAMPLE
        Register-XPCScheduledTask -TaskName "Daily Security Briefing" -Command "Invoke-PerplexityEventAnalysis -LogName Security -GroupBySource" -Trigger daily -Time "07:00" -OutputPath "C:\Reports\security-daily.md" -Notify

        Creates a daily task at 07:00 that analyzes Security event log entries, saves the
        output to C:\Reports\security-daily.md, and sends a toast notification.
    .EXAMPLE
        Register-XPCScheduledTask -TaskName "Weekly Compliance Check" -Command "Invoke-Perplexity 'List current Windows 10 CIS benchmark critical findings for domain-joined enterprise endpoints' -Model sonar-pro" -Trigger weekly -DayOfWeek Monday -Time "08:00"

        Creates a weekly task every Monday at 08:00 to run a compliance query.
    .EXAMPLE
        Register-XPCScheduledTask -TaskName "Hourly Log Monitor" -Command "Invoke-PerplexityEventAnalysis -EntryType Critical -After (Get-Date).AddHours(-1)" -Trigger hourly -Notify

        Creates an hourly task that checks for critical log entries and notifies on completion.
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory = $true)]
        [string]$TaskName,

        [Parameter(Mandatory = $true)]
        [string]$Command,

        [Parameter()]
        [ValidateSet('daily', 'weekly', 'hourly', 'logon', 'startup')]
        [string]$Trigger = 'daily',

        [Parameter()]
        [string]$Time = '07:00',

        [Parameter()]
        [string]$DayOfWeek,

        [Parameter()]
        [string]$OutputPath,

        [Parameter()]
        [switch]$Notify,

        [Parameter()]
        [string]$Description = '',

        [Parameter()]
        [int]$Port = $script:DefaultPort,

        [Parameter()]
        [ValidateSet('sonar', 'sonar-pro', 'sonar-reasoning-pro', 'sonar-deep-research')]
        [string]$Model = 'sonar-pro'
    )

    # Build the inner script lines
    $scriptLines = [System.Collections.ArrayList]@()
    $null = $scriptLines.Add('Import-Module PerplexityXPC -ErrorAction Stop')
    $null = $scriptLines.Add('')

    if ($OutputPath) {
        $null = $scriptLines.Add(('$result = {0}' -f $Command))
        $null = $scriptLines.Add(('$result | Out-File -FilePath "{0}" -Encoding UTF8' -f $OutputPath))
    }
    else {
        $null = $scriptLines.Add($Command)
    }

    if ($Notify) {
        if ($OutputPath) {
            $null = $scriptLines.Add(('Send-XPCNotification -Title "PerplexityXPC: {0}" -Message ("Task completed. Output saved to {1}") -Port {2}' -f $TaskName, $OutputPath, $Port))
        }
        else {
            $null = $scriptLines.Add(('Send-XPCNotification -Title "PerplexityXPC: {0}" -Message "Scheduled task completed." -Port {1}' -f $TaskName, $Port))
        }
    }

    $innerScript = $scriptLines -join [System.Environment]::NewLine
    $encodedCommand = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($innerScript))
    $actionArg = ('-NoProfile -WindowStyle Hidden -EncodedCommand {0}' -f $encodedCommand)

    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $actionArg

    # Build trigger
    switch ($Trigger) {
        'daily' {
            $timeParts = $Time -split ':'
            $hour = [int]$timeParts[0]
            $minute = if ($timeParts.Length -gt 1) { [int]$timeParts[1] } else { 0 }
            $atTime = (Get-Date -Hour $hour -Minute $minute -Second 0)
            $taskTrigger = New-ScheduledTaskTrigger -Daily -At $atTime
        }
        'weekly' {
            $timeParts = $Time -split ':'
            $hour = [int]$timeParts[0]
            $minute = if ($timeParts.Length -gt 1) { [int]$timeParts[1] } else { 0 }
            $atTime = (Get-Date -Hour $hour -Minute $minute -Second 0)
            if ($DayOfWeek) {
                $taskTrigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek $DayOfWeek -At $atTime
            }
            else {
                $taskTrigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek Monday -At $atTime
                Write-Warning 'No -DayOfWeek specified for weekly trigger; defaulting to Monday.'
            }
        }
        'hourly' {
            $taskTrigger = New-ScheduledTaskTrigger -RepetitionInterval (New-TimeSpan -Hours 1) -Once -At (Get-Date)
        }
        'logon' {
            $taskTrigger = New-ScheduledTaskTrigger -AtLogOn
        }
        'startup' {
            $taskTrigger = New-ScheduledTaskTrigger -AtStartup
        }
        default {
            $taskTrigger = New-ScheduledTaskTrigger -Daily -At (Get-Date -Hour 7 -Minute 0 -Second 0)
        }
    }

    $principal = New-ScheduledTaskPrincipal -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) -RunLevel Highest

    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Hours 2) -MultipleInstances IgnoreNew

    $taskDescription = if ($Description) { $Description } else { ('PerplexityXPC scheduled task: {0}' -f $TaskName) }

    $taskParams = @{
        TaskName    = $TaskName
        TaskPath    = '\PerplexityXPC\'
        Action      = $action
        Trigger     = $taskTrigger
        Principal   = $principal
        Settings    = $settings
        Description = $taskDescription
        Force       = $true
    }

    if ($PSCmdlet.ShouldProcess($TaskName, 'Register Scheduled Task')) {
        try {
            $registeredTask = Register-ScheduledTask @taskParams -ErrorAction Stop
            Write-Verbose ('Registered scheduled task: \PerplexityXPC\{0}' -f $TaskName)
            [PSCustomObject]@{
                TaskName    = $registeredTask.TaskName
                TaskPath    = $registeredTask.TaskPath
                State       = $registeredTask.State
                Description = $taskDescription
                Trigger     = $Trigger
            }
        }
        catch {
            Write-Error ('Failed to register scheduled task "{0}": {1}' -f $TaskName, $_.Exception.Message)
        }
    }
}

function Get-XPCScheduledTask {
    <#
    .SYNOPSIS
        Lists all PerplexityXPC scheduled tasks registered on this machine.
    .DESCRIPTION
        Retrieves scheduled tasks from the \PerplexityXPC\ task folder and displays
        their name, state, next run time, and last run result. Optionally filters by
        task name.
    .PARAMETER TaskName
        Optional filter string. Only tasks whose names contain this value are returned.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .PARAMETER Model
        Perplexity model context (reserved for future use). Default: sonar-pro.
    .EXAMPLE
        Get-XPCScheduledTask

        Lists all scheduled tasks under \PerplexityXPC\ in Task Scheduler.
    .EXAMPLE
        Get-XPCScheduledTask -TaskName "Security"

        Lists all PerplexityXPC tasks whose names contain "Security".
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [string]$TaskName,

        [Parameter()]
        [int]$Port = $script:DefaultPort,

        [Parameter()]
        [ValidateSet('sonar', 'sonar-pro', 'sonar-reasoning-pro', 'sonar-deep-research')]
        [string]$Model = 'sonar-pro'
    )

    try {
        $tasks = Get-ScheduledTask -TaskPath '\PerplexityXPC\' -ErrorAction SilentlyContinue
    }
    catch {
        $tasks = $null
    }

    if (-not $tasks) {
        Write-Verbose 'No PerplexityXPC scheduled tasks found.'
        return
    }

    if ($TaskName) {
        $tasks = $tasks | Where-Object { $_.TaskName -like ('*{0}*' -f $TaskName) }
    }

    foreach ($task in $tasks) {
        $taskInfo = Get-ScheduledTaskInfo -TaskName $task.TaskName -TaskPath $task.TaskPath -ErrorAction SilentlyContinue
        [PSCustomObject]@{
            TaskName       = $task.TaskName
            State          = $task.State
            NextRunTime    = if ($taskInfo) { $taskInfo.NextRunTime } else { $null }
            LastRunTime    = if ($taskInfo) { $taskInfo.LastRunTime } else { $null }
            LastRunResult  = if ($taskInfo) { $taskInfo.LastTaskResult } else { $null }
            Description    = $task.Description
        }
    }
}

function Remove-XPCScheduledTask {
    <#
    .SYNOPSIS
        Removes a PerplexityXPC scheduled task from Task Scheduler.
    .DESCRIPTION
        Unregisters the specified scheduled task from the \PerplexityXPC\ task folder.
        Supports -WhatIf and -Confirm via SupportsShouldProcess.
    .PARAMETER TaskName
        Name of the scheduled task to remove. Must exist in the \PerplexityXPC\ path.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .PARAMETER Model
        Perplexity model context (reserved for future use). Default: sonar-pro.
    .EXAMPLE
        Remove-XPCScheduledTask -TaskName "Daily Security Briefing"

        Removes the "Daily Security Briefing" task from the PerplexityXPC task folder.
    .EXAMPLE
        Remove-XPCScheduledTask -TaskName "Hourly Log Monitor" -WhatIf

        Shows what would happen if the "Hourly Log Monitor" task were removed, without
        actually removing it.
    #>
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory = $true)]
        [string]$TaskName,

        [Parameter()]
        [int]$Port = $script:DefaultPort,

        [Parameter()]
        [ValidateSet('sonar', 'sonar-pro', 'sonar-reasoning-pro', 'sonar-deep-research')]
        [string]$Model = 'sonar-pro'
    )

    if ($PSCmdlet.ShouldProcess(('\PerplexityXPC\{0}' -f $TaskName), 'Unregister Scheduled Task')) {
        try {
            Unregister-ScheduledTask -TaskName $TaskName -TaskPath '\PerplexityXPC\' -Confirm:$false -ErrorAction Stop
            Write-Verbose ('Removed scheduled task: \PerplexityXPC\{0}' -f $TaskName)
        }
        catch {
            Write-Error ('Failed to remove scheduled task "{0}": {1}' -f $TaskName, $_.Exception.Message)
        }
    }
}

function Register-XPCSearchProvider {
    <#
    .SYNOPSIS
        Registers or unregisters the PerplexityXPC URI protocol handler and Run alias.
    .DESCRIPTION
        Creates a perplexity:// URI protocol handler in the Windows registry so that
        any application or the Win+R Run dialog can launch a Perplexity query by opening
        a perplexity:// URI. Also creates a Run command alias "perplexity" so users can
        type "perplexity <query>" in Win+R. The helper script is saved to
        C:\ProgramData\PerplexityXPC\Search-PerplexityXPC.ps1.

        Use -Unregister to remove all registry entries and the helper script.
    .PARAMETER Unregister
        If specified, removes the perplexity:// protocol handler and Run alias from
        the registry, and deletes the helper script.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .PARAMETER Model
        Perplexity model context (reserved for future use). Default: sonar-pro.
    .EXAMPLE
        Register-XPCSearchProvider

        Registers the perplexity:// URI protocol handler and creates the Win+R alias.
    .EXAMPLE
        Register-XPCSearchProvider -Unregister

        Removes the perplexity:// URI protocol handler and Win+R alias from the registry.
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter()]
        [switch]$Unregister,

        [Parameter()]
        [int]$Port = $script:DefaultPort,

        [Parameter()]
        [ValidateSet('sonar', 'sonar-pro', 'sonar-reasoning-pro', 'sonar-deep-research')]
        [string]$Model = 'sonar-pro'
    )

    $helperDir  = 'C:\ProgramData\PerplexityXPC'
    $helperPath = ('{0}\Search-PerplexityXPC.ps1' -f $helperDir)
    $protocolRoot = 'HKCU:\Software\Classes\perplexity'
    $runAliasRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths\perplexity.exe'

    if ($Unregister) {
        if ($PSCmdlet.ShouldProcess('perplexity:// protocol handler', 'Unregister')) {
            try {
                if (Test-Path $protocolRoot) {
                    Remove-Item -Path $protocolRoot -Recurse -Force -ErrorAction Stop
                    Write-Verbose 'Removed perplexity:// protocol handler registry entries.'
                }
                if (Test-Path $runAliasRoot) {
                    Remove-Item -Path $runAliasRoot -Recurse -Force -ErrorAction Stop
                    Write-Verbose 'Removed perplexity Run alias registry entries.'
                }
                if (Test-Path $helperPath) {
                    Remove-Item -Path $helperPath -Force -ErrorAction Stop
                    Write-Verbose ('Removed helper script: {0}' -f $helperPath)
                }
                Write-Host 'PerplexityXPC search provider unregistered successfully.'
            }
            catch {
                Write-Error ('Failed to unregister search provider: {0}' -f $_.Exception.Message)
            }
        }
        return
    }

    if ($PSCmdlet.ShouldProcess('perplexity:// protocol handler', 'Register')) {
        try {
            # Create helper script directory
            if (-not (Test-Path $helperDir)) {
                $null = New-Item -ItemType Directory -Path $helperDir -Force
            }

            # Write the helper script
            $helperScript = @'
param([string]$Uri)
# Strip the protocol prefix: perplexity:// or perplexity:<query>
$query = $Uri -replace '^perplexity://', '' -replace '^perplexity:', ''
$query = [System.Uri]::UnescapeDataString($query).Trim('/')
if ([string]::IsNullOrWhiteSpace($query)) {
    [System.Windows.Forms.MessageBox]::Show('No query provided. Usage: perplexity://<your question>', 'PerplexityXPC', 'OK', 'Information')
    return
}
Import-Module PerplexityXPC -ErrorAction Stop
$result = Invoke-PerplexitySearch -Query $query -Popup
'@
            $helperScript | Out-File -FilePath $helperPath -Encoding UTF8 -Force

            # Register perplexity:// URI protocol handler under HKCU\Software\Classes
            $null = New-Item -Path $protocolRoot -Force
            $null = New-ItemProperty -Path $protocolRoot -Name '(Default)' -Value 'URL:Perplexity Protocol' -PropertyType String -Force
            $null = New-ItemProperty -Path $protocolRoot -Name 'URL Protocol' -Value '' -PropertyType String -Force

            $shellPath  = ('{0}\shell' -f $protocolRoot)
            $openPath   = ('{0}\open' -f $shellPath)
            $cmdPath    = ('{0}\command' -f $openPath)

            $null = New-Item -Path $shellPath -Force
            $null = New-Item -Path $openPath  -Force
            $null = New-Item -Path $cmdPath   -Force

            $cmdValue = ('powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "{0}" "%1"' -f $helperPath)
            $null = New-ItemProperty -Path $cmdPath -Name '(Default)' -Value $cmdValue -PropertyType String -Force

            # Register Run alias so "perplexity" works in Win+R
            $null = New-Item -Path $runAliasRoot -Force
            $null = New-ItemProperty -Path $runAliasRoot -Name '(Default)' -Value ('powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "{0}"' -f $helperPath) -PropertyType String -Force

            Write-Host 'PerplexityXPC search provider registered successfully.'
            Write-Host ('Helper script: {0}' -f $helperPath)
            Write-Host 'You can now open perplexity://<query> in any browser or Win+R dialog.'
        }
        catch {
            Write-Error ('Failed to register search provider: {0}' -f $_.Exception.Message)
        }
    }
}

function Invoke-PerplexitySearch {
    <#
    .SYNOPSIS
        Performs a quick Perplexity query, designed for use from Run dialog or shortcuts.
    .DESCRIPTION
        Sends a query to the PerplexityXPC broker and displays the result either in the
        console or in a Windows Forms message box (when -Popup is specified). This function
        is lightweight and intended for quick interactive lookups from Win+R, desktop
        shortcuts, or other launchers.
    .PARAMETER Query
        The search query or question to send to Perplexity. This is a positional parameter.
    .PARAMETER Model
        Perplexity model to use. Default: sonar.
    .PARAMETER Popup
        If specified, shows the result in a Windows Forms MessageBox instead of writing
        to the console. Useful when invoked from non-interactive contexts like Win+R.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Invoke-PerplexitySearch -Query "What is the current time in Tokyo?"

        Sends the query to Perplexity and prints the result to the console.
    .EXAMPLE
        Invoke-PerplexitySearch "PowerShell fastest way to list open TCP connections" -Popup

        Sends the query and displays the answer in a pop-up message box.
    .EXAMPLE
        Invoke-PerplexitySearch -Query "Convert 500 USD to EUR" -Model sonar-pro -Popup

        Uses the sonar-pro model and shows the result in a message box.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string]$Query,

        [Parameter()]
        [ValidateSet('sonar', 'sonar-pro', 'sonar-reasoning-pro', 'sonar-deep-research')]
        [string]$Model = 'sonar',

        [Parameter()]
        [switch]$Popup,

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    $result = Invoke-Perplexity -Query $Query -Model $Model -Port $Port -Raw

    if ($Popup) {
        try {
            Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        }
        catch {
            Write-Warning ('System.Windows.Forms assembly failed to load: {0}' -f $_.Exception.Message)
            Write-Output $result
            return
        }
        $null = [System.Windows.Forms.MessageBox]::Show(
            $result,
            ('Perplexity: {0}' -f ($Query.Substring(0, [Math]::Min($Query.Length, 60)))),
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Information
        )
    }
    else {
        Write-Output $result
    }
}

function Invoke-PerplexityRDPAnalysis {
    <#
    .SYNOPSIS
        Analyzes RDP session information and diagnoses Remote Desktop connection issues.
    .DESCRIPTION
        Collects RDP session data and/or connection diagnostics and sends them to the
        PerplexityXPC broker for AI-powered analysis. When -AnalyzeActive is specified,
        active RDP sessions on the local machine are analyzed. When -DiagnoseConnection
        is specified along with -Target, connectivity and configuration to the target
        machine is diagnosed.
    .PARAMETER Target
        Remote computer name or IP address to analyze or diagnose.
    .PARAMETER AnalyzeActive
        Analyzes currently active RDP sessions on this machine using quser.exe,
        qwinsta.exe, and TerminalServices event log data.
    .PARAMETER DiagnoseConnection
        Diagnoses RDP connection issues to -Target by checking network connectivity,
        firewall rules, NLA configuration, and certificate status.
    .PARAMETER Model
        Perplexity model to use. Default: sonar-pro.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Invoke-PerplexityRDPAnalysis -AnalyzeActive

        Analyzes all currently active RDP sessions on the local machine.
    .EXAMPLE
        Invoke-PerplexityRDPAnalysis -Target "server01.domain.local" -DiagnoseConnection

        Diagnoses RDP connection issues to server01.domain.local, including port
        connectivity, firewall rules, and NLA settings.
    .EXAMPLE
        Invoke-PerplexityRDPAnalysis -Target "10.0.1.50" -DiagnoseConnection

        Diagnoses RDP connection issues to the host at 10.0.1.50.
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [string]$Target,

        [Parameter()]
        [switch]$AnalyzeActive,

        [Parameter()]
        [switch]$DiagnoseConnection,

        [Parameter()]
        [ValidateSet('sonar', 'sonar-pro', 'sonar-reasoning-pro', 'sonar-deep-research')]
        [string]$Model = 'sonar-pro',

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    $systemPrompt = 'You are a Windows Server and Remote Desktop expert. Analyze the provided session data or connection diagnostics and provide: 1) Current status assessment 2) Security concerns 3) Recommended actions'

    $dataLines = [System.Collections.ArrayList]@()

    if ($AnalyzeActive) {
        Write-Verbose 'Collecting active RDP session data...'
        $null = $dataLines.Add('=== Active RDP Sessions (quser) ===')
        try {
            $quserOutput = & quser.exe 2>&1
            $null = $dataLines.Add(($quserOutput -join [System.Environment]::NewLine))
        }
        catch {
            $null = $dataLines.Add(('quser.exe failed: {0}' -f $_.Exception.Message))
        }

        $null = $dataLines.Add('')
        $null = $dataLines.Add('=== Session List (qwinsta) ===')
        try {
            $qwinstaOutput = & qwinsta.exe 2>&1
            $null = $dataLines.Add(($qwinstaOutput -join [System.Environment]::NewLine))
        }
        catch {
            $null = $dataLines.Add(('qwinsta.exe failed: {0}' -f $_.Exception.Message))
        }

        $null = $dataLines.Add('')
        $null = $dataLines.Add('=== TerminalServices Session Manager Events (last 50) ===')
        try {
            $rdpEvents = Get-WinEvent -LogName 'Microsoft-Windows-TerminalServices-LocalSessionManager/Operational' -MaxEvents 50 -ErrorAction SilentlyContinue
            if ($rdpEvents) {
                foreach ($evt in $rdpEvents) {
                    $null = $dataLines.Add(('{0} [{1}] ID={2}: {3}' -f $evt.TimeCreated, $evt.LevelDisplayName, $evt.Id, $evt.Message))
                }
            }
            else {
                $null = $dataLines.Add('No TerminalServices-LocalSessionManager events found.')
            }
        }
        catch {
            $null = $dataLines.Add(('RDP event collection failed: {0}' -f $_.Exception.Message))
        }
    }

    if ($DiagnoseConnection -and $Target) {
        Write-Verbose ('Diagnosing RDP connection to {0}...' -f $Target)
        $null = $dataLines.Add(('=== Network Connectivity Test to {0} on port 3389 ===' -f $Target))
        try {
            $connTest = Test-NetConnection -ComputerName $Target -Port 3389 -WarningAction SilentlyContinue
            $null = $dataLines.Add(('TcpTestSucceeded: {0}' -f $connTest.TcpTestSucceeded))
            $null = $dataLines.Add(('PingSucceeded: {0}' -f $connTest.PingSucceeded))
            $null = $dataLines.Add(('RemoteAddress: {0}' -f $connTest.RemoteAddress))
            $null = $dataLines.Add(('RemotePort: {0}' -f $connTest.RemotePort))
        }
        catch {
            $null = $dataLines.Add(('Test-NetConnection failed: {0}' -f $_.Exception.Message))
        }

        $null = $dataLines.Add('')
        $null = $dataLines.Add('=== Local Firewall Rules for RDP ===')
        try {
            $fwRules = Get-NetFirewallRule -DisplayGroup 'Remote Desktop' -ErrorAction SilentlyContinue
            if ($fwRules) {
                foreach ($rule in $fwRules) {
                    $null = $dataLines.Add(('{0} | Enabled={1} | Direction={2} | Action={3}' -f $rule.DisplayName, $rule.Enabled, $rule.Direction, $rule.Action))
                }
            }
            else {
                $null = $dataLines.Add('No Remote Desktop firewall rules found.')
            }
        }
        catch {
            $null = $dataLines.Add(('Firewall rule query failed: {0}' -f $_.Exception.Message))
        }

        $null = $dataLines.Add('')
        $null = $dataLines.Add('=== Local NLA (Network Level Authentication) Settings ===')
        try {
            $nlaKey = 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp'
            if (Test-Path $nlaKey) {
                $nlaVal = (Get-ItemProperty -Path $nlaKey -Name 'UserAuthentication' -ErrorAction SilentlyContinue).UserAuthentication
                $null = $dataLines.Add(('UserAuthentication (NLA): {0} ({1})' -f $nlaVal, (if ($nlaVal -eq 1) { 'NLA Enabled' } else { 'NLA Disabled' })))
                $rdpEnabled = (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server' -Name 'fDenyTSConnections' -ErrorAction SilentlyContinue).fDenyTSConnections
                $null = $dataLines.Add(('RDP Enabled (fDenyTSConnections=0 means enabled): {0}' -f $rdpEnabled))
            }
            else {
                $null = $dataLines.Add('RDP-Tcp registry key not found.')
            }
        }
        catch {
            $null = $dataLines.Add(('NLA check failed: {0}' -f $_.Exception.Message))
        }

        $null = $dataLines.Add('')
        $null = $dataLines.Add('=== RDP TLS Certificate (LocalMachine\Remote Desktop) ===')
        try {
            $rdpCerts = Get-ChildItem -Path 'Cert:\LocalMachine\Remote Desktop' -ErrorAction SilentlyContinue
            if ($rdpCerts) {
                foreach ($cert in $rdpCerts) {
                    $null = $dataLines.Add(('Subject={0} | Thumbprint={1} | NotAfter={2}' -f $cert.Subject, $cert.Thumbprint, $cert.NotAfter))
                }
            }
            else {
                $null = $dataLines.Add('No certificates found in LocalMachine\Remote Desktop store.')
            }
        }
        catch {
            $null = $dataLines.Add(('Certificate check failed: {0}' -f $_.Exception.Message))
        }
    }

    if ($dataLines.Count -eq 0) {
        Write-Warning 'No analysis mode selected. Use -AnalyzeActive or -DiagnoseConnection -Target <host>.'
        return
    }

    $queryPrefix = if ($DiagnoseConnection) {
        'Diagnose this RDP connection issue and provide remediation steps:'
    }
    else {
        'Analyze these RDP sessions for security concerns, stale sessions, and resource usage:'
    }

    $fullQuery = ('{0}{1}{1}{2}' -f $queryPrefix, [System.Environment]::NewLine, ($dataLines -join [System.Environment]::NewLine))

    Invoke-Perplexity -Query $fullQuery -Model $Model -Port $Port -SystemPrompt $systemPrompt
}

function Invoke-PerplexityServerAnalysis {
    <#
    .SYNOPSIS
        Analyzes Windows Server health, AD replication, DNS, DHCP, GPO, services,
        certificates, or all components via Perplexity AI.
    .DESCRIPTION
        Collects diagnostic data for the specified Windows Server component(s) and
        sends it to the PerplexityXPC broker for AI-powered analysis and remediation
        advice. Supports local and remote servers via -ComputerName. When -Component
        is "all", all components are collected sequentially with Write-Progress updates.
    .PARAMETER Component
        The server component to analyze. Valid values:
          health         - OS uptime, memory, CPU, disk, and top processes
          ad-replication - AD replication summary and partner metadata
          dns            - DNS zones, diagnostics, and forwarders
          dhcp           - DHCP scopes and scope statistics
          gpo            - Group Policy Objects and gpresult
          services       - Stopped auto-start services
          certificates   - Certificates expiring within 30 days
          all            - All of the above
        Default: health
    .PARAMETER ComputerName
        Remote server to analyze. Default: localhost.
    .PARAMETER Model
        Perplexity model to use. Default: sonar-pro.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Invoke-PerplexityServerAnalysis -Component health

        Analyzes the health of the local server (uptime, memory, CPU, disk, processes).
    .EXAMPLE
        Invoke-PerplexityServerAnalysis -Component ad-replication -ComputerName dc01

        Collects and analyzes AD replication status from dc01.
    .EXAMPLE
        Invoke-PerplexityServerAnalysis -Component all

        Runs all component checks on the local server and provides a comprehensive analysis.
    .EXAMPLE
        Invoke-PerplexityServerAnalysis -Component certificates

        Checks for certificates expiring within 30 days in the local machine store.
    .EXAMPLE
        Invoke-PerplexityServerAnalysis -Component services -ComputerName fileserver01

        Checks for stopped auto-start services on fileserver01.
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [ValidateSet('health', 'ad-replication', 'dns', 'dhcp', 'gpo', 'services', 'certificates', 'all')]
        [string]$Component = 'health',

        [Parameter()]
        [string]$ComputerName = 'localhost',

        [Parameter()]
        [ValidateSet('sonar', 'sonar-pro', 'sonar-reasoning-pro', 'sonar-deep-research')]
        [string]$Model = 'sonar-pro',

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    $systemPrompt = 'You are a Windows Server administrator expert. Analyze this server diagnostic data and provide: 1) Health assessment 2) Issues found 3) Recommended remediation steps with PowerShell commands'

    $dataLines = [System.Collections.ArrayList]@()

    $isRemote = ($ComputerName -ne 'localhost' -and $ComputerName -ne '.' -and $ComputerName -ne $env:COMPUTERNAME)

    $components = if ($Component -eq 'all') {
        @('health', 'services', 'certificates', 'ad-replication', 'dns', 'dhcp', 'gpo')
    }
    else {
        @($Component)
    }

    $totalComponents = $components.Count
    $currentIndex = 0

    foreach ($comp in $components) {
        $currentIndex++
        if ($totalComponents -gt 1) {
            Write-Progress -Activity 'PerplexityXPC Server Analysis' -Status ('Collecting {0} data...' -f $comp) -PercentComplete (($currentIndex / $totalComponents) * 100)
        }

        switch ($comp) {
            'health' {
                $null = $dataLines.Add(('=== Server Health: {0} ===' -f $ComputerName))
                try {
                    $cimParams = @{ ClassName = 'Win32_OperatingSystem'; ErrorAction = 'SilentlyContinue' }
                    if ($isRemote) { $cimParams['ComputerName'] = $ComputerName }
                    $os = Get-CimInstance @cimParams
                    if ($os) {
                        $uptime = (Get-Date) - $os.LastBootUpTime
                        $null = $dataLines.Add(('OS: {0}' -f $os.Caption))
                        $null = $dataLines.Add(('Uptime: {0} days {1} hours' -f [int]$uptime.TotalDays, $uptime.Hours))
                        $null = $dataLines.Add(('Total RAM: {0} GB' -f [Math]::Round($os.TotalVisibleMemorySize / 1MB, 2)))
                        $null = $dataLines.Add(('Free RAM: {0} GB ({1}%)' -f [Math]::Round($os.FreePhysicalMemory / 1MB, 2), [Math]::Round(($os.FreePhysicalMemory / $os.TotalVisibleMemorySize) * 100, 1)))
                    }
                }
                catch {
                    $null = $dataLines.Add(('OS info failed: {0}' -f $_.Exception.Message))
                }

                try {
                    $cpuParams = @{ ClassName = 'Win32_Processor'; ErrorAction = 'SilentlyContinue' }
                    if ($isRemote) { $cpuParams['ComputerName'] = $ComputerName }
                    $cpus = Get-CimInstance @cpuParams
                    $avgLoad = ($cpus | Measure-Object -Property LoadPercentage -Average).Average
                    $null = $dataLines.Add(('CPU Load Average: {0}%' -f $avgLoad))
                }
                catch {
                    $null = $dataLines.Add(('CPU info failed: {0}' -f $_.Exception.Message))
                }

                try {
                    $diskParams = @{ ClassName = 'Win32_LogicalDisk'; Filter = "DriveType=3"; ErrorAction = 'SilentlyContinue' }
                    if ($isRemote) { $diskParams['ComputerName'] = $ComputerName }
                    $disks = Get-CimInstance @diskParams
                    $null = $dataLines.Add('--- Disk Space ---')
                    foreach ($disk in $disks) {
                        $pctFree = if ($disk.Size -gt 0) { [Math]::Round(($disk.FreeSpace / $disk.Size) * 100, 1) } else { 0 }
                        $null = $dataLines.Add(('{0} Size={1}GB Free={2}GB ({3}%)' -f $disk.DeviceID, [Math]::Round($disk.Size / 1GB, 1), [Math]::Round($disk.FreeSpace / 1GB, 1), $pctFree))
                    }
                }
                catch {
                    $null = $dataLines.Add(('Disk info failed: {0}' -f $_.Exception.Message))
                }

                try {
                    $procParams = @{ ErrorAction = 'SilentlyContinue' }
                    if ($isRemote) { $procParams['ComputerName'] = $ComputerName }
                    $topProcs = Get-Process @procParams | Sort-Object -Property CPU -Descending | Select-Object -First 10
                    $null = $dataLines.Add('--- Top 10 Processes by CPU ---')
                    foreach ($proc in $topProcs) {
                        $null = $dataLines.Add(('{0} (PID={1}) CPU={2}s WS={3}MB' -f $proc.Name, $proc.Id, [Math]::Round($proc.CPU, 1), [Math]::Round($proc.WorkingSet64 / 1MB, 1)))
                    }
                }
                catch {
                    $null = $dataLines.Add(('Process info failed: {0}' -f $_.Exception.Message))
                }
            }

            'services' {
                $null = $dataLines.Add('=== Stopped Auto-Start Services ===')
                try {
                    $svcParams = @{ ErrorAction = 'SilentlyContinue' }
                    if ($isRemote) { $svcParams['ComputerName'] = $ComputerName }
                    $stoppedSvcs = Get-Service @svcParams | Where-Object { $_.Status -eq 'Stopped' -and $_.StartType -eq 'Automatic' }
                    if ($stoppedSvcs) {
                        foreach ($svc in $stoppedSvcs) {
                            $null = $dataLines.Add(('{0} ({1})' -f $svc.DisplayName, $svc.Name))
                        }
                    }
                    else {
                        $null = $dataLines.Add('All automatic-start services are running.')
                    }
                }
                catch {
                    $null = $dataLines.Add(('Service query failed: {0}' -f $_.Exception.Message))
                }
            }

            'certificates' {
                $null = $dataLines.Add('=== Certificates Expiring Within 30 Days ===')
                try {
                    $threshold = (Get-Date).AddDays(30)
                    $certs = Get-ChildItem -Path 'Cert:\LocalMachine\My' -ErrorAction SilentlyContinue | Where-Object { $_.NotAfter -lt $threshold }
                    if ($certs) {
                        foreach ($cert in $certs) {
                            $daysLeft = [int]($cert.NotAfter - (Get-Date)).TotalDays
                            $null = $dataLines.Add(('Subject={0} | Thumbprint={1} | Expires={2} ({3} days)' -f $cert.Subject, $cert.Thumbprint, $cert.NotAfter.ToString('yyyy-MM-dd'), $daysLeft))
                        }
                    }
                    else {
                        $null = $dataLines.Add('No certificates expiring within 30 days.')
                    }
                }
                catch {
                    $null = $dataLines.Add(('Certificate query failed: {0}' -f $_.Exception.Message))
                }
            }

            'ad-replication' {
                $null = $dataLines.Add('=== AD Replication Summary ===')
                try {
                    $replSummary = & repadmin.exe /replsummary 2>&1
                    $null = $dataLines.Add(($replSummary -join [System.Environment]::NewLine))
                }
                catch {
                    $null = $dataLines.Add(('repadmin failed: {0}' -f $_.Exception.Message))
                }

                try {
                    if (Get-Command -Name 'Get-ADReplicationPartnerMetadata' -ErrorAction SilentlyContinue) {
                        $replMeta = Get-ADReplicationPartnerMetadata -Target $ComputerName -ErrorAction SilentlyContinue
                        if ($replMeta) {
                            $null = $dataLines.Add('--- Replication Partner Metadata ---')
                            foreach ($partner in $replMeta) {
                                $null = $dataLines.Add(('Partner={0} LastReplication={1} ConsecutiveFailures={2}' -f $partner.Partner, $partner.LastReplicationSuccess, $partner.ConsecutiveReplicationFailures))
                            }
                        }
                    }
                }
                catch {
                    $null = $dataLines.Add(('Get-ADReplicationPartnerMetadata failed: {0}' -f $_.Exception.Message))
                }
            }

            'dns' {
                $null = $dataLines.Add('=== DNS Server Configuration ===')
                try {
                    if (Get-Command -Name 'Get-DnsServerZone' -ErrorAction SilentlyContinue) {
                        $dnsZones = Get-DnsServerZone -ComputerName $ComputerName -ErrorAction SilentlyContinue
                        if ($dnsZones) {
                            $null = $dataLines.Add('--- DNS Zones ---')
                            foreach ($zone in $dnsZones) {
                                $null = $dataLines.Add(('{0} Type={1} Dynamic={2}' -f $zone.ZoneName, $zone.ZoneType, $zone.DynamicUpdate))
                            }
                        }
                        $forwarders = Get-DnsServerForwarder -ComputerName $ComputerName -ErrorAction SilentlyContinue
                        if ($forwarders) {
                            $null = $dataLines.Add(('Forwarders: {0}' -f ($forwarders.IPAddress -join ', ')))
                        }
                    }
                    else {
                        $null = $dataLines.Add('DnsServer module not available.')
                    }
                }
                catch {
                    $null = $dataLines.Add(('DNS query failed: {0}' -f $_.Exception.Message))
                }
            }

            'dhcp' {
                $null = $dataLines.Add('=== DHCP Server Status ===')
                try {
                    if (Get-Command -Name 'Get-DhcpServerv4Scope' -ErrorAction SilentlyContinue) {
                        $scopes = Get-DhcpServerv4Scope -ComputerName $ComputerName -ErrorAction SilentlyContinue
                        if ($scopes) {
                            foreach ($scope in $scopes) {
                                $stats = Get-DhcpServerv4ScopeStatistics -ScopeId $scope.ScopeId -ComputerName $ComputerName -ErrorAction SilentlyContinue
                                $null = $dataLines.Add(('Scope={0} Name={1} State={2} InUse={3} Free={4}' -f $scope.ScopeId, $scope.Name, $scope.State, ($stats.InUse), ($stats.Free)))
                            }
                        }
                        else {
                            $null = $dataLines.Add('No DHCP scopes found.')
                        }
                    }
                    else {
                        $null = $dataLines.Add('DhcpServer module not available.')
                    }
                }
                catch {
                    $null = $dataLines.Add(('DHCP query failed: {0}' -f $_.Exception.Message))
                }
            }

            'gpo' {
                $null = $dataLines.Add('=== Group Policy Objects ===')
                try {
                    if (Get-Command -Name 'Get-GPO' -ErrorAction SilentlyContinue) {
                        $gpos = Get-GPO -All -ErrorAction SilentlyContinue
                        if ($gpos) {
                            foreach ($gpo in $gpos) {
                                $null = $dataLines.Add(('{0} | ID={1} | Modified={2}' -f $gpo.DisplayName, $gpo.Id, $gpo.ModificationTime))
                            }
                        }
                        else {
                            $null = $dataLines.Add('No GPOs found.')
                        }
                    }
                    else {
                        $null = $dataLines.Add('GroupPolicy module not available.')
                    }
                }
                catch {
                    $null = $dataLines.Add(('GPO query failed: {0}' -f $_.Exception.Message))
                }

                $null = $dataLines.Add('--- gpresult /R ---')
                try {
                    $gpresult = & gpresult.exe /R 2>&1
                    $null = $dataLines.Add(($gpresult -join [System.Environment]::NewLine))
                }
                catch {
                    $null = $dataLines.Add(('gpresult failed: {0}' -f $_.Exception.Message))
                }
            }
        }

        $null = $dataLines.Add('')
    }

    if ($totalComponents -gt 1) {
        Write-Progress -Activity 'PerplexityXPC Server Analysis' -Completed
    }

    $query = ('Analyze the following Windows Server diagnostic data for {0} and provide assessment, issues, and remediation:{1}{1}{2}' -f $ComputerName, [System.Environment]::NewLine, ($dataLines -join [System.Environment]::NewLine))

    Invoke-Perplexity -Query $query -Model $Model -Port $Port -SystemPrompt $systemPrompt
}

function Invoke-PerplexityADAnalysis {
    <#
    .SYNOPSIS
        Performs deep AI-powered analysis of an Active Directory environment.
    .DESCRIPTION
        Collects Active Directory data based on the specified focus area and sends it to
        the PerplexityXPC broker for security assessment, compliance review, and hardening
        recommendations. Requires the ActiveDirectory PowerShell module (RSAT).
    .PARAMETER Focus
        The AD area to analyze. Valid values:
          users         - User counts, disabled accounts, inactive accounts, password settings
          groups        - Group membership, nested groups, empty groups
          computers     - Computer counts, OS breakdown, stale computers (90+ days)
          stale-objects - Users and computers inactive for 90+ days
          security      - Admin group members, service accounts, Kerberos delegation, password policies
          lockouts      - Recent account lockout events from the Security log
          replication   - AD replication status via repadmin
        Default: security
    .PARAMETER Model
        Perplexity model to use. Default: sonar-reasoning-pro.
    .PARAMETER Port
        Port the broker is listening on. Default: 47777.
    .EXAMPLE
        Invoke-PerplexityADAnalysis -Focus security

        Analyzes admin group members, service accounts, Kerberos delegation, and password
        policies for security concerns.
    .EXAMPLE
        Invoke-PerplexityADAnalysis -Focus stale-objects

        Identifies users and computers inactive for more than 90 days.
    .EXAMPLE
        Invoke-PerplexityADAnalysis -Focus lockouts

        Retrieves recent account lockout events and analyzes them for patterns and remediation.
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [ValidateSet('users', 'groups', 'computers', 'stale-objects', 'security', 'lockouts', 'replication')]
        [string]$Focus = 'security',

        [Parameter()]
        [ValidateSet('sonar', 'sonar-pro', 'sonar-reasoning-pro', 'sonar-deep-research')]
        [string]$Model = 'sonar-reasoning-pro',

        [Parameter()]
        [int]$Port = $script:DefaultPort
    )

    $systemPrompt = 'You are an Active Directory security and administration expert. Analyze this AD data and provide: 1) Current state assessment 2) Security concerns 3) Compliance issues 4) Recommended hardening steps'

    # Ensure AD module is available
    try {
        Import-Module ActiveDirectory -ErrorAction Stop
    }
    catch {
        Write-Error ('ActiveDirectory module not available. Install RSAT: {0}' -f $_.Exception.Message)
        return
    }

    $dataLines = [System.Collections.ArrayList]@()
    $staleDate = (Get-Date).AddDays(-90)

    switch ($Focus) {
        'users' {
            $null = $dataLines.Add('=== Active Directory Users ===')
            try {
                $allUsers  = Get-ADUser -Filter * -Properties LastLogonDate, PasswordNeverExpires, Enabled, PasswordLastSet -ErrorAction Stop
                $totalCount    = $allUsers.Count
                $disabledCount = ($allUsers | Where-Object { -not $_.Enabled }).Count
                $neverLoggedIn = ($allUsers | Where-Object { $null -eq $_.LastLogonDate }).Count
                $pwNeverExpires = ($allUsers | Where-Object { $_.PasswordNeverExpires }).Count
                $null = $dataLines.Add(('Total users: {0}' -f $totalCount))
                $null = $dataLines.Add(('Disabled accounts: {0}' -f $disabledCount))
                $null = $dataLines.Add(('Never logged in: {0}' -f $neverLoggedIn))
                $null = $dataLines.Add(('Password never expires: {0}' -f $pwNeverExpires))
                $recentlyActive = ($allUsers | Where-Object { $_.Enabled -and $_.LastLogonDate -gt $staleDate }).Count
                $null = $dataLines.Add(('Active in last 90 days: {0}' -f $recentlyActive))
            }
            catch {
                $null = $dataLines.Add(('User query failed: {0}' -f $_.Exception.Message))
            }
        }

        'groups' {
            $null = $dataLines.Add('=== Active Directory Groups ===')
            try {
                $allGroups = Get-ADGroup -Filter * -Properties Members, Description -ErrorAction Stop
                $null = $dataLines.Add(('Total groups: {0}' -f $allGroups.Count))
                $emptyGroups = $allGroups | Where-Object { $_.Members.Count -eq 0 }
                $null = $dataLines.Add(('Empty groups: {0}' -f $emptyGroups.Count))
                if ($emptyGroups) {
                    $null = $dataLines.Add(('Empty group names: {0}' -f (($emptyGroups | Select-Object -First 20 -ExpandProperty Name) -join ', ')))
                }

                $null = $dataLines.Add('--- Nested Group Analysis (large groups) ---')
                $largeGroups = $allGroups | Where-Object { $_.Members.Count -gt 50 } | Select-Object -First 10
                foreach ($grp in $largeGroups) {
                    $null = $dataLines.Add(('{0}: {1} members' -f $grp.Name, $grp.Members.Count))
                }
            }
            catch {
                $null = $dataLines.Add(('Group query failed: {0}' -f $_.Exception.Message))
            }
        }

        'computers' {
            $null = $dataLines.Add('=== Active Directory Computers ===')
            try {
                $allComputers = Get-ADComputer -Filter * -Properties OperatingSystem, LastLogonDate, Enabled -ErrorAction Stop
                $null = $dataLines.Add(('Total computers: {0}' -f $allComputers.Count))
                $staleComputers = ($allComputers | Where-Object { $_.Enabled -and ($null -eq $_.LastLogonDate -or $_.LastLogonDate -lt $staleDate) }).Count
                $null = $dataLines.Add(('Stale computers (90+ days): {0}' -f $staleComputers))
                $disabled = ($allComputers | Where-Object { -not $_.Enabled }).Count
                $null = $dataLines.Add(('Disabled computers: {0}' -f $disabled))

                $null = $dataLines.Add('--- OS Breakdown ---')
                $osGroups = $allComputers | Where-Object { $_.OperatingSystem } | Group-Object -Property OperatingSystem | Sort-Object -Property Count -Descending
                foreach ($osGroup in $osGroups) {
                    $null = $dataLines.Add(('{0}: {1}' -f $osGroup.Name, $osGroup.Count))
                }
            }
            catch {
                $null = $dataLines.Add(('Computer query failed: {0}' -f $_.Exception.Message))
            }
        }

        'stale-objects' {
            $null = $dataLines.Add('=== Stale AD Objects (inactive 90+ days) ===')
            try {
                $staleUsers = Get-ADUser -Filter { Enabled -eq $true } -Properties LastLogonDate -ErrorAction Stop |
                    Where-Object { $null -eq $_.LastLogonDate -or $_.LastLogonDate -lt $staleDate }
                $null = $dataLines.Add(('Stale enabled users: {0}' -f $staleUsers.Count))
                if ($staleUsers) {
                    $null = $dataLines.Add('Sample stale users (first 20):')
                    foreach ($u in ($staleUsers | Select-Object -First 20)) {
                        $null = $dataLines.Add(('{0} | LastLogon={1}' -f $u.SamAccountName, $u.LastLogonDate))
                    }
                }
            }
            catch {
                $null = $dataLines.Add(('Stale user query failed: {0}' -f $_.Exception.Message))
            }

            try {
                $staleComputers = Get-ADComputer -Filter { Enabled -eq $true } -Properties LastLogonDate -ErrorAction Stop |
                    Where-Object { $null -eq $_.LastLogonDate -or $_.LastLogonDate -lt $staleDate }
                $null = $dataLines.Add(('Stale enabled computers: {0}' -f $staleComputers.Count))
                if ($staleComputers) {
                    $null = $dataLines.Add('Sample stale computers (first 20):')
                    foreach ($c in ($staleComputers | Select-Object -First 20)) {
                        $null = $dataLines.Add(('{0} | LastLogon={1}' -f $c.Name, $c.LastLogonDate))
                    }
                }
            }
            catch {
                $null = $dataLines.Add(('Stale computer query failed: {0}' -f $_.Exception.Message))
            }
        }

        'security' {
            $null = $dataLines.Add('=== Active Directory Security Analysis ===')
            try {
                $null = $dataLines.Add('--- Domain Admins ---')
                $domainAdmins = Get-ADGroupMember -Identity 'Domain Admins' -Recursive -ErrorAction Stop
                $null = $dataLines.Add(('Domain Admin count: {0}' -f $domainAdmins.Count))
                foreach ($member in $domainAdmins) {
                    $null = $dataLines.Add(('{0} ({1})' -f $member.SamAccountName, $member.objectClass))
                }
            }
            catch {
                $null = $dataLines.Add(('Domain Admins query failed: {0}' -f $_.Exception.Message))
            }

            try {
                $null = $dataLines.Add('--- Enterprise Admins ---')
                $entAdmins = Get-ADGroupMember -Identity 'Enterprise Admins' -Recursive -ErrorAction SilentlyContinue
                if ($entAdmins) {
                    $null = $dataLines.Add(('Enterprise Admin count: {0}' -f $entAdmins.Count))
                    foreach ($member in $entAdmins) {
                        $null = $dataLines.Add(('{0} ({1})' -f $member.SamAccountName, $member.objectClass))
                    }
                }
            }
            catch {
                $null = $dataLines.Add(('Enterprise Admins query failed (normal if not forest root): {0}' -f $_.Exception.Message))
            }

            try {
                $null = $dataLines.Add('--- Service Accounts (accounts with "svc" or "service" in name) ---')
                $svcAccounts = Get-ADUser -Filter { SamAccountName -like '*svc*' -or SamAccountName -like '*service*' } -Properties PasswordNeverExpires, LastLogonDate -ErrorAction SilentlyContinue
                if ($svcAccounts) {
                    $null = $dataLines.Add(('Service account count: {0}' -f $svcAccounts.Count))
                    foreach ($svc in $svcAccounts) {
                        $null = $dataLines.Add(('{0} | PwNeverExpires={1} | LastLogon={2}' -f $svc.SamAccountName, $svc.PasswordNeverExpires, $svc.LastLogonDate))
                    }
                }
                else {
                    $null = $dataLines.Add('No service accounts found matching naming convention.')
                }
            }
            catch {
                $null = $dataLines.Add(('Service account query failed: {0}' -f $_.Exception.Message))
            }

            try {
                $null = $dataLines.Add('--- Unconstrained Kerberos Delegation ---')
                $unconstrainedDelegation = Get-ADComputer -Filter { TrustedForDelegation -eq $true } -Properties TrustedForDelegation -ErrorAction SilentlyContinue
                if ($unconstrainedDelegation) {
                    foreach ($comp in $unconstrainedDelegation) {
                        $null = $dataLines.Add(('WARNING: {0} has unconstrained delegation enabled' -f $comp.Name))
                    }
                }
                else {
                    $null = $dataLines.Add('No computers with unconstrained delegation found.')
                }
            }
            catch {
                $null = $dataLines.Add(('Kerberos delegation query failed: {0}' -f $_.Exception.Message))
            }

            try {
                $null = $dataLines.Add('--- Default Domain Password Policy ---')
                $pwPolicy = Get-ADDefaultDomainPasswordPolicy -ErrorAction SilentlyContinue
                if ($pwPolicy) {
                    $null = $dataLines.Add(('MinLength={0} | Complexity={1} | MaxAge={2} | LockoutThreshold={3}' -f $pwPolicy.MinPasswordLength, $pwPolicy.ComplexityEnabled, $pwPolicy.MaxPasswordAge, $pwPolicy.LockoutThreshold))
                }
            }
            catch {
                $null = $dataLines.Add(('Password policy query failed: {0}' -f $_.Exception.Message))
            }
        }

        'lockouts' {
            $null = $dataLines.Add('=== Recent Account Lockout Events ===')
            try {
                $lockoutEvents = Get-WinEvent -FilterHashtable @{
                    LogName  = 'Security'
                    Id       = 4740
                    StartTime = (Get-Date).AddDays(-7)
                } -ErrorAction SilentlyContinue
                if ($lockoutEvents) {
                    $null = $dataLines.Add(('Total lockout events in last 7 days: {0}' -f $lockoutEvents.Count))
                    foreach ($evt in ($lockoutEvents | Select-Object -First 50)) {
                        $null = $dataLines.Add(('{0} | {1}' -f $evt.TimeCreated, $evt.Message -replace '\s+', ' '))
                    }
                }
                else {
                    $null = $dataLines.Add('No account lockout events found in the last 7 days.')
                }
            }
            catch {
                $null = $dataLines.Add(('Lockout event query failed (may require elevated privileges): {0}' -f $_.Exception.Message))
            }
        }

        'replication' {
            $null = $dataLines.Add('=== AD Replication Status ===')
            try {
                $replSummary = & repadmin.exe /replsummary 2>&1
                $null = $dataLines.Add(($replSummary -join [System.Environment]::NewLine))
            }
            catch {
                $null = $dataLines.Add(('repadmin /replsummary failed: {0}' -f $_.Exception.Message))
            }

            try {
                $replErrors = & repadmin.exe /showrepl 2>&1
                $null = $dataLines.Add('--- Replication Details ---')
                $null = $dataLines.Add(($replErrors -join [System.Environment]::NewLine))
            }
            catch {
                $null = $dataLines.Add(('repadmin /showrepl failed: {0}' -f $_.Exception.Message))
            }

            try {
                if (Get-Command -Name 'Get-ADReplicationPartnerMetadata' -ErrorAction SilentlyContinue) {
                    $replMeta = Get-ADReplicationPartnerMetadata -Target * -ErrorAction SilentlyContinue
                    if ($replMeta) {
                        $null = $dataLines.Add('--- Partner Metadata ---')
                        foreach ($partner in $replMeta) {
                            $null = $dataLines.Add(('Partner={0} LastSuccess={1} Failures={2}' -f $partner.Partner, $partner.LastReplicationSuccess, $partner.ConsecutiveReplicationFailures))
                        }
                    }
                }
            }
            catch {
                $null = $dataLines.Add(('Replication partner metadata failed: {0}' -f $_.Exception.Message))
            }
        }
    }

    $query = ('Analyze the following Active Directory {0} data and provide assessment, security concerns, compliance issues, and hardening recommendations:{1}{1}{2}' -f $Focus, [System.Environment]::NewLine, ($dataLines -join [System.Environment]::NewLine))

    Invoke-Perplexity -Query $query -Model $Model -Port $Port -SystemPrompt $systemPrompt
}

#endregion
