using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PerplexityXPC.RemoteGateway.Configuration;

namespace PerplexityXPC.RemoteGateway.Middleware;

/// <summary>
/// ASP.NET Core middleware that validates the Bearer token present in the
/// Authorization header of every inbound request.
///
/// The /health endpoint is exempt so Cloudflare Tunnel health checks and
/// infrastructure monitors can probe liveness without a token.
/// </summary>
public sealed class ApiTokenAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiTokenAuthMiddleware> _logger;
    private readonly RemoteConfig _config;

    /// <summary>Initializes the middleware with the required dependencies.</summary>
    public ApiTokenAuthMiddleware(
        RequestDelegate next,
        ILogger<ApiTokenAuthMiddleware> logger,
        RemoteConfig config)
    {
        _next = next;
        _logger = logger;
        _config = config;
    }

    /// <summary>
    /// Validates the Bearer token and either passes the request to the next
    /// delegate or short-circuits with a 401 response.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // Health endpoint is always public - no token required.
        if (context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Reject requests when no ApiToken has been configured.
        if (string.IsNullOrWhiteSpace(_config.ApiToken))
        {
            _logger.LogError("ApiToken is not configured. All non-health requests are rejected.");
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Gateway is not configured - ApiToken is missing."
            });
            return;
        }

        string? authHeader = context.Request.Headers.Authorization.FirstOrDefault();

        if (authHeader is null || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            LogAuthFailure(context, "Missing or malformed Authorization header.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized - Bearer token required." });
            return;
        }

        string providedToken = authHeader["Bearer ".Length..].Trim();

        if (!ConstantTimeEquals(providedToken, _config.ApiToken))
        {
            LogAuthFailure(context, "Invalid Bearer token.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized - Invalid token." });
            return;
        }

        await _next(context);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Compares two strings in constant time to prevent timing-based token
    /// enumeration attacks.
    /// </summary>
    private static bool ConstantTimeEquals(string a, string b)
    {
        byte[] aBytes = Encoding.UTF8.GetBytes(a);
        byte[] bBytes = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }

    private void LogAuthFailure(HttpContext context, string reason)
    {
        string ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        _logger.LogWarning(
            "Auth failure from {IP} on {Method} {Path}: {Reason}",
            ip,
            context.Request.Method,
            context.Request.Path,
            reason);
    }
}
