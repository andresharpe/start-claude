using System.Diagnostics;
using System.Runtime.Versioning;

namespace StartClaude.Service;

[SupportedOSPlatform("windows")]
public sealed class Spawner
{
    private readonly WatchdogOptions _options;
    private readonly ILogger<Spawner> _logger;

    public Spawner(WatchdogOptions options, ILogger<Spawner> logger)
    {
        _options = options;
        _logger = logger;
    }

    public bool TriggerLauncher(out string output)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/Run /TN \"{_options.LauncherTaskName}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var p = Process.Start(psi)
                ?? throw new InvalidOperationException("Process.Start returned null");
            p.WaitForExit(15_000);
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            output = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : $"{stdout.Trim()} | {stderr.Trim()}";
            if (p.ExitCode != 0)
            {
                _logger.LogWarning("schtasks /Run exited with code {Code}: {Output}", p.ExitCode, output);
                return false;
            }
            _logger.LogInformation("Triggered scheduled task {Task}: {Output}", _options.LauncherTaskName, output);
            return true;
        }
        catch (Exception ex)
        {
            output = ex.Message;
            _logger.LogError(ex, "Failed to invoke schtasks for task {Task}", _options.LauncherTaskName);
            return false;
        }
    }
}
