using System.Net;

namespace StartClaude.Service;

/// <summary>
/// Discovers the machine's public IPv4 by asking a few echo services. The value
/// is cached and refreshed in the background, so callers (the dashboard polls
/// every few seconds) never block on a network round trip.
/// </summary>
public sealed class PublicIpProvider : IDisposable
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinRetryInterval = TimeSpan.FromSeconds(30);

    // Plain-text endpoints that echo back the caller's public IP.
    private static readonly string[] Endpoints =
    {
        "https://api.ipify.org",
        "https://icanhazip.com",
        "https://ifconfig.me/ip",
    };

    private readonly ILogger<PublicIpProvider> _logger;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private volatile string? _cached;
    private DateTime _fetchedUtc = DateTime.MinValue;
    private DateTime _lastAttemptUtc = DateTime.MinValue;

    public PublicIpProvider(ILogger<PublicIpProvider> logger)
    {
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("start-claude-watchdog");
    }

    /// <summary>
    /// Returns the last known public IP without blocking. Kicks off a background
    /// refresh when the cached value is missing or stale. Returns null until the
    /// first successful lookup completes.
    /// </summary>
    public string? Current()
    {
        var now = DateTime.UtcNow;
        var stale = _cached is null || now - _fetchedUtc > Ttl;
        var cooledDown = now - _lastAttemptUtc > MinRetryInterval;
        if (stale && cooledDown)
        {
            _ = RefreshAsync();
        }
        return _cached;
    }

    private async Task RefreshAsync()
    {
        // Drop the request if a refresh is already in flight.
        if (!await _refreshGate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            _lastAttemptUtc = DateTime.UtcNow;
            foreach (var url in Endpoints)
            {
                try
                {
                    var text = (await _http.GetStringAsync(url).ConfigureAwait(false)).Trim();
                    if (IPAddress.TryParse(text, out var ip))
                    {
                        _cached = ip.ToString();
                        _fetchedUtc = DateTime.UtcNow;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Public IP lookup failed via {Url}", url);
                }
            }
            _logger.LogDebug("All public IP lookups failed this round");
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void Dispose() => _http.Dispose();
}
