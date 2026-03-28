using System.Diagnostics;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using PerplexityXPC.McpServer.Protocol;

namespace PerplexityXPC.McpServer.Tools;

/// <summary>
/// Provides system information queries: CPU, memory, disk, processes, services, and network.
/// Uses .NET APIs and WMI where available; degrades gracefully on non-Windows builds.
/// </summary>
public sealed class SystemInfoTool
{
    // -------------------------------------------------------------------------
    //  Tool definitions
    // -------------------------------------------------------------------------

    /// <summary>Returns MCP tool definitions for all system info operations.</summary>
    public IEnumerable<McpTool> GetToolDefinitions()
    {
        yield return new McpTool
        {
            Name        = "system_info.get_system",
            Description = "Get system overview: OS version, CPU info, memory usage, disk space, and uptime.",
            InputSchema = new { type = "object", properties = new { } },
        };

        yield return new McpTool
        {
            Name        = "system_info.get_processes",
            Description = "List running processes sorted by CPU or memory usage.",
            InputSchema = new
            {
                type       = "object",
                properties = new
                {
                    top       = new { type = "integer", description = "Number of top processes to return. Default 20." },
                    sort_by   = new { type = "string",  description = "Sort field: cpu or memory. Default memory." },
                },
            },
        };

        yield return new McpTool
        {
            Name        = "system_info.get_services",
            Description = "List Windows services with their status (Running, Stopped, etc.).",
            InputSchema = new
            {
                type       = "object",
                properties = new
                {
                    filter = new { type = "string", description = "Optional name filter substring." },
                    status = new { type = "string", description = "Optional status filter: Running or Stopped." },
                },
            },
        };

        yield return new McpTool
        {
            Name        = "system_info.get_network",
            Description = "Get network adapter info: IP addresses, MAC addresses, DNS servers.",
            InputSchema = new { type = "object", properties = new { } },
        };
    }

    // -------------------------------------------------------------------------
    //  Operations
    // -------------------------------------------------------------------------

    /// <summary>Returns a system overview snapshot.</summary>
    public ToolCallResult GetSystem(JsonElement args)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== System Information ===");
            sb.AppendLine();

            // OS
            sb.AppendLine($"OS:            {Environment.OSVersion}");
            sb.AppendLine($"MachineName:   {Environment.MachineName}");
            sb.AppendLine($"UserName:      {Environment.UserName}");
            sb.AppendLine($"Is64Bit:       {Environment.Is64BitOperatingSystem}");
            sb.AppendLine($"CLR Version:   {Environment.Version}");
            sb.AppendLine($"Uptime:        {FormatUptime(TimeSpan.FromMilliseconds(Environment.TickCount64))}");
            sb.AppendLine();

            // CPU
            sb.AppendLine($"CPU Cores:     {Environment.ProcessorCount} logical processors");
            try
            {
                using var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                cpuCounter.NextValue(); // first call always 0
                System.Threading.Thread.Sleep(100);
                var cpu = cpuCounter.NextValue();
                sb.AppendLine($"CPU Usage:     {cpu:F1}%");
            }
            catch
            {
                sb.AppendLine("CPU Usage:     (unavailable)");
            }
            sb.AppendLine();

            // Memory
            try
            {
                using var memFree = new PerformanceCounter("Memory", "Available MBytes");
                var freeMb = (long)memFree.NextValue();
                sb.AppendLine($"Memory Free:   {freeMb:N0} MB");

                // Total via WMI
                try
                {
                    using var searcher = new System.Management.ManagementObjectSearcher(
                        "SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
                    foreach (var obj in searcher.Get())
                    {
                        var totalKb = Convert.ToInt64(obj["TotalVisibleMemorySize"]);
                        var totalMb = totalKb / 1024;
                        sb.AppendLine($"Memory Total:  {totalMb:N0} MB");
                        sb.AppendLine($"Memory Used:   {totalMb - freeMb:N0} MB  ({(double)(totalMb - freeMb) / totalMb * 100:F1}%)");
                    }
                }
                catch
                {
                    sb.AppendLine("Memory Total:  (unavailable via WMI)");
                }
            }
            catch
            {
                sb.AppendLine("Memory:        (performance counters unavailable)");
            }
            sb.AppendLine();

            // Disks
            sb.AppendLine("=== Disk Drives ===");
            sb.AppendLine($"{"Drive",-8} {"Label",-20} {"Format",-8} {"Total",12} {"Free",12} {"Used%",7}");
            sb.AppendLine(new string('-', 72));
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                try
                {
                    var used = 100.0 - (double)drive.AvailableFreeSpace / drive.TotalSize * 100;
                    sb.AppendLine($"{drive.Name,-8} {drive.VolumeLabel,-20} {drive.DriveFormat,-8} " +
                                  $"{FormatBytes(drive.TotalSize),12} {FormatBytes(drive.AvailableFreeSpace),12} {used,6:F1}%");
                }
                catch { /* skip unready drives */ }
            }

