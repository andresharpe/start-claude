using System.Net;
using System.Runtime.Versioning;

namespace StartClaude.Service;

[SupportedOSPlatform("windows")]
public sealed class WatchdogService : BackgroundService
{
    private readonly WatchdogOptions _options;
    private readonly HttpOptions _http;
    private readonly TailscaleBindingState _binding;
    private readonly ClaudeProcessQuery _query;
    private readonly Spawner _spawner;
    private readonly StatusStore _status;
    private readonly ILogger<WatchdogService> _logger;
    private readonly DateTimeOffset _processStartUtc = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastSpawnUtc = DateTimeOffset.MinValue;
    private IPAddress? _mismatchIp;
    private int _mismatchPolls;

    public WatchdogService(
        WatchdogOptions options,
        HttpOptions http,
        TailscaleBindingState binding,
        ClaudeProcessQuery query,
        Spawner spawner,
        StatusStore status,
        ILogger<WatchdogService> logger)
    {
        _options = options;
        _http = http;
        _binding = binding;
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
                // Only on the timer, never inside Tick(), so POST /spawn cannot
                // trigger a process exit.
                CheckTailscaleBinding();
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
                var launcherAlive = _query.FindLauncherProcesses().Count > 0;
                var withinCooldown =
                    DateTimeOffset.UtcNow - _lastSpawnUtc < TimeSpan.FromSeconds(_options.SpawnCooldownSeconds);

                if (launcherAlive && withinCooldown)
                {
                    // A launch we requested is still booting claude. Don't stack windows.
                    _logger.LogDebug("Launch in flight (launcher pwsh alive, within cooldown), skipping trigger");
                    return;
                }

                _logger.LogWarning("No target claude.exe found, triggering launcher task");
                if (_spawner.TriggerLauncher(out var output))
                {
                    _lastSpawnUtc = DateTimeOffset.UtcNow;
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

    /// <summary>
    /// Kestrel's listeners are fixed at startup, so when the Tailscale interface
    /// gains or changes its tailnet address after we bound, the only way to serve
    /// it is a process restart. This exits abnormally on purpose: a graceful stop
    /// reports a clean exit and the service control manager leaves it down, while
    /// an abnormal one triggers the restart-on-failure recovery install.ps1 sets.
    /// Guardrails: nothing happens in the first two minutes of process life, and
    /// the same mismatching address must be seen on two consecutive polls, so a
    /// flapping interface or a boot race cannot cause a restart loop.
    /// </summary>
    private void CheckTailscaleBinding()
    {
        try
        {
            if (DateTimeOffset.UtcNow - _processStartUtc < TimeSpan.FromSeconds(120))
            {
                return;
            }

            var current = TailscaleNetwork.TryDiscoverTailscaleIp(_http.TailscaleInterfaceNameContains);
            if (!TailscaleNetwork.ShouldRebind(_binding.BoundIp, current))
            {
                _mismatchIp = null;
                _mismatchPolls = 0;
                return;
            }

            if (_mismatchIp is null || !_mismatchIp.Equals(current))
            {
                _mismatchIp = current;
                _mismatchPolls = 1;
                return;
            }

            _mismatchPolls++;
            if (_mismatchPolls < 2)
            {
                return;
            }

            _logger.LogWarning(
                "Tailscale interface holds {Current} but the service is bound to {Bound}; exiting so the service control manager restarts it with the right bind",
                current,
                _binding.BoundIp?.ToString() ?? "loopback only");
            Serilog.Log.CloseAndFlush();
            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tailscale binding check failed");
        }
    }
}
