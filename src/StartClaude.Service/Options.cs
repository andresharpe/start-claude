namespace StartClaude.Service;

public sealed class WatchdogOptions
{
    public int PollIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Absolute path to the claude.exe the watchdog keeps alive. An empty value
    /// resolves to <see cref="ResolveClaudeExecutablePath"/> at startup. The
    /// installer writes a concrete path here so the service does not fall back
    /// to its own profile when it runs as LocalSystem.
    /// </summary>
    public string ClaudeExecutablePath { get; set; } = "";

    public string LauncherTaskName { get; set; } = "StartClaudeLauncher";
    public bool ImmediateCheckOnStartup { get; set; } = true;

    /// <summary>
    /// After triggering a launch, treat a still-running launcher process as a boot in
    /// progress for this many seconds. Past this window, if no claude.exe is present the
    /// watchdog launches again, so a launcher window whose claude has exited still gets
    /// recovered. Default comfortably covers a cold start at the 60s poll interval.
    /// </summary>
    public int SpawnCooldownSeconds { get; set; } = 120;

    /// <summary>
    /// Substring matched (case-insensitive) against pwsh.exe command lines to recognise an
    /// in-flight launcher the watchdog itself started. Mirrors the launcher task's command
    /// in scripts/install.ps1.
    /// </summary>
    public string LauncherCommandLineMatch { get; set; } = "claude --dangerously-skip-permissions";

    /// <summary>Default claude.exe location under the current user profile.</summary>
    public static string ResolveClaudeExecutablePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "bin", "claude.exe");
}

public sealed class HttpOptions
{
    public int Port { get; set; } = 19720;
    public string TailscaleInterfaceNameContains { get; set; } = "Tailscale";

    /// <summary>
    /// How long to wait at startup for the Tailscale interface to acquire an IPv4
    /// address before giving up and binding loopback only. At boot this service
    /// otherwise starts first, finds no interface, and stays unreachable over the
    /// tailnet for the life of the process. Kept under the service control
    /// manager's 30s start timeout, since this runs before the host reports
    /// started. install.ps1 also makes the service depend on Tailscale, so this
    /// wait only has to cover the gap between that service running and the
    /// interface holding an address.
    /// </summary>
    public int TailscaleWaitSeconds { get; set; } = 20;
}

public sealed class LogOptions
{
    /// <summary>
    /// Rolling log file path. An empty value resolves to a logs/ directory next
    /// to the executable at startup. The installer points this at the repo's
    /// logs/ directory.
    /// </summary>
    public string FilePath { get; set; } = "";

    public int RetainedFileCountLimit { get; set; } = 14;

    /// <summary>Default log path next to the running executable.</summary>
    public static string ResolveFilePath() =>
        Path.Combine(AppContext.BaseDirectory, "logs", "start-claude-.log");
}
