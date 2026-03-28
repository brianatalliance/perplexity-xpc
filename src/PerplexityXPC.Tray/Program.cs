using System.Reflection;
using PerplexityXPC.Tray;

// Enable Per-Monitor V2 DPI awareness before any UI is created.
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

// ──────────────────────────────────────────────────────────────────────────────
// Single-instance guard
// One tray per Windows session (per user).  We embed the current username so
// multiple Windows sessions on the same machine each get their own mutex.
// ──────────────────────────────────────────────────────────────────────────────
string mutexName = $@"Global\PerplexityXPC-Tray-{Environment.UserName}";
using var mutex = new Mutex(initiallyOwned: true, name: mutexName, out bool createdNew);

if (!createdNew)
{
    // Another instance is already running – signal it to show its window and exit.
    BringExistingInstanceToFront();
    return;
}

try
{
    // ──────────────────────────────────────────────────────────────────────────
    // Start the tray application
    // ──────────────────────────────────────────────────────────────────────────
    using var context = new TrayApplicationContext();
    Application.Run(context);
}
finally
{
    mutex.ReleaseMutex();
}

// ─── Helpers ─────────────────────────────────────────────────────────────────

/// <summary>
/// Sends a named-pipe message to the already-running instance so it can
/// surface its query popup.  If the pipe is unavailable we fail silently —
/// the user simply knows the app is already running.
/// </summary>
static void BringExistingInstanceToFront()
{
    try
    {
        using var pipe = new System.IO.Pipes.NamedPipeClientStream(
            ".",
            $"PerplexityXPC-Tray-IPC-{Environment.UserName}",
            System.IO.Pipes.PipeDirection.Out,
            System.IO.Pipes.PipeOptions.None);

        // Short timeout — don't block the exit path.
        pipe.Connect(timeoutMs: 500);
        using var writer = new System.IO.StreamWriter(pipe);
        writer.WriteLine("SHOW");
        writer.Flush();
    }
    catch
    {
        // Pipe not ready or other error — ignore; just exit.
    }
}
