using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using PerplexityXPC.RemoteGateway.Configuration;

namespace PerplexityXPC.RemoteGateway.Middleware;

/// <summary>
/// Simple in-memory sliding-window rate limiter.
///
/// Each unique client IP is allowed at most <see cref="RemoteConfig.RateLimitPerMinute"/>
/// requests within any rolling 60-second window.  When the limit is exceeded the
/// middleware short-circuits with HTTP 429 Too Many Requests.
///
/// Note: this limiter is intentionally lightweight and does not persist state
/// across process restarts.  For distributed deployments a shared backing store
/// (e.g. Redis) would be required.
/// </summary>
public sealed class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitMiddleware> _logger;
    private readonly RemoteConfig _config;

    // Key: client IP.  Value: queue of UTC timestamps for recent requests.
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _requestLog = new();

    // Used to periodically purge stale entries and keep memory bounded.
    private DateTime _lastCleanup = DateTime.UtcNow;
    private readonly TimeSpan _windowSize = TimeSpan.FromMinutes(1);

    /// <summary>Initializes the middleware.</summary>
    public RateLimitMiddleware(
        RequestDelegate next,
        ILogger<RateLimitMiddleware> logger,
        RemoteConfig config)
    {
        _next = next;
        _logger = logger;
        _config = config;
    }

    /// <summary>
    /// Checks the rate limit for the requesting IP and either forwards the
    /// request or returns 429.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        string clientKey = GetClientKey(context);
        DateTime now = DateTime.UtcNow;

        // Occasional cleanup - runs at most once per minute.
        if (now - _lastCleanup > _windowSize)
        {
            CleanupStaleEntries(now);
            _lastCleanup = now;
        }

        Queue<DateTime> requestTimes = _requestLog.GetOrAdd(clientKey, _ => new Queue<DateTime>());

        lock (requestTimes)
        {
            // Remove timestamps outside the current window.
            while (requestTimes.Count > 0 && now - requestTimes.Peek() > _windowSize)
            {
                requestTimes.Dequeue();
            }

            if (requestTimes.Count >= _config.RateLimitPerMinute)
            {
                _logger.LogWarning(
                    "Rate limit exceeded for {Client} - {Count}/{Limit} requests in the last minute.",
                    clientKey,
                    requestTimes.Count,
                    _config.RateLimitPerMinute);

                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers["Retry-After"] = "60";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Rate limit exceeded. Please wait before sending more requests.",
                    retryAfterSeconds = 60
                });
                return;
            }

            requestTimes.Enqueue(now);
        }

        await _next(context);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string GetClientKey(HttpContext context)
    {
        // Respect X-Forwarded-For set by Cloudflare so the real client IP is
        // used rather than the Cloudflare edge IP.
        string? forwarded = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
                         ?? context.Request.Headers["X-Forwarded-For"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            // X-Forwarded-For can be a comma-separated list; take the first.
            return forwarded.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private void CleanupStaleEntries(DateTime now)
    {
        foreach (string key in _requestLog.Keys)
        {
            if (_requestLog.TryGetValue(key, out Queue<DateTime>? queue))
            {
                lock (queue)
                {
                    while (queue.Count > 0 && now - queue.Peek() > _windowSize)
                    {
                        queue.Dequeue();
                    }

                    if (queue.Count == 0)
                    {
                        _requestLog.TryRemove(key, out _);
                    }
                }
            }
        }
    }
}
