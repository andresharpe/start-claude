using System.Net;
using System.Runtime.Versioning;
using StartClaude.Service;

namespace StartClaude.Service.Tests;

[SupportedOSPlatform("windows")]
public class TailscaleNetworkTests
{
    [Theory]
    [InlineData("100.100.1.5", true)]
    [InlineData("100.64.0.0", true)]
    [InlineData("100.127.255.255", true)]
    [InlineData("100.63.255.255", false)]
    [InlineData("100.128.0.0", false)]
    [InlineData("169.254.10.10", false)]
    [InlineData("192.168.1.5", false)]
    [InlineData("fd7a:115c:a1e0::1", false)]
    [InlineData("::1", false)]
    public void IsTailscaleAddress_accepts_only_the_cgnat_range(string address, bool expected)
    {
        Assert.Equal(expected, TailscaleNetwork.IsTailscaleAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData(null, "100.100.1.5", true)]
    [InlineData("100.100.1.5", "100.100.2.9", true)]
    [InlineData("100.100.1.5", "100.100.1.5", false)]
    [InlineData("100.100.1.5", null, false)]
    [InlineData(null, null, false)]
    public void ShouldRebind_only_when_a_valid_address_exists_and_differs(string? bound, string? current, bool expected)
    {
        var boundIp = bound is null ? null : IPAddress.Parse(bound);
        var currentIp = current is null ? null : IPAddress.Parse(current);
        Assert.Equal(expected, TailscaleNetwork.ShouldRebind(boundIp, currentIp));
    }
}
