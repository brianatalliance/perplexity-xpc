# PerplexityXPC Remote Access

Access your Windows PC's PerplexityXPC installation securely from anywhere -
your iPhone, another computer, or any HTTP client.

---

## Overview

PerplexityXPC Remote Access exposes the local broker's capabilities through a
lightweight HTTP gateway, secured by a Cloudflare Tunnel and a Bearer token.
No port forwarding, no firewall holes, no static IP required.

### What remote access gives you

- Run Perplexity queries from your iPhone (via Shortcuts or any HTTP client)
- Monitor system health: CPU, memory, disk, services, event logs
- Execute PowerShell commands and PerplexityXPC module functions
- Read files from your PC
- All over HTTPS, from anywhere in the world

### Architecture

```
[Phone / Browser / iOS Shortcuts]
          |
          | HTTPS (port 443)
          v
[Cloudflare Edge - your domain]
          |
          | Cloudflare Tunnel (encrypted, outbound-only)
          v
[cloudflared service on your PC]
          |
          | HTTP (loopback only - 127.0.0.1:47778)
          v
[RemoteGateway - PerplexityXPCRemote service]
          |
          | Named pipe / IPC
          v
[PerplexityXPC Broker]
```

Traffic never touches your router or firewall. The `cloudflared` service
opens an outbound connection to Cloudflare's edge, and traffic flows through
that persistent tunnel. Your PC is never directly reachable from the internet.

### Security model

| Layer | Mechanism |
|---|---|
| Transport | TLS 1.3 (Cloudflare edge) |
| Network | Cloudflare Tunnel - no open ports on your router |
| Identity | Cloudflare Access - email OTP or SSO before any request reaches your PC |
| API auth | Bearer token - 64-char hex (256-bit entropy) |
| Command safety | Command allowlist on the gateway - blocks dangerous operations |
| Rate limiting | Per-IP request rate limiting on the gateway |
| Loopback-only | Gateway binds to 127.0.0.1 only - LAN cannot reach it directly |

---

## Prerequisites

