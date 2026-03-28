using System.Diagnostics;
using System.ServiceProcess;

namespace PerplexityXPC.RemoteGateway.Services;

// -----------------------------------------------------------------------
// Data transfer objects
// -----------------------------------------------------------------------

/// <summary>Top-level system snapshot.</summary>
public sealed record SystemInfo(
    string Hostname,
    string OsVersion,
    double CpuUsagePercent,
    long TotalMemoryMb,
    long FreeMemoryMb,
    long UsedMemoryMb,
    double MemoryUsagePercent,
    TimeSpan Uptime,
    string UptimeFormatted,
    DateTime CapturedAt);

/// <summary>Lightweight per-process summary.</summary>
public sealed record ProcessInfo(
    int Pid,
    string Name,
    long MemoryMb,
    int ThreadCount,
    string Status);

/// <summary>Windows service summary.</summary>
public sealed record ServiceInfo(
    string Name,
    string DisplayName,
    string Status,
    string StartType);

/// <summary>Windows Event Log entry.</summary>
public sealed record EventInfo(
    string Log,
    string Source,
    string EntryType,
    string Message,
    DateTime TimeGenerated,
    long EventId);

/// <summary>Disk volume information.</summary>
public sealed record DiskInfo(
    string Drive,
    string VolumeLabel,
    string DriveFormat,
    long TotalGb,
    long FreeGb,
    long UsedGb,
    double UsedPercent);

// -----------------------------------------------------------------------
// Service
// -----------------------------------------------------------------------

/// <summary>
/// Collects system metrics using .NET APIs and WMI/CIM where needed.
/// All methods are async even when the underlying operation is synchronous
/// so the route layer can await them uniformly.
/// </summary>
public sealed class SystemMonitor
{
    private readonly ILogger<SystemMonitor> _logger;

