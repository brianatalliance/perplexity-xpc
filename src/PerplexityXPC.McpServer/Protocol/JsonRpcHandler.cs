using System.Text.Json;
using PerplexityXPC.McpServer.Tools;

namespace PerplexityXPC.McpServer.Protocol;

/// <summary>
/// Core JSON-RPC 2.0 dispatcher. Parses incoming messages, routes them to
/// the appropriate handler, and produces well-formed responses.
/// </summary>
public sealed class JsonRpcHandler
{
    private readonly FilesystemTool _filesystem;
    private readonly SystemInfoTool _systemInfo;
    private readonly EventLogTool _eventLog;
    private readonly RegistryTool _registry;
    private readonly PowerShellTool _powerShell;
    private readonly ClipboardTool _clipboard;
    private readonly PerplexityProxyTool _perplexityProxy;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    /// <summary>
    /// Raised when the client sends a "shutdown" request. The host should
    /// stop reading and exit cleanly after this fires.
    /// </summary>
    public event EventHandler? ShutdownRequested;

    /// <summary>
    /// Initialises the handler with all tool implementations.
    /// </summary>
    public JsonRpcHandler(
        FilesystemTool filesystem,
        SystemInfoTool systemInfo,
        EventLogTool eventLog,
        RegistryTool registry,
        PowerShellTool powerShell,
        ClipboardTool clipboard,
        PerplexityProxyTool perplexityProxy)
    {
        _filesystem       = filesystem;
        _systemInfo       = systemInfo;
        _eventLog         = eventLog;
        _registry         = registry;
        _powerShell       = powerShell;
        _clipboard        = clipboard;
        _perplexityProxy  = perplexityProxy;
    }

    /// <summary>
    /// Processes a single raw JSON line and returns the serialized JSON-RPC response.
    /// Returns null for notifications (requests without an id) that don't need a reply.
    /// </summary>
    public string? HandleLine(string line)
    {
        JsonRpcRequest? request = null;

        try
        {
            request = JsonSerializer.Deserialize<JsonRpcRequest>(line, JsonOptions);
            if (request is null)
                return SerializeError(null, JsonRpcError.Codes.InvalidRequest, "Null request");
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"[McpServer] JSON parse error: {ex.Message}");
            return SerializeError(null, JsonRpcError.Codes.ParseError, "Parse error: " + ex.Message);
        }

        // Notifications (no id) -- process but do not reply
        bool isNotification = request.Id is null;

