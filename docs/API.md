# PerplexityXPC HTTP API Reference

The PerplexityXPC broker exposes a REST API and WebSocket interface on `http://127.0.0.1:47777`. All endpoints are localhost-only.

---

## Table of Contents

- [Base URL and Authentication](#base-url-and-authentication)
- [Endpoints](#endpoints)
  - [POST /perplexity](#post-perplexity)
  - [POST /perplexity/stream](#post-perplexitystream)
  - [GET /status](#get-status)
  - [POST /mcp](#post-mcp)
  - [GET /mcp/servers](#get-mcpservers)
  - [POST /mcp/servers/{name}/restart](#post-mcpserversnamerstart)
  - [GET /config](#get-config)
  - [PUT /config](#put-config)
  - [WS /ws](#ws-ws)
- [WebSocket Protocol](#websocket-protocol)
- [SSE Streaming Format](#sse-streaming-format)
- [Error Codes](#error-codes)

---

## Base URL and Authentication

**Base URL:** `http://127.0.0.1:47777`

**Authentication:** None. The service binds exclusively to the loopback interface (`127.0.0.1`). Only processes running on the same machine can reach it. No API key, token, or header is required for HTTP requests.

**Content-Type:** All `POST` and `PUT` requests must include `Content-Type: application/json`.

---

## Endpoints

### POST /perplexity

Proxies a non-streaming chat completion request to the Perplexity Sonar API and returns the full response.

#### Request Body

```json
{
  "model": "sonar",
  "messages": [
    {
      "role": "system",
      "content": "You are a helpful assistant."
    },
    {
      "role": "user",
      "content": "What is VLAN trunking?"
    }
  ],
  "temperature": 0.7,
  "top_p": 1.0,
  "max_tokens": 1024,
  "search_mode": "web",
  "search_domain_filter": ["cisco.com", "networklessons.com"],
  "search_recency_filter": "month",
  "return_images": false,
  "return_related_questions": false,
  "response_format": null,
  "stream": false
}
```

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `model` | string | Yes | `sonar` | Model to use: `sonar`, `sonar-pro`, `sonar-reasoning-pro`, `sonar-deep-research` |
| `messages` | array | Yes | - | Conversation messages. Each has `role` (system/user/assistant) and `content` |
| `temperature` | float | No | (model default) | Sampling temperature 0-2 |
| `top_p` | float | No | (model default) | Nucleus sampling probability 0-1 |
| `max_tokens` | int | No | (model default) | Maximum tokens to generate |
| `search_mode` | string | No | `web` | Search scope: `web`, `academic`, `sec` |
| `search_domain_filter` | string[] | No | null | Restrict web results to these domains |
| `search_recency_filter` | string | No | null | Time window: `hour`, `day`, `week`, `month`, `year` |
| `return_images` | bool | No | false | Include image results in the response |
| `return_related_questions` | bool | No | false | Include suggested related questions |
| `response_format` | object | No | null | JSON schema constraint for structured output |
| `stream` | bool | No | false | Enable SSE streaming (prefer `POST /perplexity/stream` instead) |

#### Response Body

```json
{
  "id": "abc123xyz",
  "object": "chat.completion",
  "created": 1711580400,
  "model": "sonar",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "VLAN trunking is a method of carrying multiple VLANs over a single network link..."
      },
      "finish_reason": "stop"
    }
  ],
  "citations": [
    "https://www.cisco.com/c/en/us/td/docs/vlans.html",
    "https://networklessons.com/vlan/vlan-trunking"
  ],
  "usage": {
    "prompt_tokens": 45,
    "completion_tokens": 312,
    "total_tokens": 357
  },
  "search_results": [
    {
      "title": "VLAN Trunking Explained",
      "url": "https://networklessons.com/vlan/vlan-trunking",
      "snippet": "VLAN trunking allows switches to carry traffic...",
      "date": "2024-01-15"
    }
  ]
}
```

#### Error Responses

| Status | Description |
|--------|-------------|
| `400 Bad Request` | Missing or invalid request body |
| `502 Bad Gateway` | Perplexity API returned an error |
| `504 Gateway Timeout` | Perplexity API did not respond in time |

#### curl Example

```bash
curl -s -X POST http://127.0.0.1:47777/perplexity \
  -H "Content-Type: application/json" \
  -d '{
    "model": "sonar",
    "messages": [{"role": "user", "content": "What is VLAN trunking?"}]
  }'
```

#### PowerShell Example

```powershell
$body = @{
    model    = 'sonar-pro'
    messages = @(
        @{ role = 'system'; content = 'Answer concisely.' },
        @{ role = 'user';   content = 'What is VLAN trunking?' }
    )
} | ConvertTo-Json -Depth 5

$response = Invoke-RestMethod http://127.0.0.1:47777/perplexity `
    -Method Post -ContentType 'application/json' -Body $body

$response.choices[0].message.content
$response.citations
```

---

### POST /perplexity/stream

Proxies a streaming chat completion request and forwards the response as Server-Sent Events (SSE).

#### Request Body

Same as `POST /perplexity`.

#### Response

`Content-Type: text/event-stream`

The response is a stream of SSE data lines. Each line has the format:

```
data: <json-chunk>
```

Each chunk is a partial `ChatResponse` object containing a `delta` field instead of a full `message`:

```
data: {"id":"abc123","object":"chat.completion.chunk","created":1711580400,"model":"sonar","choices":[{"index":0,"delta":{"role":"assistant","content":"VLAN"},"finish_reason":null}]}

data: {"id":"abc123","object":"chat.completion.chunk","created":1711580400,"model":"sonar","choices":[{"index":0,"delta":{"content":" trunking"},"finish_reason":null}]}

data: {"id":"abc123","object":"chat.completion.chunk","created":1711580400,"model":"sonar","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

data: [DONE]
```

The stream ends with `data: [DONE]`.

#### curl Example

```bash
curl -N -X POST http://127.0.0.1:47777/perplexity/stream \
  -H "Content-Type: application/json" \
  -d '{"model":"sonar","messages":[{"role":"user","content":"Explain BGP in 3 sentences."}]}'
```

#### PowerShell Example

```powershell
# Note: PowerShell's Invoke-RestMethod does not natively support SSE.
# Use System.Net.Http.HttpClient for streaming:

$client = [System.Net.Http.HttpClient]::new()
$request = [System.Net.Http.HttpRequestMessage]::new(
    [System.Net.Http.HttpMethod]::Post,
    'http://127.0.0.1:47777/perplexity/stream'
)
$body = '{"model":"sonar","messages":[{"role":"user","content":"Explain BGP briefly."}]}'
$request.Content = [System.Net.Http.StringContent]::new($body, [Text.Encoding]::UTF8, 'application/json')

$response = $client.SendAsync($request, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).Result
$stream = $response.Content.ReadAsStreamAsync().Result
$reader = [System.IO.StreamReader]::new($stream)

while (-not $reader.EndOfStream) {
    $line = $reader.ReadLine()
    if ($line.StartsWith('data: ') -and $line -ne 'data: [DONE]') {
        $chunk = ($line -replace '^data: ') | ConvertFrom-Json
        Write-Host $chunk.choices[0].delta.content -NoNewline
    }
}
```

---

### GET /status

Returns service health information including version, uptime, and MCP server statuses.

#### Response Body

```json
{
  "status": "running",
  "version": "1.0.0",
  "uptime": "02:14:37",
  "started_at": "2026-03-28T08:00:00Z",
  "http_port": 47777,
  "mcp_servers": [
    {
      "name": "filesystem",
      "status": "running",
      "pid": 12345,
      "uptime": "02:14:30"
    },
    {
      "name": "github",
      "status": "stopped",
      "pid": 0,
      "uptime": null
    }
  ]
}
```

| Field | Type | Description |
|-------|------|-------------|
| `status` | string | Broker status: `running` or `degraded` |
| `version` | string | Service version |
| `uptime` | string | Time since service start (HH:MM:SS) |
| `started_at` | string | ISO 8601 UTC timestamp of service start |
| `http_port` | int | Port the HTTP server is bound to |
| `mcp_servers` | array | List of registered MCP servers and their state |
| `mcp_servers[].name` | string | Server identifier |
| `mcp_servers[].status` | string | `running`, `stopped`, or `error` |
| `mcp_servers[].pid` | int | Process ID (0 if not running) |
| `mcp_servers[].uptime` | string | Server uptime, or null if not running |

#### curl Example

```bash
curl -s http://127.0.0.1:47777/status | python -m json.tool
```

#### PowerShell Example

```powershell
$status = Invoke-RestMethod http://127.0.0.1:47777/status
Write-Host "Service status: $($status.status)"
Write-Host "Uptime: $($status.uptime)"
$status.mcp_servers | Format-Table name, status, pid, uptime
```

---

### POST /mcp

Sends a JSON-RPC 2.0 request to a named MCP server managed by the broker.

#### Request Body

```json
{
  "server": "filesystem",
  "method": "tools/call",
  "params": {
    "name": "read_file",
    "arguments": {
      "path": "C:\\Users\\YourUsername\\Documents\\notes.txt"
    }
  }
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `server` | string | Yes | Name of the MCP server to target |
| `method` | string | Yes | MCP method to invoke (e.g., `tools/list`, `tools/call`, `resources/read`) |
| `params` | object | No | Parameters to pass to the method |

#### Response Body

The response is the raw JSON-RPC result from the MCP server:

```json
{
  "content": [
    {
      "type": "text",
      "text": "Meeting notes from 2026-03-28:\n- Reviewed Q1 metrics..."
    }
  ],
  "isError": false
}
```

#### Error Responses

| Status | Description |
|--------|-------------|
| `400 Bad Request` | Missing `server` or `method` field |
| `404 Not Found` | No MCP server with the given name is registered |
| `502 Bad Gateway` | MCP server returned an error |
| `504 Gateway Timeout` | MCP server did not respond within the timeout |

#### curl Example

```bash
# List tools available on the filesystem server
curl -s -X POST http://127.0.0.1:47777/mcp \
  -H "Content-Type: application/json" \
  -d '{"server":"filesystem","method":"tools/list","params":{}}'

# Read a file via the filesystem server
curl -s -X POST http://127.0.0.1:47777/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "server": "filesystem",
    "method": "tools/call",
    "params": {
      "name": "read_file",
      "arguments": {"path": "C:\\Users\\YourUsername\\Documents\\notes.txt"}
    }
  }'
```

#### PowerShell Example

```powershell
# List tools
$tools = Invoke-RestMethod http://127.0.0.1:47777/mcp -Method Post `
    -ContentType 'application/json' `
    -Body '{"server":"filesystem","method":"tools/list","params":{}}'
$tools.tools | Select-Object name, description

# Call a tool
$body = @{
    server = 'github'
    method = 'tools/call'
    params = @{
        name      = 'list_repos'
        arguments = @{ owner = 'your-org'; type = 'private' }
    }
} | ConvertTo-Json -Depth 5

Invoke-RestMethod http://127.0.0.1:47777/mcp -Method Post `
    -ContentType 'application/json' -Body $body
```

---

### GET /mcp/servers

Lists all MCP servers registered with the broker.

#### Response Body

```json
[
  {
    "name": "filesystem",
    "status": "running",
    "pid": 12345,
    "uptime": "01:23:45",
    "command": "npx",
    "disabled": false
  },
  {
    "name": "github",
    "status": "stopped",
    "pid": 0,
    "uptime": null,
    "command": "npx",
    "disabled": false
  },
  {
    "name": "brave-search",
    "status": "stopped",
    "pid": 0,
    "uptime": null,
    "command": "npx",
    "disabled": true
  }
]
```

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | Unique server identifier |
| `status` | string | `running`, `stopped`, or `error` |
| `pid` | int | OS process ID, 0 if not running |
| `uptime` | string | Uptime string, null if not running |
| `command` | string | Launch command |
| `disabled` | bool | Whether the server is disabled in config |

#### curl Example

```bash
curl -s http://127.0.0.1:47777/mcp/servers | python -m json.tool
```

#### PowerShell Example

```powershell
Invoke-RestMethod http://127.0.0.1:47777/mcp/servers | Format-Table name, status, pid, uptime
```

---

### POST /mcp/servers/{name}/restart

Restarts a specific MCP server by name.

#### URL Parameters

| Parameter | Description |
|-----------|-------------|
| `name` | The name of the MCP server to restart (must match a key in `mcp-servers.json`) |

#### Request Body

None required.

#### Response Body

```json
{
  "success": true,
  "name": "filesystem",
  "status": "running",
  "pid": 12399
}
```

#### Error Responses

| Status | Description |
|--------|-------------|
| `404 Not Found` | No MCP server with that name is registered |
| `500 Internal Server Error` | Server failed to restart |

#### curl Example

```bash
curl -s -X POST http://127.0.0.1:47777/mcp/servers/filesystem/restart
```

#### PowerShell Example

```powershell
Invoke-RestMethod http://127.0.0.1:47777/mcp/servers/filesystem/restart -Method Post
```

---

### GET /config

Returns the current non-sensitive broker configuration.

#### Response Body

```json
{
  "http_port": 47777,
  "default_model": "sonar",
  "api_timeout_sec": 60,
  "max_tokens": 2048,
  "log_level": "Information",
  "pipe_server_name": "PerplexityXPCPipe",
  "max_file_size_kb": 10240,
  "mcp": {
    "auto_restart": true,
    "timeout_sec": 30,
    "max_concurrent_servers": 5
  }
}
```

Note: The API key is never included in the response.

#### curl Example

```bash
curl -s http://127.0.0.1:47777/config | python -m json.tool
```

#### PowerShell Example

```powershell
$config = Invoke-RestMethod http://127.0.0.1:47777/config
$config | ConvertTo-Json
```

---

### PUT /config

Updates broker configuration at runtime.

#### Request Body

A JSON object with any subset of writable configuration fields:

```json
{
  "default_model": "sonar-pro",
  "api_timeout_sec": 90,
  "log_level": "Debug",
  "max_tokens": 4096
}
```

| Writable Field | Type | Description |
|----------------|------|-------------|
| `default_model` | string | Default Sonar model |
| `api_timeout_sec` | int | API request timeout in seconds |
| `max_tokens` | int | Default max tokens per response |
| `log_level` | string | Logging level (Trace/Debug/Information/Warning/Error) |
| `mcp.auto_restart` | bool | Auto-restart crashed MCP servers |
| `mcp.timeout_sec` | int | MCP request timeout |

Note: `http_port` and `pipe_server_name` cannot be changed at runtime; restart the service after editing `appsettings.json`.

#### Response Body

```json
{
  "success": true,
  "updated": ["default_model", "api_timeout_sec", "log_level"]
}
```

#### curl Example

```bash
curl -s -X PUT http://127.0.0.1:47777/config \
  -H "Content-Type: application/json" \
  -d '{"default_model":"sonar-pro","log_level":"Debug"}'
```

#### PowerShell Example

```powershell
$settings = @{
    default_model   = 'sonar-reasoning-pro'
    api_timeout_sec = 120
} | ConvertTo-Json

Invoke-RestMethod http://127.0.0.1:47777/config -Method Put `
    -ContentType 'application/json' -Body $settings
```

---

### WS /ws

WebSocket endpoint for persistent streaming connections.

**URL:** `ws://127.0.0.1:47777/ws`

---

## WebSocket Protocol

Connect to `ws://127.0.0.1:47777/ws` to establish a persistent connection for streaming responses.

### Send a Query

Send a JSON message with the same schema as `POST /perplexity`:

```json
{
  "model": "sonar",
  "messages": [
    {"role": "user", "content": "Explain TCP/IP in simple terms."}
  ]
}
```

### Receive Streaming Chunks

The server sends streaming delta chunks as individual WebSocket text messages:

```json
{"type":"chunk","delta":"TCP"}
{"type":"chunk","delta":"/IP"}
{"type":"chunk","delta":" is a set of networking protocols..."}
{"type":"done","citations":["https://example.com/tcp-ip"]}
```

| Message Type | Fields | Description |
|-------------|--------|-------------|
| `chunk` | `delta` | Partial text content |
| `done` | `citations`, `usage` | Stream complete - includes citations and token usage |
| `error` | `message`, `code` | Error occurred during streaming |

### PowerShell WebSocket Example

```powershell
Add-Type -AssemblyName System.Net.WebSockets

$ws = [System.Net.WebSockets.ClientWebSocket]::new()
$ws.ConnectAsync([Uri]'ws://127.0.0.1:47777/ws', [Threading.CancellationToken]::None).Wait()

# Send a query
$query = '{"model":"sonar","messages":[{"role":"user","content":"What is DNS?"}]}'
$bytes = [Text.Encoding]::UTF8.GetBytes($query)
$segment = [ArraySegment[byte]]::new($bytes)
$ws.SendAsync($segment, [Net.WebSockets.WebSocketMessageType]::Text, $true, [Threading.CancellationToken]::None).Wait()

# Receive chunks
$buffer = [byte[]]::new(4096)
while ($ws.State -eq [Net.WebSockets.WebSocketState]::Open) {
    $result = $ws.ReceiveAsync([ArraySegment[byte]]::new($buffer), [Threading.CancellationToken]::None).Result
    $text = [Text.Encoding]::UTF8.GetString($buffer, 0, $result.Count)
    $msg = $text | ConvertFrom-Json
    if ($msg.type -eq 'chunk') { Write-Host $msg.delta -NoNewline }
    if ($msg.type -eq 'done')  { Write-Host ''; break }
}

$ws.Dispose()
```

---

## SSE Streaming Format

`POST /perplexity/stream` uses Server-Sent Events (SSE) with the following format:

```
data: <json-object>\n\n
```

Each JSON object is a partial chat completion chunk matching the OpenAI streaming format:

```json
{
  "id": "chatcmpl-abc123",
  "object": "chat.completion.chunk",
  "created": 1711580400,
  "model": "sonar",
  "choices": [
    {
      "index": 0,
      "delta": {
        "role": "assistant",
        "content": "Hello"
      },
      "finish_reason": null
    }
  ]
}
```

The final chunk has `finish_reason: "stop"` and is followed by:

```
data: [DONE]
```

**Client-side handling:**

- Split the stream on `\n\n`
- Strip the `data: ` prefix from each line
- Parse the remaining string as JSON
- Stop when you receive `data: [DONE]`

---

## Error Codes

| HTTP Status | Description | Common Causes |
|------------|-------------|---------------|
| `400` | Bad Request | Malformed JSON, missing required fields (`model`, `messages`) |
| `404` | Not Found | MCP server name does not match any registered server |
| `405` | Method Not Allowed | Wrong HTTP method for the endpoint |
| `422` | Unprocessable Entity | Valid JSON but invalid field values (e.g., unknown model name) |
| `502` | Bad Gateway | Perplexity API or MCP server returned an error |
| `503` | Service Unavailable | Perplexity API is unreachable (network issue or API outage) |
| `504` | Gateway Timeout | Perplexity API or MCP server did not respond within the timeout |

### Error Response Body

All error responses return a problem detail object:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Upstream API Error",
  "status": 502,
  "detail": "The Perplexity API returned HTTP 401 - check your API key."
}
```

### Checking for Errors in PowerShell

```powershell
try {
    $response = Invoke-RestMethod http://127.0.0.1:47777/perplexity `
        -Method Post -ContentType 'application/json' -Body $body -ErrorAction Stop
}
catch {
    $statusCode = [int]$_.Exception.Response.StatusCode
    Write-Error "HTTP $statusCode - $($_.Exception.Message)"
}
```
