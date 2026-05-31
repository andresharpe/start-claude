using System.Management;
using System.Runtime.Versioning;

namespace StartClaude.Service;

public sealed record ClaudeProcessInfo(int Pid, string ExecutablePath, string CommandLine);

[SupportedOSPlatform("windows")]
public sealed class ClaudeProcessQuery
{
    private readonly WatchdogOptions _options;
    private readonly ILogger<ClaudeProcessQuery> _logger;

    public ClaudeProcessQuery(WatchdogOptions options, ILogger<ClaudeProcessQuery> logger)
    {
        _options = options;
        _logger = logger;
    }

    public IReadOnlyList<ClaudeProcessInfo> FindTargetProcesses()
    {
        var target = _options.ClaudeExecutablePath;
        var results = new List<ClaudeProcessInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process WHERE Name='claude.exe'");
            using var collection = searcher.Get();
            foreach (ManagementObject mo in collection)
            {
                try
                {
                    var pid = Convert.ToInt32(mo["ProcessId"]);
                    var exePath = mo["ExecutablePath"] as string ?? string.Empty;
                    var cmdLine = mo["CommandLine"] as string ?? string.Empty;
                    if (string.Equals(exePath, target, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new ClaudeProcessInfo(pid, exePath, cmdLine));
                    }
                }
                finally
                {
                    mo.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate claude.exe processes via WMI");
        }

        return results;
    }

    public int KillAllTargets()
    {
        var killed = 0;
        foreach (var p in FindTargetProcesses())
        {
            try
            {
                using var proc = System.Diagnostics.Process.GetProcessById(p.Pid);
                proc.Kill(entireProcessTree: true);
                killed++;
                _logger.LogInformation("Killed claude.exe pid={Pid}", p.Pid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not kill pid={Pid}", p.Pid);
            }
        }
        return killed;
    }
}