    /// <summary>Initializes the <see cref="SystemMonitor"/>.</summary>
    public SystemMonitor(ILogger<SystemMonitor> logger)
    {
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns a point-in-time snapshot of CPU, memory, and uptime.
    /// CPU usage is measured over a 500 ms sampling interval.
    /// </summary>
    public async Task<SystemInfo> GetSystemInfoAsync()
    {
        double cpu = await MeasureCpuAsync();

        long totalMemMb = 0;
        long freeMemMb = 0;

        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");

            foreach (System.Management.ManagementObject obj in searcher.Get())
            {
                totalMemMb = Convert.ToInt64(obj["TotalVisibleMemorySize"]) / 1024;
                freeMemMb = Convert.ToInt64(obj["FreePhysicalMemory"]) / 1024;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WMI memory query failed; falling back to GC metrics.");
            long gcTotal = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024;
            totalMemMb = gcTotal;
            freeMemMb = gcTotal - Environment.WorkingSet / 1024 / 1024;
        }

        long usedMemMb = totalMemMb - freeMemMb;
        double memPct = totalMemMb > 0
            ? Math.Round((double)usedMemMb / totalMemMb * 100, 1)
            : 0;

        TimeSpan uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

        return new SystemInfo(
            Hostname: Environment.MachineName,
            OsVersion: Environment.OSVersion.ToString(),
            CpuUsagePercent: cpu,
            TotalMemoryMb: totalMemMb,
            FreeMemoryMb: freeMemMb,
            UsedMemoryMb: usedMemMb,
            MemoryUsagePercent: memPct,
            Uptime: uptime,
            UptimeFormatted: FormatUptime(uptime),
            CapturedAt: DateTime.UtcNow);
    }

    /// <summary>
    /// Returns the top <paramref name="top"/> processes sorted by working set
    /// (memory) descending.
    /// </summary>
    public Task<List<ProcessInfo>> GetProcessesAsync(int top = 10)
    {
        top = Math.Clamp(top, 1, 200);
        var processes = Process.GetProcesses()
            .OrderByDescending(p => SafeWorkingSet(p))
            .Take(top)
            .Select(p => new ProcessInfo(
                Pid: p.Id,
                Name: p.ProcessName,
                MemoryMb: SafeWorkingSet(p) / 1024 / 1024,
                ThreadCount: SafeThreadCount(p),
                Status: "Running"))
            .ToList();

        return Task.FromResult(processes);
    }

    /// <summary>
    /// Returns Windows services filtered by status keyword.
    /// </summary>
    /// <param name="filter">
    /// Optional case-insensitive filter applied to service status
    /// (e.g. "running", "stopped") or service name.
    /// Pass null or empty to return all services.
    /// </param>
    public Task<List<ServiceInfo>> GetServicesAsync(string? filter = null)
    {
        ServiceController[] all = ServiceController.GetServices();

        IEnumerable<ServiceController> query = all;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(s =>
                s.Status.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                s.ServiceName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                s.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        var result = query.Select(s => new ServiceInfo(
            Name: s.ServiceName,
            DisplayName: s.DisplayName,
            Status: s.Status.ToString(),
            StartType: SafeStartType(s)))
            .OrderBy(s => s.Name)
            .ToList();

        return Task.FromResult(result);
    }

    /// <summary>
    /// Returns recent entries from the specified Windows Event Log.
    /// </summary>
    /// <param name="logName">Log name, e.g. "System", "Application".</param>
    /// <param name="maxEvents">Maximum number of entries to return.</param>
    public Task<List<EventInfo>> GetEventLogsAsync(string logName = "System", int maxEvents = 20)
    {
        maxEvents = Math.Clamp(maxEvents, 1, 200);
        var entries = new List<EventInfo>();

        try
        {
            using var log = new EventLog(logName);
            int total = log.Entries.Count;
            int start = Math.Max(0, total - maxEvents);

            for (int i = total - 1; i >= start && entries.Count < maxEvents; i--)
            {
                EventLogEntry e = log.Entries[i];
                entries.Add(new EventInfo(
                    Log: logName,
                    Source: e.Source,
                    EntryType: e.EntryType.ToString(),
                    Message: TruncateMessage(e.Message, 500),
                    TimeGenerated: e.TimeGenerated,
                    EventId: e.InstanceId));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read event log '{Log}'.", logName);
        }

        return Task.FromResult(entries);
    }

    /// <summary>
    /// Returns disk space information for all fixed local drives.
    /// </summary>
    public Task<List<DiskInfo>> GetDiskSpaceAsync()
    {
        var disks = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => new DiskInfo(
                Drive: d.Name,
                VolumeLabel: d.VolumeLabel,
                DriveFormat: d.DriveFormat,
                TotalGb: d.TotalSize / 1024 / 1024 / 1024,
                FreeGb: d.AvailableFreeSpace / 1024 / 1024 / 1024,
                UsedGb: (d.TotalSize - d.AvailableFreeSpace) / 1024 / 1024 / 1024,
                UsedPercent: d.TotalSize > 0
                    ? Math.Round((double)(d.TotalSize - d.AvailableFreeSpace) / d.TotalSize * 100, 1)
                    : 0))
            .ToList();

        return Task.FromResult(disks);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async Task<double> MeasureCpuAsync()
    {
        try
        {
            using var counter = new PerformanceCounter(
                "Processor", "% Processor Time", "_Total", readOnly: true);

            // First read always returns 0; sample twice.
            counter.NextValue();
            await Task.Delay(500);
            return Math.Round(counter.NextValue(), 1);
        }
        catch
        {
            return -1;
        }
    }

    private static long SafeWorkingSet(Process p)
    {
        try { return p.WorkingSet64; }
        catch { return 0; }
    }

    private static int SafeThreadCount(Process p)
    {
        try { return p.Threads.Count; }
        catch { return 0; }
    }

    private static string SafeStartType(ServiceController sc)
    {
        try { return sc.StartType.ToString(); }
        catch { return "Unknown"; }
    }

    private static string TruncateMessage(string msg, int maxChars) =>
        msg.Length <= maxChars ? msg : msg[..maxChars] + "...";

    private static string FormatUptime(TimeSpan ts) =>
        $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m {ts.Seconds}s";
}
