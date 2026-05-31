using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using Serilog;
using StartClaude.Service;

[assembly: SupportedOSPlatform("windows")]

const string DashboardHtml = @"<!doctype html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>start-claude</title>
<style>
  :root { color-scheme: dark; }
  body { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; background:#0d1117; color:#e6edf3; margin:0; padding:24px; }
  h1 { font-size:18px; margin:0 0 16px; letter-spacing:0.5px; }
  .pill { display:inline-block; padding:2px 10px; border-radius:999px; font-size:12px; font-weight:600; }
  .ok { background:#1f6f3a; color:#d4f7df; }
  .bad { background:#7a1f23; color:#fbd6d8; }
  .muted { color:#8b949e; }
  .grid { display:grid; grid-template-columns: 160px 1fr; gap:8px 16px; margin:16px 0; }
  .grid div:nth-child(odd) { color:#8b949e; }
  button { background:#21262d; color:#e6edf3; border:1px solid #30363d; padding:8px 14px; border-radius:6px; font:inherit; cursor:pointer; margin-right:8px; }
  button:hover { background:#30363d; }
  button.danger { border-color:#7a1f23; color:#fbd6d8; }
  button.danger:hover { background:#7a1f23; }
  pre { background:#010409; border:1px solid #30363d; padding:12px; border-radius:6px; max-height:50vh; overflow:auto; font-size:12px; line-height:1.4; }
  .row { display:flex; align-items:center; gap:12px; margin-bottom:8px; flex-wrap:wrap; }
  a { color:#58a6ff; }
</style>
</head>
<body>
  <h1>start-claude <span class=""muted"" id=""host""></span> <span class=""muted"" id=""ts""></span></h1>
  <div class=""row"">
    <span id=""running"" class=""pill bad"">unknown</span>
    <button onclick=""act('/spawn','POST')"">Spawn now</button>
    <button class=""danger"" onclick=""confirmKill()"">Kill all</button>
    <button onclick=""load()"">Refresh</button>
  </div>
  <h2 style=""font-size:14px;margin:24px 0 8px;"">Watchdog</h2>
  <div class=""grid"" id=""grid""></div>
  <h2 style=""font-size:14px;margin:24px 0 8px;"">Machine vitals</h2>
  <div class=""grid"" id=""vitals""></div>
  <h2 style=""font-size:14px;margin:24px 0 8px;"">Screens <button onclick=""capture()"" style=""margin-left:8px;font-weight:600;"">Capture now</button> <span class=""muted"" id=""shotInfo""></span></h2>
  <div id=""shots"" style=""display:flex;flex-wrap:wrap;gap:12px;""></div>
  <h2 style=""font-size:14px;margin:24px 0 8px;"">Recent log <span class=""muted"">- last 200 lines, newest first</span></h2>
  <pre id=""logs"">loading...</pre>
  <p class=""muted"" style=""font-size:12px;"">Endpoints: <a href=""/status"">/status</a> | <a href=""/healthz"">/healthz</a> | <a href=""/logs/tail?lines=500"">/logs/tail</a></p>
<script>
function iso(d) { return d ? new Date(d).toISOString() : '-'; }
function fmtBytes(n) {
  if (!n && n !== 0) return '-';
  const units = ['B','KiB','MiB','GiB','TiB'];
  let i=0; let v=n;
  while (v >= 1024 && i < units.length-1) { v/=1024; i++; }
  return v.toFixed(v>=10?0:1) + ' ' + units[i];
}
function fmtPct(n) { return (n==null) ? '-' : n.toFixed(1) + '%'; }
function fmtUptime(sec) {
  if (sec==null) return '-';
  const d = Math.floor(sec/86400);
  const h = Math.floor((sec%86400)/3600);
  const m = Math.floor((sec%3600)/60);
  const s = Math.floor(sec%60);
  return (d?`${d}d `:'') + `${String(h).padStart(2,'0')}:${String(m).padStart(2,'0')}:${String(s).padStart(2,'0')}`;
}
async function load() {
  try {
    const s = await (await fetch('/status')).json();
    document.getElementById('ts').textContent = new Date().toISOString();
    document.getElementById('host').textContent = s.vitals ? '@ ' + s.vitals.machineName : '';
    const pill = document.getElementById('running');
    if (s.targetClaudeRunning) { pill.textContent = `running x${s.targetClaudeCount}`; pill.className = 'pill ok'; }
    else { pill.textContent = 'no claude session'; pill.className = 'pill bad'; }
    const g = document.getElementById('grid');
    const rows = [
      ['Last poll',     iso(s.lastPollUtc)],
      ['Last spawn',    iso(s.lastSpawnUtc)],
      ['Last error',    s.lastError || '-'],
      ['Poll interval', s.config.pollIntervalSeconds + 's'],
      ['Claude exe',    s.config.claudeExecutablePath],
      ['Launcher task', s.config.launcherTaskName],
      ['HTTP port',     s.config.port],
      ['Tailscale IP',  s.tailscaleIp || '(none)'],
      ['Public IP',     s.publicIp || '(unknown)'],
      ['Processes',     (s.processes||[]).map(p=>`#${p.pid}`).join(', ') || '-'],
    ];
    g.innerHTML = rows.map(([k,v])=>`<div>${k}</div><div>${escapeHtml(String(v))}</div>`).join('');
    const v = s.vitals || {};
    const vrows = [
      ['Machine',  v.machineName || '-'],
      ['OS',       v.os || '-'],
      ['.NET',     v.dotNetVersion || '-'],
      ['Uptime',   fmtUptime(v.uptimeSeconds)],
      ['CPU',      fmtPct(v.cpuPercent)],
      ['Memory',   `used ${fmtBytes(v.usedMemoryBytes)} (${fmtPct(v.memoryPercent)}) - free ${fmtBytes((v.totalMemoryBytes||0)-(v.usedMemoryBytes||0))} - total ${fmtBytes(v.totalMemoryBytes)}`],
      ['Disk C:',  `used ${fmtBytes((v.diskTotalBytes||0)-(v.diskFreeBytes||0))} (${fmtPct(v.diskUsedPercent)}) - free ${fmtBytes(v.diskFreeBytes)} - total ${fmtBytes(v.diskTotalBytes)}`],
    ];
    document.getElementById('vitals').innerHTML = vrows.map(([k,v])=>`<div>${k}</div><div>${escapeHtml(String(v))}</div>`).join('');
    const t = await (await fetch('/logs/tail?lines=200')).text();
    const reversed = t ? t.split(/\r?\n/).reverse().join('\n') : '';
    document.getElementById('logs').textContent = reversed || '(no log yet)';
  } catch (e) {
    document.getElementById('logs').textContent = 'load error: ' + e;
  }
}
async function act(path, method) {
  try { await fetch(path, { method }); } catch (e) {}
  setTimeout(load, 400);
}
async function capture() {
  const info = document.getElementById('shotInfo');
  const shots = document.getElementById('shots');
  info.textContent = 'capturing...';
  try {
    const r = await fetch('/screenshots', { method: 'POST' });
    if (!r.ok) { info.textContent = 'capture failed (' + r.status + ')'; return; }
    const data = await r.json();
    info.textContent = 'captured at ' + new Date(data.capturedAtUtc).toISOString();
    shots.innerHTML = data.screens.map(s => `
      <a href=""${s.url}"" target=""_blank"" rel=""noopener"" style=""text-decoration:none;color:inherit;"">
        <div style=""border:1px solid #30363d;border-radius:6px;padding:8px;background:#010409;"">
          <div class=""muted"" style=""font-size:11px;margin-bottom:6px;"">#${s.index} ${escapeHtml(s.deviceName)} ${s.width}x${s.height}${s.primary?' (primary)':''}</div>
          <img src=""${s.url}"" style=""max-width:480px;max-height:300px;display:block;"" alt=""screen ${s.index}"">
        </div>
      </a>`).join('');
  } catch (e) {
    info.textContent = 'error: ' + e;
  }
}
function confirmKill() {
  const code = String(Math.floor(Math.random() * 90) + 10);
  const answer = prompt(`Kill all target claude.exe processes?\n\nType ${code} to confirm.`);
  if (answer === null) return;
  if (answer.trim() === code) {
    act('/kill-all','POST');
  } else {
    alert('Cancelled - code did not match.');
  }
}
function escapeHtml(s){return s.replace(/[&<>]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;'}[c]));}
load();
capture();
setInterval(load, 5000);
</script>
</body>
</html>";

// Bootstrap logger so any early startup error has somewhere to go.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Make this a Windows Service when launched by the SCM.
    builder.Host.UseWindowsService(o => o.ServiceName = "StartClaudeService");

    // Bind option POCOs.
    var watchdogOptions = builder.Configuration.GetSection("Watchdog").Get<WatchdogOptions>() ?? new();
    var httpOptions = builder.Configuration.GetSection("Http").Get<HttpOptions>() ?? new();
    var logOptions = builder.Configuration.GetSection("Logging").Get<LogOptions>() ?? new();
    var screenshotOptions = builder.Configuration.GetSection("Screenshot").Get<ScreenshotOptions>() ?? new();

    // Resolve "auto" (empty) path settings to machine-neutral defaults. The
    // installer normally writes concrete values, so this mainly covers running
    // straight from source during development.
    if (string.IsNullOrWhiteSpace(watchdogOptions.ClaudeExecutablePath))
        watchdogOptions.ClaudeExecutablePath = WatchdogOptions.ResolveClaudeExecutablePath();
    if (string.IsNullOrWhiteSpace(logOptions.FilePath))
        logOptions.FilePath = LogOptions.ResolveFilePath();

    builder.Services.AddSingleton(watchdogOptions);
    builder.Services.AddSingleton(httpOptions);
    builder.Services.AddSingleton(logOptions);
    builder.Services.AddSingleton(screenshotOptions);

    // Serilog as the host logger.
    builder.Host.UseSerilog((ctx, sp, lc) =>
    {
        lc.MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", Serilog.Events.LogEventLevel.Information)
            .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                path: logOptions.FilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: logOptions.RetainedFileCountLimit,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
    });

    // Kestrel binding: localhost always, plus Tailscale interface when present.
    builder.WebHost.ConfigureKestrel((ctx, k) =>
    {
        k.Listen(IPAddress.Loopback, httpOptions.Port);
        var tailscaleIp = TryDiscoverTailscaleIp(httpOptions.TailscaleInterfaceNameContains);
        if (tailscaleIp is not null)
        {
            k.Listen(tailscaleIp, httpOptions.Port);
        }
    });

    // Watchdog + helpers.
    builder.Services.AddSingleton<StatusStore>();
    builder.Services.AddSingleton<ClaudeProcessQuery>();
    builder.Services.AddSingleton<Spawner>();
    builder.Services.AddSingleton<WatchdogService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<WatchdogService>());
    builder.Services.AddSingleton<VitalsSampler>();
    builder.Services.AddSingleton<Screenshooter>();
    builder.Services.AddSingleton<PublicIpProvider>();

    var app = builder.Build();

    app.Logger.LogInformation(
        "Listening on http://127.0.0.1:{Port}{Tail}",
        httpOptions.Port,
        TryDiscoverTailscaleIp(httpOptions.TailscaleInterfaceNameContains) is { } tip
            ? $" and http://{tip}:{httpOptions.Port}"
            : " (no Tailscale interface found)");

    app.MapGet("/", () => Results.Content(DashboardHtml, "text/html; charset=utf-8"));

    app.MapGet("/healthz", () => Results.Ok(new { ok = true, utc = DateTime.UtcNow }));

    app.MapGet("/status", (StatusStore status, ClaudeProcessQuery query, VitalsSampler vitals, PublicIpProvider publicIp, WatchdogOptions wopt, HttpOptions hopt) =>
    {
        var procs = query.FindTargetProcesses();
        var v = vitals.CurrentSnapshot();
        return Results.Json(new
        {
            targetClaudeRunning = procs.Count > 0,
            targetClaudeCount = procs.Count,
            processes = procs.Select(p => new { p.Pid, p.ExecutablePath, p.CommandLine }),
            lastPollUtc = status.LastPollUtc,
            lastSpawnUtc = status.LastSpawnUtc,
            lastError = status.LastError,
            lastTargetCount = status.LastTargetCount,
            config = new
            {
                wopt.PollIntervalSeconds,
                wopt.ClaudeExecutablePath,
                wopt.LauncherTaskName,
                hopt.Port,
            },
            tailscaleIp = TryDiscoverTailscaleIp(hopt.TailscaleInterfaceNameContains)?.ToString(),
            publicIp = publicIp.Current(),
            vitals = new
            {
                v.MachineName,
                v.Os,
                v.DotNetVersion,
                uptimeSeconds = (long)v.SystemUptime.TotalSeconds,
                v.CpuPercent,
                v.TotalMemoryBytes,
                v.UsedMemoryBytes,
                v.MemoryPercent,
                v.DiskTotalBytes,
                v.DiskFreeBytes,
                v.DiskUsedPercent,
            },
        });
    });

    app.MapPost("/spawn", (WatchdogService watchdog) =>
    {
        watchdog.Tick();
        return Results.Ok(new { triggered = true });
    });

    app.MapPost("/kill-all", (ClaudeProcessQuery query) =>
    {
        var killed = query.KillAllTargets();
        return Results.Ok(new { killed });
    });

    app.MapGet("/logs/tail", (int? lines, LogOptions logOpts) =>
    {
        var n = Math.Clamp(lines ?? 200, 1, 5000);
        var content = ReadTodayLogTail(logOpts.FilePath, n);
        return Results.Text(content);
    });

    app.MapPost("/screenshots", async (Screenshooter shooter, CancellationToken ct) =>
    {
        var result = await shooter.CaptureAsync(ct);
        if (result is null)
        {
            return Results.Problem(
                detail: "Screenshot capture failed or timed out. Check that the StartClaudeScreenshot scheduled task is registered and the user is logged on.",
                statusCode: 503);
        }
        return Results.Json(new
        {
            capturedAtUtc = result.CapturedAtUtc,
            screens = result.Screens.Select(s => new
            {
                s.Index,
                s.Width,
                s.Height,
                s.X,
                s.Y,
                s.DeviceName,
                s.Primary,
                url = $"/screenshots/{s.Index}?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            }),
        });
    });

    app.MapGet("/screenshots/{index:int}", (int index, Screenshooter shooter) =>
    {
        var path = shooter.TryGetScreenPath(index);
        if (path is null) return Results.NotFound();
        return Results.File(path, "image/png");
    });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

return 0;

static IPAddress? TryDiscoverTailscaleIp(string interfaceNameContains)
{
    try
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.Name.IndexOf(interfaceNameContains, StringComparison.OrdinalIgnoreCase) < 0 &&
                nic.Description.IndexOf(interfaceNameContains, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ua.Address;
                }
            }
        }
    }
    catch
    {
        // best effort
    }
    return null;
}

static string ReadTodayLogTail(string pattern, int lines)
{
    try
    {
        var dir = Path.GetDirectoryName(pattern);
        var prefix = Path.GetFileNameWithoutExtension(pattern);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return string.Empty;
        var candidates = Directory
            .EnumerateFiles(dir, $"{prefix}*.log")
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .ToList();
        if (candidates.Count == 0) return string.Empty;
        var latest = candidates[0];
        // Open shared so we don't fight Serilog's writer.
        using var fs = new FileStream(latest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        var all = new List<string>();
        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            all.Add(line);
        }
        var skip = Math.Max(0, all.Count - lines);
        return string.Join(Environment.NewLine, all.Skip(skip));
    }
    catch (Exception ex)
    {
        return $"<error reading log tail: {ex.Message}>";
    }
}
