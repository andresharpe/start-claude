using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace StartClaude.Service;

/// <summary>
/// The Tailscale IPv4 address Kestrel bound at startup, or null when the
/// service came up loopback-only. Immutable for the life of the process
/// because Kestrel cannot add listeners after start; the watchdog compares
/// it against live discovery to decide when a restart would fix the bind.
/// </summary>
public sealed record TailscaleBindingState(IPAddress? BoundIp);

/// <summary>
/// Discovery of the machine's Tailscale IPv4 address. Tailscale always assigns
/// from the carrier-grade NAT range 100.64.0.0/10 (RFC 6598), so anything
/// outside that range - notably the APIPA 169.254.x.x address Windows puts on
/// the TUN adapter before tailscaled is ready - is never a tailnet address.
/// </summary>
public static class TailscaleNetwork
{
    public static bool IsTailscaleAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        Span<byte> bytes = stackalloc byte[4];
        if (!address.TryWriteBytes(bytes, out _)) return false;
        // Network byte order: 100.64.0.0/10 is first octet 100, top two bits
        // of the second octet equal to 01 (64-127).
        return bytes[0] == 100 && (bytes[1] & 0xC0) == 0x40;
    }

    /// <summary>
    /// True when a valid tailnet address exists now and differs from what was
    /// bound at startup, including the case where nothing was bound. Never true
    /// while Tailscale is down - a working loopback service is not restarted to
    /// chase a missing interface.
    /// </summary>
    public static bool ShouldRebind(IPAddress? boundIp, IPAddress? currentIp) =>
        currentIp is not null && !currentIp.Equals(boundIp);

    public static IPAddress? TryDiscoverTailscaleIp(string interfaceNameContains)
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
                // The adapter can hold an APIPA address alongside (or before) the
                // tailnet one, so keep scanning instead of taking the first IPv4.
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (IsTailscaleAddress(ua.Address))
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

    /// <summary>
    /// Polls for the Tailscale interface until it has a tailnet IPv4 address or
    /// the timeout expires. Returns null on timeout, which leaves the caller
    /// bound to loopback.
    /// </summary>
    public static IPAddress? WaitForTailscaleIp(string interfaceNameContains, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (TryDiscoverTailscaleIp(interfaceNameContains) is { } ip) return ip;
            if (DateTime.UtcNow >= deadline) return null;
            Thread.Sleep(500);
        }
    }
}