- Windows 10 or Windows 11
- [.NET 8 Runtime](https://dot.net) (for the RemoteGateway service)
- A [Cloudflare account](https://dash.cloudflare.com) (free tier works)
- A domain name with DNS managed by Cloudflare
  - Can be a subdomain on an existing domain
  - Free domains available at freenom.com if needed
- `cloudflared` CLI - installed automatically by `Install-CloudflareTunnel.ps1`

---

## Quick Start

### Step 1 - Build and install the RemoteGateway

Open PowerShell as Administrator from the repository root:

```powershell
cd remote
.\Install-RemoteGateway.ps1
```

The script will:
- Build the RemoteGateway project
- Generate a random API token
- Install the `PerplexityXPCRemote` Windows Service
- Configure the firewall
- Start the service

Your API token will be printed and saved to:
`%USERPROFILE%\.perplexityxpc\remote-token.txt`

Verify it is running:

```powershell
Get-Service PerplexityXPCRemote
# Status should be: Running
```

Test locally:

```powershell
.\Test-RemoteAccess.ps1
# Should show: Health check PASS
```

### Step 2 - Set up Cloudflare Tunnel

You need a domain added to Cloudflare before this step.
Replace `xpc.yourdomain.com` with your actual subdomain.

```powershell
.\Install-CloudflareTunnel.ps1 -Hostname xpc.yourdomain.com
```

What happens:
1. Installs `cloudflared` via winget (if not already installed)
2. Opens your browser to authenticate with Cloudflare
3. Creates a tunnel named `perplexity-xpc`
4. Writes the config to `%USERPROFILE%\.cloudflared\config.yml`
5. Installs `cloudflared` as a Windows Service
6. Creates the DNS CNAME record automatically

Verify tunnel is up:

```powershell
.\Install-CloudflareTunnel.ps1 -Status
```

### Step 3 - Configure Cloudflare Access (email OTP)

This is the most important security step. Without it, anyone who knows your
URL can attempt to access the API (relying only on the Bearer token).

1. Go to [Cloudflare Zero Trust dashboard](https://one.cloudflare.com)
2. Navigate to **Access** -> **Applications** -> **Add an application**
3. Choose **Self-hosted**
4. Fill in:
   - Application name: `PerplexityXPC`
   - Session duration: `24 hours`
   - Application domain: `xpc.yourdomain.com`
5. Click **Next**
6. Create a policy:
   - Policy name: `Owner only`
   - Action: `Allow`
   - Include rule: **Emails** - add your email address
7. Click **Next** -> **Add application**

Now, any request to `https://xpc.yourdomain.com` will first prompt for an
email OTP before it even reaches your gateway. This is a hard outer gate.

For iOS Shortcuts, you can use a [Cloudflare Access service token](https://developers.cloudflare.com/cloudflare-one/identity/service-tokens/)
instead of email OTP - see the **Using from iPhone** section.

### Step 4 - Test from phone

Get your API token:

```powershell
Get-Content "$env:USERPROFILE\.perplexityxpc\remote-token.txt"
```

Test with curl from any terminal:

```bash
curl -H "Authorization: Bearer YOUR_TOKEN" https://xpc.yourdomain.com/health
```

Or run the full test suite:

```powershell
.\Test-RemoteAccess.ps1 `
    -TunnelUrl https://xpc.yourdomain.com `
    -ApiToken YOUR_TOKEN
```

---

## API Reference

All authenticated endpoints require:

```
Authorization: Bearer <your-64-char-token>
Content-Type: application/json  (for POST requests)
```

Base URL (local): `http://127.0.0.1:47778`
Base URL (external): `https://xpc.yourdomain.com`

---

### Health

#### GET /health

Returns gateway health. No authentication required.

```bash
curl https://xpc.yourdomain.com/health
```

Response:

```json
{
  "status": "ok",
  "service": "PerplexityXPC RemoteGateway",
  "version": "1.0.0",
  "uptime": "02:14:37",
  "broker_connected": true
}
```

---

### Perplexity Queries

#### POST /api/query

Send a query through the PerplexityXPC broker and get a response.

```bash
curl -X POST https://xpc.yourdomain.com/api/query \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"query": "What is the weather in Seattle?", "timeout": 30}'
```

Request body:

```json
{
  "query": "Your question here",
  "timeout": 30
}
```

Response:

```json
{
  "success": true,
  "query": "What is the weather in Seattle?",
  "response": "Currently in Seattle...",
  "elapsed_ms": 1842
}
```

#### GET /api/status

Returns the broker connection status.

```bash
curl https://xpc.yourdomain.com/api/status \
  -H "Authorization: Bearer YOUR_TOKEN"
```

Response:

```json
{
  "broker_running": true,
  "broker_pid": 12048,
  "queries_today": 47,
  "last_query_at": "2026-03-28T08:30:00Z"
}
```

---

### PowerShell Execution

#### POST /api/execute

Run a PowerShell command on the remote PC. Commands are validated against an
allowlist before execution.

```bash
curl -X POST https://xpc.yourdomain.com/api/execute \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"command": "Get-Date", "timeout": 10}'
```

Request body:

```json
{
  "command": "Get-Date",
  "timeout": 10
}
```

Response:

```json
{
  "success": true,
  "command": "Get-Date",
  "output": "Saturday, March 28, 2026 8:42:00 AM",
  "exit_code": 0,
  "elapsed_ms": 312
}
```

Error response (blocked command):

```json
{
  "success": false,
  "error": "Command blocked by allowlist policy",
  "command": "Remove-Item",
  "blocked": true
}
```

#### POST /api/execute/module

Run a PerplexityXPC module function.

```bash
curl -X POST https://xpc.yourdomain.com/api/execute/module \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"module": "SystemMonitor", "function": "GetCpuUsage", "args": {}}'
```

Request body:

```json
{
  "module": "SystemMonitor",
  "function": "GetCpuUsage",
  "args": {}
}
```

Response:

```json
{
  "success": true,
  "module": "SystemMonitor",
  "function": "GetCpuUsage",
  "result": {
    "cpu_percent": 14.2,
    "cores": 12
  }
}
```

---

### System Monitoring

#### GET /api/system

Complete system overview: CPU, memory, uptime, network.

```bash
curl https://xpc.yourdomain.com/api/system \
  -H "Authorization: Bearer YOUR_TOKEN"
```

Response:

```json
{
  "hostname": "MYPC",
  "os": "Windows 11 Pro 23H2",
  "uptime": "3 days, 4 hours",
  "cpu": {
    "name": "AMD Ryzen 9 7950X",
    "cores": 16,
    "threads": 32,
    "usage_percent": 8.4
  },
  "memory": {
    "total_gb": 64.0,
    "used_gb": 22.1,
    "free_gb": 41.9,
    "usage_percent": 34.5
  },
  "network": {
    "bytes_sent": 1048576,
    "bytes_recv": 5242880
  }
}
```

#### GET /api/system/processes

Top processes by CPU and memory usage.

```bash
curl "https://xpc.yourdomain.com/api/system/processes?top=20" \
  -H "Authorization: Bearer YOUR_TOKEN"
```

Query params:
- `top` - number of processes to return (default: 20)
- `sort` - `cpu` or `memory` (default: `cpu`)

Response:

```json
{
  "processes": [
    {
      "pid": 4820,
      "name": "chrome",
      "cpu_percent": 4.2,
      "memory_mb": 512,
      "status": "running"
    }
  ],
  "count": 20
}
```

#### GET /api/system/services

Windows services list with status.

```bash
curl "https://xpc.yourdomain.com/api/system/services?filter=running" \
  -H "Authorization: Bearer YOUR_TOKEN"
```

Query params:
- `filter` - `running`, `stopped`, or `all` (default: `running`)
- `search` - partial name match

Response:

```json
{
  "services": [
    {
      "name": "PerplexityXPC",
      "display_name": "PerplexityXPC Broker",
      "status": "Running",
      "start_type": "Automatic"
    },
    {
      "name": "PerplexityXPCRemote",
      "display_name": "PerplexityXPC Remote Gateway",
      "status": "Running",
      "start_type": "Automatic"
    }
  ],
  "count": 2
}
```

#### GET /api/system/events

Recent Windows Event Log entries.

```bash
curl "https://xpc.yourdomain.com/api/system/events?log=Application&count=50&level=Error" \
  -H "Authorization: Bearer YOUR_TOKEN"
```

Query params:
- `log` - `Application`, `System`, or `Security` (default: `Application`)
- `count` - number of entries (default: 50, max: 500)
- `level` - `Information`, `Warning`, `Error`, or `all` (default: `all`)
- `source` - filter by event source

Response:

```json
{
  "log": "Application",
  "events": [
    {
      "time": "2026-03-28T07:15:00Z",
      "level": "Error",
      "source": "Application Error",
      "event_id": 1000,
      "message": "Faulting application name: example.exe..."
    }
  ],
  "count": 1
}
```

#### GET /api/system/disks

Disk space usage for all drives.

```bash
curl https://xpc.yourdomain.com/api/system/disks \
  -H "Authorization: Bearer YOUR_TOKEN"
```

Response:

```json
{
  "disks": [
    {
      "drive": "C:\\",
      "label": "Windows",
      "total_gb": 953.0,
      "used_gb": 412.3,
      "free_gb": 540.7,
      "usage_percent": 43.3,
      "filesystem": "NTFS"
    }
  ]
}
```

---

### File Operations

#### GET /api/files

Directory listing.

```bash
curl "https://xpc.yourdomain.com/api/files?path=C%3A%5CUsers%5CMe%5CDocuments" \
  -H "Authorization: Bearer YOUR_TOKEN"
```

Query params:
- `path` - directory path (URL-encoded). Default: user home directory.

Response:

```json
{
  "path": "C:\\Users\\Me\\Documents",
  "items": [
    {
      "name": "report.pdf",
      "type": "file",
      "size_bytes": 204800,
      "modified": "2026-03-27T14:00:00Z"
    },
    {
      "name": "Projects",
      "type": "directory",
      "modified": "2026-03-20T09:00:00Z"
    }
  ],
  "count": 2
}
```

#### GET /api/files/read

Read a text file's content.

```bash
curl "https://xpc.yourdomain.com/api/files/read?path=C%3A%5CUsers%5CMe%5Cnotes.txt" \
  -H "Authorization: Bearer YOUR_TOKEN"
```

Query params:
- `path` - file path (URL-encoded)
- `maxBytes` - maximum bytes to return (default: 102400 = 100 KB)

Response:

```json
{
  "path": "C:\\Users\\Me\\notes.txt",
  "content": "File content here...",
  "size_bytes": 1024,
  "encoding": "utf-8",
  "truncated": false
}
```

#### GET /api/files/exists

Check if a path exists.

```bash
curl "https://xpc.yourdomain.com/api/files/exists?path=C%3A%5CProgram+Files%5CMyApp" \
  -H "Authorization: Bearer YOUR_TOKEN"
```

Response:

```json
{
  "path": "C:\\Program Files\\MyApp",
  "exists": true,
  "type": "directory"
}
```

---

## Using from iPhone

### Option A - iOS Shortcuts (Recommended)

iOS Shortcuts can make HTTP requests. Build shortcuts that call the API and
display results in a notification or speak results via Siri.

**Setup: Create a Shortcuts global variable for your token**

1. Open Shortcuts -> tap your name/icon -> Shortcuts Settings
2. Under iCloud settings, note that variables are per-shortcut
3. In each shortcut, add a "Text" action at the top containing your token,
   and name the variable "XPC Token"

Or store the token in a private note in Apple Notes and retrieve it via
a "Get Notes" action at the start of each shortcut.

---

### Shortcut Recipe 1 - "Check PC Status"

Calls `/api/system` and speaks a summary via Siri.

**Actions:**

1. **Text** - `YOUR_API_TOKEN` -> variable: `XPCToken`
2. **Get Contents of URL**
   - URL: `https://xpc.yourdomain.com/api/system`
   - Method: GET
   - Headers:
     - Key: `Authorization`
     - Value: `Bearer [XPCToken]`
3. **Get Dictionary Value** - Key: `cpu` from step 2 result
4. **Get Dictionary Value** - Key: `usage_percent` from step 3
5. **Get Dictionary Value** - Key: `memory` from step 2 result
6. **Get Dictionary Value** - Key: `usage_percent` from step 5
7. **Text** - `PC Status: CPU [step 4]%, RAM [step 6]%`
8. **Speak Text** - input from step 7

---

### Shortcut Recipe 2 - "Run Query"

Ask Siri a question, send it to Perplexity on your PC, speak the answer.

**Actions:**

1. **Text** - `YOUR_API_TOKEN` -> variable: `XPCToken`
2. **Ask for Input** - Prompt: `What do you want to ask?` -> variable: `UserQuery`
3. **Dictionary**
   - `query`: `[UserQuery]`
   - `timeout`: `30`
4. **Get Contents of URL**
   - URL: `https://xpc.yourdomain.com/api/query`
   - Method: POST
   - Request Body: JSON (use dictionary from step 3)
   - Headers:
     - `Authorization`: `Bearer [XPCToken]`
     - `Content-Type`: `application/json`
5. **Get Dictionary Value** - Key: `response` from step 4
6. **Speak Text** - input from step 5

---

### Shortcut Recipe 3 - "Check Services"

Shows running PerplexityXPC services in a notification.

**Actions:**

1. **Text** - `YOUR_API_TOKEN` -> variable: `XPCToken`
2. **Get Contents of URL**
   - URL: `https://xpc.yourdomain.com/api/system/services?search=Perplexity`
   - Method: GET
   - Headers: `Authorization`: `Bearer [XPCToken]`
3. **Get Dictionary Value** - Key: `services` from step 2
4. **Repeat with Each** item in step 3:
   - **Get Dictionary Value** - Key: `display_name`
   - **Get Dictionary Value** - Key: `status`
   - **Add to Variable** `ServiceList`: `[display_name]: [status]`
5. **Show Notification** - Body: `[ServiceList]`

---

### Shortcut Recipe 4 - "View Event Logs"

Fetches recent errors from the Application log and displays them.

**Actions:**

1. **Text** - `YOUR_API_TOKEN` -> variable: `XPCToken`
2. **Get Contents of URL**
   - URL: `https://xpc.yourdomain.com/api/system/events?level=Error&count=10`
   - Method: GET
   - Headers: `Authorization`: `Bearer [XPCToken]`
3. **Get Dictionary Value** - Key: `events` from step 2
4. **Get Item from List** - First item
5. **Get Dictionary Value** - Key: `message` from step 4
6. **Show Result** - input from step 5

---

### Option B - curl / HTTP clients

Any HTTP client works. For quick tests:

```bash
# From any terminal (Mac, Linux, another Windows PC)
TOKEN="your64hextoken"
URL="https://xpc.yourdomain.com"

# Health
curl "$URL/health"

# System info
curl -H "Authorization: Bearer $TOKEN" "$URL/api/system"

# Quick query
curl -X POST "$URL/api/query" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"query":"what time is it?","timeout":10}'
```

---

## Security Hardening

### Cloudflare Access - step-by-step

Cloudflare Access acts as an authentication gate in front of your tunnel.
Even if someone discovers your URL, they cannot reach your API without
first passing the Access check.

**[Screenshot placeholder: Cloudflare Zero Trust dashboard - Access -> Applications]**

1. Log in to [dash.cloudflare.com](https://dash.cloudflare.com)
2. Click your account -> **Zero Trust** (left sidebar)
3. Go to **Access** -> **Applications** -> **Add an application**

**[Screenshot placeholder: Application type selection - choose Self-hosted]**

4. Select **Self-hosted**
5. Enter:
   - **Application name**: `PerplexityXPC Remote`
   - **Session duration**: `24 Hours`
   - **Application domain**: `xpc.yourdomain.com`
6. Under **Identity providers**: select **One-time PIN** (no setup required)
7. Click **Next**

**[Screenshot placeholder: Policy creation screen]**

8. Create a policy:
   - **Policy name**: `Owner only`
   - **Action**: `Allow`
   - Under **Configure rules** -> **Include**:
     - Selector: `Emails`
     - Value: your email address
9. Click **Next** -> **Add application**

Now any browser visiting your URL will see a Cloudflare-hosted login page
asking for your email and a one-time code sent to it.

**For iOS Shortcuts (bypassing the email OTP):**

Create a Service Token for your shortcut:
1. Zero Trust -> **Access** -> **Service Auth** -> **Service Tokens**
2. Click **Create Service Token**, name it `iPhone Shortcuts`
3. Copy the **Client ID** and **Client Secret**
4. Add a policy rule in your application: **Include** -> **Service Token** -> select your token
5. In your Shortcuts, add two headers:
   - `CF-Access-Client-Id`: your Client ID
   - `CF-Access-Client-Secret`: your Client Secret

### API token rotation

Rotate your token periodically or immediately if you suspect compromise.

```powershell
# Generate new token and update config + service
.\Generate-RemoteToken.ps1 -UpdateConfig -SaveToFile

# Restart service to pick up new token
Restart-Service -Name PerplexityXPCRemote

# Update your iPhone Shortcuts with the new token
Get-Content "$env:USERPROFILE\.perplexityxpc\remote-token.txt"
```

**Token rotation schedule recommendation:**
- Routine: every 90 days
- After sharing: immediately after sharing the token with any tool/app
- After suspected compromise: immediately

### Command allowlist customization

The RemoteGateway only allows specific PowerShell commands through `/api/execute`.
The allowlist is defined in `appsettings.json`:

```json
{
  "RemoteGateway": {
    "AllowedCommands": [
      "Get-Date",
      "Get-Process",
      "Get-Service",
      "Get-EventLog",
      "Get-Disk",
      "Get-ChildItem",
      "Get-Content",
      "Test-Path",
      "Invoke-PerplexityQuery"
    ]
  }
}
```

To add or remove commands, edit `appsettings.json` in the install directory
and restart the service:

```powershell
notepad "C:\Program Files\PerplexityXPC\Remote\appsettings.json"
Restart-Service PerplexityXPCRemote
```

Never add these to the allowlist:
- `Remove-Item`, `Format-*`, `Clear-*` - destructive file operations
- `Invoke-Expression`, `iex` - arbitrary code execution
- `Set-ExecutionPolicy` - security policy changes
- `net user`, `net localgroup` - user account manipulation

### Monitoring and audit logging

The gateway logs every request. View recent access:

```powershell
# Windows Event Log
Get-EventLog -LogName Application -Source PerplexityXPCRemote -Newest 50

# Or check the log file (if configured in appsettings.json)
Get-Content "C:\Program Files\PerplexityXPC\Remote\logs\gateway.log" -Tail 100
```

Log entries include:
- Timestamp
- Client IP (as seen by Cloudflare)
- Endpoint accessed
- HTTP status returned
- Token hash (first 8 chars, for identifying which token was used)

For security alerting, set up a Windows Task Scheduler job that watches for
401/403 responses and sends an email:

```powershell
# Example: alert on 5+ auth failures in 10 minutes
$failures = Get-EventLog -LogName Application -Source PerplexityXPCRemote -Newest 50 |
    Where-Object { $_.Message -like '*401*' -and $_.TimeGenerated -gt (Get-Date).AddMinutes(-10) }

if ($failures.Count -ge 5) {
    Send-MailMessage -To "you@email.com" -Subject "XPC Auth Failures" -Body "$($failures.Count) failures detected"
}
```

### Emergency: disable remote access

To immediately stop all remote access:

```powershell
# Stop the gateway (blocks all API access)
Stop-Service PerplexityXPCRemote

# Stop the tunnel (blocks external routing)
Stop-Service cloudflared
```

To disable at startup too:

```powershell
Set-Service PerplexityXPCRemote -StartupType Disabled
Set-Service cloudflared -StartupType Disabled
```

To completely remove:

```powershell
.\Install-RemoteGateway.ps1 -Uninstall
.\Install-CloudflareTunnel.ps1 -Uninstall
```

---

## Troubleshooting

### Tunnel not connecting

**Symptom:** Cloudflare Tunnel shows as healthy but requests time out.

Check cloudflared service status:
```powershell
Get-Service cloudflared
# Should be: Running

# View recent tunnel errors
Get-EventLog -LogName Application -Source cloudflared -Newest 20
```

Check the config file exists and has a valid tunnel ID:
```powershell
Get-Content "$env:USERPROFILE\.cloudflared\config.yml"
```

Re-run login if authentication expired:
```powershell
cloudflared tunnel login
Restart-Service cloudflared
```

Verify the gateway is running first:
```powershell
.\Test-RemoteAccess.ps1
# Health check must pass locally before tunnel can work
```

---

### 401 Unauthorized

**Symptom:** All API calls return `{"error": "Unauthorized"}`.

Common causes:

1. **Wrong token** - verify the token you are sending matches the file:
   ```powershell
   Get-Content "$env:USERPROFILE\.perplexityxpc\remote-token.txt"
   ```

2. **Header format wrong** - must be exactly `Bearer <token>` with a space:
   ```bash
   # CORRECT
   -H "Authorization: Bearer abc123..."
   # WRONG
   -H "Authorization: abc123..."
   -H "Authorization: bearer abc123..."
   ```

3. **Token rotated but service not restarted** - after editing appsettings.json:
   ```powershell
   Restart-Service PerplexityXPCRemote
   ```

4. **Cloudflare Access blocking** - if the request is blocked before reaching
   your gateway, you get a Cloudflare-branded 403 page. Check your Access
   policy in the Zero Trust dashboard.

---

### 429 Rate limited

**Symptom:** Returns `{"error": "Rate limit exceeded"}` with HTTP 429.

The gateway enforces per-IP rate limiting. Default limit is 60 requests per
minute per IP address.

Wait 60 seconds and retry. For iOS Shortcuts that call multiple endpoints,
add a "Wait" action (0.5s) between requests.

To increase the limit, edit `appsettings.json`:

```json
{
  "RemoteGateway": {
    "RateLimitPerMinute": 120
  }
}
```

Then restart: `Restart-Service PerplexityXPCRemote`

---

### Command blocked

**Symptom:** `/api/execute` returns `{"blocked": true}`.

The command is not in the allowlist. Either:

1. Use a different command that is already allowed
2. Add the command to the allowlist in `appsettings.json` (see Security Hardening)

View the current allowlist:
```powershell
$s = Get-Content "C:\Program Files\PerplexityXPC\Remote\appsettings.json" | ConvertFrom-Json
$s.RemoteGateway.AllowedCommands
```

---

### Gateway won't start

**Symptom:** `PerplexityXPCRemote` service fails to start.

Check Event Viewer:
```powershell
Get-EventLog -LogName Application -Newest 20 | Where-Object { $_.Source -like '*Perplexity*' }
```

Common fixes:

1. **.NET runtime missing** - install from [dot.net](https://dot.net)
2. **Port already in use**:
   ```powershell
   netstat -ano | findstr :47778
   # Note the PID, then:
   Get-Process -Id <PID>
   ```
3. **appsettings.json syntax error** - validate JSON:
   ```powershell
   Get-Content "C:\Program Files\PerplexityXPC\Remote\appsettings.json" | ConvertFrom-Json
   # Will throw an error if JSON is invalid
   ```
4. **Binary not found** - re-run `Install-RemoteGateway.ps1`

---

### Port conflict on 47778

If another service already uses port 47778, install the gateway on a different port
and update the Cloudflare Tunnel config:

```powershell
# Install on different port
.\Install-RemoteGateway.ps1 -Port 47779

# Update tunnel config manually in:
# %USERPROFILE%\.cloudflared\config.yml
# Change: service: http://127.0.0.1:47778
# To:     service: http://127.0.0.1:47779

Restart-Service cloudflared
```

---

## File Reference

| File | Purpose |
|---|---|
| `Install-CloudflareTunnel.ps1` | Set up and manage the Cloudflare Tunnel |
| `Install-RemoteGateway.ps1` | Build, install, and configure the gateway service |
| `Generate-RemoteToken.ps1` | Generate or rotate the API token |
| `Test-RemoteAccess.ps1` | Verify all endpoints are working |
| `config-template.yml` | Cloudflare Tunnel config file template |
| `README.md` | This file |