        try
        {
            var result = DispatchMethod(request);
            if (result is null)
                return null; // explicit null = no reply needed

            if (isNotification)
                return null;

            return SerializeResponse(request.Id, result, null);
        }
        catch (ToolException tex)
        {
            Console.Error.WriteLine($"[McpServer] Tool error ({request.Method}): {tex.Message}");
            if (isNotification) return null;
            return SerializeResponse(request.Id, null, new JsonRpcError
            {
                Code    = JsonRpcError.Codes.InternalError,
                Message = tex.Message,
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[McpServer] Unhandled error ({request.Method}): {ex}");
            if (isNotification) return null;
            return SerializeResponse(request.Id, null, new JsonRpcError
            {
                Code    = JsonRpcError.Codes.InternalError,
                Message = "Internal server error: " + ex.Message,
            });
        }
    }

    // -------------------------------------------------------------------------
    //  Method dispatch
    // -------------------------------------------------------------------------

    private object? DispatchMethod(JsonRpcRequest request)
    {
        return request.Method switch
        {
            "initialize"           => HandleInitialize(),
            "initialized"          => null, // client notification, no reply
            "tools/list"           => HandleToolsList(),
            "tools/call"           => HandleToolsCall(request),
            "shutdown"             => HandleShutdown(),
            _                      => throw new ToolException(
                                          $"Method not found: {request.Method}",
                                          JsonRpcError.Codes.MethodNotFound),
        };
    }

    private static InitializeResult HandleInitialize() => new();

    private ToolsListResult HandleToolsList()
    {
        var tools = new List<McpTool>();
        tools.AddRange(_filesystem.GetToolDefinitions());
        tools.AddRange(_systemInfo.GetToolDefinitions());
        tools.AddRange(_eventLog.GetToolDefinitions());
        tools.AddRange(_registry.GetToolDefinitions());
        tools.AddRange(_powerShell.GetToolDefinitions());
        tools.AddRange(_clipboard.GetToolDefinitions());
        tools.AddRange(_perplexityProxy.GetToolDefinitions());
        return new ToolsListResult { Tools = tools };
    }

    private ToolCallResult HandleToolsCall(JsonRpcRequest request)
    {
        if (request.Params is null)
            throw new ToolException("Missing params for tools/call", JsonRpcError.Codes.InvalidParams);

        string? toolName = null;
        JsonElement? arguments = null;

        if (request.Params.Value.TryGetProperty("name", out var nameProp))
            toolName = nameProp.GetString();

        if (request.Params.Value.TryGetProperty("arguments", out var argsProp))
            arguments = argsProp;

        if (string.IsNullOrWhiteSpace(toolName))
            throw new ToolException("Missing tool name", JsonRpcError.Codes.InvalidParams);

        arguments ??= JsonSerializer.Deserialize<JsonElement>("{}");

        Console.Error.WriteLine($"[McpServer] tools/call -> {toolName}");

        return toolName switch
        {
            // filesystem
            "filesystem.list_directory" => _filesystem.ListDirectory(arguments.Value),
            "filesystem.read_file"      => _filesystem.ReadFile(arguments.Value),
            "filesystem.search_files"   => _filesystem.SearchFiles(arguments.Value),
            "filesystem.file_info"      => _filesystem.FileInfo(arguments.Value),

            // system_info
            "system_info.get_system"    => _systemInfo.GetSystem(arguments.Value),
            "system_info.get_processes" => _systemInfo.GetProcesses(arguments.Value),
            "system_info.get_services"  => _systemInfo.GetServices(arguments.Value),
            "system_info.get_network"   => _systemInfo.GetNetwork(arguments.Value),

            // event_logs
            "event_logs.get_events"         => _eventLog.GetEvents(arguments.Value),
            "event_logs.get_event_sources"  => _eventLog.GetEventSources(arguments.Value),

            // registry
            "registry.get_value"    => _registry.GetValue(arguments.Value),
            "registry.list_keys"    => _registry.ListKeys(arguments.Value),
            "registry.search_values"=> _registry.SearchValues(arguments.Value),

            // powershell
            "powershell.execute"    => _powerShell.Execute(arguments.Value),
            "powershell.get_modules"=> _powerShell.GetModules(arguments.Value),

            // clipboard
            "clipboard.get_clipboard" => _clipboard.GetClipboard(arguments.Value),
            "clipboard.set_clipboard" => _clipboard.SetClipboard(arguments.Value),

            // perplexity proxy
            "perplexity.query"   => _perplexityProxy.Query(arguments.Value),
            "perplexity.status"  => _perplexityProxy.Status(arguments.Value),

            _ => ToolCallResult.Failure($"Unknown tool: {toolName}"),
        };
    }

    private object HandleShutdown()
    {
        ShutdownRequested?.Invoke(this, EventArgs.Empty);
        return new { };
    }

    // -------------------------------------------------------------------------
    //  Serialization helpers
    // -------------------------------------------------------------------------

    private static string SerializeError(JsonElement? id, int code, string message)
    {
        var resp = new JsonRpcResponse
        {
            Id    = id,
            Error = new JsonRpcError { Code = code, Message = message },
        };
        return JsonSerializer.Serialize(resp, JsonOptions);
    }

    private static string SerializeResponse(JsonElement? id, object? result, JsonRpcError? error)
    {
        var resp = new JsonRpcResponse
        {
            Id     = id,
            Result = result,
            Error  = error,
        };
        return JsonSerializer.Serialize(resp, JsonOptions);
    }
}

/// <summary>
/// Exception thrown by tool handlers to surface user-visible errors.
/// </summary>
public sealed class ToolException : Exception
{
    /// <summary>Gets the JSON-RPC error code to embed in the response.</summary>
    public int RpcCode { get; }

    /// <inheritdoc />
    public ToolException(string message, int rpcCode = JsonRpcError.Codes.InternalError)
        : base(message)
    {
        RpcCode = rpcCode;
    }
}
