# PerplexityXPC PowerShell Module Reference

The `PerplexityXPC` PowerShell module provides 14 functions for querying Perplexity AI, managing MCP servers, analyzing files and folders, running batch research, and integrating with IT management tools.

---

## Table of Contents

- [Installation](#installation)
- [Module Overview](#module-overview)
- [Category 1 - Core Functions](#category-1---core-functions)
  - [Invoke-Perplexity](#invoke-perplexity)
  - [Get-XPCStatus](#get-xpcstatus)
  - [Get-XPCConfig](#get-xpcconfig)
  - [Set-XPCConfig](#set-xpcconfig)
- [Category 2 - MCP Management](#category-2---mcp-management)
  - [Get-McpServer](#get-mcpserver)
  - [Restart-McpServer](#restart-mcpserver)
  - [Invoke-McpRequest](#invoke-mcprequest)
- [Category 3 - File Analysis](#category-3---file-analysis)
  - [Invoke-PerplexityFileAnalysis](#invoke-perplexityfileanalysis)
  - [Invoke-PerplexityFolderAnalysis](#invoke-perplexityfolderanalysis)
- [Category 4 - Batch Research](#category-4---batch-research)
  - [Invoke-PerplexityBatch](#invoke-perplexitybatch)
  - [Invoke-PerplexityReport](#invoke-perplexityreport)
- [Category 5 - IT Integration](#category-5---it-integration)
  - [Invoke-PerplexityTicketAnalysis](#invoke-perplexityticketanalysis)
  - [Invoke-PerplexityDeviceAnalysis](#invoke-perplexitydeviceanalysis)
  - [Invoke-PerplexitySecurityAnalysis](#invoke-perplexitysecurityanalysis)
- [Pipeline Examples](#pipeline-examples)
- [Tips and Best Practices](#tips-and-best-practices)

---

## Installation

### Option A - Install with the PerplexityXPC Installer

The main installer (`Install-PerplexityXPC.ps1`) installs the module automatically. After installation, import it:

```powershell
Import-Module PerplexityXPC
```

### Option B - Manual Install

```powershell
# Create the module directory
$modulePath = "$env:USERPROFILE\Documents\PowerShell\Modules\PerplexityXPC"
New-Item -ItemType Directory -Path $modulePath -Force

# Copy module files
Copy-Item -Path ".\module\PerplexityXPC\*" -Destination $modulePath -Recurse

# Import the module
Import-Module PerplexityXPC

# Verify
Get-Command -Module PerplexityXPC
```

### For PowerShell 5.1 (Windows PowerShell)

```powershell
# Use the Documents\WindowsPowerShell path
$modulePath = "$env:USERPROFILE\Documents\WindowsPowerShell\Modules\PerplexityXPC"
New-Item -ItemType Directory -Path $modulePath -Force
Copy-Item -Path ".\module\PerplexityXPC\*" -Destination $modulePath -Recurse

# Import
Import-Module PerplexityXPC
```

### Auto-import on Session Start

Add to your PowerShell profile:

```powershell
# Add this line to $PROFILE
Import-Module PerplexityXPC -ErrorAction SilentlyContinue
```

---

## Module Overview

All module functions communicate with the PerplexityXPC Windows Service via HTTP on `http://127.0.0.1:47777`. The service must be running before calling any function.

**Check service status before using the module:**

```powershell
Get-Service PerplexityXPC  # Status should be Running
Get-XPCStatus              # More detailed health check
```

**Available Sonar models:**

| Model | Description |
|-------|-------------|
| `sonar` | Fast, cost-effective. Good for simple queries. |
| `sonar-pro` | Higher accuracy, more context. Default for analysis functions. |
| `sonar-reasoning-pro` | Step-by-step reasoning. Best for complex analysis. |
| `sonar-deep-research` | Multi-step research. Best for comprehensive reports. |

---

## Category 1 - Core Functions

### Invoke-Perplexity

Sends a query to the Perplexity Sonar API via the PerplexityXPC broker.

#### Synopsis

```powershell
Invoke-Perplexity [-Query] <string> [-Model <string>] [-SystemPrompt <string>]
                  [-Temperature <double>] [-MaxTokens <int>] [-SearchMode <string>]
                  [-DomainFilter <string[]>] [-RecencyFilter <string>]
                  [-Raw] [-Port <int>]
```

#### Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `Query` | string | Yes | - | The question or prompt to send (positional, position 0) |
| `Model` | string | No | `sonar` | Model to use: `sonar`, `sonar-pro`, `sonar-reasoning-pro`, `sonar-deep-research` |
| `SystemPrompt` | string | No | - | System-level instruction to shape the model's behavior |
| `Temperature` | double | No | (model default) | Sampling temperature 0-2. Higher = more random |
| `MaxTokens` | int | No | (model default) | Maximum tokens in the response |
| `SearchMode` | string | No | `web` | Search scope: `web`, `academic`, `sec` |
| `DomainFilter` | string[] | No | - | Restrict search to specific domains |
| `RecencyFilter` | string | No | - | Time window: `hour`, `day`, `week`, `month`, `year` |
| `Raw` | switch | No | false | Return the full JSON response object instead of just the answer text |
| `Port` | int | No | `47777` | Port the broker is listening on |

#### Examples

**Example 1 - Simple query:**

```powershell
Invoke-Perplexity 'What is the latest version of PowerShell?'
```

**Example 2 - Specify model and recency filter:**

```powershell
Invoke-Perplexity 'What CVEs were disclosed this week?' -Model sonar-pro -RecencyFilter week
```

**Example 3 - Get raw response object for custom processing:**

```powershell
$result = Invoke-Perplexity 'List S&P 500 top movers today' -Raw
$result.choices[0].message.content
$result.citations
$result.usage.total_tokens
```

**Example 4 - Restrict search to specific domains:**

```powershell
Invoke-Perplexity 'CVE-2024-1234 details and patch' -DomainFilter 'nvd.nist.gov','cve.mitre.org'
```

#### Notes

- Without `-Raw`, the function prints the answer text and lists citations below it.
- Domain filter values should not include `https://` - use the bare domain name only.
- Temperature is not set by default (the model uses its own default). Setting it to 0 produces the most deterministic results.

---

### Get-XPCStatus

Retrieves and displays the status of the PerplexityXPC broker.

#### Synopsis

```powershell
Get-XPCStatus [-Port <int>]
```

#### Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `Port` | int | No | `47777` | Port the broker is listening on |

#### Examples

**Example 1 - Default status check:**

```powershell
Get-XPCStatus
```

Output:

```
=== PerplexityXPC Broker Status ===
Version : 1.0.0
Status  : running
Uptime  : 02:14:37

--- MCP Servers ---
Name         Status  PID   Uptime
----         ------  ---   ------
filesystem   running 12345 02:14:30
github       stopped 0     -
```

**Example 2 - Custom port:**

```powershell
Get-XPCStatus -Port 48000
```

**Example 3 - Use in a conditional check:**

```powershell
$status = Get-XPCStatus
if ($status.status -ne 'running') {
    Write-Warning 'Broker is not running - starting service...'
    Start-Service PerplexityXPC
}
```

#### Notes

- Returns the status object so it can be captured for programmatic use.
- MCP server table is shown only if at least one server is registered.

---

### Get-XPCConfig

Retrieves the current non-sensitive configuration of the PerplexityXPC broker.

#### Synopsis

```powershell
Get-XPCConfig [-Port <int>]
```

#### Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `Port` | int | No | `47777` | Port the broker is listening on |

#### Examples

**Example 1 - Read current config:**

```powershell
Get-XPCConfig
```

**Example 2 - Inspect a specific value:**

```powershell
$cfg = Get-XPCConfig
$cfg.default_model
$cfg.http_port
$cfg.log_level
```

**Example 3 - Check before making changes:**

```powershell
$current = Get-XPCConfig
Write-Host "Current model: $($current.default_model)"
Write-Host "Current timeout: $($current.api_timeout_sec)s"
```

#### Notes

- The API key is never returned by this endpoint. Sensitive fields are stripped server-side.

---

### Set-XPCConfig

Updates the PerplexityXPC broker configuration at runtime.

#### Synopsis

```powershell
Set-XPCConfig -Settings <hashtable> [-Port <int>]
```

#### Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `Settings` | hashtable | Yes | - | Key-value pairs to update in the broker configuration |
| `Port` | int | No | `47777` | Port the broker is listening on |

#### Examples

**Example 1 - Change the default model:**

```powershell
Set-XPCConfig -Settings @{ default_model = 'sonar-pro' }
```

**Example 2 - Update multiple settings at once:**

```powershell
Set-XPCConfig -Settings @{
    default_model   = 'sonar-reasoning-pro'
    api_timeout_sec = 90
    log_level       = 'Debug'
}
```

**Example 3 - Reset log level after debugging:**

```powershell
Set-XPCConfig -Settings @{ log_level = 'Information' }
```

#### Notes

- Changes take effect immediately without restarting the service.
- Not all settings can be updated at runtime. Port changes require a service restart.

---

## Category 2 - MCP Management

### Get-McpServer

Lists all MCP servers managed by the PerplexityXPC broker.

#### Synopsis

```powershell
Get-McpServer [-Port <int>]
```

#### Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `Port` | int | No | `47777` | Port the broker is listening on |

#### Examples

**Example 1 - List all servers:**

```powershell
Get-McpServer
```

Output:

```
Name         Status  PID   Uptime
----         ------  ---   ------
filesystem   running 12345 01:23:45
github       running 12346 01:23:44
brave-search stopped 0     -
```

**Example 2 - Filter to running servers only:**

```powershell
Get-McpServer | Where-Object { $_.status -eq 'running' }
```

**Example 3 - Count running servers:**

```powershell
(Get-McpServer | Where-Object { $_.status -eq 'running' }).Count
```

#### Notes

- Returns the array of server objects so it can be used in pipelines.

---

### Restart-McpServer

Restarts a named MCP server managed by the PerplexityXPC broker.

#### Synopsis

```powershell
Restart-McpServer -Name <string> [-Port <int>] [-WhatIf] [-Confirm]
```

#### Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `Name` | string | Yes | - | The name of the MCP server to restart |
| `Port` | int | No | `47777` | Port the broker is listening on |
| `WhatIf` | switch | No | - | Show what would happen without actually restarting |
| `Confirm` | switch | No | - | Prompt for confirmation before restarting |

#### Examples

**Example 1 - Restart a server:**

```powershell
Restart-McpServer -Name 'filesystem'
```

**Example 2 - Preview without executing:**

```powershell
Restart-McpServer -Name 'github' -WhatIf
```

**Example 3 - Restart all stopped servers:**

```powershell
Get-McpServer | Where-Object { $_.status -eq 'stopped' } | ForEach-Object {
    Restart-McpServer -Name $_.name
}
```

#### Notes

- Supports `-WhatIf` and `-Confirm` via `SupportsShouldProcess`.
- If the server is not registered, the broker returns a 404 error.

---

### Invoke-McpRequest

Sends a JSON-RPC 2.0 request to a specific MCP server via the broker.

#### Synopsis

```powershell
Invoke-McpRequest -Server <string> -Method <string> [-Params <hashtable>] [-Port <int>]
```

#### Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `Server` | string | Yes | - | The name of the target MCP server |
| `Method` | string | Yes | - | The MCP method to call (e.g., `tools/list`, `tools/call`) |
| `Params` | hashtable | No | - | Parameters to pass to the MCP method |
| `Port` | int | No | `47777` | Port the broker is listening on |

#### Examples

**Example 1 - List available tools on a server:**

```powershell
Invoke-McpRequest -Server 'filesystem' -Method 'tools/list'
```

**Example 2 - Call a specific tool:**

```powershell
Invoke-McpRequest -Server 'filesystem' -Method 'tools/call' -Params @{
    name      = 'read_file'
    arguments = @{ path = 'C:\Users\YourUsername\Documents\notes.txt' }
}
```

**Example 3 - List GitHub repositories:**

```powershell
Invoke-McpRequest -Server 'github' -Method 'tools/call' -Params @{
    name      = 'list_repos'
    arguments = @{ owner = 'your-org'; type = 'private' }
}
```

#### Notes

- The `Method` value follows MCP spec naming: category/action (e.g., `tools/list`, `resources/read`).
- Refer to the documentation for each MCP server package to find available methods and parameters.

---

## Category 3 - File Analysis

### Invoke-PerplexityFileAnalysis

Analyzes a file using the Perplexity Sonar API.

#### Synopsis

```powershell
Invoke-PerplexityFileAnalysis -Path <string> [-Prompt <string>] [-Model <string>] [-Port <int>]
```

#### Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `Path` | string | Yes | - | Path to the file. Accepts pipeline input from `Get-ChildItem`. |
| `Prompt` | string | No | (auto) | Custom analysis prompt. Default asks for general analysis. |
| `Model` | string | No | `sonar-pro` | Sonar model to use |
| `Port` | int | No | `47777` | Port the broker is listening on |

#### Examples

**Example 1 - Analyze a PowerShell script:**

```powershell
Invoke-PerplexityFileAnalysis -Path 'C:\scripts\deploy.ps1'
```

**Example 2 - Pipeline all scripts in a folder:**

```powershell
Get-ChildItem 'C:\scripts' -Filter '*.ps1' | Invoke-PerplexityFileAnalysis
```

**Example 3 - Analyze a log file with a custom prompt:**

```powershell
Invoke-PerplexityFileAnalysis -Path 'C:\logs\error.log' `
    -Prompt 'Identify critical errors and suggest root causes' `
    -Model sonar-reasoning-pro
```

#### Notes

- Files larger than 10,000 characters are automatically truncated. A warning is displayed when truncation occurs.
- The file extension is included in the prompt to help the model interpret the format.
- Supports `ValueFromPipelineByPropertyName` so `Get-ChildItem | Invoke-PerplexityFileAnalysis` works via the `FullName` alias.

---

### Invoke-PerplexityFolderAnalysis

Analyzes a folder structure using the Perplexity Sonar API.

#### Synopsis

```powershell
Invoke-PerplexityFolderAnalysis -Path <string> [-Prompt <string>] [-Model <string>] [-Port <int>]
```

#### Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `Path` | string | Yes | - | Path to the folder to analyze |
| `Prompt` | string | No | (auto) | Custom prompt. Default asks for structural analysis. |
| `Model` | string | No | `sonar-pro` | Sonar model to use |
| `Port` | int | No | `47777` | Port the broker is listening on |

#### Examples

**Example 1 - Analyze a project folder:**

```powershell
Invoke-PerplexityFolderAnalysis -Path 'C:\Projects\MyApp'
```

**Example 2 - Ask a specific question about a folder:**

```powershell
Invoke-PerplexityFolderAnalysis -Path 'C:\scripts' `
    -Prompt 'What kind of automation tasks does this script library support?'
```

**Example 3 - Analyze with a research-grade model:**

```powershell
Invoke-PerplexityFolderAnalysis -Path 'C:\Projects\LegacySystem' `
    -Prompt 'Identify any security risks or outdated patterns in this codebase structure' `
    -Model sonar-deep-research
```

#### Notes

- Up to 200 items are included in the directory listing. Deeper trees are truncated.
- Each item includes file size in KB for context.

---

## Category 4 - Batch Research

### Invoke-PerplexityBatch

Sends multiple queries to Perplexity and collects all results.

#### Synopsis

```powershell
Invoke-PerplexityBatch -Queries <string[]> [-Model <string>] [-OutputPath <string>]
                       [-DelayMs <int>] [-Port <int>]
```

#### Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `Queries` | string[] | Yes | - | Array of query strings |
| `Model` | string | No | `sonar` | Sonar model to use for all queries |
| `OutputPath` | string | No | - | Export path. Use `.csv` or `.json` extension. |
| `DelayMs` | int | No | `500` | Milliseconds to wait between queries (rate limiting) |
| `Port` | int | No | `47777` | Port the broker is listening on |

#### Examples

**Example 1 - Research a list of topics:**

```powershell
$queries = @(
    'What is Azure AD?',
    'What is Microsoft Intune?',
    'What is Defender for Endpoint?'
)
$results = Invoke-PerplexityBatch -Queries $queries -Model sonar-pro
$results | Format-Table Query, Response -Wrap
```

**Example 2 - Export results to CSV:**

```powershell
Invoke-PerplexityBatch -Queries $queries -OutputPath 'C:\reports\research.csv' -DelayMs 1000
```

**Example 3 - Read queries from a text file:**

```powershell
$queries = Get-Content 'C:\queries.txt'
Invoke-PerplexityBatch -Queries $queries -Model sonar -OutputPath 'C:\output\results.json'
```

#### Notes

- A progress bar shows query number, total count, and a preview of the current query.
- Results are returned as `PSCustomObject` with fields: `Query`, `Response`, `Citations`, `Model`, `Timestamp`.
- Increase `DelayMs` if you encounter rate limiting errors from the Perplexity API.

---

### Invoke-PerplexityReport

Generates a structured markdown research report on a topic.

#### Synopsis

```powershell
Invoke-PerplexityReport -Topic <string> [-Questions <string[]>] [-Model <string>]
                        [-OutputPath <string>] [-Port <int>]
```

#### Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `Topic` | string | Yes | - | The research topic |
| `Questions` | string[] | No | (auto-generated) | Specific sub-questions. If omitted, Perplexity generates 5 key questions. |
| `Model` | string | No | `sonar-pro` | Sonar model to use |
| `OutputPath` | string | No | - | Save report to a `.md` file at this path |
| `Port` | int | No | `47777` | Port the broker is listening on |

#### Examples

**Example 1 - Auto-generate report questions:**

```powershell
Invoke-PerplexityReport -Topic 'Zero Trust Network Architecture'
```

**Example 2 - Specify custom sub-questions:**

```powershell
Invoke-PerplexityReport -Topic 'Windows 11 Security Features' `
    -Questions @(
        'What new security features were added in Windows 11 24H2?',
        'How does Secure Boot work in Windows 11?',
        'What is Virtualization-Based Security (VBS)?'
    ) `
    -Model sonar-deep-research
```

**Example 3 - Save report to file:**

```powershell
Invoke-PerplexityReport `
    -Topic 'Ransomware Prevention Best Practices' `
    -OutputPath 'C:\reports\ransomware-prevention.md' `
    -Model sonar-reasoning-pro
```

#### Notes

- The report includes a title, all question/answer sections, and a consolidated citations list.
- Using `sonar-deep-research` significantly increases accuracy for complex topics but takes longer.

---

## Category 5 - IT Integration

### Invoke-PerplexityTicketAnalysis

Analyzes an IT support ticket using Perplexity.

#### Synopsis

```powershell
Invoke-PerplexityTicketAnalysis -TicketData <PSObject> [-Model <string>]
                                [-IncludeResolution] [-Port <int>]
```

#### Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `TicketData` | PSObject/hashtable | Yes | - | Object with ticket fields (Title, Description, Category, Priority, Status) |
| `Model` | string | No | `sonar-pro` | Sonar model to use |
| `IncludeResolution` | switch | No | false | Also request a step-by-step resolution guide |
| `Port` | int | No | `47777` | Port the broker is listening on |

#### Examples

**Example 1 - Analyze a ticket hashtable:**

```powershell
$ticket = @{
    Title       = 'Outlook not syncing emails'
    Description = 'User reports Outlook stopped syncing since this morning. Error 0x80040610 in event log.'
    Category    = 'Email'
    Priority    = 'High'
}
Invoke-PerplexityTicketAnalysis -TicketData $ticket
```

**Example 2 - Include resolution steps:**

```powershell
Invoke-PerplexityTicketAnalysis -TicketData $ticket -IncludeResolution -Model sonar-reasoning-pro
```

**Example 3 - Pipeline Atera tickets:**

```powershell
# Requires the atera-connector module
Get-AteraTickets -Status Open -Priority High |
    Invoke-PerplexityTicketAnalysis -IncludeResolution -Model sonar-pro
```

#### Notes

- Works with both hashtables and PSObjects. Automatically maps common field names.
- The system prompt is tuned for Windows/M365/Azure AD environments.
- Accepts pipeline input from any ticket-returning cmdlet.

---

### Invoke-PerplexityDeviceAnalysis

Analyzes an Intune or managed device object using Perplexity.

#### Synopsis

```powershell
Invoke-PerplexityDeviceAnalysis -DeviceData <PSObject> [-Focus <string>]
                                [-Model <string>] [-Port <int>]
```

#### Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `DeviceData` | PSObject/hashtable | Yes | - | Device properties object |
| `Focus` | string | No | `general` | Analysis focus: `compliance`, `security`, `performance`, `general` |
| `Model` | string | No | `sonar-pro` | Sonar model to use |
| `Port` | int | No | `47777` | Port the broker is listening on |

Recognized device fields: `DeviceName`, `DeviceId`, `OS`, `OSVersion`, `ComplianceState`, `LastSyncDateTime`, `ManagementAgent`, `OwnerType`, `EnrolledDateTime`, `Manufacturer`, `Model`, `SerialNumber`, `UserPrincipalName`, `AADRegistered`, `MDMStatus`, `EncryptionStatus`, `JailBroken`.

#### Examples

**Example 1 - Analyze a device for compliance:**

```powershell
$device = @{
    DeviceName       = 'LAPTOP-ABC123'
    OS               = 'Windows 11'
    OSVersion        = '22H2'
    ComplianceState  = 'NonCompliant'
    LastSyncDateTime = '2026-03-25T14:30:00Z'
}
Invoke-PerplexityDeviceAnalysis -DeviceData $device -Focus compliance
```

**Example 2 - Security assessment:**

```powershell
Invoke-PerplexityDeviceAnalysis -DeviceData $device -Focus security -Model sonar-reasoning-pro
```

**Example 3 - Pipeline non-compliant Intune devices:**

```powershell
# Requires the Microsoft.Graph PowerShell module
Get-MgDeviceManagementManagedDevice -Filter "complianceState eq 'noncompliant'" |
    Invoke-PerplexityDeviceAnalysis -Focus compliance -Model sonar-pro
```

#### Notes

- The function reads available properties from the device object and ignores any that are null or empty.
- Focus-specific prompts include requests for actionable PowerShell commands and Intune OMA-URI policies.

---

### Invoke-PerplexitySecurityAnalysis

Analyzes a security finding or vulnerability using Perplexity.

#### Synopsis

```powershell
Invoke-PerplexitySecurityAnalysis -Finding <string> [-Context <string>]
                                  [-Model <string>] [-Port <int>]
```

#### Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `Finding` | string | Yes | - | Description of the security finding, alert, or CVE |
| `Context` | string | No | - | Environmental context (OS, management tools, domain join status, etc.) |
| `Model` | string | No | `sonar-reasoning-pro` | Sonar model to use |
| `Port` | int | No | `47777` | Port the broker is listening on |

#### Examples

**Example 1 - Analyze a CVE:**

```powershell
Invoke-PerplexitySecurityAnalysis -Finding 'CVE-2024-21447 detected on 15 endpoints'
```

**Example 2 - Analyze a Defender alert with context:**

```powershell
Invoke-PerplexitySecurityAnalysis `
    -Finding 'Suspicious PowerShell execution policy bypass detected by Defender for Endpoint' `
    -Context 'Windows 11 22H2, Intune-managed, Azure AD joined, no admin rights for standard users'
```

**Example 3 - Analyze an ASR alert:**

```powershell
Invoke-PerplexitySecurityAnalysis `
    -Finding 'ASR rule "Block Office applications from creating child processes" triggered 47 times today' `
    -Context 'Microsoft 365 Apps for Enterprise, Outlook and Word affected' `
    -Model sonar-deep-research
```

#### Notes

- The response always includes: severity assessment, immediate containment steps, root cause explanation, remediation steps with specific commands, and long-term hardening recommendations.
- Uses `sonar-reasoning-pro` by default for the best accuracy on security topics.

---

## Pipeline Examples

**Analyze all changed files in a git diff:**

```powershell
git diff --name-only HEAD~1 |
    Where-Object { Test-Path $_ } |
    Invoke-PerplexityFileAnalysis -Prompt 'Summarize what changed and flag any potential issues'
```

**Batch analyze open tickets and export:**

```powershell
$tickets = Get-AteraTickets -Status Open
$results = $tickets | ForEach-Object {
    [PSCustomObject]@{
        TicketId = $_.id
        Title    = $_.title
        Analysis = (Invoke-PerplexityTicketAnalysis -TicketData $_ | Out-String)
    }
}
$results | Export-Csv 'C:\reports\ticket-analysis.csv' -NoTypeInformation
```

**Research multiple CVEs and build a report:**

```powershell
$cves = @('CVE-2024-21447', 'CVE-2024-38080', 'CVE-2024-30080')
$results = Invoke-PerplexityBatch -Queries ($cves | ForEach-Object { "$_ severity and patch status" }) -Model sonar-pro
$results | Select-Object Query, Response | Format-Table -Wrap
```

**Get status and restart stopped MCP servers in one pipeline:**

```powershell
Get-McpServer |
    Where-Object { $_.status -eq 'stopped' } |
    ForEach-Object { Restart-McpServer -Name $_.name }
```

---

## Tips and Best Practices

**Choose the right model:**
- Use `sonar` for quick lookups and simple Q&A
- Use `sonar-pro` for analysis, summaries, and IT support workflows
- Use `sonar-reasoning-pro` for step-by-step troubleshooting and CVE analysis
- Use `sonar-deep-research` for comprehensive reports that require multiple research passes

**Rate limiting:**
- The Perplexity API enforces rate limits. Use `DelayMs` in `Invoke-PerplexityBatch` (default 500ms) to avoid errors.
- If you hit rate limits frequently, consider a higher-tier Perplexity plan.

**Verbose output:**
- All module functions support `-Verbose`. Add it to see the HTTP requests being made:
  ```powershell
  Invoke-Perplexity 'Hello' -Verbose
  ```

**Error handling in scripts:**
- Check for errors before processing results:
  ```powershell
  $result = Invoke-Perplexity 'Test query' -Raw -ErrorVariable err
  if ($err) { Write-Warning "Query failed: $($err[0].Exception.Message)"; return }
  ```

**Keep sessions alive:**
- Import the module once per session. The module-level variables (`$script:BaseUrl`, `$script:DefaultPort`) persist for the session.
- If you change the broker port via `appsettings.json`, restart PowerShell or update the port via `-Port` parameter.

**Pipeline efficiency:**
- `Invoke-PerplexityFileAnalysis` uses `process {}` block properly, so pipeline input is processed one file at a time. Memory use is bounded regardless of how many files are piped in.
