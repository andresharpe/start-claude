using System.Runtime.Versioning;

namespace StartClaude.Service;

[SupportedOSPlatform("windows")]
public sealed class WatchdogService : BackgroundService
{
    private readonly WatchdogOptions _options;
    private readonly ClaudeProcessQuery _query;
    private readonly Spawner _spawner;
    private readonly StatusStore _status;
    private readonly ILogger<WatchdogService> _logger;

    public WatchdogService(
        WatchdogOptions options,
        ClaudeProcessQuery query,
        Spawner spawner,
        StatusStore status,
        ILogger<WatchdogService> logger)
    {
        _options = options;
        _query = query;
        _spawner = spawner;
        _status = status;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Watchdog starting. PollInterval={Interval}s ClaudeExe={Exe} Task={Task} ImmediateCheck={Immediate}",
            _options.PollIntervalSeconds,
            _options.ClaudeExecutablePath,
            _options.LauncherTaskName,
            _options.ImmediateCheckOnStartup);

        if (_options.ImmediateCheckOnStartup)
        {
            Tick();
        }

        var period = TimeSpan.FromSeconds(Math.Max(5, _options.PollIntervalSeconds));
        using var timer = new PeriodicTimer(period);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                Tick();
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
    }

    public void Tick()
    {
        try
        {
            var procs = _query.FindTargetProcesses();
            _status.RecordPoll(procs.Count);
            // Routine poll - keep it out of the default log stream.
            _logger.LogDebug("Poll: {Count} target claude.exe process(es)", procs.Count);

            if (procs.Count == 0)
            {
                _logger.LogWarning("No target claude.exe found, triggering launcher task");
                if (_spawner.TriggerLauncher(out var output))
                {
                    _status.RecordSpawn();
                }
                else
                {
                    _status.RecordError(output);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Watchdog tick failed");
            _status.RecordError(ex.Message);
        }
    }
}
