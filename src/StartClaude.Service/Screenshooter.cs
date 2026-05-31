using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;

namespace StartClaude.Service;

public sealed record ScreenInfo(int Index, string File, int Width, int Height, int X, int Y, string DeviceName, bool Primary);

public sealed record ScreenshotResult(DateTime CapturedAtUtc, IReadOnlyList<ScreenInfo> Screens);

public sealed class ScreenshotOptions
{
    public string TaskName { get; set; } = "StartClaudeScreenshot";
    public string OutputDir { get; set; } = @"C:\ProgramData\StartClaude\screenshots";
    public int WaitSeconds { get; set; } = 8;
}

[SupportedOSPlatform("windows")]
public sealed class Screenshooter
{
    private readonly ScreenshotOptions _options;
    private readonly ILogger<Screenshooter> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Screenshooter(ScreenshotOptions options, ILogger<Screenshooter> logger)
    {
        _options = options;
        _logger = logger;
    }

    public string OutputDir => _options.OutputDir;

    public async Task<ScreenshotResult?> CaptureAsync(CancellationToken cancellationToken)
    {
        // Serialize concurrent capture requests so we don't trigger the task many times in parallel.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var metaPath = Path.Combine(_options.OutputDir, "metadata.json");
            DateTime baseline = File.Exists(metaPath) ? File.GetLastWriteTimeUtc(metaPath) : DateTime.MinValue;

            if (!TriggerTask(out var output))
            {
                _logger.LogWarning("schtasks /Run failed for {Task}: {Output}", _options.TaskName, output);
                return null;
            }

            // Wait for the metadata.json file to update past `baseline`.
            var deadline = DateTime.UtcNow.AddSeconds(_options.WaitSeconds);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(metaPath) && File.GetLastWriteTimeUtc(metaPath) > baseline)
                {
                    try
                    {
                        await using var fs = new FileStream(metaPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        var parsed = await JsonSerializer.DeserializeAsync<MetadataFile>(fs, _jsonOpts, cancellationToken).ConfigureAwait(false);
                        if (parsed is null) return null;
                        return new ScreenshotResult(
                            parsed.CapturedAtUtc,
                            parsed.Screens.Select(s => new ScreenInfo(s.Index, s.File, s.Width, s.Height, s.X, s.Y, s.DeviceName, s.Primary)).ToList());
                    }
                    catch (IOException)
                    {
                        // Briefly racing with the writer - try again shortly.
                    }
                }
                await Task.Delay(150, cancellationToken).ConfigureAwait(false);
            }
            _logger.LogWarning("Screenshot capture timed out after {Seconds}s waiting for {Path}",
                _options.WaitSeconds, metaPath);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public string? TryGetScreenPath(int index)
    {
        // Files are named screen-{index}.png by the script.
        var path = Path.Combine(_options.OutputDir, $"screen-{index}.png");
        return File.Exists(path) ? path : null;
    }

    private bool TriggerTask(out string output)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/Run /TN \"{_options.TaskName}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        try
        {
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null");
            p.WaitForExit(10_000);
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            output = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : $"{stdout.Trim()} | {stderr.Trim()}";
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            output = ex.Message;
            return false;
        }
    }

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class MetadataFile
    {
        public DateTime CapturedAtUtc { get; set; }
        public int ScreenCount { get; set; }
        public List<MetadataScreen> Screens { get; set; } = new();
    }

    private sealed class MetadataScreen
    {
        public int Index { get; set; }
        public string File { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public string DeviceName { get; set; } = "";
        public bool Primary { get; set; }
    }
}