            return ToolCallResult.Success(sb.ToString());
        }
        catch (Exception ex)
        {
            return ToolCallResult.Failure($"Error retrieving system info: {ex.Message}");
        }
    }

    /// <summary>Returns top N processes sorted by CPU or memory.</summary>
    public ToolCallResult GetProcesses(JsonElement args)
    {
        try
        {
            int top    = 20;
            string sortBy = "memory";

            if (args.TryGetProperty("top", out var topProp) && topProp.TryGetInt32(out var t))
                top = Math.Clamp(t, 1, 100);
            if (args.TryGetProperty("sort_by", out var sp))
                sortBy = sp.GetString()?.ToLowerInvariant() ?? "memory";

            var processes = Process.GetProcesses();
            var rows = new List<(string Name, int Pid, long MemKb, double Cpu, string Status)>();

            foreach (var p in processes)
            {
                try
                {
                    rows.Add((p.ProcessName, p.Id, p.WorkingSet64 / 1024, 0.0, "Running"));
                }
                catch { /* process may have exited */ }
            }

            IEnumerable<(string Name, int Pid, long MemKb, double Cpu, string Status)> sorted =
                sortBy == "cpu"
                    ? rows.OrderByDescending(r => r.Cpu).ThenByDescending(r => r.MemKb)
                    : rows.OrderByDescending(r => r.MemKb);

            var sb = new StringBuilder();
            sb.AppendLine($"Top {top} processes by {sortBy}:");
            sb.AppendLine();
            sb.AppendLine($"{"PID",7} {"Name",-35} {"Memory",12} {"Status",-10}");
            sb.AppendLine(new string('-', 68));

            foreach (var r in sorted.Take(top))
                sb.AppendLine($"{r.Pid,7} {r.Name,-35} {FormatBytes(r.MemKb * 1024),12} {r.Status,-10}");

            sb.AppendLine();
            sb.AppendLine($"Total processes: {processes.Length}");

            return ToolCallResult.Success(sb.ToString());
        }
        catch (Exception ex)
        {
            return ToolCallResult.Failure($"Error retrieving processes: {ex.Message}");
        }
    }

    /// <summary>Returns Windows services with optional name/status filtering.</summary>
    public ToolCallResult GetServices(JsonElement args)
    {
        try
        {
            string? filter = null;
            string? statusFilter = null;

            if (args.TryGetProperty("filter", out var fp)) filter = fp.GetString();
            if (args.TryGetProperty("status", out var sp)) statusFilter = sp.GetString();

            var services = ServiceController.GetServices();

            IEnumerable<ServiceController> filtered = services;
            if (!string.IsNullOrWhiteSpace(filter))
                filtered = filtered.Where(s =>
                    s.ServiceName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    s.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(statusFilter) &&
                Enum.TryParse<ServiceControllerStatus>(statusFilter, true, out var statusEnum))
                filtered = filtered.Where(s =>
                {
                    try { return s.Status == statusEnum; } catch { return false; }
                });

            var sb = new StringBuilder();
            sb.AppendLine("Windows Services:");
            sb.AppendLine();
            sb.AppendLine($"{"Status",-12} {"ServiceName",-40} {"DisplayName"}");
            sb.AppendLine(new string('-', 90));

            int count = 0;
            foreach (var svc in filtered.OrderBy(s => s.ServiceName))
            {
                try
                {
                    sb.AppendLine($"{svc.Status,-12} {svc.ServiceName,-40} {svc.DisplayName}");
                    count++;
                }
                catch { /* skip if access denied */ }
            }

            sb.AppendLine();
            sb.AppendLine($"Total: {count} service(s)");

            return ToolCallResult.Success(sb.ToString());
        }
        catch (Exception ex)
        {
            return ToolCallResult.Failure($"Error retrieving services: {ex.Message}");
        }
    }

    /// <summary>Returns network interface info including IP addresses and DNS servers.</summary>
    public ToolCallResult GetNetwork(JsonElement args)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Network Interfaces ===");
            sb.AppendLine();

            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                sb.AppendLine($"Name:          {adapter.Name}");
                sb.AppendLine($"Description:   {adapter.Description}");
                sb.AppendLine($"Type:          {adapter.NetworkInterfaceType}");
                sb.AppendLine($"Status:        {adapter.OperationalStatus}");
                sb.AppendLine($"Speed:         {(adapter.Speed > 0 ? $"{adapter.Speed / 1_000_000} Mbps" : "N/A")}");
                sb.AppendLine($"MAC:           {adapter.GetPhysicalAddress()}");

                var ipProps = adapter.GetIPProperties();

                var ipv4 = ipProps.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(a => $"{a.Address}/{PrefixToMask(a.PrefixLength)}")
                    .ToList();

                var ipv6 = ipProps.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                    .Select(a => a.Address.ToString())
                    .ToList();

                if (ipv4.Count > 0)
                    sb.AppendLine($"IPv4:          {string.Join(", ", ipv4)}");
                if (ipv6.Count > 0)
                    sb.AppendLine($"IPv6:          {string.Join(", ", ipv6)}");

                var gateways = ipProps.GatewayAddresses
                    .Select(g => g.Address.ToString())
                    .ToList();
                if (gateways.Count > 0)
                    sb.AppendLine($"Gateways:      {string.Join(", ", gateways)}");

                var dns = ipProps.DnsAddresses.Select(d => d.ToString()).ToList();
                if (dns.Count > 0)
                    sb.AppendLine($"DNS:           {string.Join(", ", dns)}");

                sb.AppendLine();
            }

            return ToolCallResult.Success(sb.ToString());
        }
        catch (Exception ex)
        {
            return ToolCallResult.Failure($"Error retrieving network info: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    //  Private helpers
    // -------------------------------------------------------------------------

    private static string FormatUptime(TimeSpan ts) =>
        $"{(int)ts.TotalDays}d {ts.Hours:D2}h {ts.Minutes:D2}m {ts.Seconds:D2}s";

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024               => $"{bytes} B",
        < 1024 * 1024        => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _                    => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
    };

    private static string PrefixToMask(int prefix)
    {
        if (prefix < 0 || prefix > 32) return prefix.ToString();
        uint mask = prefix == 0 ? 0 : ~((1u << (32 - prefix)) - 1);
        return $"{(mask >> 24) & 255}.{(mask >> 16) & 255}.{(mask >> 8) & 255}.{mask & 255}";
    }
}
