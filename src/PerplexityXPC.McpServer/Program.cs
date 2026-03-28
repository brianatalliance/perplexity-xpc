using System.Text;
using System.Text.Json;
using PerplexityXPC.McpServer.Configuration;
using PerplexityXPC.McpServer.Protocol;
using PerplexityXPC.McpServer.Tools;

// ============================================================================
//  PerplexityXPC Bundled MCP Server
//  Version 1.4.0
//
//  Communicates via JSON-RPC 2.0 over stdio.
//  All diagnostic output is written to stderr.
//  stdout is reserved exclusively for JSON-RPC responses.
// ============================================================================

Console.Error.WriteLine("[McpServer] PerplexityXPC.McpServer v1.4.0 starting");
Console.Error.WriteLine($"[McpServer] PID: {Environment.ProcessId}");
Console.Error.WriteLine($"[McpServer] Args: {string.Join(" ", args)}");

// Ensure stdout is not line-buffered and uses UTF-8 without a BOM
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Console.InputEncoding  = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

// -------------------------------------------------------------------------
//  Load configuration
// -------------------------------------------------------------------------

var config = McpServerConfig.Load(args);
Console.Error.WriteLine($"[McpServer] BrokerUrl: {config.BrokerUrl}");
Console.Error.WriteLine($"[McpServer] AllowedDirectories: {string.Join(", ", config.AllowedDirectories)}");

// -------------------------------------------------------------------------
//  Construct tool instances
// -------------------------------------------------------------------------

var filesystemTool      = new FilesystemTool(config);
var systemInfoTool      = new SystemInfoTool();
var eventLogTool        = new EventLogTool();
var registryTool        = new RegistryTool();
var powerShellTool      = new PowerShellTool(config);
var clipboardTool       = new ClipboardTool();
var perplexityProxyTool = new PerplexityProxyTool(config);

// -------------------------------------------------------------------------
//  Construct the JSON-RPC handler
// -------------------------------------------------------------------------

var handler = new JsonRpcHandler(
    filesystemTool,
    systemInfoTool,
    eventLogTool,
    registryTool,
    powerShellTool,
    clipboardTool,
    perplexityProxyTool);

bool shutdownRequested = false;
handler.ShutdownRequested += (_, _) =>
{
    Console.Error.WriteLine("[McpServer] Shutdown requested by client");
    shutdownRequested = true;
};

Console.Error.WriteLine("[McpServer] Ready - waiting for JSON-RPC requests on stdin");

// -------------------------------------------------------------------------
//  Main read loop
// -------------------------------------------------------------------------

try
{
    string? line;
    while (!shutdownRequested && (line = Console.ReadLine()) is not null)
    {
        if (string.IsNullOrWhiteSpace(line))
            continue;

        Console.Error.WriteLine($"[McpServer] << {TruncateForLog(line, 200)}");

        string? response;
        try
        {
            response = handler.HandleLine(line);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[McpServer] Unhandled exception in HandleLine: {ex}");
            response = BuildInternalError(line, ex.Message);
        }

        if (response is not null)
        {
            Console.WriteLine(response);
            Console.Out.Flush();
            Console.Error.WriteLine($"[McpServer] >> {TruncateForLog(response, 200)}");
        }
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[McpServer] Fatal error in read loop: {ex}");
    Environment.Exit(1);
}

Console.Error.WriteLine("[McpServer] Exiting cleanly");
perplexityProxyTool.Dispose();

// -------------------------------------------------------------------------
//  Helpers
// -------------------------------------------------------------------------

static string TruncateForLog(string s, int max) =>
    s.Length <= max ? s : s[..max] + $"... [{s.Length - max} more chars]";

static string BuildInternalError(string rawLine, string message)
{
    // Try to extract the id from the raw line so the error response is correlated
    JsonElement? id = null;
    try
    {
        using var doc = JsonDocument.Parse(rawLine);
        if (doc.RootElement.TryGetProperty("id", out var idProp))
            id = idProp.Clone();
    }
    catch { /* ignore */ }

    var response = new JsonRpcResponse
    {
        Id    = id,
        Error = new JsonRpcError
        {
            Code    = JsonRpcError.Codes.InternalError,
            Message = "Internal server error: " + message,
        },
    };

    return JsonSerializer.Serialize(response);
}
