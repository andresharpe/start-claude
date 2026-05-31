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
